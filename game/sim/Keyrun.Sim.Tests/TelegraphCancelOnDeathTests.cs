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
/// TELEGRAPH-CANCEL-ON-DEATH (wrong data shown is P0; BIBLE s3; PR #396 run B). A spansCast telegraph whose lead is
/// longer than its cast (the Pair's spread mark: lead 137, cast 40) is drawn about 97 ticks before its cast starts, when
/// its cast will start (SLAM-TELEGRAPH). If the cast can no longer start before then, because its caster died or no
/// living player is left for a cast that needs one, the floor must stop calling it on that tick. Before this the
/// telegraph stayed up until its would-be start tick (TELEGRAPH-CANCEL checked only casts under way): on #396 run B the
/// solo Warden died at 354 and SPREAD stayed drawn until 392. Everything runs through <see cref="RunRoot.StepRun"/>.
/// </summary>
public sealed class TelegraphCancelOnDeathTests
{
    private const int Player = 1;
    private const int Marker = 10;
    private const int Killer = 11;
    private const string Mark = "e.mark";   // target-shaped, cast 40 (pair.spreadMark's shape and cast time)
    private const string Nuke = "e.nuke";   // instant, kills the player
    private const string Kill = "p.kill";   // the player's one-hit kill on the marker
    private const int MarkTick = 200;       // the mark's cast starts here; with lead 137 it is drawn at 103
    private const int KillTick = 150;

    private static ScriptStep Telegraphed(int tick, string cast) => new(StepTrigger.AtTick, cast)
    {
        AtTick = tick,
        Telegraph = new TelegraphSpec("circle", 137) { Radius = 3000, SpansCast = true },
    };

    private static ScriptStep Plain(int tick, string cast) => new(StepTrigger.AtTick, cast) { AtTick = tick };

    private static Run BuildRun(IReadOnlyList<ScriptStep> marker, IReadOnlyList<ScriptStep> killer, long playerHealth = 400)
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            [Mark] = RunTestKit.Ability(Mark, new EffectOp[] { new DamageOp(1, "physical") }) with { CastTicks = 40 },
            [Nuke] = RunTestKit.Ability(Nuke, new EffectOp[] { new DamageOp(1_000_000, "physical") }),
            [Kill] = RunTestKit.Ability(Kill, new EffectOp[] { new DamageOp(100_000_000, "physical") }),
        };
        var content = RunTestKit.Content(abilities) with
        {
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Marker] = marker, [Killer] = killer },
        };
        var party = new[] { RunTestKit.Member(Player, "warden", ActorRole.Tank, 0, 1000, playerHealth, 100) };
        var dungeon = RunTestKit.OnePull("d", "p", RunTestKit.Spawn(Marker, "foe", 0, 0, 1_000_000), RunTestKit.Spawn(Killer, "foe", 500, 0, 1_000_000));
        return RunRoot.CreateRun(7, party, dungeon, content);
    }

    private static List<SimEvent> Play(Run run, int until)
    {
        var events = new List<SimEvent>();
        while (run.State.Tick < until)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
        }
        return events;
    }

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    [Fact(DisplayName = "A mark telegraph drawn ahead of its cast is cancelled on the tick its only target dies")]
    public void Mark_telegraph_is_cancelled_on_the_tick_its_only_target_dies()
    {
        var events = Play(BuildRun(new[] { Telegraphed(MarkTick, Mark) }, new[] { Plain(KillTick, Nuke) }), 260);

        var drawn = Assert.Single(events, e => e.Type == "telegraph" && Str(e, "cast") == Mark);
        Assert.True(drawn.Tick < KillTick, FormattableString.Invariant($"control: the mark is drawn while the player lives (drawn at {drawn.Tick})"));
        var death = Assert.Single(events, e => e.Type == "death" && Int(e, "actorId") == Player);
        Assert.Equal(KillTick, death.Tick);
        var cancel = Assert.Single(events, e => e.Type == "telegraph_cancel" && Str(e, "cast") == Mark);
        Assert.Equal(death.Tick, cancel.Tick);
        Assert.Equal(Marker, Int(cancel, "actorId"));
        Assert.DoesNotContain(events, e => e.Type == "telegraph_hit");
    }

    [Fact(DisplayName = "A telegraph drawn ahead of its cast is cancelled on the tick its caster dies")]
    public void Telegraph_is_cancelled_on_the_tick_its_caster_dies()
    {
        // The player kills the marker with one hit on KillTick, after its telegraph is drawn and before its cast starts.
        var run = BuildRun(new[] { Telegraphed(MarkTick, Mark) }, Array.Empty<ScriptStep>());
        var events = new List<SimEvent>();
        while (run.State.Tick < 260)
        {
            var tick = run.State.Tick;
            var cmds = tick == KillTick ? new Command[] { new CastCommand(tick, Player, Kill, Marker) } : Array.Empty<Command>();
            var stepped = RunRoot.StepRun(run, cmds);
            events.AddRange(stepped.Events);
            run = stepped.Run;
        }

        var drawn = Assert.Single(events, e => e.Type == "telegraph" && Str(e, "cast") == Mark);
        Assert.True(drawn.Tick < KillTick);
        var death = Assert.Single(events, e => e.Type == "death" && Int(e, "actorId") == Marker);
        var cancel = Assert.Single(events, e => e.Type == "telegraph_cancel" && Str(e, "cast") == Mark);
        Assert.Equal(death.Tick, cancel.Tick);
    }

    [Fact(DisplayName = "Control: a mark telegraph whose target lives is not cancelled and resolves as its cast lands")]
    public void Mark_telegraph_with_a_living_target_still_resolves()
    {
        var events = Play(BuildRun(new[] { Telegraphed(MarkTick, Mark) }, Array.Empty<ScriptStep>()), 260);

        Assert.Single(events, e => e.Type == "telegraph" && Str(e, "cast") == Mark);
        Assert.DoesNotContain(events, e => e.Type == "telegraph_cancel");
        Assert.Contains(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == Mark && e.Tick == MarkTick + 40);
    }
}
