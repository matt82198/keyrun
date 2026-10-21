using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// STUN-STOPS-CAST (finding f-8b3fff18; BIBLE s9.3 "Stops as well as kicks": "A stun, freeze or silence that lands on a
/// caster during an interruptible cast also ends the cast, as a Mythic+ stop does ... A stop counts as an interrupt for
/// Echo"). The rule: a stop effect (RunContent.StopEffectIds) that lands on an actor mid an interruptible cast ends that
/// cast on the tick it lands, with an <c>interrupt</c> event (carrying <c>stop</c>, no school lockout), so the cast never
/// completes and its floor telegraph is cancelled with no hit. An uninterruptible cast is not stopped. Everything runs
/// through <see cref="RunRoot.StepRun"/>, including the Break-bar stun (the bar's onEmpty patch).
/// </summary>
public sealed class StunStopsCastTests
{
    private const int Player = 1;
    private const int Enemy = 10;
    private const string Kickable = "e.kickable"; // 40-tick cast, interruptible
    private const string Slam = "e.slam";         // 40-tick cast, not interruptible
    private const string Control = "p.control";   // instant, control-tagged, no ops (drains the bar only)
    private const string Stun = "p.stun";         // instant, applies stunned directly
    private const string Silence = "p.silence";   // instant, applies silenced directly
    private const int StopTick = 30;

    private static readonly string[] Stops = { "stunned", "silenced" };

    private static AbilityDef EnemyCast(string id, bool interruptible) => RunTestKit.Ability(
        id, new EffectOp[] { new DamageOp(10, "physical") }) with
    {
        CastTicks = 40,
        Interruptible = interruptible,
    };

    private static ScriptStep At(int tick, string cast) => new(StepTrigger.AtTick, cast)
    {
        AtTick = tick,
        Telegraph = new TelegraphSpec("circle", 40) { Radius = 2000, SpansCast = true },
    };

    private static Run BuildRun(string enemyCast, IReadOnlyList<string>? stops = null)
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            [Kickable] = EnemyCast(Kickable, interruptible: true),
            [Slam] = EnemyCast(Slam, interruptible: false),
            [Control] = RunTestKit.Ability(Control, Array.Empty<EffectOp>()) with { Tags = new[] { "control" } },
            [Stun] = RunTestKit.Ability(Stun, new EffectOp[] { new ApplyEffectOp("stunned", 20, 1) }),
            [Silence] = RunTestKit.Ability(Silence, new EffectOp[] { new ApplyEffectOp("silenced", 20, 1) }),
        };
        var effects = new Dictionary<string, EffectDef>(StringComparer.Ordinal)
        {
            ["stunned"] = new("stunned", 1, "physical", new EffectModifiers()),
            ["silenced"] = new("silenced", 1, "magic", new EffectModifiers()),
        };
        var bar = new BreakBarSource("bar", new[] { Enemy }, 20, 20, 20, new[] { new BreakBarOnEmptyEffect("stunned", 100, 1) });
        var content = RunTestKit.Content(abilities, effects) with
        {
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Enemy] = new[] { At(20, enemyCast) } },
            BreakBarSources = new[] { bar },
            StopEffectIds = stops,
        };
        var party = new[] { RunTestKit.Member(Player, "warden", ActorRole.Tank, 0, 1000, 1_000_000, 100) };
        var dungeon = RunTestKit.OnePull("d", "p", RunTestKit.Spawn(Enemy, "foe", 0, 0, 1_000_000));
        return RunRoot.CreateRun(7, party, dungeon, content);
    }

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    private static (Run Run, List<SimEvent> Events) Play(Run run, params Command[] commands) =>
        RunTestKit.StepUntil(run, 90, commands);

    /// <summary>The cast was stopped on <see cref="StopTick"/>: an interrupt naming the stop, no completion, no hit,
    /// its telegraph cancelled that tick, and no school lockout.</summary>
    private static void AssertStopped(Run run, List<SimEvent> events, string stop)
    {
        var interrupt = Assert.Single(events, e => e.Type == "interrupt");
        Assert.Equal(StopTick, interrupt.Tick);
        Assert.Equal(Enemy, Int(interrupt, "targetId"));
        Assert.Equal(Kickable, Str(interrupt, "abilityId"));
        Assert.Equal(stop, Str(interrupt, "stop"));
        Assert.DoesNotContain(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == Kickable);
        Assert.DoesNotContain(events, e => e.Type == "telegraph_hit");
        var cancel = Assert.Single(events, e => e.Type == "telegraph_cancel");
        Assert.Equal(StopTick, cancel.Tick);
        Assert.Equal(Kickable, Str(cancel, "cast"));
        Assert.Empty(run.Encounter.Telegraphs);
        Assert.DoesNotContain(run.State.Actors[Enemy].Effects, e => e.EffectId.StartsWith("lockout:", StringComparison.Ordinal));
    }

    /// <summary>The cast landed on tick 60 with its telegraph hit, and nothing interrupted or cancelled it.</summary>
    private static void AssertLands(List<SimEvent> events, string cast)
    {
        Assert.DoesNotContain(events, e => e.Type == "interrupt");
        Assert.DoesNotContain(events, e => e.Type == "telegraph_cancel");
        Assert.Contains(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == cast && e.Tick == 60);
        Assert.Contains(events, e => e.Type == "telegraph_hit" && e.Tick == 60);
    }

    [Fact(DisplayName = "A Break-bar stun stops the stunned caster's interruptible cast on the stagger tick")]
    public void Break_bar_stun_stops_the_cast()
    {
        var (run, events) = Play(BuildRun(Kickable, Stops), new CastCommand(StopTick, Player, Control, Enemy));
        Assert.Contains(events, e => e.Type == "stagger" && e.Tick == StopTick);
        AssertStopped(run, events, "stunned");
    }

    [Fact(DisplayName = "A stun applied by an ability stops the caster's interruptible cast that tick")]
    public void Ability_stun_stops_the_cast()
    {
        var (run, events) = Play(BuildRun(Kickable, Stops), new CastCommand(StopTick, Player, Stun, Enemy));
        AssertStopped(run, events, "stunned");
    }

    [Fact(DisplayName = "A silence applied by an ability stops the caster's interruptible cast that tick")]
    public void Ability_silence_stops_the_cast()
    {
        var (run, events) = Play(BuildRun(Kickable, Stops), new CastCommand(StopTick, Player, Silence, Enemy));
        AssertStopped(run, events, "silenced");
    }

    [Fact(DisplayName = "Control: an unstunned interruptible cast still lands")]
    public void Unstunned_cast_lands()
    {
        var (_, events) = Play(BuildRun(Kickable, Stops));
        AssertLands(events, Kickable);
    }

    [Fact(DisplayName = "A stun does not stop an uninterruptible cast")]
    public void Stun_does_not_stop_an_uninterruptible_cast()
    {
        var (_, events) = Play(BuildRun(Slam, Stops), new CastCommand(StopTick, Player, Control, Enemy));
        Assert.Contains(events, e => e.Type == "stagger" && e.Tick == StopTick);
        AssertLands(events, Slam);
    }

    [Fact(DisplayName = "A run without stop effects (every oracle run) keeps the stunned caster's cast")]
    public void Run_without_stop_effects_is_unchanged()
    {
        var (_, events) = Play(BuildRun(Kickable, stops: null), new CastCommand(StopTick, Player, Stun, Enemy));
        AssertLands(events, Kickable);
    }
}
