using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-e1ff13e3 (follow-up to PR #347, f-50d8099e): the <c>--write-movie</c> proof frames are evidence for Matt
/// (D15), and evidence is deterministic. #347 made <see cref="MovieDirector"/> pick each frame from the trace, but its
/// own tests (MovieDirectorTests) drive hand-built states. This guard drives the real default <c>--write-movie</c> run
/// headlessly, built and stepped as SimDriver.StartRun / StepOnce do: HostRunSetup.Build at the default key and seed with
/// bots on, the bots' default loadouts, BossRules.Arm, seat 1 the autorun <see cref="ScriptedWarden"/> (PlayerInput's
/// scripted path), seats 2 and 3 <see cref="BotCommandSource"/> at its default dial, each step through
/// <see cref="Backfill.Step"/> with an empty queue, for the default tick count, feeding one <see cref="MovieDirector"/>
/// with each step's before/after states and events exactly as SimDriver.MaybeCapture does (the capture tick is the
/// post-step tick it hands CaptureAfterDraw), at the default and the first rotating-affix key, at every weekly rotation
/// index. A <c>--write-movie</c> launch without <c>--rotation N</c> is pinned to <see cref="AffixWeek.MovieRotationIndex"/>,
/// so two launches in different weeks capture the same frames at the same ticks.
/// <para>SimDriver.cs and its ArenaOptions are Godot-bound and cannot be linked here, so their defaults are mirrored as
/// literals below, each naming its source line.</para>
/// </summary>
public sealed class MovieFramesDefaultRunTests
{
    /// <summary>Mirrors ArenaOptions.DefaultKeyLevel (game/src/Host/SimDriver.cs:181).</summary>
    private const int Key = 2;

    /// <summary>Mirrors ArenaOptions.DefaultSeed (game/src/Host/SimDriver.cs:184).</summary>
    private const long Seed = 1;

    /// <summary>Mirrors ArenaOptions.DefaultTicks (game/src/Host/SimDriver.cs:178), the --write-movie run's length.</summary>
    private const int DefaultTicks = 400;

    /// <summary>Mirrors SimDriver.DungeonId (game/src/Host/SimDriver.cs:433).</summary>
    private const string DungeonId = "hollow-vault";

    /// <summary>Mirrors SimDriver.WardenId (game/src/Host/SimDriver.cs:430).</summary>
    private const int WardenId = 1;

    /// <summary>SimDriver.HumanClassId is Lobby.DefaultClassId (game/src/Host/SimDriver.cs:439); Lobby is linked here.</summary>
    private const string HumanClassId = Lobby.DefaultClassId;

    /// <summary>The only interruptible enemy ability in content/data/abilities/enemies.json: the Cantor's heal.</summary>
    private const string HealCast = "choir.healCast";

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the measured ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public MovieFramesDefaultRunTests(ITestOutputHelper output) => _output = output;

    /// <summary>One Cantor heal cast as the run played it: the first and last post-step tick it was being cast.</summary>
    private sealed record HealWindow(int ActorId, int StartTick, int LastCastingTick, int? EndsTick);

    /// <summary>One frame the director picked: its file, the post-step capture tick, the event it names, the events of
    /// the step it was captured on, and what the frame shows (every actor's id, position, health and cast then).</summary>
    private sealed record Shot(string Name, int CaptureTick, string Event, int EventTick, IReadOnlyList<SimEvent> StepEvents, string Shows);

    private static string Shows(SimState s) => string.Join(";", s.Actors.Values.OrderBy(a => a.Id).Select(a =>
        FormattableString.Invariant($"{a.Id}@({a.Pos.X},{a.Pos.Z}) hp={a.Health}/{a.MaxHealth} cast={a.CastId ?? "-"}")));

    private sealed record Drive(int Rotation, List<HealWindow> Heals, List<Shot> Shots, List<string> Kicks);

    /// <summary>The <c>--write-movie</c> run at <paramref name="key"/> (<c>--key</c>) for <paramref name="ticks"/> ticks.</summary>
    private static Drive DriveRun(int key, int rotation, int ticks)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, key, HumanClassId, withBots: true, rotationIndex: rotation);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var director = new MovieDirector(WardenId, run.Content.Abilities);
        var backfill = BackfillState.Empty;
        var heals = new List<HealWindow>();
        var open = new Dictionary<int, int>();
        var shots = new List<Shot>();
        var kicks = new List<string>();
        for (var i = 0; i < ticks; i++)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            // SimDriver.StepOnce: the human's commands first (PlayerInput's scripted path), then every bot seat's.
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            var before = run.State;
            run = step.Step.Run;
            var after = run.State;
            foreach (var a in after.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.ClassId == ScriptedWarden.LieutenantClass))
            {
                var casting = a.Alive && a.CastId == HealCast && a.CastStartTick is not null;
                if (casting && (!open.TryGetValue(a.Id, out var at) || heals[at].StartTick != a.CastStartTick))
                {
                    open[a.Id] = heals.Count;
                    heals.Add(new HealWindow(a.Id, a.CastStartTick!.Value, after.Tick, a.CastEndsTick));
                }
                else if (casting)
                {
                    heals[open[a.Id]] = heals[open[a.Id]] with { LastCastingTick = after.Tick };
                }
                else
                {
                    open.Remove(a.Id);
                }
            }
            foreach (var e in step.Step.Events.Where(KicksHeal))
            {
                var by = e.Get("sourceId") is JsInt src ? src.Value : 0;
                kicks.Add($"{e.Type} event_tick={e.Tick} post_step_tick={after.Tick} by={by}");
            }
            foreach (var s in director.Observe(before, after, step.Step.Events))
            {
                shots.Add(new Shot(s.Name, after.Tick, s.Event, s.EventTick, step.Step.Events, Shows(after)));
            }
        }
        return new Drive(rotation, heals, shots, kicks);
    }

    private static int RotationLength() => AffixWeek.RotationLength(ContentLoader.Load().Clauses.Count);

    private void Print(Drive d)
    {
        foreach (var h in d.Heals)
        {
            _output.WriteLine($"MOVIE rotation={d.Rotation} heal actor={h.ActorId} start={h.StartTick} lastCasting={h.LastCastingTick} endsTick={h.EndsTick}");
        }
        foreach (var k in d.Kicks)
        {
            _output.WriteLine($"MOVIE rotation={d.Rotation} kick {k}");
        }
        foreach (var s in d.Shots)
        {
            _output.WriteLine($"MOVIE rotation={d.Rotation} frame {s.Name} capture_tick={s.CaptureTick} event=\"{s.Event}\" event_tick={s.EventTick}");
        }
    }

    private static bool KicksHeal(SimEvent e) =>
        (e.Type == "interrupt" && e.Get("abilityId") is JsString kicked && kicked.Value == HealCast) ||
        (e.Type == "cast_stopped" && e.Get("abilityId") is JsString stopped && stopped.Value == HealCast);

    /// <summary>
    /// The framed launches: <c>--write-movie --ticks 1200</c> at the default key and at the first rotating-affix key
    /// (run.json affixes.oneAffixKey), at every weekly rotation index (<c>--rotation N</c>). 1200 ticks, because at the
    /// default 400 the first heal (272) is kicked at 274, before <see cref="MovieDirector.CastBarLeadTicks"/>, and no
    /// cast-bar frame is due until the second heal (664); that default-length gap is reported to the orchestrator, not
    /// asserted here.
    /// </summary>
    public static IEnumerable<object[]> FramedLaunchData()
    {
        var oneAffixKey = ContentLoader.Load().Run.Affixes.OneAffixKey;
        return new[] { Key, oneAffixKey }
            .SelectMany(k => Enumerable.Range(0, RotationLength()).Select(r => new object[] { k, FramedTicks, r }));
    }

    /// <summary>The <c>--ticks</c> of <see cref="FramedLaunchData"/>.</summary>
    private const int FramedTicks = 1200;

    [Theory(DisplayName = "Movie (f-e1ff13e3): in the real --write-movie run frame 02 is taken inside a Cantor heal cast window")]
    [MemberData(nameof(FramedLaunchData))]
    public void Cast_bar_frame_lands_inside_a_cantor_heal_cast(int key, int ticks, int rotation)
    {
        var d = DriveRun(key, rotation, ticks);
        _output.WriteLine($"MOVIE key={key} ticks={ticks}");
        Print(d);
        // Non-vacuity: the Cantor must start a heal inside the run, or there is no cast bar to show.
        Assert.True(d.Heals.Count >= 1, $"rotation {rotation}: the Cantor started no {HealCast} in {ticks} ticks");
        var bar = d.Shots.SingleOrDefault(s => s.Name == MovieDirector.CastBarFrame);
        Assert.True(bar is not null, $"key {key} rotation {rotation}: no {MovieDirector.CastBarFrame} in {ticks} ticks");
        Assert.Contains($"ability={HealCast}", bar!.Event, StringComparison.Ordinal);
        Assert.True(d.Heals.Any(h => bar.CaptureTick >= h.StartTick && bar.CaptureTick <= h.LastCastingTick),
            $"key {key} rotation {rotation}: {MovieDirector.CastBarFrame} captured at tick {bar.CaptureTick}, outside every Cantor heal window " +
            string.Join(", ", d.Heals.Select(h => $"[{h.StartTick}..{h.LastCastingTick}]")));
    }

    [Theory(DisplayName = "Movie (f-e1ff13e3): in the real --write-movie run frame 03 lands on the step that kicks that heal")]
    [MemberData(nameof(FramedLaunchData))]
    public void After_kick_frame_lands_on_the_interrupt_step(int key, int ticks, int rotation)
    {
        var d = DriveRun(key, rotation, ticks);
        var bar = d.Shots.SingleOrDefault(s => s.Name == MovieDirector.CastBarFrame);
        var kick = d.Shots.SingleOrDefault(s => s.Name == MovieDirector.AfterKickFrame);
        Assert.True(bar is not null && kick is not null, $"key {key} rotation {rotation}: frames {string.Join(",", d.Shots.Select(s => s.Name))}");
        var window = d.Heals.Single(h => bar!.CaptureTick >= h.StartTick && bar.CaptureTick <= h.LastCastingTick);
        // The step that carried the kick of that heal: its events hold the interrupt (or cast_stopped) of choir.healCast,
        // and it is the step right after the last step the heal was still being cast.
        Assert.Contains(kick!.StepEvents, KicksHeal);
        Assert.Contains(HealCast, kick.Event, StringComparison.Ordinal);
        Assert.Equal(window.LastCastingTick + 1, kick.CaptureTick);
    }

    /// <summary>The launches the determinism fact covers: the default <c>--write-movie</c> (key 2, below every affix),
    /// <c>--write-movie --key K</c> at the first rotating-affix key from content (run.json affixes.oneAffixKey, where the
    /// week flips Fortified and Tyrannical), and the same with <c>--ticks 1200</c> (long enough for all three frames).</summary>
    public static IEnumerable<object[]> LaunchData()
    {
        var oneAffixKey = ContentLoader.Load().Run.Affixes.OneAffixKey;
        yield return new object[] { Key, DefaultTicks };
        yield return new object[] { oneAffixKey, DefaultTicks };
        yield return new object[] { oneAffixKey, 1200 };
    }

    [Theory(DisplayName = "Movie (f-e1ff13e3): two --write-movie launches in different weeks capture the same frames at the same ticks")]
    [MemberData(nameof(LaunchData))]
    public void Write_movie_frames_do_not_depend_on_the_week(int key, int ticks)
    {
        var length = RotationLength();
        // One --write-movie launch per week across a whole rotation (no --rotation, no replay, so the clock is all that
        // differs): the index the host builds the run with (SimDriver._Ready's AffixWeek.ForRun call) must not follow it.
        var launches = Enumerable.Range(0, length)
            .Select(w => AffixWeek.EpochReset.AddDays(7 * w).AddHours(1))
            .Select(now => (now, index: AffixWeek.ForRun(replaying: false, recorded: null, overrideIndex: null, now, length, writeMovie: true)))
            .Select(l => (l.now, l.index, shots: DriveRun(key, l.index, ticks).Shots))
            .ToList();
        foreach (var l in launches)
        {
            _output.WriteLine($"MOVIE key={key} ticks={ticks} launch utc={l.now:yyyy-MM-dd HH:mm} rotation={l.index} frames " +
                string.Join(" ", l.shots.Select(s => $"{s.Name}@{s.CaptureTick}")));
        }
        Assert.NotEmpty(launches[0].shots);
        // Same frames, same capture ticks, and the same picture in each (every actor's position, health and cast).
        Assert.Single(launches.Select(l => string.Join("|", l.shots.Select(s => $"{s.Name}@{s.CaptureTick}:{s.Shows}"))).Distinct());
    }
}
