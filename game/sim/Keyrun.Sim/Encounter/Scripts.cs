using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>One call's output (scripts.ts:118 StepEnemyScriptsResult).</summary>
/// <param name="Commands">Casts (then chase moves) for the caller to feed to combat this tick.</param>
/// <param name="Draws">TELEGRAPH-DRAW: the telegraphs drawn this tick, in emission order.</param>
/// <param name="FiredOnDeath">The fire-once map carried forward plus this call's onDeath casts; pass it back next call.</param>
public sealed record StepEnemyScriptsResult(IReadOnlyList<Command> Commands, IReadOnlyList<TelegraphDraw> Draws, IReadOnlyDictionary<int, IReadOnlyList<string>> FiredOnDeath)
{
    /// <summary>The "telegraph" events announced this tick: a projection of <see cref="Draws"/>, in the same order.</summary>
    public IReadOnlyList<SimEvent> TelegraphEvents => Draws.Select(d => d.ToEvent()).ToList();

    /// <summary>
    /// TELEGRAPH-CANCEL: for each of <see cref="Draws"/>, in the same order, the tick its cast is due to start when the
    /// telegraph spans that cast (<see cref="TelegraphSpec.SpansCast"/>), else null: a projection of
    /// <see cref="TelegraphDraw.CastStartTick"/>.
    /// </summary>
    public IReadOnlyList<int?> TelegraphCastStarts => Draws.Select(d => d.CastStartTick).ToList();

    /// <summary>
    /// CAST-FINISH: actor id to the steps still waiting for the actor's cast to land, in firing order; pass it back
    /// next call (<see cref="EncounterState.PendingSteps"/>). Empty = none waiting.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> PendingSteps { get; init; } = new SortedDictionary<int, IReadOnlyList<int>>();

    /// <summary>
    /// STARTCAST-BUSY: frozen-TS replay steps still held for a busy caster (<see cref="CastDeferral"/>); pass it back
    /// next call (<see cref="EncounterState.DeferredCasts"/>). Empty = none held.
    /// </summary>
    public IReadOnlyList<DeferredCast> DeferredCasts { get; init; } = Array.Empty<DeferredCast>();
}

/// <summary>
/// Enemy behaviour as data-driven state machines (sim/encounter/scripts.ts). Each tick, for every scripted actor in
/// ascending id order and each step in script order: a living actor casts when the step fires on this tick, and
/// announces the step's telegraph <c>leadTicks</c> ahead, with origin and yaw snapshotted from the caster now and
/// <c>resolvesTick</c> on the cast tick (a <see cref="TelegraphSpec.SpansCast"/> telegraph: on the tick the cast lands,
/// lead at least the cast time, and only when <see cref="WillStart"/>). An onDeath step fires once, the first call that sees its actor dead (one
/// tick after the death event), with its telegraph on that same tick; the fire-once guarantee rides on
/// <see cref="StepEnemyScriptsResult.FiredOnDeath"/>. Chase moves follow the casts.
/// </summary>
public static class Scripts
{
    /// <summary>
    /// scripts.ts:86 firesAt: a loop fires at every positive multiple of <c>every</c> (never tick 0, never with every
    /// 0 or absent); a numeric <c>at</c> with a positive every fires at <c>at + k * every</c>, otherwise only at
    /// <c>at</c>; onDeath never fires on schedule. Ticks are non-negative, so <c>%</c> matches JS (ORACLE.md s8).
    /// </summary>
    public static bool FiresAt(ScriptStep step, int tick)
    {
        switch (step.Trigger)
        {
            case StepTrigger.Loop:
                var every = step.Every ?? 0;
                return every > 0 && tick > 0 && tick % every == 0;
            case StepTrigger.OnDeath:
                return false;
            default:
                if (step.Every is { } e && e > 0)
                {
                    return tick >= step.AtTick && (tick - step.AtTick) % e == 0;
                }
                return tick == step.AtTick;
        }
    }

