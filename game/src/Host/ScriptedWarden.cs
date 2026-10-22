using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// The autorun Warden (Godot-free, deterministic): walks at the Choir Cantor (the lieutenant) until his kick is in
/// range, kicks the Cantor's heal once it is half cast (so the cast bar is on screen for two seconds first), and spends
/// the GCD on his rotation against whatever is in reach: Reckoning at full Momentum, Vigil at 50, Guard Break when it
/// closes an ally's Window, else Strike (the filler, every free global; trash pulls only, R-WARDEN-FILLER-3: in the boss
/// pull Guard Break whenever ready, as on main), and Brace below half health. Every command goes through the same <see cref="CastTargeting"/> rule as a key press.
/// On a layout run (lane NAV-CORE, STATE.md D18) with no living enemy he walks the route instead
/// (<see cref="Follow.LeadRoute"/>: each room's far doorway in turn, but in the active pull's room he holds at the
/// standoff point until the party is inside, LEADER-WAITS), so the pull spawns once all are in; then the combat script above. With no living lieutenant but a living enemy (lane
/// WARDEN-ENGAGES) he targets the nearest one (lowest id on a tie), walks into melee range and faces it, so the
/// rotation keeps landing until the pull is clear.
/// </summary>
public sealed class ScriptedWarden
{
    /// <summary>Stop walking once the lieutenant is this close (Shield Bash range is 3000 mm).</summary>
    public const long ApproachMm = 2400;

    /// <summary>The lieutenant's class id.</summary>
    public const string LieutenantClass = "choir.lieutenant";

    private static readonly string[] Rotation = { "warden.reckoning", "warden.vigil", "warden.guardBreak", "warden.strike" };

    private const string Kick = "warden.shieldBash";
    private const string Defensive = "warden.brace";
    private const string Taunt = "warden.taunt";

    /// <summary>Guard Break: the cooldown-free Payoff that Strike (the filler) yields to only when it closes a Window.</summary>
    private const string PayoffBuilder = "warden.guardBreak";

    /// <summary>Appends the scripted commands for <paramref name="tick"/>.</summary>
    /// <param name="tick">The pre-step tick.</param>
    /// <param name="run">The run.</param>
    /// <param name="into">The command list.</param>
    /// <param name="openSeats">Abandoned seats not yet refilled (the leader does not wait for them), or null.</param>
    public void Sample(int tick, Run run, List<Command> into, IReadOnlyList<int>? openSeats = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(into);
        var start = into.Count;
        SampleRotation(tick, run, into, openSeats);
        if (run.State.Actors.TryGetValue(HostRunSetup.HumanId, out var me) && me.Alive)
        {
            ApplyFrontalAnswer(CleaveAnswer(run, me, tick), me.Id, into, start);
        }
    }

    /// <summary>
    /// CLEAVE-ANSWER: folds a <see cref="CleaveAnswer"/> into the commands one seat queued this tick from
    /// <paramref name="start"/> on: Brace is added (off the GCD, ahead of whatever else he pressed), a sidestep replaces
    /// every move he queued (the dodge wins, as the bots' danger dodge does).
    /// </summary>
    /// <param name="answer">The answer, or null.</param>
    /// <param name="actorId">The Warden.</param>
    /// <param name="into">The command list.</param>
    /// <param name="start">The first index this seat wrote this tick.</param>
    public static void ApplyFrontalAnswer(Command? answer, int actorId, List<Command> into, int start)
    {
        ArgumentNullException.ThrowIfNull(into);
        switch (answer)
        {
            case CastCommand brace when !into.Skip(start).Any(c => c is CastCommand cc && cc.ActorId == actorId && cc.AbilityId == brace.AbilityId):
                into.Insert(start, brace);
                break;
            case MoveCommand step:
                for (var i = into.Count - 1; i >= start; i--)
                {
                    if (into[i].ActorId == actorId && into[i] is MoveCommand or DashCommand)
                    {
                        into.RemoveAt(i);
                    }
                }
                into.Add(step);
                break;
        }
    }

