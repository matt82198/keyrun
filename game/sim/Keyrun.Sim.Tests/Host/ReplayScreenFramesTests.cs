using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Host;
using Xunit;

namespace Keyrun.Sim.Tests.Host;

/// <summary>
/// Finding f-6a00609e: a <c>--replay-input</c> log must be able to drive the between-runs Screen and its talent panel
/// before any run exists, on spaced ticks, so the D15 capture can show them. Before the fix the replay read only
/// <c>SimDriver.TicksRun</c>, which never advances while there is no run, so every event after tick 0 waited for Enter.
/// The rule (<see cref="ReplayClock"/>): until the launch's first run exists, the replay clock is the count of host
/// frames the first Screen has been up (0 = the frame it opens); from the first run on it is those frames plus
/// <c>TicksRun</c> (ruling R-CLOCK-1, finding f-6b922005), and the recorder stamps the same value, so a live recording
/// round-trips. These tests drive the real Godot-free pieces SimDriver uses (<see cref="InputLogDocument"/>,
/// <see cref="ReplayClock"/>, <see cref="ReplayFeed{T}"/>) through a frame loop in SimDriver._Process's order: a replay
/// pass at the top of the frame, the screen frame counted when no run exists, otherwise one step per frame (60 Hz sim at
/// 60 fps) with a replay pass before it. A replayed Enter on the first Screen starts the run in the same frame, as
/// Screen.Queue -> SimDriver.OnQueued -> QueuedRunGate (host answers ready at the pop for a log without readyKeys) ->
/// StartQueuedRun does synchronously.
/// </summary>
public sealed class ReplayScreenFramesTests
{
    // C:/Users/Mattt/Desktop/keyrun-screens/talents/talent-keys-spaced.input-log.json, byte for byte (outside the repo).
    private const string SpacedTalentKeys =
        "{\n" +
        "  \"schema\": \"keyrun.input-log/1\",\n" +
        "  \"seed\": 1,\n" +
        "  \"key\": 2,\n" +
        "  \"events\": [\n" +
        "    { \"tick\": 0, \"key\": \"T\", \"pressed\": true },\n" +
        "    { \"tick\": 40, \"key\": \"Key1\", \"pressed\": true },\n" +
        "    { \"tick\": 60, \"key\": \"Down\", \"pressed\": true },\n" +
        "    { \"tick\": 80, \"key\": \"Key2\", \"pressed\": true },\n" +
        "    { \"tick\": 100, \"key\": \"Backspace\", \"pressed\": true },\n" +
        "    { \"tick\": 120, \"key\": \"Key1\", \"pressed\": true },\n" +
        "    { \"tick\": 160, \"key\": \"T\", \"pressed\": true },\n" +
        "    { \"tick\": 200, \"key\": \"Enter\", \"pressed\": true }\n" +
        "  ]\n" +
        "}\n";

    private const string EnterPingPath = "game/fixtures/input/example-enter-ping.input-log.json";

    private sealed record Fired(string Key, int Frame, bool RunExisted, int TicksRun);

    // SimDriver._Process in order, for a replay of a log without readyKeys on the interactive launch (StartsOnScreen).
    private static List<Fired> Play(InputLogDocument log, int frames)
    {
        var clock = new ReplayClock();
        var feed = new ReplayFeed<InputLogRawEvent>(log.Events, e => e.Tick);
        var fired = new List<Fired>();
        var run = false;
        var ticksRun = 0;
        void Replay(int frame)
        {
            foreach (var e in feed.Due(clock.Now(run, ticksRun)))
            {
                fired.Add(new Fired(e.Key, frame, run, ticksRun));
                if (!run && e.Key == "Enter" && e.Pressed)
                {
                    run = true; // Screen.Queue -> StartQueuedRun, inside the same Input delivery
                }
            }
        }
        for (var frame = 0; frame < frames; frame++)
        {
            Replay(frame);
            if (!run)
            {
                clock.EndFrame(run, screenUp: true);
                continue;
            }
            Replay(frame);
            ticksRun++;
        }
        return fired;
    }

    [Fact(DisplayName = "f-6a00609e: the spaced talent-keys log fires T at frame 0, the picks at 40/80/120, T at 160 and Enter at 200, all before any run exists")]
    public void Spaced_screen_events_fire_on_screen_frames_before_the_run()
    {
        var log = InputLogDocument.Parse(SpacedTalentKeys, "talent-keys-spaced");
        var fired = Play(log, 400);
        var expected = new[]
        {
            new Fired("T", 0, false, 0),
            new Fired("Key1", 40, false, 0),
            new Fired("Down", 60, false, 0),
            new Fired("Key2", 80, false, 0),
            new Fired("Backspace", 100, false, 0),
            new Fired("Key1", 120, false, 0),
            new Fired("T", 160, false, 0),
            new Fired("Enter", 200, false, 0),
        };
        Assert.Equal(expected, fired);
    }

