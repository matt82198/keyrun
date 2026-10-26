using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Keyrun.Sim.Nav;

namespace Keyrun.Bots;

/// <summary>bots/src/bot.ts BotMemory: the skill dial and the next tick the bot may act.</summary>
/// <param name="SkillDial">0..100.</param>
/// <param name="NextActionTick">Role dispatch waits until this tick.</param>
/// <param name="FillerHistory">
/// Filler abilities this seat cast, least recently cast first (null = none yet). <see cref="Roles.Fillers.ArrangeFillers"/>
/// reads it so the least recently cast ready filler is chosen, not the same one every global (finding f-c356528f).
/// </param>
/// <param name="BloomNoProgressTicks">R-PATH-5 (z): consecutive seed-bloom decisions that did not set a new best distance (0 = none).</param>
/// <param name="BloomBestDistSq">Smallest squared distance (mm squared, integer) to the bloom target seen in the current walk (null = none, or reset).</param>
/// <param name="BloomTick">Tick of the last seed-bloom decision (-1 = none); a decision on any other tick is not a bloom walk.</param>
/// <param name="HeldPackId">R-PATH-5 (z): the held pack's enemy id a stuck bloom walk dropped for; the tank keeps approaching it (null = none).</param>
/// <param name="LastAllyPressuredTick">R-AGGRO-1 (ab): the last tick a tank seat saw an ally pressured (-1 = never); the ally-pressure signal is sticky for 60 ticks after it.</param>
public sealed record BotMemory(
    int SkillDial,
    int NextActionTick,
    IReadOnlyList<string>? FillerHistory = null,
    int BloomNoProgressTicks = 0,
    long? BloomBestDistSq = null,
    int BloomTick = -1,
    int? HeldPackId = null,
    int LastAllyPressuredTick = -1)
{
    /// <summary>bot.ts defaultMemory: <c>{skillDial, nextActionTick: 0}</c>.</summary>
    /// <param name="skillDial">Skill dial (default 100).</param>
    /// <returns>Fresh memory.</returns>
    public static BotMemory Default(int skillDial = 100) => new(skillDial, 0);
}

/// <summary>One bot decision: commands, the next memory and the advanced PRNG.</summary>
/// <param name="Commands">Commands for this tick.</param>
/// <param name="Memory">Next memory.</param>
/// <param name="Prng">Next PRNG state.</param>
public sealed record BotDecision(IReadOnlyList<Command> Commands, BotMemory Memory, BotPrngState Prng);

/// <summary>A runBots result: every bot's commands in actorId order, the memories, and the threaded PRNG.</summary>
/// <param name="Commands">All commands.</param>
/// <param name="Memories">Memories by actorId (ascending).</param>
/// <param name="Prng">PRNG after the last bot.</param>
public sealed record BotBatch(IReadOnlyList<Command> Commands, SortedDictionary<int, BotMemory> Memories, BotPrngState Prng)
{
    /// <summary>One <see cref="Reason"/> per command, index-aligned with <see cref="Commands"/> (G2.4). Receipt data only: it never feeds a decision or the fingerprint.</summary>
    public IReadOnlyList<Reason> Reasons { get; init; } = Array.Empty<Reason>();
}

/// <summary>bots/src/bot.ts Bot: a pure function <c>(view, memory, actorId, prng, pings) =&gt; {commands, memory, prng}</c>.</summary>
public interface IBot
{
    /// <summary>bot.ts Bot.decide.</summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="memory">This bot's memory.</param>
    /// <param name="actorId">The actor it drives.</param>
    /// <param name="prng">Bot PRNG state.</param>
    /// <param name="pings">Live party pings (null = none).</param>
    /// <returns>The decision.</returns>
    BotDecision Decide(BotView view, BotMemory memory, int actorId, BotPrngState prng, IReadOnlyList<PingEvent>? pings = null);
}

/// <summary>
/// Port of bots/src/bot.ts: the bot runner. Each tick, highest priority first: (1) danger-telegraph avoidance,
/// skill-dial-free; (2) party-ping reactions, also skill-dial-free (a ping cast skips role dispatch so a bot
/// never casts twice in a tick); (3) role dispatch through <see cref="RoleSet"/>, gated by the reaction delay
/// and the mistake roll.
/// </summary>
public static class Bot
{
    private sealed class RunnerBot : IBot
    {
        private readonly RoleSet _roles;

        public RunnerBot(RoleSet roles) => _roles = roles;

        public BotDecision Decide(BotView view, BotMemory memory, int actorId, BotPrngState prng, IReadOnlyList<PingEvent>? pings = null)
        {
            var decision = DecideForActor(_roles, view, memory, actorId, prng, pings ?? Array.Empty<PingEvent>());
            // R-PATH-5 (y): a decision that was not a seed-bloom walk on this tick ends any stuck-bloom run.
            return decision.Memory.BloomTick == view.State.Tick
                ? decision
                : decision with { Memory = ClearBloomWalk(decision.Memory) };
        }
    }