    /// <summary>
    /// SEED-PULL-TEACHES (C#-only, layout runs; STATE.md D27): <paramref name="step"/> on a clock that starts at
    /// <paramref name="originTick"/>, the tick its pull started. A loop of period E fires at origin + k*E (k &gt;= 1),
    /// a numeric <c>at</c> at origin + at (+ k*every); onDeath is unchanged, and origin 0 returns the step itself, so a
    /// run whose pulls start at tick 0 schedules exactly as <see cref="FiresAt"/> always has. Without it a room-entry
    /// pull runs on the run clock, and an elite could start its first slam a few ticks after it appeared, with that
    /// slam's telegraph due before the elite existed.
    /// </summary>
    /// <param name="step">The content step (already Tempo-scaled).</param>
    /// <param name="originTick">The pull's pull_start tick.</param>
    /// <returns>The step scheduled from <paramref name="originTick"/>.</returns>
    public static ScriptStep StartingAt(ScriptStep step, int originTick)
    {
        System.ArgumentNullException.ThrowIfNull(step);
        if (originTick == 0)
        {
            return step;
        }
        return step.Trigger switch
        {
            StepTrigger.Loop when step.Every is { } every && every > 0 => step with { Trigger = StepTrigger.AtTick, AtTick = originTick + every },
            StepTrigger.AtTick => step with { AtTick = originTick + step.AtTick },
            _ => step,
        };
    }

    /// <summary>
    /// SWING-PHASE (C#-only, layout runs; ruling R-307-C item 3): is <paramref name="step"/> an auto-attack? A repeating
    /// step (a loop, or a numeric <c>at</c> with a positive every) with no telegraph whose ability is an instant
    /// (castTicks 0), target-shaped hit carrying a damage op: the enemy's swing, with no cast bar and no floor shape.
    /// </summary>
    /// <param name="step">The script step.</param>
    /// <param name="ability">Its ability, or null when the content has none.</param>
    /// <returns>True for an auto-attack.</returns>
    public static bool IsAutoAttack(ScriptStep step, AbilityDef? ability)
    {
        System.ArgumentNullException.ThrowIfNull(step);
        return ability is not null
            && step.Trigger != StepTrigger.OnDeath
            && step.Every is > 0
            && step.Telegraph is null
            && ability.CastTicks == 0
            && ability.Shape.Kind == ShapeKind.Target
            && ability.Effects.Any(op => op is DamageOp);
    }

    /// <summary>
    /// SWING-PHASE (C#-only, layout runs; ruling R-307-C item 3, BIBLE section 3 "cast bars are the enemy's language"):
    /// member <paramref name="member"/> (0-based, ascending actor id) of an engaged group of <paramref name="members"/>
    /// scripted enemies swings its auto-attack (<see cref="IsAutoAttack"/>) <c>member * every / members</c> ticks after
    /// the group's schedule, so the group's swings are spread over the swing period instead of landing as one
    /// unannounced burst on one tick; the period is unchanged and member 0 keeps the content schedule. In WoW each
    /// enemy's swing timer runs on its own from its own engage, so a pack's auto-attacks never land in lockstep. A step
    /// that is not an auto-attack, a group of one, and member 0 return the step unchanged. A loop step (only on a group
    /// engaged at tick 0, before <see cref="StartingAt"/> has moved it to a numeric <c>at</c>) becomes the numeric step
    /// with the same firing ticks plus the phase.
    /// </summary>
    /// <param name="step">The step, already on its group's engage clock (<see cref="StartingAt"/>).</param>
    /// <param name="ability">Its ability, or null.</param>
    /// <param name="member">The member's index in its group.</param>
    /// <param name="members">How many scripted members the group has.</param>
    /// <returns>The phased step.</returns>
    public static ScriptStep SwingPhased(ScriptStep step, AbilityDef? ability, int member, int members)
    {
        System.ArgumentNullException.ThrowIfNull(step);
        if (member <= 0 || members <= 1 || !IsAutoAttack(step, ability))
        {
            return step;
        }
        var every = step.Every!.Value;
        var phase = IntMath.FloorDiv((long)member * every, members);
        var at = step.Trigger == StepTrigger.Loop ? every : step.AtTick;
        return step with { Trigger = StepTrigger.AtTick, AtTick = (int)(at + phase) };
    }