    /// <summary>
    /// CLEAVE-ANSWER (BIBLE s3 "Boss hits are bigger by design and are telegraphed: that is Brace's job"; s9 "Cleave: a
    /// slow frontal cone, readable from the boss's facing"; f-57c04b4a: once Tempo stopped dropping Cleaves, every one
    /// landed and the unanswered Warden died before the Lock opened). While the armed boss's frontal telegraph (a cone
    /// of a damage cast) is drawn over the Warden and he is not already Braced through its landing tick: Brace when it
    /// is ready (its 60-tick buff covers the 40-tick bar), else the sidestep to the nearest point outside the cone (a
    /// casting enemy does not turn, Encounter/Chase.cs, so the cone stays where it was drawn). Null when no frontal is
    /// coming at him. The R-PATROL-3 trash Brace rule (two attackers, under half health) is unchanged: this is the
    /// boss's telegraphed hit, a separate trigger.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="me">The Warden.</param>
    /// <param name="tick">The pre-step tick.</param>
    /// <returns>The Brace cast, the sidestep move, or null.</returns>
    public static Command? CleaveAnswer(Run run, ActorState me, int tick)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(me);
        if (run.Encounter.Boss is not { } boss || !run.Content.Abilities.TryGetValue(Defensive, out var brace))
        {
            return null;
        }
        var bossId = boss.Config.BossActorId;
        var selfMm = PointMm.Of(me.Pos);
        var frontals = run.Encounter.Telegraphs
            .Where(t => t.SourceId == bossId && t.ResolvesTick > tick && t.Shape == "cone"
                && run.Content.Abilities.TryGetValue(t.AbilityId, out var a) && a.Effects.Any(op => op is DamageOp)
                && !Positioning.IsSpent(BotViews.ToBotTelegraph(t), run.State))
            .ToList();
        // Near the cone at all (its radius plus a stride): beyond that the frontal is no business of his.
        if (!frontals.Any(t => Near(t, me.Pos)))
        {
            return null;
        }
        var lands = frontals.Max(t => t.ResolvesTick);
        if (me.Effects.Exists(e => e.EffectId == BraceEffect && e.EndsTick > lands))
        {
            return null;
        }
        if (!frontals.Any(t => InFrontal(t, me.Pos)))
        {
            // Clear of the cone, unbraced: hold still until it lands, so the rotation's approach does not walk him back in.
            return new MoveCommand(tick, me.Id, new Dir2(0, 0), me.Yaw);
        }
        if (Ready(me, brace, tick))
        {
            return new CastCommand(tick, me.Id, Defensive, me.Id);
        }
        var danger = frontals.Select(t => BotViews.ToBotTelegraph(t) with { Danger = true }).ToList();
        var safe = Positioning.NearestSafePoint(selfMm, danger, 250, 40,
            p => !frontals.Any(t => InFrontal(t, new Vec3(p.X, me.Pos.Y, p.Z))));
        return safe is { } s ? Moves.MoveToward(tick, me.Id, selfMm, s) : null;
    }

    /// <summary>CLEAVE-ANSWER: the sidestep clears the drawn cone by this much angle (1/1024 turn, about 8 degrees a side).</summary>
    private const int FrontalMarginAngle1024 = 48;

    /// <summary>CLEAVE-ANSWER: and by this much radius, mm.</summary>
    private const long FrontalMarginMm = 600;

    // The sim's own cone test (Telegraphs.PointInCone) over the drawn cone grown by the margins, so a point the bots'
    // float geometry calls safe on the edge is never one the integer resolve still hits.
    private static bool InFrontal(Keyrun.Sim.Encounter.Telegraph t, Vec3 p) =>
        Keyrun.Sim.Encounter.Telegraphs.PointInCone(t.Origin, t.Yaw, (t.Angle1024 ?? 0) + FrontalMarginAngle1024, (t.Radius ?? 0) + FrontalMarginMm, p);

    private static bool Near(Keyrun.Sim.Encounter.Telegraph t, Vec3 p)
    {
        var r = (t.Radius ?? 0) + (2 * FrontalMarginMm);
        var dx = p.X - t.Origin.X;
        var dz = p.Z - t.Origin.Z;
        return (dx * dx) + (dz * dz) <= r * r;
    }

    /// <summary>Brace's buff (content/data/abilities/warden.json, warden.brace applyEffect).</summary>
    private const string BraceEffect = "braceSelf";

    private void SampleRotation(int tick, Run run, List<Command> into, IReadOnlyList<int>? openSeats)
    {
        var state = run.State;
        if (!state.Actors.TryGetValue(HostRunSetup.HumanId, out var me) || !me.Alive)
        {
            return;
        }
        if (run.Content.Layout is not null && !Follow.AnyLivingEnemy(state))
        {
            if (Follow.LeadRoute(tick, me, run, openSeats) is { } walk)
            {
                into.Add(walk);
            }
            return;
        }
        var abilities = run.Content.Abilities;
        var lieutenant = state.Actors.Values.FirstOrDefault(a => a.ClassId == LieutenantClass && EngagedTargets.IsEngagedHostile(me, a));

        // R-WARDEN-KICK-1 (core loop defers to WoW: the tank interrupts the caster that is casting): the kick goes to the
        // engaged lieutenant that is mid-way through an interruptible cast inside Shield Bash range, the one whose cast
        // resolves sooner, not the first lieutenant in list order (a healer casting beside an idle Cantor was never kicked).
        var kickable = KickableLieutenant(state, me, tick, abilities);
        if (kickable is not null && Ready(me, abilities[Kick], tick))
        {
            into.Add(new CastCommand(tick, me.Id, Kick, kickable.Id));
        }

        // LOOSE-PICKUP (BIBLE section 3, the bot tank's TankEngage rule): an engaged enemy aimed at another seat that the
        // Warden does not hold is taunted back (Taunt is off the GCD); with Taunt ready but the enemy out of its range he
        // walks at it first. Run C at key 2: the Choir melee chased the careless Cinder and killed it, and he never taunted.
        var loose = TankEngage.PickLooseEnemy(state.Actors, me);
        var tauntReady = loose is not null && abilities.TryGetValue(Taunt, out var taunt) && Ready(me, taunt, tick);
        var tauntInRange = tauntReady && Casting.RangeMm(me, loose!) <= abilities[Taunt].Range;
        if (tauntInRange)
        {
            into.Add(new CastCommand(tick, me.Id, Taunt, loose!.Id));
        }

        // R-TAUNT-2b (ruling on #573): while Taunt is cooling down, he walks toward an enemy damaging a non-tank seat outside
        // Taunt's range, so the Taunt has that enemy in reach when it comes ready (an aggro-range pull lets a Choir melee chase
        // the Cinder away from him; he kept hitting his own target and the Cinder died before the Taunt reached it).
        var tauntCooling = abilities.TryGetValue(Taunt, out var tauntDef) && !Ready(me, tauntDef, tick);
        var chaser = tauntCooling ? DamagingSeatOutOfReach(state, me, tauntDef!) : null;
        if (tauntReady && !tauntInRange)
        {
            into.Add(Toward(tick, me, loose!));
        }
        else if (chaser is not null)
        {
            into.Add(Toward(tick, me, chaser));
        }
        else if (lieutenant is not null && Casting.RangeMm(me, lieutenant) > ApproachMm)
        {
            into.Add(Toward(tick, me, lieutenant));
        }
        else if (lieutenant is null && NearestEnemy(state, me) is { } foe)
        {
            // Lane WARDEN-ENGAGES (KF2b): no lieutenant left to kick, so fight the pull until it is clear. Target the
            // nearest living enemy (lowest id on a tie) so Strike and Reckoning go at it, walk into melee range, and
            // once there turn to face it so Guard Break's cone lands (a zero-length move only sets the yaw).
            if (me.TargetId != foe.Id)
            {
                into.Add(new TargetCommand(tick, me.Id, foe.Id));
            }
            into.Add(Casting.RangeMm(me, foe) > ApproachMm
                ? Toward(tick, me, foe)
                : new MoveCommand(tick, me.Id, new Dir2(0, 0), Fixed.YawBetween(me.Pos, foe.Pos)));
        }

        // R-307-C item 2 (HOLDER-LOCK D30): an enemy another living player holds is taunted back. PILE-INT merge: #326's
        // LOOSE-PICKUP above taunts an enemy aimed at another seat; this holder check runs only when that one did not
        // already queue the Taunt this tick, so the two rules never press it twice.
        if (!tauntInRange && LooseEnemy(state, me, abilities[Taunt], tick) is { } held)
        {
            into.Add(new CastCommand(tick, me.Id, Taunt, held.Id));
        }

        if (!Cooldowns.IsGcdReady(me, tick))
        {
            return;
        }
        // R-PATROL-3: Brace is held for the moment two living enemies are actually hitting the Warden (two trash mobs, the
        // WoW tank's defensive window), not spent on a lone mob during the Choir and left on cooldown for the patrol.
        if (me.Health * 2 < me.MaxHealth && Ready(me, abilities[Defensive], tick) && AttackersOn(state, me) >= 2)
        {
            into.Add(new CastCommand(tick, me.Id, Defensive, me.Id));
            return;
        }
        // R-WARDEN-FILLER-3 (BIBLE s9 "The Lock"; LockAnswer is the bots' half): while the boss casts or holds a Lock no
        // partner Setup has tagged yet, the Warden spends no global, so his Payoff is off the GCD when the setter's Setup
        // lands; once it has, Guard Break goes first. Without the hold a Forge 8 ticks before the setter's Setup put the
        // Payoff 2 ticks past the Window's end (lock_failed at 5280, Forge at 5282).
        var lockSetup = LockSetupBy(run);
        if (lockSetup == LockHold)
        {
            return;
        }
        var order = lockSetup is { } setter && setter != me.Id ? LockPayoffFirst : Rotation;
        foreach (var id in order)
        {
            var def = abilities[id];
            if (!Ready(me, def, tick))
            {
                continue;
            }
            var target = CastTargeting.For(run, me.Id, id);
            // R-WARDEN-FILLER-3: the filler gate is a trash-pull rule; in the boss pull Guard Break keeps main's priority.
            if (id == PayoffBuilder && !InBossPull(run) && !PayoffCloses(run, me, def, target, tick))
            {
                continue;
            }
            if (def.Shape.Kind == ShapeKind.Self || (state.Actors.TryGetValue(target, out var t) && t.Id != me.Id && Casting.RangeMm(me, t) <= def.Range))
            {
                into.Add(new CastCommand(tick, me.Id, id, target));
                return;
            }
        }
    }

    /// <summary>R-TAUNT-2b: an enemy this close to the seat it is aimed at is damaging that seat now (melee reach), mm.</summary>
    internal const long OnSeatMm = 3000;

    /// <summary>
    /// R-TAUNT-2b (ruling on #573): the nearest living engaged enemy outside Taunt's range that is damaging a non-tank seat
    /// now: aimed at it (its holder, else its target) and in melee reach of it, or casting at it. Null when there is none.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="me">The Warden.</param>
    /// <param name="taunt">Taunt.</param>
    /// <returns>The enemy to walk toward, or null.</returns>
    internal static ActorState? DamagingSeatOutOfReach(SimState state, ActorState me, AbilityDef taunt)
    {
        bool Damaging(ActorState e) =>
            (e.HolderId ?? e.TargetId) is { } id && id != me.Id && state.Actors.TryGetValue(id, out var seat) && seat.Alive &&
            seat.Kind == ActorKind.Player && seat.Role != ActorRole.Tank &&
            ((e.CastId is not null && (e.CastTargetId ?? e.TargetId) == seat.Id) || Casting.RangeMm(e, seat) <= OnSeatMm);
        return state.Actors.Values
            .Where(e => e.Kind == ActorKind.Enemy && e.Alive && !e.Idle && Casting.RangeMm(me, e) > taunt.Range && Damaging(e))
            .OrderBy(e => Casting.RangeMm(me, e))
            .ThenBy(e => e.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// HOLDER-LOCK (STATE.md D30): the nearest living engaged enemy in Taunt range whose locked holder is another living
    /// player (lowest id on a tie), when Taunt is ready; else null. Only a taunt moves a holder, so this is the one way
    /// the Warden takes back an enemy a party member body-pulled (the bot tank's rule, TankEngage.PickLooseEnemy).
    /// </summary>
    internal static ActorState? LooseEnemy(SimState state, ActorState me, AbilityDef taunt, int tick)
    {
        if (!Ready(me, taunt, tick))
        {
            return null;
        }
        return state.Actors.Values
            .Where(e => e.Kind == ActorKind.Enemy && e.Alive && !e.Idle
                && e.HolderId is { } h && h != me.Id
                && state.Actors.TryGetValue(h, out var holder) && holder.Alive && holder.Kind == ActorKind.Player
                && Casting.RangeMm(me, e) <= taunt.Range)
            .OrderBy(e => Casting.RangeMm(me, e))
            .ThenBy(e => e.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// WARDEN-FILLER (finding f-95d23cfc; the core loop defers to WoW Mythic+: a tank's filler fires every global nothing
    /// better is ready for): Guard Break has no cooldown and no cost, so pressed whenever ready it took every global from
    /// Strike (2 Strikes in a 166 s KF1 run). It is a Payoff (BIBLE section 5), so it is the better button only when its
    /// target carries an open Window the Warden may close for a Combo: another class's Window (the Warden's own Sundered is
    /// a self_combo denial), and for a Lock Window not the class that closed the last one (Payoff.TryClosePayoff's rule).
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="me">The Warden.</param>
    /// <param name="def">Guard Break as loaded (a talent may patch its shape).</param>
    /// <param name="targetId">Guard Break's cast target.</param>
    /// <param name="tick">The pre-step tick.</param>
    /// <returns>True when the Payoff would close a Window and fire a Combo.</returns>
    /// <summary>
    /// R-WARDEN-FILLER-3 (supersedes the withdrawn R-WARDEN-FILLER-2): true while the armed boss
    /// (<c>run.Encounter.Boss</c>) is alive and engaged. The WARDEN-FILLER gate on Guard Break applies to trash pulls
    /// only; in the boss pull the Warden presses Guard Break whenever ready, as on main: Strike-as-filler there opened a
    /// Sundered Window every global, and the Consecrate heals masked a careless seat (BossTeachesTests).
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>True in the boss pull.</returns>
    internal static bool InBossPull(Run run) =>
        run.Encounter.Boss is { } boss
        && run.State.Actors.TryGetValue(boss.Config.BossActorId, out var b) && b.Alive && !b.Idle;

    /// <summary><see cref="LockSetupBy"/>'s "hold the global" answer: a Lock is cast or open and no Setup has tagged it.</summary>
    private const int LockHold = -1;

    /// <summary>The rotation while a partner's Setup is on the boss inside a Lock: the Payoff first.</summary>
    private static readonly string[] LockPayoffFirst = { "warden.guardBreak", "warden.reckoning", "warden.vigil", "warden.strike" };

    /// <summary>
    /// The boss Lock's state for the Warden's global: null when no Lock is being cast or held open (or outside the boss
    /// pull), <see cref="LockHold"/> while the Lock is cast or open with no Setup inside its Window yet, else the actor
    /// whose Setup tagged the boss inside the Window (Encounter/Boss.cs AfterCombat records it).
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The Lock state as above.</returns>
    internal static int? LockSetupBy(Run run)
    {
        if (!InBossPull(run) || run.Encounter.Boss is not { } boss)
        {
            return null;
        }
        if (boss.Active is { } open && run.State.Tick < open.EndsTick)
        {
            return open.SetupActorId ?? LockHold;
        }
        return boss.Casting is not null ? LockHold : null;
    }

    internal static bool PayoffCloses(Run run, ActorState me, AbilityDef def, int targetId, int tick)
    {
        // The Payoff closes the Window on its primary target only: a single-target patch (Focused Break) goes at the cast
        // target, the cone at the lowest-id hostile inside it (Targeting.ResolveTargets, Windows.ApplyWindowsAndCombosTo).
        var target = def.Shape.Kind == ShapeKind.Target
            ? (run.State.Actors.TryGetValue(targetId, out var t) && t.Id != me.Id && Casting.RangeMm(me, t) <= def.Range ? t : null)
            : Targeting.ResolveTargets(run.State, me, def, Affinities.Of(def, run.Content.Effects)).FirstOrDefault();
        if (target is null || !target.Alive
            || WindowState.FindOpenWindow(target, tick) is not { } window
            || WindowState.ParseWindowEffectId(window.EffectId) is not { } parsed)
        {
            return false;
        }
        var denied = parsed.Alternate ? WindowState.LastSetupClassOf(target) : parsed.SetupClass;
        return denied != me.ClassId && run.Content.Combos?.Lookup(parsed.SetupClass, me.ClassId) is not null;
    }

    private static bool Ready(ActorState me, AbilityDef def, int tick) =>
        Cooldowns.IsAbilityReady(me, def, tick) && (def.Cost.Resource == "none" || me.Resource >= def.Cost.Amount);

    /// <summary>A full-speed move from <paramref name="me"/> straight at <paramref name="to"/>, facing it.</summary>
    private static MoveCommand Toward(int tick, ActorState me, ActorState to)
    {
        var dx = to.Pos.X - me.Pos.X;
        var dz = to.Pos.Z - me.Pos.Z;
        var len = Movement.Isqrt((dx * dx) + (dz * dz));
        var dir = new Dir2(IntMath.TruncDiv(dx * 1000, len), IntMath.TruncDiv(dz * 1000, len));
        return new MoveCommand(tick, me.Id, dir, Atan.YawFromComponents(dir.X, dir.Z));
    }

    /// <summary>
    /// The living ENGAGED enemy nearest <paramref name="me"/> (squared XZ distance, lowest id on a tie), or null. An idle
    /// prespawned enemy is never picked (PRESPAWN-AGGRO; <see cref="EngagedTargets.Nearest"/>).
    /// </summary>
    private static ActorState? NearestEnemy(SimState state, ActorState me) => EngagedTargets.Nearest(state, me);

    /// <summary>R-PATROL-3: the living engaged enemies whose target is <paramref name="me"/> (an idle prespawned enemy never counts).</summary>
    private static int AttackersOn(SimState state, ActorState me) =>
        state.Actors.Values.Count(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle && a.TargetId == me.Id);

    /// <summary>
    /// R-WARDEN-KICK-1: the engaged lieutenant mid-way through an interruptible cast, inside Shield Bash range at
    /// <paramref name="tick"/>, whose cast ends soonest (lowest id on a tie); null when none is kickable.
    /// </summary>
    private static ActorState? KickableLieutenant(SimState state, ActorState me, int tick, IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        ActorState? best = null;
        long bestEnd = long.MaxValue;
        foreach (var a in state.Actors.Values)
        {
            if (a.ClassId != LieutenantClass || !EngagedTargets.IsEngagedHostile(me, a) || a.CastId is not { } castId
                || a.CastStartTick is not { } start || !abilities.TryGetValue(castId, out var cast) || !cast.Interruptible
                || tick < start + (cast.CastTicks / 2) || Casting.RangeMm(me, a) > abilities[Kick].Range)
            {
                continue;
            }
            var end = a.CastEndsTick ?? long.MaxValue;
            if (best is null || end < bestEnd || (end == bestEnd && a.Id < best.Id))
            {
                best = a;
                bestEnd = end;
            }
        }
        return best;
    }
}
