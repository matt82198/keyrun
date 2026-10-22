using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;
using C = Keyrun.Content;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-a3de0a01: autorun could not give a bot a talent (RunStart.Of and QueuedKit.Queue patch only
/// <c>Loadouts[human]</c>), so a capture of a talent-driven D15 row (the ECHO chip, Encore's Chorus) needed a human seat
/// driven by an input log. <c>--talent SEAT:TALENT</c> (<see cref="SeatTalent"/>, <see cref="RunRequest.Talents"/>)
/// patches that seat's loadout in <see cref="HostRunFactory.Create"/>, the one path SimDriver.StartRun and
/// PlaytestRunner.Play share; SimDriver logs <see cref="SeatTalent.ProofLine"/> once. These cases drive the factory and
/// the run C shape (HUD scene test run C: default key and seed, three seats, seat 1 the autorun
/// <see cref="ScriptedWarden"/>, seats 2 and 3 <see cref="BotCommandSource"/>) as StatusChipsEchoTests does.
/// </summary>
public sealed class SeatTalentsTests
{
    private const long Seed = 1;            // ArenaOptions.DefaultSeed
    private const int Key = 2;              // ArenaOptions.DefaultKeyLevel
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = Lobby.DefaultClassId;
    private const string Chorus = "chanter.chorus";
    private const int ChanterSeat = 2;      // HostRunSetup.Roster: Warden 1, Chanter 2, Cinder 3
    private const int Ticks = 2000;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public SeatTalentsTests(ITestOutputHelper output) => _output = output;

    // The host run (SimDriver.StartRun's RunRequest on an --autorun start: no request, no progression, bot gear on).
    private static HostRun HostRun(params SeatTalent[] talents)
    {
        var registry = C.ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        return HostRunFactory.Create(setup, new RunRequest(Seed, registry, HumanClassId) { Talents = talents });
    }

    // Whether the run's Chorus (mend verse) applies Encore's chorusEcho: the talented ability is in the run's map.
    private static bool ChorusEchoes(Run run) =>
        run.Content.Abilities[Chorus].Variants is { } v && v.TryGetValue("mend", out var mend) &&
        mend.OfType<ApplyEffectOp>().Any(op => op.EffectId == "chorusEcho");

    [Fact(DisplayName = "--talent (f-a3de0a01): SEAT:TALENT parses; a malformed value is a named error")]
    public void Parse_reads_seat_and_talent_and_refuses_malformed_values()
    {
        Assert.Equal(new SeatTalent(2, "encore"), SeatTalent.Parse("2:encore"));
        Assert.Equal("TALENT seat=2 id=encore", SeatTalent.Parse("2:encore").ProofLine());
        foreach (var bad in new[] { "encore", "2:", ":encore", "0:encore", "-1:encore", "x:encore", "2:en core", "" })
        {
            var ex = Assert.Throws<ArgumentException>(() => SeatTalent.Parse(bad));
            Assert.Contains("--talent", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "--talent 2:encore (f-a3de0a01): the factory patches seat 2's Chorus with Encore; the default run does not")]
    public void Talent_on_seat_two_patches_the_chanter_chorus()
    {
        var plain = HostRun();
        var talented = HostRun(new SeatTalent(ChanterSeat, "encore"));

        Assert.False(ChorusEchoes(plain.Run));
        Assert.True(ChorusEchoes(talented.Run));
        Assert.Equal(100, talented.Run.Content.Abilities[Chorus].CooldownTicks); // content: Encore's cooldownTicks
        Assert.NotEqual(100, plain.Run.Content.Abilities[Chorus].CooldownTicks);
    }

    [Fact(DisplayName = "--talent (f-a3de0a01): a talent the seat's class lacks, or a seat not in the party, is a named error")]
    public void Unknown_talent_or_seat_is_refused_with_a_named_error()
    {
        var wrongClass = Assert.Throws<ArgumentException>(() => HostRun(new SeatTalent(1, "encore"))); // seat 1 is the Warden
        _output.WriteLine(wrongClass.Message);
        Assert.Contains("\"encore\"", wrongClass.Message, StringComparison.Ordinal);
        Assert.Contains("\"warden\"", wrongClass.Message, StringComparison.Ordinal);

        var unknown = Assert.Throws<ArgumentException>(() => HostRun(new SeatTalent(ChanterSeat, "nope")));
        _output.WriteLine(unknown.Message);
        Assert.Contains("\"nope\"", unknown.Message, StringComparison.Ordinal);

        var seat = Assert.Throws<ArgumentException>(() => HostRun(new SeatTalent(9, "encore")));
        _output.WriteLine(seat.Message);
        Assert.Contains("seat 9", seat.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "--talent (f-a3de0a01): the human seat's talent rides the queued loadout, as the screen's pick does")]
    public void Talent_on_the_human_seat_reaches_the_run()
    {
        var registry = C.ContentLoader.Load();
        var warden = registry.Classes[HumanClassId];
        var fork = warden.Talents.First(f => f.AbilityId == "warden.guardBreak");
        var option = fork.Options.First(o => o.Id == "surge"); // content: cooldownTicks 30
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var run = HostRunFactory.Create(setup, new RunRequest(Seed, registry, HumanClassId) { Talents = new[] { new SeatTalent(1, option.Id) } });

        var expected = C.TalentPicks.ApplyPatch(registry.Abilities[fork.AbilityId], option.Patch);
        Assert.NotEqual(registry.Abilities[fork.AbilityId].CooldownTicks, expected.CooldownTicks);
        Assert.Equal(expected.CooldownTicks, run.Run.Content.Abilities[fork.AbilityId].CooldownTicks);
        Assert.Contains(HostRunSetup.HumanId, run.Start.Options?.Loadouts?.Keys ?? Array.Empty<int>());
    }

    [Fact(DisplayName = "--talent 2:encore (f-a3de0a01): the run C party shows the ECHO chip on a frame")]
    public void Encore_talent_puts_the_echo_chip_on_a_run_c_party_frame()
    {
        var registry = C.ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var run = HostRunFactory.Create(setup, new RunRequest(Seed, registry, HumanClassId) { Talents = new[] { new SeatTalent(ChanterSeat, "encore") } }).Run;
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var backfill = BackfillState.Empty;
        string? first = null;
        for (var i = 0; i < Ticks && first is null; i++)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            var known = run.Content.Effects.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (var actor in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player))
            {
                var echo = StatusChips.For(actor, run.State.Tick, known).FirstOrDefault(c => c.Id == "chorusEcho");
                if (echo is not null)
                {
                    first = echo.ProofLine(actor.Id, run.State.Tick);
                    break;
                }
            }
        }
        _output.WriteLine(first ?? "no ECHO chip");
        Assert.NotNull(first);
        Assert.Matches(@"^HUD chip seat=[123] id=chorusEcho label=ECHO colour=7dffb0 at=\d+$", first);
    }

    [Fact(DisplayName = "--talent (f-a3de0a01): PlaytestOptions.Talents reaches the headless playtest run")]
    public void Playtest_options_talents_patch_the_playtest_run()
    {
        var plain = PlaytestRunner.Play(new PlaytestOptions(Key, Seed, null) { MaxTicks = 1 });
        var talented = PlaytestRunner.Play(new PlaytestOptions(Key, Seed, null)
        {
            MaxTicks = 1,
            Talents = new[] { new SeatTalent(ChanterSeat, "encore") },
        });

        Assert.False(ChorusEchoes(plain.Run));
        Assert.True(ChorusEchoes(talented.Run));
    }
}