    // scripts.ts:126 castCommand: null when the target resolution blocks it.
    private static CastCommand? CastCommandFor(SimState state, int actorId, ActorState actor, ScriptStep step, IReadOnlyDictionary<string, ScriptAbility> abilities)
    {
        abilities.TryGetValue(step.Cast, out var ability);
        var (targetId, blocked) = ScriptTarget.Resolve(state, actor, step.Target, ability);
        // LOCK-KEEPS-TARGET: a self-shaped cast lands on its caster whatever the command's target, and StartCast sets
        // the caster's target from the command, so naming the caster turned a boss casting its Lock onto itself (off
        // its tank, off TankPressure's attacker count). In WoW a boss casting on itself keeps its threat target. A
        // frozen-TS replay (TsCastReplace) keeps the TS rule its oracle trace recorded.
        if (!step.TsCastReplace && ability?.ShapeKind == "self" && targetId == actorId)
        {
            targetId = null;
        }
        return blocked ? null : new CastCommand(state.Tick, actorId, step.Cast, targetId);
    }

    private static readonly IReadOnlyDictionary<string, ScriptAbility> NoAbilities = new Dictionary<string, ScriptAbility>();

    /// <summary>
    /// SLAM-TELEGRAPH (wrong data shown is P0): would the step's cast, fired on <paramref name="fireTick"/>, start? Its
    /// target must resolve (<see cref="CastCommandFor"/>) and <see cref="Casting.StartError"/>, the checks StartCast
    /// itself makes, must pass on that tick (cooldown and charges, GCD, cost, school lockout, range). Enemies are never
    /// silenced (Rules hands them no Clause), so no silence check. An ability with no definition in the view passes.
    /// Judged on the tick's pre-combat state: the cast command this tick is the next thing combat runs.
    /// </summary>
    private static bool WillStart(SimState state, int actorId, ActorState actor, ScriptStep step, ScriptAbility ability, IReadOnlyDictionary<string, ScriptAbility> abilities, int fireTick)
    {
        if (CastCommandFor(state, actorId, actor, step, abilities) is not { } cmd)
        {
            return false;
        }
        return ability.Def is not { } def || Casting.StartError(state, actor, def, cmd.TargetId, fireTick) is null;
    }

    /// <summary>
    /// TELEGRAPH-HONEST (BIBLE s3; wrong data shown is P0): the index of the script step whose cast the actor is left
    /// holding once combat has run every scheduled cast due on <paramref name="fireTick"/>, or -1 when none starts.
    /// STARTCAST-BUSY (#325): StartCast refuses a busy caster and <see cref="CastDeferral"/> holds a step that fires while
    /// its caster is busy, so nothing due on <paramref name="fireTick"/> starts before <paramref name="freeTick"/> (the
    /// first tick the caster is free of the cast in flight and of the casts already held for it), and the first step
    /// whose cast stays in flight (castTicks &gt; 0) claims the caster: every later step that tick is held, not started.
    /// An earlier step that starts a GCD (gcdTicks &gt; 0) refuses every later one, and a second step of an ability
    /// already started that tick is refused when that took its only charge. Each step's own start is
    /// <see cref="WillStart"/>; a step with no ability view starts whenever its target resolves. Without this a
    /// spansCast telegraph was drawn for every step that could start on its own, and on a shared tick every one but the
    /// cast the actor kept announced a cast that never came.
    /// </summary>
    private static int CastHeldAt(SimState state, int actorId, ActorState actor, IReadOnlyList<ScriptStep> script, IReadOnlyDictionary<string, ScriptAbility> abilities, int fireTick, int freeTick)
    {
        if (fireTick < freeTick)
        {
            return -1;
        }
        var held = -1;
        var gcdStarted = false;
        var started = new HashSet<string>(StringComparer.Ordinal);
        for (var j = 0; j < script.Count && !gcdStarted; j++)
        {
            var s = script[j];
            if (s.Trigger == StepTrigger.OnDeath || !FiresAt(s, fireTick))
            {
                continue;
            }
            var def = abilities.TryGetValue(s.Cast, out var ability) ? ability.Def : null;
            if (def is not null && started.Contains(s.Cast) && def.CooldownTicks > 0 && def.Charges <= 1)
            {
                continue;
            }
            var starts = ability is not null
                ? WillStart(state, actorId, actor, s, ability, abilities, fireTick)
                : CastCommandFor(state, actorId, actor, s, abilities) is not null;
            if (starts)
            {
                held = j;
                if (CastDeferral.LeavesCastInFlight(s.Cast, abilities))
                {
                    break;
                }
                started.Add(s.Cast);
                gcdStarted = def is not null && def.GcdTicks > 0;
            }
        }
        return held;
    }

