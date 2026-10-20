using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>What the runner tells every policy seat about the run each tick (the bot view has no encounter).</summary>
public sealed class PolicyContext
{
    /// <summary>The active pull's policy id: "choir", "pair", "seed" or "boss".</summary>
    public string PullId { get; set; } = "choir";
}

/// <summary>
/// One seat played to a playtester's <see cref="Policy"/> with human hands (<see cref="Hands"/>). The order mirrors the
/// scripted runner (Keyrun.Bots Bot.cs): dodge, travel, then the seat's own decisions, but every skill a player would
/// have to know is gated by the policy, never filled in by a script:
/// <list type="bullet">
/// <item>Kicks: only the casts a <see cref="KickRule"/> names, by the first kicker in its order whose interrupt is ready
/// and in range, a seeded 0.4-1.0 s after the cast bar appears, with a seeded chance the press is late. The scripted
/// kick ledger is switched off.</item>
/// <item>Dodging: telegraphs and floor patches only when <see cref="Policy.DodgeTelegraphs"/> /
/// <see cref="Policy.DodgeFloor"/> say so, after a reaction delay.</item>
/// <item>Defensive (E): under the policy's health threshold, or, for a <see cref="Policy.Mitigate"/> class, whenever its
/// script would (the scripted tank's pressure rule, which then also reaches its group mitigation). Utility (R): only for
/// listed classes.</item>
/// <item>Targets: damage dealers attack their target (else the tank's), or the policy's focus list first.</item>
/// <item>Holds: an ability held for other pulls is never pressed in this one.</item>
/// </list>
/// The rotation itself (which of the five to press next, Windows and Payoffs) is the scripted role's, pressed with the
/// same hands. Stateful per seat; one instance per seat per run.
/// </summary>
public sealed class PolicyBot : IBot
{
    private readonly Policy _policy;
    private readonly PolicyContext _context;
    private readonly C.ContentRegistry _registry;
    private readonly Hands _hands;
    private readonly int _tankId;
    private int _nextActionTick;
    private int? _dodgeAt;
    private int? _defensiveAt;
    private readonly Dictionary<(int EnemyId, int CastStart), int> _kickAt = new();

