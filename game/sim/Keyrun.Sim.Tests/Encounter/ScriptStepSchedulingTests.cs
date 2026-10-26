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
/// Findings f-7947b969 (two steps due on one tick: the second never fires) and f-ea28e329 (a refused step re-emits its
/// cast command on every retry tick). Drives <see cref="Scripts.StepEnemyScripts"/> and the real combat rules tick by
/// tick over an inline enemy script.
/// </summary>
public sealed class ScriptStepSchedulingTests
{
    private const int Boss = 910;
    private const int Player = 1;
    private const string Crush = "sched.crush";
    private const string Buster = "sched.buster";
    private const int DueTick = 110;
    private const int RunFrom = 100;

    private static AbilityDef DefOf(string id, int castTicks, int range, int cost) =>
        Def(id, new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(30, "arcane") }, castTicks: castTicks, gcdTicks: 0, range: range, costAmount: cost);

    private sealed record Rig(IReadOnlyDictionary<string, AbilityDef> Defs, IReadOnlyDictionary<string, ScriptAbility> Views);

    private static Rig MakeRig(int castTicks, int range, int cost = 0)
    {
        var crush = DefOf(Crush, castTicks, range, cost);
        var buster = DefOf(Buster, castTicks, range, cost);
        return new Rig(
            AbilityMap(crush, buster),
            new Dictionary<string, ScriptAbility>(StringComparer.Ordinal)
            {
                [Crush] = new ScriptAbility(Crush, range, "target", false) { CastTicks = castTicks, Def = crush },
                [Buster] = new ScriptAbility(Buster, range, "target", false) { CastTicks = castTicks, Def = buster },
            });
    }

    /// <summary>Runs scheduler plus rules; returns every cast command the scheduler issued (tick, cast) and every cast start (tick, cast).</summary>
    private static (List<(int Tick, string Cast)> Issued, List<(int Tick, string Cast)> Started) Run(
        Rig rig, IReadOnlyList<ScriptStep> script, int toTick, int playerX, Func<int, long>? bossResourceAt = null)
    {
        var rules = Rules.Make(Config(rig.Defs));
        var scripts = EncounterTestKit.Scripts(Boss, script.ToArray());
        var state = At(
            RunFrom,
            Make(Boss, ActorKind.Enemy, "boss", ActorRole.None, x: 0, maxHealth: 1000, maxResource: 100),
            Make(Player, ActorKind.Player, "warden", ActorRole.Tank, x: playerX, maxHealth: 1000));

        var issued = new List<(int, string)>();
        var started = new List<(int, string)>();
        IReadOnlyDictionary<int, IReadOnlyList<int>>? pending = null;
        IReadOnlyList<DeferredCast> deferred = Array.Empty<DeferredCast>();
        while (state.Tick < toTick)
        {
            var t = state.Tick;
            if (bossResourceAt is not null)
            {
                state.Actors[Boss].Resource = bossResourceAt(t);
            }
            var result = Scripts.StepEnemyScripts(state, scripts, null, rig.Views, pending, deferred, null, null);
            pending = result.PendingSteps;
            deferred = result.DeferredCasts;
            foreach (var c in result.Commands.OfType<CastCommand>())
            {
                issued.Add((t, c.AbilityId));
            }

            Tick(state, rules, result.Commands, _ => { });

            var caster = state.Actors[Boss];
            if (caster.CastStartTick == t && caster.CastId is { } id)
            {
                started.Add((t, id));
            }
        }
        return (issued, started);
    }

    private static string Describe(IEnumerable<(int Tick, string Cast)> xs) =>
        xs.Any() ? string.Join(", ", xs.Select(s => $"{s.Cast}@{s.Tick}")) : "none";

    [Fact(DisplayName = "script steps (a): two short-bar steps due on tick 110 both start, the first on 110")]
    public void Two_short_bar_steps_due_on_one_tick_both_start()
    {
        var rig = MakeRig(castTicks: 2, range: 5000);
        var script = new[] { ScriptStep.At(DueTick, Crush), ScriptStep.At(DueTick, Buster) };

        var (_, started) = Run(rig, script, DueTick + 40, playerX: 2000);

        // Short bars: the first starts on the due tick, the second waits at most the 2-tick bar.
        Assert.Contains((DueTick, Crush), started);
        Assert.True(
            started.Any(s => s.Cast == Buster && s.Tick <= DueTick + 4),
            $"buster (second step, same tick) must start within its wait; starts: {Describe(started)}");
    }

    [Fact(DisplayName = "script steps (a): two cast-time steps due on tick 110 both start a cast")]
    public void Two_cast_time_steps_due_on_one_tick_both_start()
    {
        var rig = MakeRig(castTicks: 10, range: 5000);
        var script = new[] { ScriptStep.At(DueTick, Crush), ScriptStep.At(DueTick, Buster) };

        var (_, started) = Run(rig, script, DueTick + 60, playerX: 2000);

        Assert.True(
            started.Any(s => s.Cast == Crush) && started.Any(s => s.Cast == Buster),
            $"both same-tick steps must start; starts: {Describe(started)}");
    }

    [Fact(DisplayName = "script steps (b): a step refused 10 ticks (insufficient resource) issues at most one cast command in that window")]
    public void Refused_step_issues_at_most_one_cast_command_per_retry_window()
    {
        const int ClearTick = DueTick + 10;
        var rig = MakeRig(castTicks: 2, range: 5000, cost: 10);
        var script = new[] { ScriptStep.At(DueTick, Crush) };

        // The boss has 0 resource (cost 10: insufficient_resource, a StartError the target check does not see) until ClearTick.
        var (issued, started) = Run(rig, script, ClearTick + 20, playerX: 2000, bossResourceAt: t => t >= ClearTick ? 100 : 0);

        var inWindow = issued.Where(i => i.Cast == Crush && i.Tick >= DueTick && i.Tick < ClearTick).ToList();
        Assert.True(
            inWindow.Count <= 1,
            $"a step refused for {ClearTick - DueTick} ticks must issue at most one cast command in the window; issued: {Describe(inWindow)}");
        Assert.True(
            started.Any(s => s.Cast == Crush && s.Tick >= ClearTick),
            $"the refused step must still start once the refusal clears; starts: {Describe(started)}; issued: {Describe(issued)}");
    }
}