    /// <summary>
    /// CAST-FINISH: one actor's script steps (those not <see cref="ScriptStep.TsCastReplace"/>) played forward under
    /// the Mythic+ rule that an enemy finishes the cast it starts. On each tick the steps waiting from earlier ticks go
    /// first, in the order they came due, then the steps due on that tick in script order; while a cast bar is under way
    /// (it lands after that tick) every one of them waits, and once a step starts a bar the later ones wait for it too.
    /// A bar that lands on a tick does not block it: combat completes due casts before it runs that tick's commands, so
    /// a waiting step fires on the bar's landing tick, the first moment the enemy is free. Kicks and stuns still end a
    /// cast early (the actor is then idle and the waiting steps fire on the next call).
    /// </summary>
    private sealed class CastQueue
    {
        private readonly SimState _state;
        private readonly int _actorId;
        private readonly ActorState _actor;
        private readonly IReadOnlyList<ScriptStep> _script;
        private readonly IReadOnlyDictionary<string, ScriptAbility> _abilities;
        private readonly Dictionary<string, int> _readyAt = new(StringComparer.Ordinal);
        private int _busyUntil;
        private int _gcdUntil;

        public CastQueue(SimState state, int actorId, ActorState actor, IReadOnlyList<ScriptStep> script, IReadOnlyDictionary<string, ScriptAbility> abilities, IReadOnlyList<int>? pending)
        {
            _state = state;
            _actorId = actorId;
            _actor = actor;
            _script = script;
            _abilities = abilities;
            Pending = pending?.Where(j => j >= 0 && j < script.Count).ToList() ?? new List<int>();
            _busyUntil = actor.CastId is not null && actor.CastEndsTick is { } ends && ends > state.Tick ? ends : state.Tick;
        }

        // TEMPO-LOOP-CLAMP: the cast is off cooldown (charges, GCD) on t, judged on this call's state plus the casts this
        // queue has already started ahead of the real clock.
        private bool CooldownAllows(string cast, AbilityDef def, int t) =>
            Cooldowns.IsAbilityReady(_actor, def, t)
            && !(_readyAt.TryGetValue(cast, out var ready) && ready > t)
            && !(def.GcdTicks > 0 && _gcdUntil > t);

        /// <summary>Steps still waiting after the last <see cref="Advance"/>, in firing order.</summary>
        public List<int> Pending { get; private set; }

        /// <summary>
        /// One tick: the steps that fire on <paramref name="t"/>, in order, each with whether its cast is judged to
        /// start (target, cooldown, range: <see cref="WillStart"/> on this call's state, plus the cooldowns and GCD of
        /// the casts this queue has already started ahead of the real clock).
        /// </summary>
        public List<(int Step, bool Starts)> Advance(int t)
        {
            var queue = new List<int>(Pending);
            for (var j = 0; j < _script.Count; j++)
            {
                var s = _script[j];
                if (s.Trigger != StepTrigger.OnDeath && !s.TsCastReplace && FiresAt(s, t) && !queue.Contains(j))
                {
                    queue.Add(j);
                }
            }
            var fired = new List<(int, bool)>();
            var waiting = new List<int>();
            foreach (var j in queue)
            {
                if (_busyUntil > t)
                {
                    waiting.Add(j);
                    continue;
                }
                var s = _script[j];
                _abilities.TryGetValue(s.Cast, out var ability);
                var def = ability?.Def;
                if (def is not null && !CooldownAllows(s.Cast, def, t))
                {
                    // TEMPO-LOOP-CLAMP (f-57c04b4a, BIBLE s0 test 1): a step never fires its cast before the cast's
                    // cooldown allows it. A loop whose period Tempo (the key, or the boss's Lock stacks) shrank below
                    // the cooldown, or a step a busy caster pushed late so that its next fire comes early, waits for
                    // the cooldown instead of issuing a cast combat refuses not_ready and losing a whole period: the
                    // effective interval is max(loop, cooldown).
                    waiting.Add(j);
                    continue;
                }
                var starts = ability is not null
                    ? WillStart(_state, _actorId, _actor, s, ability, _abilities, t)
                        && !(_readyAt.TryGetValue(s.Cast, out var ready) && ready > t)
                        && !(def is not null && def.GcdTicks > 0 && _gcdUntil > t)
                    : CastCommandFor(_state, _actorId, _actor, s, _abilities) is not null;
                fired.Add((j, starts));
                if (!starts)
                {
                    continue;
                }
                if (def is not null && def.CooldownTicks > 0 && def.Charges <= 1)
                {
                    _readyAt[s.Cast] = t + def.CooldownTicks;
                }
                if (def is not null && def.GcdTicks > 0)
                {
                    _gcdUntil = t + def.GcdTicks;
                }
                if (ability is { CastTicks: > 0 })
                {
                    _busyUntil = t + ability.CastTicks;
                }
            }
            Pending = waiting;
            return fired;
        }
    }