    [Fact(DisplayName = "f-6a00609e: once the run exists an event fires on its sim tick (example-enter-ping: Enter at frame 0, G at sim tick 2, the run-end Enters at 4606 and 4609)")]
    public void Events_after_the_run_starts_keep_sim_tick_semantics()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, EnterPingPath)))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var log = InputLogDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, EnterPingPath)), EnterPingPath);
        var fired = Play(log, 5400);
        // Golden re-recorded 2026-10-21 on 24916b4b under the sim-tick recorder (R-CLOCK-2): Enter 0 -> 9, G 2 -> 11, Enter 694 -> 703, Enter 697 -> 706 (run starts on screen frame 9, so each run tick is +9 on the frame).
        // BOSS-TEACHES (R-BOSS-1, the Warden fight lasts 90-180 s): run K's run_end moved 691 -> 4603 (HudSceneRunsBKTests
        // measures it headlessly), so the run-end Enters moved 703/706 -> 4615/4618 (sim ticks 4606/4609). BOSS-CC-IMMUNE
        // (the boss refuses Steam's blind and lands its hits): run_end 4603 -> 5170, Enters 5182/5185 (sim 5173/5176).
        // R-LOCK-SETTER (#573, the bots' Lock setter is a damage seat whose Setup lands inside the Window, ahead of a
        // Payoff): run K's boss fight changed, run_end 5170 -> 5200, Enters 5212/5215 (sim 5203/5206).
        // TEMPO-LOOP-CLAMP (f-57c04b4a, no Cleave dropped inside its cooldown), measured on the tree merged with #573:
        // run_end 5200 -> 5299, Enters 5311/5314 (sim 5302/5305).
        var expected = new[]
        {
            new Fired("Enter", 9, false, 0),
            new Fired("G", 11, true, 2),
            new Fired("Enter", 5311, true, 5302),
            new Fired("Enter", 5314, true, 5305),
        };
        Assert.Equal(expected, fired);
    }

    // Ruling R-CLOCK-1 (orchestrator 2026-10-21, finding f-6b922005; supersedes #469's "fires at sim tick 250, not 50"):
    // the input log has ONE clock, ScreenFramesAtRunStart + TicksRun once the run exists, so a tick past the run start
    // is read from the screen frame the run started on.
    [Fact(DisplayName = "R-CLOCK-1 (f-6b922005): one clock across Screen and run: an event stamped 250 after an Enter at screen frame 200 fires at sim tick 50")]
    public void A_tick_past_the_run_start_is_the_screen_frames_at_run_start_plus_the_sim_tick()
    {
        var json = "{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [" +
            "{ \"tick\": 200, \"key\": \"Enter\", \"pressed\": true }, { \"tick\": 250, \"key\": \"W\", \"pressed\": true } ] }";
        var fired = Play(InputLogDocument.Parse(json, "t"), 600);
        Assert.Equal(new[] { new Fired("Enter", 200, false, 0), new Fired("W", 250, true, 50) }, fired);
    }

    private sealed record Live(string Key, int At);

    // A live --record-input launch in SimDriver's order: a key arrives in _Input before the frame's _Process and is
    // stamped with the recorder's clock (ReplayClock.Stamp, the call SimDriver makes); a first-Screen Enter starts the run
    // in that delivery; a frame with no run counts one screen frame, a frame with a run steps once. Screen keys are
    // scheduled by frame (no run yet), run keys by TicksRun. Returns the stamps and the recorded log text.
    private static (List<int> Stamps, string Json) Record(IReadOnlyList<Live> screenKeys, IReadOnlyList<Live> runKeys, int frames)
    {
        var clock = new ReplayClock();
        var stamps = new List<int>();
        var events = new List<string>();
        var run = false;
        var ticksRun = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var due = run ? runKeys.Where(k => k.At == ticksRun).ToList() : screenKeys.Where(k => k.At == frame).ToList();
            foreach (var key in due)
            {
                var stamp = clock.Stamp(run, ticksRun);
                stamps.Add(stamp);
                events.Add($"{{ \"tick\": {stamp}, \"key\": \"{key.Key}\", \"pressed\": true }}");
                if (!run && key.Key == "Enter")
                {
                    run = true; // Screen.Queue -> StartQueuedRun, inside the same Input delivery
                }
            }
            if (!run)
            {
                clock.EndFrame(run, screenUp: true);
                continue;
            }
            ticksRun++;
        }
        var json = "{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [" + string.Join(", ", events) + "] }";
        return (stamps, json);
    }

    [Fact(DisplayName = "R-CLOCK-1 (f-6b922005): a live recording (T at frame 0, Key1 at 40, Enter at 200 starting the run, W at TicksRun 10) parses and replays W at TicksRun 10")]
    public void A_live_recording_across_screen_and_run_round_trips()
    {
        var (stamps, json) = Record(
            new[] { new Live("T", 0), new Live("Key1", 40), new Live("Enter", 200) },
            new[] { new Live("W", 10) },
            400);
        Assert.Equal(new[] { 0, 40, 200, 210 }, stamps);
        var fired = Play(InputLogDocument.Parse(json, "recorded"), 400);
        Assert.Equal(new[]
        {
            new Fired("T", 0, false, 0),
            new Fired("Key1", 40, false, 0),
            new Fired("Enter", 200, false, 0),
            new Fired("W", 210, true, 10),
        }, fired);
    }

    [Fact(DisplayName = "f-6a00609e: the committed fixtures and the spaced log parse with their headers and ticks intact")]
    public void Well_formed_logs_parse()
    {
        var spaced = InputLogDocument.Parse(SpacedTalentKeys, "t");
        Assert.Equal(InputLogDocument.SchemaV1, spaced.Schema);
        Assert.Equal((1L, 2), (spaced.Seed, spaced.KeyLevel));
        Assert.Equal(new[] { 0, 40, 60, 80, 100, 120, 160, 200 }, spaced.Events.Select(e => e.Tick));
        var yaw = InputLogDocument.Parse(
            "{ \"schema\": \"keyrun.input-log/2\", \"seed\": 1, \"key\": 2, \"rotationIndex\": 3, \"startHeading\": -1.5707964, \"readyKeys\": true, " +
            "\"events\": [ { \"tick\": 0, \"key\": \"Enter\", \"pressed\": true }, { \"tick\": 0, \"key\": \"W\", \"pressed\": false } ], " +
            "\"camera\": [ { \"tick\": 60, \"yaw\": 1.4999998 } ] }", "t");
        Assert.Equal(3, yaw.RotationIndex);
        Assert.Equal(-1.5707964f, yaw.StartHeading);
        Assert.True(yaw.ReadyKeys);
        Assert.Equal(new InputLogCamera(60, 1.4999998f), Assert.Single(yaw.Camera));
    }

    [Theory(DisplayName = "f-6a00609e: a malformed input log still fails closed")]
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [ { \"tick\": 40, \"frame\": 40, \"key\": \"T\", \"pressed\": true } ] }")] // unknown event field
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"screenFrames\": true, \"events\": [] }")] // unknown header field
    [InlineData("{ \"schema\": \"keyrun.input-log/2\", \"seed\": 1, \"key\": 2, \"events\": [], \"camera\": [ { \"tick\": 1, \"yaw\": 0.5, \"frame\": 1 } ] }")] // unknown camera field
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [ { \"tick\": 40, \"key\": \"T\", \"pressed\": true }, { \"tick\": 39, \"key\": \"T\", \"pressed\": false } ] }")] // tick goes down
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [ { \"tick\": -1, \"key\": \"T\", \"pressed\": true } ] }")] // negative tick
    [InlineData("{ \"schema\": \"keyrun.input-log/2\", \"seed\": 1, \"key\": 2, \"events\": [], \"camera\": [ { \"tick\": 9, \"yaw\": 0.5 }, { \"tick\": 8, \"yaw\": 0.5 } ] }")] // camera tick goes down
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [ { \"tick\": 0, \"key\": \"T\" } ] }")] // missing pressed
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [ { \"tick\": 0, \"key\": \"\", \"pressed\": true } ] }")] // empty key name
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [], \"camera\": [] }")] // camera list in a /1 file
    [InlineData("{ \"schema\": \"keyrun.input-log/2\", \"seed\": 1, \"key\": 2, \"events\": [] }")] // /2 without a camera list
    [InlineData("{ \"schema\": \"keyrun.input-log/3\", \"seed\": 1, \"key\": 2, \"events\": [] }")] // unknown schema
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2 }")] // no events
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"events\": [ { \"tick\": 1.5, \"key\": \"T\", \"pressed\": true } ] }")] // fractional tick
    [InlineData("{ \"schema\": \"keyrun.input-log/1\", \"seed\": 1, \"key\": 2, \"startHeading\": \"x\", \"events\": [] }")] // bad startHeading
    [InlineData("not json")]
    public void A_malformed_log_is_refused(string json)
    {
        Assert.Throws<ArgumentException>(() => InputLogDocument.Parse(json, "t"));
    }
}