    /// <summary>R-PATH-5 (z): decisions that set no new best distance to the bloom target before a held-pack tank drops the walk.</summary>
    private const int BloomStuckLimit = 10;

    /// <summary>R-PATH-5 (z): the memory with the seed-bloom walk progress cleared (the held-pack id is kept).</summary>
    private static BotMemory ClearBloomWalk(BotMemory memory) =>
        memory with { BloomNoProgressTicks = 0, BloomBestDistSq = null, BloomTick = -1 };

    /// <summary>R-PATH-5 (z): squared distance in mm (integer) between two points on the floor plane.</summary>
    private static long DistSq(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }

    /// <summary>
    /// R-PATH-5 (y): true when <paramref name="enemy"/> is a live, engaged enemy held by another living party seat (not self).
    /// An idle (unheld) pack is never held by another seat.
    /// </summary>
    private static bool IsHeldByAnother(BotView view, ActorState self, ActorState enemy)
    {
        if (!enemy.Alive || enemy.Idle || enemy.Kind != ActorKind.Enemy || enemy.HolderId is not { } holderId || holderId == self.Id)
        {
            return false;
        }
        return view.State.Actors.TryGetValue(holderId, out var holder) && holder.Kind == ActorKind.Player && holder.Alive;
    }

    /// <summary>
    /// R-PATH-5 (y): the tank's engage target when it is a pack already held by another living party seat. Null for a
    /// non-tank or an unheld target.
    /// </summary>
    private static ActorState? HeldPackTarget(BotView view, ActorState self) =>
        self.Role == ActorRole.Tank && Roles.TankEngage.EngageTarget(view, self) is { } target && IsHeldByAnother(view, self, target)
            ? target
            : null;

    /// <summary>R-PATH-5 (y): the tank's TankEngage approach move (null for a non-tank or no approach move).</summary>
    private static MoveCommand? TankApproach(BotView view, ActorState self) =>
        self.Role == ActorRole.Tank ? Roles.TankEngage.ApproachMove(view, self, Roles.RoleShared.KitOf(view, self.ClassId)) : null;

    /// <summary>
    /// R-PATH-5 (z): clears the held-pack id when the tank is the holder or the held enemy no longer qualifies (dead, idle,
    /// gone, or held by nobody else). Returns <paramref name="memory"/> unchanged otherwise.
    /// </summary>
    private static BotMemory ResolveHeldPack(BotView view, ActorState self, BotMemory memory)
    {
        if (memory.HeldPackId is not { } heldId)
        {
            return memory;
        }
        var stillHeld = view.State.Actors.TryGetValue(heldId, out var enemy) && IsHeldByAnother(view, self, enemy);
        return stillHeld ? memory : memory with { HeldPackId = null };
    }

    /// <summary>
    /// bot.ts dangerAvoidCommand: a move to the nearest safe point when inside an active danger telegraph. On a layout run
    /// (LINE-DODGE, finding f-e17e190c) the safe point must be walkable and the move walks the sim's path to it: the sim
    /// clamps every actor into its open boxes after each tick, so a straight move into a wall leaves the seat inside the
    /// telegraph. Off a layout run (every oracle run) the straight move is unchanged.
    /// </summary>
    public static MoveCommand? DangerAvoidCommand(BotView view, ActorState self)
    {
        var tick = view.State.Tick;
        var selfMm = PointMm.Of(self.Pos);
        // DEAD-CASTER-TELEGRAPH: a telegraph whose caster died before its cast began warns of nothing (Positioning.IsSpent).
        var active = view.Telegraphs.Where(t => t.Danger && t.ResolvesTick > tick && !Positioning.IsSpent(t, view.State)).ToList();
        if (!Positioning.IsInAnyDanger(selfMm, active))
        {
            return null;
        }
        var safe = DangerSafePoint(view, self, active);
        if (safe is null)
        {
            return null;
        }
        return view.Layout is { } layout
            ? PathMoves.Along(tick, self, layout, new Vec3(safe.Value.X, self.Pos.Y, safe.Value.Z))
            : Moves.MoveToward(tick, self.Id, selfMm, safe.Value);
    }

    /// <summary>
    /// The nearest point clear of <paramref name="active"/> that the seat may stand on: on a layout run the nearest walkable
    /// point (LINE-DODGE), else the nearest clear point. Null when there is none.
    /// </summary>
    private static PointMm? DangerSafePoint(BotView view, ActorState self, IReadOnlyList<BotTelegraph> active)
    {
        var selfMm = PointMm.Of(self.Pos);
        if (view.Layout is { } layout)
        {
            var grid = NavGrid.Of(layout);
            return Positioning.NearestSafePoint(
                selfMm,
                active,
                extraCheck: p => grid.Walkable(new Vec3(p.X, self.Pos.Y, p.Z)));
        }
        return Positioning.NearestSafePoint(selfMm, active);
    }