    // A spansCast telegraph's cast time (it resolves as the bar lands), else 0 (it resolves as the cast starts).
    private static int SpannedCastTicks(ScriptStep step, IReadOnlyDictionary<string, ScriptAbility> abilities) =>
        step.Telegraph is { SpansCast: true } && abilities.TryGetValue(step.Cast, out var a) && a.CastTicks > 0 ? a.CastTicks : 0;

    // How many ticks before its cast starts a step's telegraph is drawn (SLAM-TELEGRAPH: a spansCast lead is at least
    // the cast time, whatever Tempo did to it).
    private static int StartOffset(ScriptStep step, IReadOnlyDictionary<string, ScriptAbility> abilities)
    {
        var spans = SpannedCastTicks(step, abilities);
        var lead = step.Telegraph?.LeadTicks ?? 0;
        return spans > 0 ? Math.Max(lead, spans) - spans : lead;
    }

    /// <summary>
    /// STARTCAST-BUSY x TELEGRAPH-HONEST: the first tick the caster is free to start a step's cast, judged now: the end
    /// of the cast in flight (rules complete it before commands on its end tick), of a cast-time cast issued for it
    /// earlier this call (<paramref name="issuedUntil"/>), and of each cast-time cast held for it, issued in turn.
    /// </summary>
    private static int FreeTick(int actorId, ActorState actor, int tick, int issuedUntil, IReadOnlyList<DeferredCast> nextHeld, IReadOnlyDictionary<string, ScriptAbility> abilities)
    {
        var free = Math.Max(tick, issuedUntil);
        if (actor.CastId is not null && actor.CastEndsTick is { } ends)
        {
            free = Math.Max(free, ends);
        }
        foreach (var h in nextHeld)
        {
            if (h.ActorId == actorId && abilities.TryGetValue(h.Step.Cast, out var a) && a.CastTicks > 0)
            {
                free += a.CastTicks;
            }
        }
        return free;
    }

