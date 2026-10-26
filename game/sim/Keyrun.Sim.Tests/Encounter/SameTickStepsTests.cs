using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Finding f-7947b969 (G7 sim defect): when two enemy script steps fall due on the same tick, the second never fires.
/// The toolkit lane saw seed.crush at tick 110 dropped and moved to 130. This file drives the real encounter scheduler
/// (<see cref="Scripts.StepEnemyScripts"/>) and the real combat rules (<see cref="Rules"/>) tick by tick over an inline
/// enemy script, and records every cast that starts (the caster's <see cref="ActorState.CastStartTick"/> lands on the
/// tick the cast began). The control has the two steps one tick apart and must stay green.
/// </summary>
public sealed class SameTickStepsTests
{
    private const int Boss = 900;
    private const int Player = 1;
    private const string Crush = "boss.crush";
    private const string Buster = "boss.buster";
    private const int CastTicks = 10;
    private const int DueTick = 110;
    private const int RunFrom = 100;
    private const int RunTo = 150;

    private static readonly AbilityDef CrushDef = Def(
        Crush,
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(30, "arcane") },
        castTicks: CastTicks,
        gcdTicks: 0,
        range: 5000);

    private static readonly AbilityDef BusterDef = Def(
        Buster,
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(40, "arcane") },
        castTicks: CastTicks,
        gcdTicks: 0,
        range: 5000);

    private static readonly IReadOnlyDictionary<string, AbilityDef> Defs = AbilityMap(CrushDef, BusterDef);

    private static readonly IReadOnlyDictionary<string, ScriptAbility> Views = new Dictionary<string, ScriptAbility>(StringComparer.Ordinal)
    {
        [Crush] = new ScriptAbility(Crush, 5000, "target", false) { CastTicks = CastTicks, Def = CrushDef },
        [Buster] = new ScriptAbility(Buster, 5000, "target", false) { CastTicks = CastTicks, Def = BusterDef },
    };

    /// <summary>Runs the scheduler and the combat rules from <paramref name="fromTick"/> to <paramref name="toTick"/>, returning each cast start as (tick, ability).</summary>
    private static List<(int Tick, string Cast)> CastStarts(IReadOnlyList<ScriptStep> script, int fromTick, int toTick, Action<SimState>? beforeTick = null)
    {
        var rules = Rules.Make(Config(Defs));
        var scripts = EncounterTestKit.Scripts(Boss, script.ToArray());
        var state = At(
            fromTick,
            Make(Boss, ActorKind.Enemy, "boss", ActorRole.None, x: 0, maxHealth: 1000),
            Make(Player, ActorKind.Player, "warden", ActorRole.Tank, x: 2000, maxHealth: 1000));

        var starts = new List<(int Tick, string Cast)>();
        IReadOnlyDictionary<int, IReadOnlyList<int>>? pending = null;
        IReadOnlyList<DeferredCast> deferred = Array.Empty<DeferredCast>();
        while (state.Tick < toTick)
        {
            var t = state.Tick;
            beforeTick?.Invoke(state);
            var result = Scripts.StepEnemyScripts(
                state,
                scripts,
                firedOnDeath: null,
                abilities: Views,
                pendingSteps: pending,
                deferredCasts: deferred,
                scriptStarts: null,
                effects: null);
            pending = result.PendingSteps;
            deferred = result.DeferredCasts;

            Tick(state, rules, result.Commands, _ => { });

            var caster = state.Actors[Boss];
            if (caster.CastStartTick == t && caster.CastId is { } started)
            {
                starts.Add((t, started));
            }
        }
        return starts;
    }

    private static string Describe(IEnumerable<(int Tick, string Cast)> starts) =>
        starts.Any() ? string.Join(", ", starts.Select(s => $"{s.Cast}@{s.Tick}")) : "none";

    [Fact(DisplayName = "same-tick steps: two steps due on tick 110 both start a cast (seed.crush dropped at 110)")]
    public void Two_steps_due_on_the_same_tick_both_start_their_casts()
    {
        var script = new[]
        {
            ScriptStep.At(DueTick, Crush),
            ScriptStep.At(DueTick, Buster),
        };

        var starts = CastStarts(script, RunFrom, RunTo);

        Assert.True(
            starts.Any(s => s.Cast == Crush) && starts.Any(s => s.Cast == Buster),
            $"two steps due on tick {DueTick} must both start a cast by tick {RunTo}; cast starts were: {Describe(starts)}");
    }

    [Fact(DisplayName = "same-tick steps control: the same two steps one tick apart both start a cast")]
    public void Two_steps_one_tick_apart_both_start_their_casts()
    {
        var script = new[]
        {
            ScriptStep.At(DueTick, Crush),
            ScriptStep.At(DueTick + 1, Buster),
        };

        var starts = CastStarts(script, RunFrom, RunTo);

        Assert.True(
            starts.Any(s => s.Cast == Crush) && starts.Any(s => s.Cast == Buster),
            $"control: two steps one tick apart must both start a cast by tick {RunTo}; cast starts were: {Describe(starts)}");
    }

    [Fact(DisplayName = "same-tick steps: a step whose WillStart refuses on its due tick still fires once the refusal clears")]
    public void StepNotStartableOnDueTickStillFiresLater()
    {
        // WillStart (Scripts.cs:209) refuses on DueTick because CastCommandFor (Scripts.cs:211) finds no living player for the
        // target-shaped Crush (ScriptTarget.Resolve blocks it, Scripts.cs:188). The Warden is dead until ClearTick, so the
        // refusal is the only thing stopping the cast; reviving the Warden at ClearTick clears it.
        const int ClearTick = 120;
        const int Deadline = ClearTick + 60;
        var script = new[] { ScriptStep.At(DueTick, Crush) };

        var starts = CastStarts(script, RunFrom, Deadline, beforeTick: state =>
        {
            var warden = state.Actors[Player];
            warden.Alive = state.Tick >= ClearTick;
            warden.Health = warden.Alive ? 1000 : 0;
        });

        Assert.True(
            starts.Any(s => s.Cast == Crush && s.Tick <= Deadline),
            $"a step due on tick {DueTick} whose WillStart refused (no living target) must start its cast by tick {Deadline} once the refusal cleared at tick {ClearTick}; cast starts were: {Describe(starts)}");
    }
}
