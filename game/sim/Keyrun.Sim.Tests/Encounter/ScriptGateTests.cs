using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// SCRIPT-GATE (D66, the Sunken Bellfoundry): a script step with a <see cref="ScriptStep.When"/> fires only while its
/// caster meets the condition, judged on the tick it would fire. Property-tested over every tick of a window and every
/// health share, so the gate is an integer threshold with no off-by-one, and a closed gate also draws no telegraph.
/// </summary>
public sealed class ScriptGateTests
{
    private const int Enemy = 1;

    private static ActorState Caster(long health, params (string Id, int Ends)[] effects)
    {
        var a = EncounterTestKit.Actor(Enemy, ActorKind.Enemy, health: health);
        foreach (var (id, ends) in effects)
        {
            a.Effects.Add(new EffectInstance { EffectId = id, SourceId = Enemy, Stacks = 1, EndsTick = ends });
        }
        return a;
    }

    private static StepEnemyScriptsResult Step(int tick, ActorState caster, params ScriptStep[] steps) =>
        Scripts.StepEnemyScripts(EncounterTestKit.State(tick, caster), EncounterTestKit.Scripts(Enemy, steps));

    [Fact(DisplayName = "SCRIPT-GATE: an ungated step is unchanged (the gate is opt-in)")]
    public void Ungated_step_fires_as_before()
    {
        var step = ScriptStep.Loop(60, "x");
        Assert.True(Scripts.GateOpen(step, Caster(1), 60));
        Assert.Single(Step(60, Caster(1), step).Commands);
    }

    [Fact(DisplayName = "SCRIPT-GATE healthBelowBp: the step fires exactly when health * 10000 < maxHealth * bp, at every health")]
    public void Health_gate_is_an_exact_integer_threshold()
    {
        var step = ScriptStep.Loop(60, "boil") with { When = new ScriptGate(null, 3000) };
        for (long health = 1; health <= 1000; health++)
        {
            var open = health * 10000 < 1000L * 3000;
            Assert.Equal(open, Scripts.GateOpen(step, Caster(health), 60));
            Assert.Equal(open ? 1 : 0, Step(60, Caster(health), step).Commands.Count);
        }
    }

    [Fact(DisplayName = "SCRIPT-GATE hasEffect: the step fires only while the effect runs (EndsTick > tick), on every tick of the window")]
    public void Effect_gate_follows_the_effect_lifetime()
    {
        var step = ScriptStep.Loop(20, "burst") with { When = new ScriptGate("overheated", null) };
        for (var tick = 20; tick <= 400; tick += 20)
        {
            var caster = Caster(1000, ("overheated", 200), ("other", 400));
            Assert.Equal(tick < 200, Scripts.GateOpen(step, caster, tick));
            Assert.Equal(tick < 200 ? 1 : 0, Step(tick, caster, step).Commands.Count);
        }
        Assert.False(Scripts.GateOpen(step, Caster(1000, ("other", 400)), 20));
    }

    [Fact(DisplayName = "SCRIPT-GATE onDeath: a gated death burst fires once if the caster died carrying the effect, never if not")]
    public void Death_step_reads_the_effects_it_died_with()
    {
        var step = ScriptStep.OnDeath("slagBurst") with { When = new ScriptGate("overheated", null) };
        var hot = Caster(0, ("overheated", 900));
        hot.Alive = false;
        var first = Scripts.StepEnemyScripts(EncounterTestKit.State(50, hot), EncounterTestKit.Scripts(Enemy, step));
        Assert.Equal(new Command[] { new CastCommand(50, Enemy, "slagBurst") }, first.Commands);
        var again = Scripts.StepEnemyScripts(EncounterTestKit.State(51, hot), EncounterTestKit.Scripts(Enemy, step), first.FiredOnDeath);
        Assert.Empty(again.Commands);

        var kicked = Caster(0);
        kicked.Alive = false;
        var cold = Scripts.StepEnemyScripts(EncounterTestKit.State(50, kicked), EncounterTestKit.Scripts(Enemy, step));
        Assert.Empty(cold.Commands);
        // Judged once: it stays unfired-and-spent on later ticks too.
        Assert.Empty(Scripts.StepEnemyScripts(EncounterTestKit.State(51, kicked), EncounterTestKit.Scripts(Enemy, step), cold.FiredOnDeath).Commands);
    }

    [Fact(DisplayName = "SCRIPT-GATE: a closed gate draws no telegraph for the cast it would announce")]
    public void Closed_gate_draws_no_telegraph()
    {
        var step = ScriptStep.Loop(100, "boil") with
        {
            When = new ScriptGate(null, 3000),
            Telegraph = new TelegraphSpec("circle", 40) { Radius = 2000 },
        };
        Assert.Single(Step(60, Caster(100), step).TelegraphEvents);
        Assert.Empty(Step(60, Caster(1000), step).TelegraphEvents);
    }

    [Fact(DisplayName = "SCRIPT-GATE: Tempo scaling keeps the gate")]
    public void Tempo_keeps_the_gate()
    {
        var gate = new ScriptGate(null, 3000);
        var scaled = Tempo.ScaleScript(new[] { ScriptStep.Loop(100, "boil") with { When = gate } }, 10, _ => 0);
        Assert.Equal(gate, Assert.Single(scaled).When);
    }
}