    /// <summary>scripts.ts:139 stepEnemyScripts (rules in the class summary). Never mutates <paramref name="state"/>.</summary>
    /// <param name="state">The tick's state.</param>
    /// <param name="scripts">Actor id to script.</param>
    /// <param name="firedOnDeath">The previous call's <see cref="StepEnemyScriptsResult.FiredOnDeath"/>; null = none fired.</param>
    /// <param name="abilities">Ability views for targeting, range gating and chase; null = none known (legacy path).</param>
    /// <param name="pendingSteps">The previous call's <see cref="StepEnemyScriptsResult.PendingSteps"/>; null = none waiting.</param>
    /// <param name="deferredCasts">The previous call's <see cref="StepEnemyScriptsResult.DeferredCasts"/>; null = none held.</param>
    public static StepEnemyScriptsResult StepEnemyScripts(
        SimState state,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? firedOnDeath = null,
        IReadOnlyDictionary<string, ScriptAbility>? abilities = null,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? pendingSteps = null,
        IReadOnlyList<DeferredCast>? deferredCasts = null)
    {
        var abil = abilities ?? NoAbilities;
        var held = deferredCasts ?? Array.Empty<DeferredCast>();
        var nextHeld = new List<DeferredCast>();
        var nextPending = new SortedDictionary<int, List<int>>();
        var tick = state.Tick;
        var commands = new List<Command>();
        var draws = new List<TelegraphDraw>();
        var nextFired = new SortedDictionary<int, List<string>>();
        if (firedOnDeath is not null)
        {
            foreach (var kv in firedOnDeath)
            {
                nextFired[kv.Key] = kv.Value.ToList();
            }
        }

        foreach (var actorId in scripts.Keys.OrderBy(k => k))
        {
            var script = scripts[actorId];
            if (script is null || !state.Actors.TryGetValue(actorId, out var actor))
            {
                continue;
            }

            // CAST-FINISH: the steps that play the Mythic+ rule (every step but a frozen-TS replay's) fire through the
            // actor's cast queue, waiting out a cast under way; the queue is then played on ahead to the furthest
            // telegraph lead, so a telegraph is drawn only for the tick its cast really starts, waits included.
            HashSet<(int Step, int Tick)>? queuedStarts = null;
            if (actor.Alive && script.Any(s => s.Trigger != StepTrigger.OnDeath && !s.TsCastReplace))
            {
                IReadOnlyList<int>? waitingIn = null;
                pendingSteps?.TryGetValue(actorId, out waitingIn);
                var queue = new CastQueue(state, actorId, actor, script, abil, waitingIn);
                var firing = queue.Advance(tick);
                foreach (var (j, _) in firing)
                {
                    if (CastCommandFor(state, actorId, actor, script[j], abil) is { } queuedCast)
                    {
                        commands.Add(queuedCast);
                    }
                }
                if (queue.Pending.Count > 0)
                {
                    nextPending[actorId] = queue.Pending.ToList();
                }
                queuedStarts = firing.Where(f => f.Starts).Select(f => (f.Step, tick)).ToHashSet();
                var horizon = script.Where(s => s.Trigger != StepTrigger.OnDeath && !s.TsCastReplace && s.Telegraph is not null)
                    .Select(s => StartOffset(s, abil)).DefaultIfEmpty(0).Max();
                for (var t = tick + 1; t <= tick + horizon; t++)
                {
                    foreach (var (j, starts) in queue.Advance(t))
                    {
                        if (starts)
                        {
                            queuedStarts.Add((j, t));
                        }
                    }
                }
            }

            // STARTCAST-BUSY (#325; scripts.ts casterGate, deferred-casts.ts): a frozen-TS replay step fires on its
            // scheduled tick, but a caster with a cast in flight is never replaced: the step is held and issued the first
            // tick the caster is free (held casts first, oldest first), its telegraph re-drawn as its cast starts.
            var issuedUntil = tick;
            Func<ScriptStep, bool>? fire = null;
            if (script.Any(s => s.TsCastReplace) || held.Any(h => h.ActorId == actorId))
            {
                fire = CastDeferral.CasterGate(actorId, actor, tick, abil, held, nextHeld, (step, redraw) =>
                {
                    if (CastCommandFor(state, actorId, actor, step, abil) is not { } cmd)
                    {
                        return false;
                    }
                    commands.Add(cmd);
                    var hasCast = abil.TryGetValue(step.Cast, out var issued) && issued.CastTicks > 0;
                    if (hasCast)
                    {
                        issuedUntil = Math.Max(issuedUntil, tick + issued!.CastTicks);
                    }
                    if (redraw && step.Telegraph is { } htg)
                    {
                        // A held spansCast cast starts now, so its re-drawn shape resolves on the tick it lands.
                        var spans = htg.SpansCast && hasCast;
                        if (TelegraphDraw.At(state, tick, actorId, actor, step, spans ? htg with { LeadTicks = issued!.CastTicks } : htg, abil, spans ? tick : null) is { } hd)
                        {
                            draws.Add(hd);
                        }
                    }
                    return true;
                });
            }

            for (var i = 0; i < script.Count; i++)
            {
                var step = script[i];
                if (step.Trigger == StepTrigger.OnDeath)
                {
                    if (actor.Alive)
                    {
                        continue;
                    }
                    if (!nextFired.TryGetValue(actorId, out var fired))
                    {
                        fired = new List<string>();
                        nextFired[actorId] = fired;
                    }
                    if (fired.Contains(step.Cast))
                    {
                        continue;
                    }
                    fired.Add(step.Cast);
                    var deathCast = CastCommandFor(state, actorId, actor, step, abil);
                    if (deathCast is not null)
                    {
                        commands.Add(deathCast);
                        if (step.Telegraph is { } dtg && TelegraphDraw.At(state, tick, actorId, actor, step, dtg, abil, null) is { } dd)
                        {
                            draws.Add(dd);
                        }
                    }
                    continue;
                }

                if (!actor.Alive)
                {
                    continue;
                }
                if (!step.TsCastReplace)
                {
                    // CAST-FINISH: fired by the queue above. A telegraph is drawn StartOffset ticks before the tick the
                    // queue starts its cast (a spansCast one then resolves as the bar lands, any other as it starts).
                    if (step.Telegraph is { } qtg && queuedStarts is not null)
                    {
                        var spans = SpannedCastTicks(step, abil);
                        var qlead = spans > 0 ? Math.Max(qtg.LeadTicks, spans) : qtg.LeadTicks;
                        var startTick = tick + qlead - spans;
                        if (queuedStarts.Contains((i, startTick))
                            && TelegraphDraw.At(state, tick, actorId, actor, step, qtg with { LeadTicks = qlead }, abil, spans > 0 ? startTick : null) is { } qd)
                        {
                            draws.Add(qd);
                        }
                    }
                    continue;
                }
                var issuedNow = FiresAt(step, tick) && fire!(step);
                if (step.Telegraph is not { } tg)
                {
                    continue;
                }
                if (tg.SpansCast && abil.TryGetValue(step.Cast, out var spanned) && spanned.CastTicks > 0)
                {
                    // SLAM-TELEGRAPH: the shape never starts after the bar (lead at least the cast time, whatever Tempo
                    // did to it) and resolves on the landing tick; it is drawn only when its cast will start.
                    var lead = Math.Max(tg.LeadTicks, spanned.CastTicks);
                    var fireTick = tick + lead - spanned.CastTicks;
                    // On its own fire tick the step was either issued now or held (STARTCAST-BUSY: a held step is drawn
                    // when it is issued); ahead of it, the caster's free tick and the steps due with it decide.
                    var kept = fireTick == tick
                        ? issuedNow && CastHeldAt(state, actorId, actor, script, abil, fireTick, tick) == i
                        : CastHeldAt(state, actorId, actor, script, abil, fireTick, FreeTick(actorId, actor, tick, issuedUntil, nextHeld, abil)) == i;
                    if (FiresAt(step, fireTick) && kept
                        && TelegraphDraw.At(state, tick, actorId, actor, step, tg with { LeadTicks = lead }, abil, fireTick) is { } sd)
                    {
                        draws.Add(sd);
                    }
                }
                else if (FiresAt(step, tick + tg.LeadTicks)
                    && (tg.TsUnchecked || CastHeldAt(state, actorId, actor, script, abil, tick + tg.LeadTicks, FreeTick(actorId, actor, tick, issuedUntil, nextHeld, abil)) == i))
                {
                    // TELEGRAPH-READY: a telegraph that resolves on its cast's start tick is drawn, like the spansCast
                    // one, only when that cast will start then. Keyed Tempo shrinks a loop's period below the
                    // ability's cooldown (the Seed's slam at key 2: every 98, cooldown 100), and without this every
                    // second telegraph announced a cast that combat refused not_ready. A frozen-TS replay
                    // (TelegraphSpec.TsUnchecked) keeps the TS rule its oracle trace recorded.
                    if (TelegraphDraw.At(state, tick, actorId, actor, step, tg, abil, null) is { } td)
                    {
                        draws.Add(td);
                    }
                }
            }
        }

        commands.AddRange(Chase.ChaseCommands(state, scripts, abil));
        var fired2 = new SortedDictionary<int, IReadOnlyList<string>>();
        foreach (var kv in nextFired)
        {
            fired2[kv.Key] = kv.Value;
        }
        var pendingOut = new SortedDictionary<int, IReadOnlyList<int>>();
        foreach (var kv in nextPending)
        {
            pendingOut[kv.Key] = kv.Value;
        }
        return new StepEnemyScriptsResult(commands, draws, fired2) { PendingSteps = pendingOut, DeferredCasts = nextHeld };
    }
}
