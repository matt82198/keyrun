using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>One call's output (scripts.ts:118 StepEnemyScriptsResult).</summary>
/// <param name="Commands">Casts (then chase moves) for the caller to feed to combat this tick.</param>
/// <param name="TelegraphEvents">"telegraph" events announced this tick.</param>
/// <param name="FiredOnDeath">The fire-once map carried forward plus this call's onDeath casts; pass it back next call.</param>
public sealed record StepEnemyScriptsResult(IReadOnlyList<Command> Commands, IReadOnlyList<SimEvent> TelegraphEvents, IReadOnlyDictionary<int, IReadOnlyList<string>> FiredOnDeath)
{
    /// <summary>
    /// TELEGRAPH-CANCEL: for each of <see cref="TelegraphEvents"/>, in the same order, the tick its cast is due to start
    /// when the telegraph spans that cast (<see cref="TelegraphSpec.SpansCast"/>), else null. Empty = none spans a cast.
    /// </summary>
    public IReadOnlyList<int?> TelegraphCastStarts { get; init; } = Array.Empty<int?>();
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

    // scripts.ts:99 telegraphEventAt: {tick, type, actorId, cast, shape, leadTicks, resolvesTick, origin, yaw,
    // radius?, angle1024?, length?, width?}.
    private static SimEvent TelegraphEventAt(int tick, int actorId, ActorState actor, string cast, TelegraphSpec tg)
    {
        var fields = new List<(string, JsValue)>
        {
            ("actorId", JsValue.Int(actorId)),
            ("cast", JsValue.Str(cast)),
            ("shape", JsValue.Str(tg.Shape)),
            ("leadTicks", JsValue.Int(tg.LeadTicks)),
            ("resolvesTick", JsValue.Int(tick + tg.LeadTicks)),
            ("origin", JsValue.Vec(actor.Pos)),
            ("yaw", JsValue.Int(actor.Yaw)),
        };
        if (tg.Radius is { } r)
        {
            fields.Add(("radius", JsValue.Int(r)));
        }
        if (tg.Angle1024 is { } a)
        {
            fields.Add(("angle1024", JsValue.Int(a)));
        }
        if (tg.Length is { } l)
        {
            fields.Add(("length", JsValue.Int(l)));
        }
        if (tg.Width is { } w)
        {
            fields.Add(("width", JsValue.Int(w)));
        }
        return new SimEvent(tick, "telegraph", fields.ToArray());
    }

    // scripts.ts:126 castCommand: null when the target resolution blocks it.
    private static CastCommand? CastCommandFor(SimState state, int actorId, ActorState actor, ScriptStep step, IReadOnlyDictionary<string, ScriptAbility> abilities)
    {
        abilities.TryGetValue(step.Cast, out var ability);
        var (targetId, blocked) = ScriptTarget.Resolve(state, actor, step.Target, ability);
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
    /// Combat runs an actor's script casts in step order and StartCast replaces a cast under way, so a later step that
    /// starts on the same tick takes the cast over (the Hollow Warden's Lock loop lands on every second slam tick); an
    /// earlier step that starts a GCD (gcdTicks &gt; 0) refuses every later one, and a second step of an ability
    /// already started that tick is refused when that took its only charge. Each step's own start is
    /// <see cref="WillStart"/>; a step with no ability view starts whenever its target resolves. Without this a
    /// spansCast telegraph was drawn for every step that could start on its own, and on a shared tick every one but the
    /// last announced a cast that never came.
    /// </summary>
    private static int CastHeldAt(SimState state, int actorId, ActorState actor, IReadOnlyList<ScriptStep> script, IReadOnlyDictionary<string, ScriptAbility> abilities, int fireTick)
    {
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
                started.Add(s.Cast);
                gcdStarted = def is not null && def.GcdTicks > 0;
            }
        }
        return held;
    }

    /// <summary>scripts.ts:139 stepEnemyScripts (rules in the class summary). Never mutates <paramref name="state"/>.</summary>
    /// <param name="state">The tick's state.</param>
    /// <param name="scripts">Actor id to script.</param>
    /// <param name="firedOnDeath">The previous call's <see cref="StepEnemyScriptsResult.FiredOnDeath"/>; null = none fired.</param>
    /// <param name="abilities">Ability views for targeting, range gating and chase; null = none known (legacy path).</param>
    public static StepEnemyScriptsResult StepEnemyScripts(
        SimState state,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? firedOnDeath = null,
        IReadOnlyDictionary<string, ScriptAbility>? abilities = null)
    {
        var abil = abilities ?? NoAbilities;
        var tick = state.Tick;
        var commands = new List<Command>();
        var telegraphEvents = new List<SimEvent>();
        var castStarts = new List<int?>();
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
                        if (step.Telegraph is { } dtg)
                        {
                            telegraphEvents.Add(TelegraphEventAt(tick, actorId, actor, step.Cast, dtg));
                            castStarts.Add(null);
                        }
                    }
                    continue;
                }

                if (!actor.Alive)
                {
                    continue;
                }
                if (FiresAt(step, tick) && CastCommandFor(state, actorId, actor, step, abil) is { } cast)
                {
                    commands.Add(cast);
                }
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
                    if (FiresAt(step, fireTick) && CastHeldAt(state, actorId, actor, script, abil, fireTick) == i)
                    {
                        telegraphEvents.Add(TelegraphEventAt(tick, actorId, actor, step.Cast, tg with { LeadTicks = lead }));
                        castStarts.Add(fireTick);
                    }
                }
                else if (FiresAt(step, tick + tg.LeadTicks)
                    && (tg.TsUnchecked || CastHeldAt(state, actorId, actor, script, abil, tick + tg.LeadTicks) == i))
                {
                    // TELEGRAPH-READY: a telegraph that resolves on its cast's start tick is drawn, like the spansCast
                    // one, only when that cast will start then. Keyed Tempo shrinks a loop's period below the
                    // ability's cooldown (the Seed's slam at key 2: every 98, cooldown 100), and without this every
                    // second telegraph announced a cast that combat refused not_ready. A frozen-TS replay
                    // (TelegraphSpec.TsUnchecked) keeps the TS rule its oracle trace recorded.
                    telegraphEvents.Add(TelegraphEventAt(tick, actorId, actor, step.Cast, tg));
                    castStarts.Add(null);
                }
            }
        }

        commands.AddRange(Chase.ChaseCommands(state, scripts, abil));
        var fired2 = new SortedDictionary<int, IReadOnlyList<string>>();
        foreach (var kv in nextFired)
        {
            fired2[kv.Key] = kv.Value;
        }
        return new StepEnemyScriptsResult(commands, telegraphEvents, fired2) { TelegraphCastStarts = castStarts };
    }
}