    /// <summary>
    /// R-GCD-1 (c) escape destinations: true when <paramref name="p"/> lies inside the aggro ring of a living enemy that is
    /// still idle (an unengaged pack). The sim's rule (EnemyAggro.SeesPlayer) engages a group when a player stands inside a
    /// member's ring, centred on its WanderAnchor (else its position) at AggroMm, so an escape landing there pulls the pack.
    /// </summary>
    private static bool InUnengagedAggro(BotView view, PointMm p)
    {
        var at = new Vec3(p.X, 0, p.Z);
        foreach (var enemy in view.State.Actors.Values)
        {
            if (enemy.Kind == ActorKind.Enemy && enemy.Alive && enemy.Idle && enemy.AggroMm is { } radius
                && Keyrun.Sim.Encounter.Aggro.InRing(at, enemy.WanderAnchor ?? enemy.Pos, radius))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The escape's destination: the nearest point clear of <paramref name="active"/> danger that is also outside every
    /// unengaged pack's aggro ring (and walkable on a layout run). Falls back to the next candidate ring by ring, so a null
    /// result sends the caller to the danger-dodge walk.
    /// </summary>
    private static PointMm? EscapeSafePoint(BotView view, ActorState self, IReadOnlyList<BotTelegraph> active)
    {
        var selfMm = PointMm.Of(self.Pos);
        var grid = view.Layout is { } layout ? NavGrid.Of(layout) : null;
        return Positioning.NearestSafePoint(
            selfMm,
            active,
            extraCheck: p => !InUnengagedAggro(view, p)
                && (grid is null || grid.Walkable(new Vec3(p.X, self.Pos.Y, p.Z))));
    }

    /// <summary>
    /// True when <paramref name="self"/> is the tank seat and is taking a pack (its TankEngage target is set) or holding one
    /// (a living enemy names it as holder). Always false for a non-tank seat.
    /// </summary>
    private static bool IsTankTakingOrHolding(BotView view, ActorState self)
    {
        if (self.Role != ActorRole.Tank)
        {
            return false;
        }
        if (Roles.TankEngage.EngageTarget(view, self) is not null)
        {
            return true;
        }
        return view.State.Actors.Values.Any(e => e.Kind == ActorKind.Enemy && e.Alive && e.HolderId == self.Id);
    }

    /// <summary>
    /// R-GCD-1 (c) escape trigger: a telegraph threatens <paramref name="self"/> when it has no enemy source (a floor
    /// telegraph or a non-enemy source) or its enemy source is held by the seat itself (the cast is aimed at it). An enemy
    /// cast aimed at another seat, such as a pack the tank holds, does not move this seat.
    /// </summary>
    private static bool CastAimedAtSeat(BotView view, ActorState self, BotTelegraph t)
    {
        if (t.SourceId is not { } sid || !view.State.Actors.TryGetValue(sid, out var src) || src.Kind != ActorKind.Enemy)
        {
            return true;
        }
        return src.HolderId == self.Id;
    }

    /// <summary>Walk speed of <paramref name="self"/> in mm per tick: its own speed, else the content default (180).</summary>
    private static long WalkSpeedMmPerTick(ActorState self) => self.SpeedMmPerTick ?? Ledger.DefaultWalkMmPerTick;

    /// <summary>
    /// R-GCD-1 (c) rule 1, the escape tier: when the seat stands in an active danger telegraph whose walk-out to the nearest
    /// safe point takes longer than the telegraph has left to resolve, it presses its class movement ability (the kit's
    /// MovementAbilityId) only when that ability clears the danger before the telegraph resolves, using the ability's own
    /// movement: chanter.glide (self buff) at the walk speed scaled by its movementBp; cinder.blink (displace along facing)
    /// only to a landing outside the danger, turning first when the current facing lands inside; warden.charge (displace
    /// toward a target) only with a living enemy target and a landing outside the danger, never aimed at a point. Returns the
    /// commands to issue (a turn then a cast, or a single cast), or null, and the walk <see cref="DangerAvoidCommand"/> stands.
    /// Only a telegraph that threatens the seat triggers it (<see cref="CastAimedAtSeat"/>), and a tank seat that is taking
    /// or holding a pack never escapes (<see cref="IsTankTakingOrHolding"/>).
    /// The walk-out is the straight distance to the safe point at the seat's walk speed, rounded up to whole ticks (a lower
    /// bound on a layout run). Skill-dial-free like the danger dodge it replaces. Fails closed without a content registry.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor (alive).</param>
    /// <returns>The escape commands, or null.</returns>
    public static IReadOnlyList<Command>? MovementEscapeCommands(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        var selfMm = PointMm.Of(self.Pos);
        var active = view.Telegraphs.Where(t => t.Danger && t.ResolvesTick > tick && !Positioning.IsSpent(t, view.State)).ToList();
        var threatening = active.Where(t => Positioning.IsInAnyDanger(selfMm, new[] { t }) && CastAimedAtSeat(view, self, t)).ToList();
        if (threatening.Count == 0)
        {
            return null;
        }
        // The tank seat never escapes while it is taking or holding a pack: its job is to take the pack, so the danger
        // dodge walk (DangerAvoidCommand) stands and the TankEngage decision keeps the pack.
        if (IsTankTakingOrHolding(view, self))
        {
            return null;
        }
        var remainingTicks = threatening.Min(t => t.ResolvesTick) - tick;
        if (EscapeSafePoint(view, self, active) is not { } safe)
        {
            return null;
        }
        // The movement slot lives on the content-derived ClassKit (Kits.cs MovementAbilityId); no registry means no movement cast.
        if (view.Kits.GetClassKit(self.ClassId)?.MovementAbilityId is not { } movementId)
        {
            return null;
        }
        var kit = Roles.RoleShared.KitOf(view, self.ClassId);
        if (!Roles.RoleShared.ReadyAndAffordable(self, movementId, tick, kit))
        {
            return null;
        }
        var dx = safe.X - selfMm.X;
        var dz = safe.Z - selfMm.Z;
        var distance = Math.Sqrt((double)((dx * dx) + (dz * dz)));
        var walkOutTicks = (long)Math.Ceiling(distance / WalkSpeedMmPerTick(self));
        if (walkOutTicks <= remainingTicks)
        {
            return null;
        }
        // Fail closed without the content registry: the ability's own movement cannot be read, so no escape is cast.
        if (view.Kits.Content is not { } registry || !registry.Abilities.TryGetValue(movementId, out var def))
        {
            return null;
        }
        var input = new EscapeInput(view, self, selfMm, active, safe, remainingTicks, distance, movementId, def, registry);
        var displace = def.Effects.OfType<Keyrun.Content.DisplaceOp>().FirstOrDefault();
        if (displace is not null)
        {
            return displace.TowardTarget ? ChargeEscape(input, displace) : BlinkEscape(input, displace);
        }
        return def.Shape.Kind == Keyrun.Content.ShapeKind.Self ? GlideEscape(input) : null;
    }

    /// <summary>Inputs the escape-tier helpers share, computed once by <see cref="MovementEscapeCommands"/>.</summary>
    /// <param name="View">Bot view.</param>
    /// <param name="Self">Deciding actor.</param>
    /// <param name="SelfMm">The seat's position.</param>
    /// <param name="Active">Live danger telegraphs.</param>
    /// <param name="Safe">The nearest safe point.</param>
    /// <param name="RemainingTicks">Ticks until the earliest telegraph the seat stands in resolves.</param>
    /// <param name="Distance">Straight distance from the seat to the safe point, mm.</param>
    /// <param name="MovementId">The kit's movement ability id.</param>
    /// <param name="Def">The movement ability's content definition.</param>
    /// <param name="Registry">The content registry.</param>
    private sealed record EscapeInput(
        BotView View,
        ActorState Self,
        PointMm SelfMm,
        IReadOnlyList<BotTelegraph> Active,
        PointMm Safe,
        long RemainingTicks,
        double Distance,
        string MovementId,
        Keyrun.Content.AbilityDef Def,
        Keyrun.Content.ContentRegistry Registry);

    /// <summary>
    /// Glide (a self-shaped buff, chanter.glide): the seat moves at its walk speed scaled by the applied effect's movementBp
    /// (walk * (10000 + bp) / 10000, so 180 becomes 270 at bp 5000). Cast only when that glide speed reaches the safe point
    /// before the telegraph resolves. Null otherwise.
    /// </summary>
    private static IReadOnlyList<Command>? GlideEscape(EscapeInput input)
    {
        var bp = input.Def.Effects.OfType<Keyrun.Content.ApplyEffectOp>()
            .Select(op => input.Registry.Effects.TryGetValue(op.EffectId, out var effect) ? (effect.Modifiers.MovementBp ?? 0) : 0)
            .DefaultIfEmpty(0)
            .Max();
        if (bp <= 0)
        {
            return null;
        }
        var glideMmPerTick = WalkSpeedMmPerTick(input.Self) * (10000 + bp) / 10000;
        var glideTicks = (long)Math.Ceiling(input.Distance / glideMmPerTick);
        if (glideTicks > input.RemainingTicks)
        {
            return null;
        }
        return new Command[] { Reasons.Tag(new CastCommand(input.View.State.Tick, input.Self.Id, input.MovementId), "bot:movement-escape") };
    }

    /// <summary>
    /// Blink (a self-shaped displace along the seat's facing, cinder.blink). Blink is instant, so the cast clears the danger
    /// when its landing (the facing's displacement) is outside every active danger. When the current facing lands inside,
    /// the seat first turns to a yaw whose landing is clear, preferring the yaw toward the safe point, and blink follows the
    /// turn in the same decision (the sim applies a move's yaw before a later command in the list). Null when no yaw clears.
    /// </summary>
    private static IReadOnlyList<Command>? BlinkEscape(EscapeInput input, Keyrun.Content.DisplaceOp displace)
    {
        var tick = input.View.State.Tick;
        var self = input.Self;
        var cast = Reasons.Tag(new CastCommand(tick, self.Id, input.MovementId), "bot:movement-escape");
        if (BlinkLandingClear(input, self.Yaw, displace.DistanceMm))
        {
            return new Command[] { cast };
        }
        var toward = Moves.YawToward(input.SelfMm, input.Safe);
        // Candidate yaws: the yaw toward the safe point, then alternating outward in 64-step (22.5 degree) increments.
        for (var i = 0; i < 16; i++)
        {
            var offset = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 64;
            var yaw = ((toward + offset) % 1024 + 1024) % 1024;
            if (BlinkLandingClear(input, yaw, displace.DistanceMm))
            {
                var turn = Reasons.Tag(new MoveCommand(tick, self.Id, new Dir2(0, 0), yaw), "bot:movement-escape");
                return new Command[] { turn, cast };
            }
        }
        return null;
    }

    /// <summary>True when a displacement of <paramref name="distanceMm"/> along <paramref name="yaw"/> lands outside every active danger.</summary>
    private static bool BlinkLandingClear(EscapeInput input, long yaw, long distanceMm)
    {
        var face = Facing.FaceDir(yaw);
        var landing = new PointMm(
            input.SelfMm.X + (face.X * distanceMm / Facing.Q),
            input.SelfMm.Z + (face.Z * distanceMm / Facing.Q));
        return !Positioning.IsInAnyDanger(landing, input.Active) && !InUnengagedAggro(input.View, landing);
    }

    /// <summary>
    /// Charge (a target-displacing move, warden.charge): needs a living enemy target within the ability's range, since it does
    /// nothing without one and is never aimed at a point. Cast only when the seat's landing (moved toward the target by the
    /// displacement, never past it) is outside every active danger. Null otherwise.
    /// </summary>
    private static IReadOnlyList<Command>? ChargeEscape(EscapeInput input, Keyrun.Content.DisplaceOp displace)
    {
        var self = input.Self;
        if (self.TargetId is not { } targetId
            || !input.View.State.Actors.TryGetValue(targetId, out var target)
            || !target.Alive
            || target.Kind != ActorKind.Enemy)
        {
            return null;
        }
        var targetMm = PointMm.Of(target.Pos);
        var dx = targetMm.X - input.SelfMm.X;
        var dz = targetMm.Z - input.SelfMm.Z;
        var toTarget = Math.Sqrt((double)((dx * dx) + (dz * dz)));
        if (toTarget <= 0 || toTarget > input.Def.Range)
        {
            return null;
        }
        var step = Math.Min(displace.DistanceMm, (long)Math.Floor(toTarget));
        var f = step / toTarget;
        var landing = new PointMm(input.SelfMm.X + (long)Math.Round(dx * f), input.SelfMm.Z + (long)Math.Round(dz * f));
        if (Positioning.IsInAnyDanger(landing, input.Active) || InUnengagedAggro(input.View, landing))
        {
            return null;
        }
        return new Command[] { Reasons.Tag(new CastCommand(input.View.State.Tick, self.Id, input.MovementId, targetId), "bot:movement-escape") };
    }

    /// <summary>
    /// True when <paramref name="self"/> is a tank whose taunt is ready on an in-range loose enemy other than the enemy its held-pack
    /// walk is heading for (<paramref name="walkTargetId"/>; null when no walk is set). That taunt is the role's take-the-pack cast.
    /// </summary>
    private static bool TauntReadyOnLooseEnemy(BotView view, ActorState self, int? walkTargetId = null) =>
        self.Role == ActorRole.Tank
        && Roles.TankEngage.TauntTarget(view, self, Roles.RoleShared.KitOf(view, self.ClassId)) is { } taunt
        && taunt.Id != walkTargetId;

    private static BotDecision DecideForActor(RoleSet roles, BotView view, BotMemory memory, int actorId, BotPrngState prng, IReadOnlyList<PingEvent> pings)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(memory);
        if (!view.State.Actors.TryGetValue(actorId, out var self) || !self.Alive)
        {
            return new BotDecision(Array.Empty<Command>(), memory, prng);
        }
        var tick = view.State.Tick;
        memory = ResolveHeldPack(view, self, memory);
        // R-AGGRO-1 (ab): a tank seat records every tick an ally is pressured (every tick, not only deciding ticks), so the
        // sticky ally-pressure signal is read from the role's view below.
        if (self.Role == ActorRole.Tank && Roles.TankPressure.AnyAllyPressuredNow(view, self))
        {
            memory = memory with { LastAllyPressuredTick = tick };
        }

        var commands = new List<Command>();
        // R-GCD-1 (c) rule 1: a walk-out that cannot fit before the telegraph resolves is replaced by the class movement cast.
        if (MovementEscapeCommands(view, self) is { } escape)
        {
            commands.AddRange(escape);
            return new BotDecision(commands, memory, prng);
        }
        var avoid = DangerAvoidCommand(view, self);
        if (avoid is not null)
        {
            commands.Add(Reasons.Tag(avoid, "bot:danger-dodge"));
        }

        var pingCommands = Reasons.TagAll(PingsPolicy.PingReactionCommands(view, self, pings), "bot:ping-reaction");
        commands.AddRange(pingCommands);
        var pingCast = pingCommands.Any(c => c is CastCommand);

        // Travel between rooms (STATE.md D18; no TS counterpart, Follow.FollowCommand): on a layout run the bot walks the
        // route to its slot behind the leader while no enemy lives, every tick and skill-dial-free like the danger dodge
        // (the reaction delay would leave it a third of the leader's speed), and skips its role. In a fight it started
        // outside the fight's room, it walks in the same way while its role still decides (its casts are kept, its own
        // moves dropped). A dodge or a ping reaction wins over both. Inert in the oracle runner: its view carries no layout.
        // KF2 (BOTS-ENGAGE-CHOIR): a bot in the leader seat walks the route between fights, so a party with no human
        // leading reaches the next pull instead of idling at the Entrance. Skill-dial-free like the follow walk.
        // SEED-BLOOM-TEACHES (BIBLE s9.6 item 3): while an encounter floor Window (the Seed's Death Bloom) is open, a bot
        // that can land a Payoff in it walks into the ring and lands it, ahead of travel and its role; skill-dial-free like
        // travel. Inert in the oracle runner (no layout), like the travel it outranks. See EncounterWindow.
        // R-PATH-5 (z): a bloom walk is measured by its best (smallest, integer mm squared) distance to the bloom target. A
        // decision that sets no new best counts as no progress; BloomStuckLimit of them in a row drop the walk for the held
        // pack when one is held by another living seat, and the held pack's id is kept so the tank keeps approaching it
        // (persist, below) until it holds the pack. A walk that improves keeps priority.
        // R-PATH-5 (z) persist: while a dropped-for pack is still held by another seat, the tank's approach runs ahead of the
        // bloom branch (and of travel and role) until it holds the pack; a null approach (in reach) falls through.
        // Taunt-first (BotTankTakesHolderTests seed 3): a ready taunt on an in-range loose enemy goes before the persisted approach,
        // so a tank that can taunt a loose pack does not walk past it toward a pack another seat already holds.
        if (avoid is null && !pingCommands.Any() && memory.HeldPackId is not null && !TauntReadyOnLooseEnemy(view, self, memory.HeldPackId)
            && TankApproach(view, self) is { } persisted)
        {
            commands.Add(Reasons.Tag(persisted, "bot:tank-approach"));
            return new BotDecision(commands, memory, prng);
        }

        // While a held pack is set the bloom walk yields: inside the approach's reach the tank's role decides (its TankEngage casts
        // take the pack), so the walk never pulls the tank back out of reach.
        if (avoid is null && !pingCommands.Any() && memory.HeldPackId is null && EncounterWindow.Decide(view, self) is { } bloom && EncounterWindow.BloomTarget(view) is { } centre)
        {
            var dist = DistSq(self.Pos, centre);
            var improved = memory.BloomBestDistSq is not { } best || dist < best;
            var noProgress = improved ? 0 : memory.BloomNoProgressTicks + 1;
            // R-PATH-5 (z): a stuck walk is dropped whether or not a pack is held. A held pack keeps the take-the-pack yield;
            // with none the bloom commands are skipped for this decision and the role decision below runs (the wrapper in
            // Decide clears the walk progress because this decision does not set BloomTick). Taunt-first: a ready taunt on a
            // loose engaged pack is never replaced by the drop walk, so the role's taunt runs instead (BotTankTakesHolderTests seed 3).
            if (noProgress < BloomStuckLimit)
            {
                commands.AddRange(Reasons.TagAll(bloom, "bot:seed-bloom"));
                var bestNow = improved ? dist : memory.BloomBestDistSq;
                return new BotDecision(commands, memory with { BloomNoProgressTicks = noProgress, BloomBestDistSq = bestNow, BloomTick = tick }, prng);
            }
            if (HeldPackTarget(view, self) is { } held && !TauntReadyOnLooseEnemy(view, self, held.Id) && TankApproach(view, self) is { } drop)
            {
                commands.Add(Reasons.Tag(drop, "bot:tank-approach"));
                return new BotDecision(commands, ClearBloomWalk(memory) with { HeldPackId = held.Id }, prng);
            }
        }

        // KICK-LEDGER-COOLDOWN-GAP (finding f-13b5c33a, the WoW kick rotation: a melee kicker walks in to reach the caster):
        // when the party ledger names this seat to kick a cast its interrupt cannot reach yet, it walks at the caster. Every
        // tick and skill-dial-free like the walks above (the reaction delay would leave it a third of its speed, and the
        // ledger's landing estimate is at full speed). Also in the oracle runner (no layout): a straight walk, KICK-APPROACH-NO-LAYOUT.
        if (avoid is null && !pingCommands.Any() && Roles.RoleShared.LedgerKickApproachCommand(view, self, Roles.RoleShared.KitOf(view, self.ClassId)) is { } kickWalk)
        {
            commands.Add(Reasons.Tag(kickWalk, "bot:kick-approach"));
            return new BotDecision(commands, memory, prng);
        }

        var lead = avoid is null && !pingCommands.Any() ? Follow.LeadCommand(view, self) : null;
        if (lead is not null)
        {
            commands.Add(Reasons.Tag(lead, "bot:lead-route"));
            return new BotDecision(commands, memory, prng);
        }

        var follow = avoid is null && !pingCommands.Any() ? Follow.FollowCommand(view, self) : null;
        // R-AOE-1 (c), f-c8ed3a62: the bot tank in the lead seat chain-pulls the next pack while the current one is nearly dead
        // (ChainPull). Skill-dial-free like every walk above; its casts stay and its role's own moves are dropped, as when
        // travelling. The followers go with him through FollowCommand.
        var chain = avoid is null && !pingCommands.Any() && view.Layout is { } chainLayout && view.PullPlan is { } chainPlan && Follow.LeaderOf(view)?.Id == self.Id
            ? ChainPull.Move(tick, view.State, chainLayout, chainPlan, self)
            : null;
        if (chain is not null)
        {
            commands.Add(Reasons.Tag(chain, "bot:chain-pull"));
        }

        var travelling = follow is not null || chain is not null;
        // HEALER-TOPS-OFF (the WoW Mythic+ dungeon loop): between pulls a travelling healer still tops off a hurt ally
        // (its casts kept, its own moves dropped, through the same reaction delay and skill roll as a fight decision),
        // so the party never walks into the next pack hurt. Every other bot, and a healer with nobody hurt, only travels.
        var topOff = travelling && self.Role == ActorRole.Healer && !Follow.AnyLivingEnemy(view.State) && Roles.HealerRole.NeedsTopOff(view.State.Actors);
        if (follow is not null)
        {
            // TANK-TRAVEL-APPROACH: a travelling bot tank with an engaged enemy out of reach (aimed at a non-tank seat, the party
            // is fighting it) closes on that enemy instead of walking the route. This move replaces the route walk this tick (one
            // MoveCommand per seat, never two), and the tank falls through to its role decision so the taunt goes out on the same
            // tick; the role's own moves are dropped below while travelling. The non-tank seats and an in-reach tank keep the
            // travel behaviour below.
            var travelApproach = avoid is null && !pingCommands.Any() && self.Role == ActorRole.Tank
                && Roles.TankEngage.EngageTarget(view, self) is { } travelEngaged
                && !travelEngaged.Idle && Roles.TankEngage.WithinPartyLeash(view.State, self, travelEngaged)
                && (travelEngaged.HolderId ?? travelEngaged.TargetId) is { } travelSeatId && travelSeatId != self.Id
                && view.State.Actors.TryGetValue(travelSeatId, out var travelSeat) && travelSeat.Kind == ActorKind.Player && travelSeat.Alive
                    ? Roles.TankEngage.ApproachMove(view, self, Roles.RoleShared.KitOf(view, self.ClassId))
                    : null;
            // R-TANKFLIP-1 (2): a bot tank with an engaged enemy within the party leash, or one that already holds aggro (the
            // enemy's holder, else its target, is the tank), is never given bot:follow-route until that engage resolves. Walking
            // the route pulls it back inside the leader's room inset while the role's melee reapproach steps it out, so the two
            // alternate every tick. It keeps the tank-approach move above when that applies, and otherwise falls through to its
            // role decision with the route walk dropped. Out of combat, an engage the tank has not taken on, and every non-tank
            // seat keep the route.
            var tankLeashed = self.Role == ActorRole.Tank && avoid is null && !pingCommands.Any()
                && Roles.TankEngage.EngageTarget(view, self) is { Idle: false } leashEngaged
                && (Roles.TankEngage.WithinPartyLeash(view.State, self, leashEngaged) || (leashEngaged.HolderId ?? leashEngaged.TargetId) == self.Id);
            if (travelApproach is not null)
            {
                commands.Add(Reasons.Tag(travelApproach, "bot:tank-approach"));
            }
            else if (!tankLeashed)
            {
                commands.Add(Reasons.Tag(follow, "bot:follow-route"));
            }
            if (!Follow.AnyLivingEnemy(view.State) && !topOff)
            {
                return new BotDecision(commands, memory, prng);
            }
        }

        // CHASE-SPEED (f-2f6982e9, BIBLE section 3): the tank's out-of-reach approach is skill-dial-free, like travel. A
        // move holds for the one tick it is issued, so gating it behind the reaction delay and the mistake roll left the
        // Warden walking one tick in three to six (about 40 mm/tick of the 180 walk) toward an engaged pack while the
        // human out-ran him into aggro. A tick on which the role does not decide (delay or mistake) still carries the
        // approach; a deciding tick gets it from TankEngage itself, so it is never issued twice.
        var approach = avoid is null && !pingCommands.Any() && !travelling && self.Role == ActorRole.Tank
            ? Roles.TankEngage.ApproachMove(view, self, Roles.RoleShared.KitOf(view, self.ClassId))
            : null;

        if (tick < memory.NextActionTick)
        {
            if (approach is not null)
            {
                commands.Add(Reasons.Tag(approach, "bot:tank-approach"));
            }
            return new BotDecision(commands, memory, prng);
        }

        var roll = Skill.RollMistake(memory.SkillDial, prng);
        var nextMemory = memory with { NextActionTick = tick + Skill.ReactionDelayTicks(memory.SkillDial) };
        if (roll.Mistake)
        {
            if (approach is not null)
            {
                commands.Add(Reasons.Tag(approach, "bot:tank-approach"));
            }
            return new BotDecision(commands, nextMemory, roll.Next);
        }

        if (!pingCast && roles.For(self.Role) is { } role)
        {
            var history = memory.FillerHistory ?? Array.Empty<string>();
            var decided = topOff ? Reasons.TagAll(Roles.HealerRole.TopOffCommands(view, self), "bot:healer-top-off") : role.Decide(view with { FillerHistory = history, LastAllyPressuredTick = memory.LastAllyPressuredTick }, self);
            commands.AddRange(!travelling ? decided : decided.Where(c => c is not MoveCommand and not DashCommand));
            var recorded = Roles.Fillers.RecordCasts(view, self, history, decided);
            if (!ReferenceEquals(recorded, history))
            {
                nextMemory = nextMemory with { FillerHistory = recorded };
            }
        }

        return new BotDecision(commands, nextMemory, roll.Next);
    }

    /// <summary>bot.ts makeBot: a bot that dispatches roles through <paramref name="roles"/> (default: none).</summary>
    /// <param name="roles">Role registry; null means <see cref="RoleSet.None"/>.</param>
    /// <returns>The bot.</returns>
    public static IBot MakeBot(RoleSet? roles = null) => new RunnerBot(roles ?? RoleSet.None);

    /// <summary>
    /// bot.ts runBots: every bot decides in ascending actorId order, threading one PRNG state through them, all on
    /// the same view and pings. A bot with no memory starts from <see cref="BotMemory.Default"/>.
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="bots">Bots by actorId.</param>
    /// <param name="memories">Memories by actorId.</param>
    /// <param name="prng">Bot PRNG state.</param>
    /// <param name="pings">Live party pings (null = none).</param>
    /// <returns>The batch.</returns>
    public static BotBatch RunBots(
        BotView view,
        IReadOnlyDictionary<int, IBot> bots,
        IReadOnlyDictionary<int, BotMemory> memories,
        BotPrngState prng,
        IReadOnlyList<PingEvent>? pings = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(memories);
        var live = pings ?? Array.Empty<PingEvent>();
        var commands = new List<Command>();
        var nextMemories = new SortedDictionary<int, BotMemory>();
        foreach (var kv in memories)
        {
            nextMemories[kv.Key] = kv.Value;
        }
        var nextPrng = prng;
        foreach (var id in bots.Keys.OrderBy(k => k))
        {
            var memory = memories.TryGetValue(id, out var m) ? m : BotMemory.Default();
            var result = bots[id].Decide(view, memory, id, nextPrng, live);
            // R-FACING-2 (b): a targeted cast is preceded by the seat's turn to its target (CastFacing).
            commands.AddRange(CastFacing.Apply(result.Commands, view.State));
            nextMemories[id] = result.Memory;
            nextPrng = result.Prng;
        }
        return new BotBatch(commands, nextMemories, nextPrng) { Reasons = Keyrun.Bots.Reasons.OfAll(commands) };
    }
}
