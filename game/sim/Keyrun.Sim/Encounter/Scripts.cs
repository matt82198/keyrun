using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>One call's output (scripts.ts:118 StepEnemyScriptsResult).</summary>
/// <param name="Commands">Casts (then chase moves) for the caller to feed to combat this tick.</param>
/// <param name="TelegraphEvents">"telegraph" events announced this tick.</param>
/// <param name="FiredOnDeath">The fire-once map carried forward plus this call's onDeath casts; pass it back next call.</param>
public sealed record StepEnemyScriptsResult(IReadOnlyList<Command> Commands, IReadOnlyList<SimEvent> TelegraphEvents, IReadOnlyDictionary<int, IReadOnlyList<string>> FiredOnDeath);

/// <summary>
/// Enemy behaviour as data-driven state machines (sim/encounter/scripts.ts). Each tick, for every scripted actor in
/// ascending id order and each step in script order: a living actor casts when the step fires on this tick, and
/// announces the step's telegraph <c>leadTicks</c> ahead, with origin and yaw snapshotted from the caster now and
/// <c>resolvesTick</c> on the cast tick. An onDeath step fires once, the first call that sees its actor dead (one
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
            foreach (var step in script)
            {
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
                if (step.Telegraph is { } tg && FiresAt(step, tick + tg.LeadTicks))
                {
                    telegraphEvents.Add(TelegraphEventAt(tick, actorId, actor, step.Cast, tg));
                }
            }
        }

        commands.AddRange(Chase.ChaseCommands(state, scripts, abil));
        var fired2 = new SortedDictionary<int, IReadOnlyList<string>>();
        foreach (var kv in nextFired)
        {
            fired2[kv.Key] = kv.Value;
        }
        return new StepEnemyScriptsResult(commands, telegraphEvents, fired2);
    }
}
