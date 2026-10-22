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
/// Finding f-454a45a6 (D15 frame row for #442): the ECHO chip on PartyFrames. #442 gave Encore's <c>chorusEcho</c>
/// effect a chip label and the Buff palette colour, but StatusChips lived in the Godot-bound PartyFrames.cs and had no
/// headless test. It now lives in StatusChips.cs (linked here), and this guard drives the HUD run C shape headlessly, as
/// SimDriver.StartRun / StepOnce do (HostRunSetup.Build at the default key and seed with bots on, the bots' default
/// loadouts, BossRules.Arm, seat 1 the autorun <see cref="ScriptedWarden"/>, seats 2 and 3 <see cref="BotCommandSource"/>,
/// each step through <see cref="Backfill.Step"/>), reading every party frame's chips through <see cref="StatusChips.For"/> (PartyFrameModel.Build's read)
/// after each step as PartyFrames.LogChips does.
/// <para>
/// Encore is a talent, and talents are the human's only (RunStart.Of / QueuedKit.Queue patch <c>Loadouts[human]</c>),
/// so the default run's bot Chanter never applies <c>chorusEcho</c> and no ECHO chip is drawn there (the control case
/// below). The Encore case gives the bot Chanter (seat 2) the Encore-patched Chorus as its loadout, built the way
/// QueuedKit.Queue builds a talented kit (content patch, then HostRunSetup.Build over the patched abilities).
/// </para>
/// </summary>
public sealed class StatusChipsEchoTests
{
    private const long Seed = 1;            // ArenaOptions.DefaultSeed
    private const int Key = 2;              // ArenaOptions.DefaultKeyLevel
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = Lobby.DefaultClassId;
    private const string Chorus = "chanter.chorus";
    private const int ChanterSeat = 2;      // HostRunSetup.Roster: Warden 1, Chanter 2, Cinder 3
    private const int Ticks = 2000;
    private const int EchoDurationTicks = 50; // content/data/classes/chanter.json Encore mend variant

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the measured ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public StatusChipsEchoTests(ITestOutputHelper output) => _output = output;

    /// <summary>One chip as a frame showed it: the post-step tick, the seat and the chip.</summary>
    private sealed record Seen(int Tick, int Seat, StatusChip Chip);

    /// <summary>Encore's Chorus as the sim runs it, built the way QueuedKit.Queue builds a talented kit.</summary>
    private static AbilityDef EncoreChorus(C.ContentRegistry registry)
    {
        var option = registry.Classes["chanter"].Talents.First(t => t.AbilityId == Chorus).Options.First(o => o.Id == "encore");
        var patched = C.TalentPicks.ApplyPatch(registry.Abilities[Chorus], option.Patch);
        var abilities = new C.ValueMap<C.AbilityDef>(registry.Abilities.Select(kv =>
            kv.Key == Chorus ? new KeyValuePair<string, C.AbilityDef>(kv.Key, patched) : kv));
        return HostRunSetup.Build(registry with { Abilities = abilities }, DungeonId, Key, HumanClassId, withBots: true).Content.Abilities[Chorus];
    }

    /// <summary>The run C shape for <see cref="Ticks"/> ticks; every chip any party frame shows after each step.</summary>
    private static List<Seen> DriveRun(bool encoreChanter)
    {
        var registry = C.ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        if (encoreChanter)
        {
            var loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>(options.Loadouts ?? new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>());
            var kit = new Dictionary<string, AbilityDef>(
                loadouts.TryGetValue(ChanterSeat, out var carried) ? carried : new Dictionary<string, AbilityDef>(), StringComparer.Ordinal)
            {
                [Chorus] = EncoreChorus(registry),
            };
            loadouts[ChanterSeat] = kit;
            options = options with { Loadouts = loadouts };
        }
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var backfill = BackfillState.Empty;
        var seen = new List<Seen>();
        for (var i = 0; i < Ticks; i++)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            // PartyFrameModel.Build's chip read (PartyFrames.cs is Godot-bound): every player actor, the run's effect ids.
            var known = run.Content.Effects.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (var actor in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player))
            {
                seen.AddRange(StatusChips.For(actor, run.State.Tick, known).Select(c => new Seen(run.State.Tick, actor.Id, c)));
            }
        }
        return seen;
    }

    [Fact(DisplayName = "ECHO chip (f-454a45a6): with an Encore Chanter in the run C party, a frame shows ECHO in the Buff colour")]
    public void Encore_chanter_puts_an_echo_chip_in_the_buff_colour_on_a_party_frame()
    {
        var seen = DriveRun(encoreChanter: true);
        var echoes = seen.Where(s => s.Chip.Id == "chorusEcho").ToList();
        var firsts = echoes.Where((s, i) => i == 0 || !echoes.Any(p => p.Seat == s.Seat && p.Tick == s.Tick - 1)).ToList();
        foreach (var s in firsts)
        {
            _output.WriteLine(s.Chip.ProofLine(s.Seat, s.Tick));
        }

        Assert.NotEmpty(echoes);
        Assert.All(echoes, s =>
        {
            Assert.Equal("ECHO", s.Chip.Label);
            Assert.Equal(StatusChips.BuffRgb, s.Chip.Rgb);
            Assert.Equal(0x7dffb0, s.Chip.Rgb);
            Assert.InRange(s.Chip.TicksLeft, 1, EchoDurationTicks);
        });
        var first = echoes[0];
        Assert.Matches(@"^HUD chip seat=[123] id=chorusEcho label=ECHO colour=7dffb0 at=\d+$", first.Chip.ProofLine(first.Seat, first.Tick));
    }

    [Fact(DisplayName = "ECHO chip (f-454a45a6) control: the default run C party (no talents on bots) never shows ECHO")]
    public void Default_run_c_party_never_shows_an_echo_chip()
    {
        var seen = DriveRun(encoreChanter: false);

        // Non-vacuity: the frames do show chips in this run (the Chanter's Ward shield, the Warden's Brace).
        Assert.Contains(seen, s => s.Chip.Id != "downed");
        Assert.DoesNotContain(seen, s => s.Chip.Id == "chorusEcho");
    }
}