    /// <summary>Creates the seat.</summary>
    /// <param name="policy">The plan.</param>
    /// <param name="context">The shared per-tick run facts.</param>
    /// <param name="registry">The content (ability ranges, enemy ids).</param>
    /// <param name="seed">The run seed (the hands' seed).</param>
    /// <param name="actorId">The seat.</param>
    /// <param name="tankId">The tank seat (damage dealers assist its target).</param>
    public PolicyBot(Policy policy, PolicyContext context, C.ContentRegistry registry, long seed, int actorId, int tankId)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hands = new Hands(seed, actorId);
        _tankId = tankId;
    }

    /// <summary>The dodge move this seat issued on its last decision, or null (the runner keeps it for the leader).</summary>
    public MoveCommand? LastDodge { get; private set; }

    /// <summary>Kick presses issued (each a CastCommand of the seat's interrupt).</summary>
    public int KickPresses { get; private set; }

    /// <summary>Kick presses that came late (a fumble roll).</summary>
    public int LateKicks { get; private set; }

    /// <summary>Ticks this seat walked out of a danger telegraph (dodge plan only).</summary>
    public int DodgeMoves { get; private set; }

    /// <inheritdoc />
    public BotDecision Decide(BotView view, BotMemory memory, int actorId, BotPrngState prng, IReadOnlyList<PingEvent>? pings = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        LastDodge = null;
        if (!view.State.Actors.TryGetValue(actorId, out var self) || !self.Alive)
        {
            return new BotDecision(Array.Empty<Command>(), memory, prng);
        }
        var tick = view.State.Tick;
        var commands = new List<Command>();

        // 1. Telegraph dodge, only by plan, after a reaction.
        var avoid = _policy.DodgeTelegraphs ? DangerAvoid(view, self) : null;
        if (avoid is null)
        {
            _dodgeAt = null;
        }
        else
        {
            _dodgeAt ??= tick + _hands.React();
            if (tick >= _dodgeAt)
            {
                commands.Add(avoid);
                LastDodge = avoid;
                DodgeMoves++;
            }
        }

        // 2. Travel between rooms, as the scripted runner does (walking the route is not a skill the plan names).
        var follow = LastDodge is null ? Follow.FollowCommand(view, self) : null;
        var travelling = follow is not null;
        if (follow is not null)
        {
            commands.Add(follow);
            if (!Follow.AnyLivingEnemy(view.State))
            {
                return new BotDecision(commands, memory, prng);
            }
        }
        if (!Follow.AnyLivingEnemy(view.State))
        {
            _kickAt.Clear();
        }

        var kit = view.Kits.ResolveKit(self.ClassId, view.ClassKit);

        // 3. Kicks by the plan.
        if (KickCommand(view, self, kit, tick) is { } kick)
        {
            commands.Add(kick);
            return new BotDecision(commands, memory, prng);
        }

        // 4. Defensive under the plan's threshold.
        if (DefensiveCommand(self, kit, tick) is { } defensive)
        {
            commands.Add(defensive);
            return new BotDecision(commands, memory, prng);
        }

        // 5. The rotation, pressed with human hands.
        if (tick < _nextActionTick)
        {
            return new BotDecision(commands, memory, prng);
        }
        _nextActionTick = tick + _hands.React();
        if (_hands.Roll(Hands.MissPermille))
        {
            return new BotDecision(commands, memory, prng);
        }
        if (RoleSet.Default.For(self.Role) is not { } role)
        {
            return new BotDecision(commands, memory, prng);
        }
        var desired = self.Id == _tankId ? self.TargetId : DesiredTarget(view, self);
        var inFloor = Floor.FloorEscapeCommand(view, self) is not null;
        var kept = new List<Command>();
        var droppedCast = false;
        foreach (var c in role.Decide(view, self))
        {
            switch (c)
            {
                case CastCommand cast when !Allowed(cast, self, kit, view, desired):
                    droppedCast = true;
                    break;
                case MoveCommand or DashCommand when travelling:
                    break;
                case MoveCommand when inFloor && !_policy.DodgeFloor:
                    break;
                case TargetCommand t when self.Id != _tankId && t.TargetId is { } tid && IsEnemy(view, tid) && tid != desired:
                    break;
                default:
                    kept.Add(c);
                    break;
            }
        }
        if (droppedCast && !kept.Any(c => c is CastCommand) && self.Role != ActorRole.Healer && Fallback(view, self, kit, desired, tick) is { } fallback)
        {
            kept.Add(fallback);
        }
        if (self.Id != _tankId && desired is { } want && self.TargetId != want && !kept.Any(c => c is TargetCommand))
        {
            kept.Insert(0, new TargetCommand(tick, self.Id, want));
        }
        commands.AddRange(kept);
        return new BotDecision(commands, memory, prng);
    }

    private static bool IsEnemy(BotView view, int id) =>
        view.State.Actors.TryGetValue(id, out var a) && a.Kind == ActorKind.Enemy;

    // bot.ts dangerAvoidCommand, gated by the plan.
    private static MoveCommand? DangerAvoid(BotView view, ActorState self)
    {
        var tick = view.State.Tick;
        var selfMm = PointMm.Of(self.Pos);
        var active = view.Telegraphs.Where(t => t.Danger && t.ResolvesTick > tick && !Positioning.IsSpent(t, view.State)).ToList();
        if (!Positioning.IsInAnyDanger(selfMm, active))
        {
            return null;
        }
        var safe = Positioning.NearestSafePoint(selfMm, active);
        return safe is null ? null : Moves.MoveToward(tick, self.Id, selfMm, safe.Value);
    }

    private bool HeldHere(string abilityId) =>
        _policy.Hold.TryGetValue(abilityId, out var pulls) && !pulls.Contains(_context.PullId, StringComparer.Ordinal);

    private bool Allowed(CastCommand cast, ActorState self, Keyrun.Bots.EffectiveKit kit, BotView view, int? desired)
    {
        var id = cast.AbilityId;
        if (string.Equals(id, kit.InterruptAbilityId, StringComparison.Ordinal))
        {
            return false;
        }
        // STATE.md D48: a mitigating seat presses E when its script would; any other seat only by its plan threshold.
        if (string.Equals(id, kit.DefensiveAbilityId, StringComparison.Ordinal) && !_policy.Mitigate.Contains(self.ClassId, StringComparer.Ordinal))
        {
            return false;
        }
        var utility = _registry.Classes.TryGetValue(self.ClassId, out var cls) ? cls.Abilities.Utility : null;
        if (string.Equals(id, utility, StringComparison.Ordinal) && !_policy.Utility.Contains(self.ClassId, StringComparer.Ordinal))
        {
            return false;
        }
        if (HeldHere(id))
        {
            return false;
        }
        if (self.Id != _tankId && cast.TargetId is { } tid && IsEnemy(view, tid) && desired is { } want && tid != want)
        {
            return false;
        }
        return true;
    }

    private Command? Fallback(BotView view, ActorState self, Keyrun.Bots.EffectiveKit kit, int? target, int tick)
    {
        if (target is null)
        {
            return null;
        }
        var pick = Priority.PickRotationAbility(kit.Rotation, id =>
            !HeldHere(id) && Keyrun.Bots.Roles.RoleShared.ReadyAndAffordable(self, id, tick, kit));
        return pick is null ? null : new CastCommand(tick, self.Id, pick, target);
    }

    // The damage dealer's target: the first living enemy of the focus list (lowest id), else its own living target,
    // else the tank's living target, else none.
    private int? DesiredTarget(BotView view, ActorState self)
    {
        var actors = view.State.Actors;
        foreach (var enemyId in _policy.Focus)
        {
            var hit = actors.Values.FirstOrDefault(a => a.Kind == ActorKind.Enemy && a.Alive && string.Equals(a.ClassId, enemyId, StringComparison.Ordinal));
            if (hit is not null)
            {
                return hit.Id;
            }
        }
        if (self.TargetId is { } own && actors.TryGetValue(own, out var t) && t.Alive && t.Kind == ActorKind.Enemy)
        {
            return own;
        }
        if (actors.TryGetValue(_tankId, out var tank) && tank.TargetId is { } tt && actors.TryGetValue(tt, out var tankTarget) && tankTarget.Alive && tankTarget.Kind == ActorKind.Enemy)
        {
            return tt;
        }
        return null;
    }

    private long RangeOf(string abilityId) => _registry.Abilities.TryGetValue(abilityId, out var a) ? a.Range : 0;

    // The kicker for a cast: the first class in the rule's order whose seat is alive with its interrupt ready and in range.
    private int? KickerFor(BotView view, ActorState enemy, IReadOnlyList<string> order)
    {
        var tick = view.State.Tick;
        foreach (var classId in order)
        {
            var seat = view.State.Actors.Values.FirstOrDefault(a => a.Kind == ActorKind.Player && a.Alive && string.Equals(a.ClassId, classId, StringComparison.Ordinal));
            if (seat is null)
            {
                continue;
            }
            var interrupt = view.Kits.ResolveKit(seat.ClassId, view.ClassKit).InterruptAbilityId;
            if (string.IsNullOrEmpty(interrupt) || !Readiness.AbilityReady(seat, interrupt, tick))
            {
                continue;
            }
            var range = RangeOf(interrupt);
            var dx = seat.Pos.X - enemy.Pos.X;
            var dz = seat.Pos.Z - enemy.Pos.Z;
            if ((dx * dx) + (dz * dz) > range * range)
            {
                continue;
            }
            return seat.Id;
        }
        return null;
    }

    private CastCommand? KickCommand(BotView view, ActorState self, Keyrun.Bots.EffectiveKit kit, int tick)
    {
        var interrupt = kit.InterruptAbilityId;
        if (string.IsNullOrEmpty(interrupt) || _policy.Kicks.Count == 0)
        {
            return null;
        }
        var casting = view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive && a.CastId is not null && a.CastStartTick is not null)
            .OrderBy(a => a.CastEndsTick ?? int.MaxValue)
            .ThenBy(a => a.Id);
        foreach (var enemy in casting)
        {
            var order = _policy.KickersFor(enemy.CastId!);
            if (order is null || KickerFor(view, enemy, order) != self.Id)
            {
                continue;
            }
            var key = (enemy.Id, enemy.CastStartTick!.Value);
            if (!_kickAt.TryGetValue(key, out var at))
            {
                at = Math.Max(tick, enemy.CastStartTick.Value) + _hands.KickReact();
                _kickAt[key] = at;
            }
            if (tick < at)
            {
                return null;
            }
            if (_hands.Roll(Hands.KickMissPermille))
            {
                LateKicks++;
                _kickAt[key] = tick + _hands.KickReact();
                return null;
            }
            KickPresses++;
            _kickAt[key] = tick + 1;
            return new CastCommand(tick, self.Id, interrupt, enemy.Id);
        }
        return null;
    }

    private CastCommand? DefensiveCommand(ActorState self, Keyrun.Bots.EffectiveKit kit, int tick)
    {
        var defensive = kit.DefensiveAbilityId;
        if (string.IsNullOrEmpty(defensive) || !_policy.Defensive.TryGetValue(self.ClassId, out var pct) ||
            self.MaxHealth <= 0 || self.Health * 100 > pct * self.MaxHealth || !Readiness.AbilityReady(self, defensive, tick))
        {
            _defensiveAt = null;
            return null;
        }
        _defensiveAt ??= tick + _hands.React();
        if (tick < _defensiveAt)
        {
            return null;
        }
        _defensiveAt = null;
        return new CastCommand(tick, self.Id, defensive);
    }
}
