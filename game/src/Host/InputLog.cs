using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace Keyrun.Host;

/// <summary>
/// One recorded key transition of a <c>keyrun.input-log/1</c> file (lane 3E-b): at host tick <paramref name="Tick"/> the
/// physical key <paramref name="Key"/> went down (<paramref name="Pressed"/> true) or up.
/// </summary>
/// <param name="Tick">The input-log clock value (<see cref="ReplayClock"/>, findings f-6a00609e and f-6b922005, ruling
/// R-CLOCK-1): a first-Screen frame until the launch's first run exists, then the screen frames counted before the run
/// started plus <see cref="SimDriver.TicksRun"/> (ticks stepped since launch, across runs). A recording stamps it
/// (<see cref="ReplayClock.Stamp"/>) and a replay reads it (<see cref="ReplayClock.Now"/>), so a live recording
/// round-trips. The event is applied before the next step.</param>
/// <param name="Key">The Godot physical key (its <see cref="Godot.Key"/> name in the file, e.g. <c>W</c>, <c>Enter</c>,
/// <c>Key1</c>, <c>Tab</c>).</param>
/// <param name="Pressed">True for a press, false for a release.</param>
public sealed record InputLogEvent(int Tick, Key Key, bool Pressed);

/// <summary>
/// The <c>keyrun.input-log</c> file (lane 3E-b, ENGINE-PLAN row 3E; camera headings lane 3E-c): the keys a human pressed
/// during an interactive run, tick-stamped, so <c>--replay-input</c> can play them back through the real input path.
/// Shape <c>/1</c>: <c>{ "schema": "keyrun.input-log/1", "seed": S, "key": K, "events": [ { "tick": N, "key": "W",
/// "pressed": true } ] }</c>. Shape <c>/2</c> adds <c>"camera": [ { "tick": N, "yaw": Y } ]</c> after the events (Y in
/// radians, see <see cref="InputLogCamera"/>). A log is written as <c>/2</c> exactly when it holds a camera event, so a
/// run whose camera never turned writes the same <c>/1</c> bytes as before 3E-c; both shapes are read (a <c>/1</c> file
/// has no camera list). UTF-8, no BOM, LF, one event per line. Seed and key are the launch's <c>--seed</c> and
/// <c>--key</c>; a replay under a different seed or key is refused, because the same keys would not produce the same run.
/// Lane HOST-HEADING: either shape may carry an optional <c>"startHeading": Y</c> header line after <c>key</c>, the
/// CameraRig heading (radians, the <see cref="FormatYaw"/> text) the run started at. It is optional on <c>/1</c> too,
/// because a recording whose camera never turned is a <c>/1</c> file and still starts at a heading. A file without it
/// (every log written before HOST-HEADING, including the committed fixtures) replays from the legacy heading, unchanged.
/// Weekly affix rotation: either shape may carry an optional <c>"rotationIndex": N</c> header line after <c>key</c> (and
/// before <c>startHeading</c>), the <see cref="AffixWeek"/> index the run was built with; every recording writes it. A
/// replay builds its run at that index, never at the replay's own week; a file without it replays at 0.
/// Finding f-e24b4fa4 (BIBLE s10 ready check on pop): either shape may carry an optional <c>"readyKeys": true</c> header
/// line (after <c>startHeading</c>), written only when the human answered a queue pop's ready check with a key during
/// the recording, so the answers are among the events. A replay of such a log leaves the ready answer to those keys; a
/// file without it (every log written before the ready check, including the committed fixtures, and every
/// <c>--press-enter</c> recording, whose ready answers the host gives at the pop) replays with the host answering ready
/// for the human at the pop (<see cref="QueuedRunGate.HumanReadyAtPop"/>), so its run starts on the tick it always did.
/// Findings f-6a00609e and f-6b922005 (ruling R-CLOCK-1): every tick (events and camera) is on ONE clock,
/// <see cref="ReplayClock"/>, which the recorder stamps and a replay reads: until the launch's first run exists it counts
/// the host frames the first Screen has been up (0 = the frame it is hooked); from the first run on it is those frames
/// (frozen at the run start) plus <see cref="SimDriver.TicksRun"/>. So it never goes down, a live recording with keys on
/// the Screen and in the run's first ticks is monotonic and replays on the frames and ticks it was recorded on, a
/// hand-written log can drive the Screen and its talent panel on spaced frames (T at 0, picks at 40/80/120, Enter at 200),
/// and every log whose Enter is on frame 0 (every committed fixture) replays on the ticks it always did. The file shape
/// is unchanged; the parse (<see cref="InputLogDocument.Parse"/>, the Godot-free half) also refuses any field the shape
/// does not define, so an invented field is never silently ignored.
/// </summary>
public sealed class InputLog
{
    /// <summary>The key-only schema id (lane 3E-b).</summary>
    public const string SchemaV1 = InputLogDocument.SchemaV1;

    /// <summary>The schema id with camera headings (lane 3E-c).</summary>
    public const string SchemaV2 = InputLogDocument.SchemaV2;

    /// <summary>Builds a log.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="keyLevel">The launch key level.</param>
    /// <param name="events">The key events, ticks never decreasing.</param>
    /// <param name="camera">The camera headings, ticks never decreasing; null or empty for a key-only log.</param>
    /// <param name="startHeading">The heading the run started at (radians), or null when the log does not say (the
    /// legacy baseline).</param>
    /// <param name="rotationIndex">The weekly affix rotation index the run was built with (<see cref="AffixWeek"/>), or
    /// null when the log does not say (every log written before the weekly rotation; a replay then uses 0).</param>
    /// <param name="readyKeys">The human's ready-check answers are recorded keys (<see cref="ReadyKeys"/>).</param>
    /// <exception cref="ArgumentException">A tick is negative or goes down, or a yaw or the start heading is not
    /// finite, or the rotation index is negative.</exception>
    public InputLog(long seed, int keyLevel, IReadOnlyList<InputLogEvent> events, IReadOnlyList<InputLogCamera>? camera = null,
        float? startHeading = null, int? rotationIndex = null, bool readyKeys = false)
    {
        ReadyKeys = readyKeys;
        if (rotationIndex is < 0)
        {
            throw new ArgumentException($"input log: rotationIndex {rotationIndex} is negative");
        }
        RotationIndex = rotationIndex;
        ArgumentNullException.ThrowIfNull(events);
        camera ??= Array.Empty<InputLogCamera>();
        if (startHeading is { } heading && !float.IsFinite(heading))
        {
            throw new ArgumentException($"input log: startHeading {heading} is not finite");
        }
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Tick < 0 || (i > 0 && events[i].Tick < events[i - 1].Tick))
            {
                throw new ArgumentException($"input log: event {i} tick {events[i].Tick} is negative or before the previous event");
            }
        }
        for (var i = 0; i < camera.Count; i++)
        {
            if (camera[i].Tick < 0 || (i > 0 && camera[i].Tick < camera[i - 1].Tick) || !float.IsFinite(camera[i].Yaw))
            {
                throw new ArgumentException($"input log: camera {i} tick {camera[i].Tick} is negative or before the previous one, or its yaw is not finite");
            }
        }
        Seed = seed;
        KeyLevel = keyLevel;
        Events = events;
        Camera = camera;
        StartHeading = startHeading;
    }

    /// <summary>The CameraRig heading the recorded run started at (radians), or null: the file has no
    /// <c>startHeading</c> and a replay keeps the legacy heading.</summary>
    public float? StartHeading { get; }

    /// <summary>The weekly affix rotation index the recorded run was built with, or null (the file has no
    /// <c>rotationIndex</c>). A replay builds its run from this, never from the replay's date.</summary>
    public int? RotationIndex { get; }

    /// <summary>True when the human answered a ready check with a key while recording (the file's <c>readyKeys</c>):
    /// a replay leaves the answer to the replayed keys. False: the host answers ready for the human at the pop.</summary>
    public bool ReadyKeys { get; }

    /// <summary>The run seed the log was recorded at.</summary>
    public long Seed { get; }

    /// <summary>The launch key level the log was recorded at.</summary>
    public int KeyLevel { get; }

    /// <summary>The key transitions in order.</summary>
    public IReadOnlyList<InputLogEvent> Events { get; }

    /// <summary>The camera headings in order (empty for a <c>/1</c> log).</summary>
    public IReadOnlyList<InputLogCamera> Camera { get; }

    /// <summary>The schema the log is written as: <see cref="SchemaV2"/> when it holds a camera event, else
    /// <see cref="SchemaV1"/>.</summary>
    public string Schema => Camera.Count > 0 ? SchemaV2 : SchemaV1;

    /// <summary>The file text (deterministic: fixed key order, invariant numbers, LF, trailing newline).</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"schema\": \"{Schema}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"seed\": {Seed},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"key\": {KeyLevel},\n");
        if (RotationIndex is { } rotation)
        {
            sb.Append(AffixWeek.HeaderLine(rotation));
        }
        if (StartHeading is { } heading)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  \"startHeading\": {FormatYaw(heading)},\n");
        }
        if (ReadyKeys)
        {
            sb.Append("  \"readyKeys\": true,\n");
        }
        sb.Append("  \"events\": [");
        for (var i = 0; i < Events.Count; i++)
        {
            var e = Events[i];
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append(CultureInfo.InvariantCulture, $"    {{ \"tick\": {e.Tick}, \"key\": \"{e.Key}\", \"pressed\": {(e.Pressed ? "true" : "false")} }}");
        }
        sb.Append(Events.Count == 0 ? "]" : "\n  ]");
        if (Camera.Count > 0)
        {
            sb.Append(",\n  \"camera\": [");
            for (var i = 0; i < Camera.Count; i++)
            {
                var c = Camera[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append(CultureInfo.InvariantCulture, $"    {{ \"tick\": {c.Tick}, \"yaw\": {FormatYaw(c.Yaw)} }}");
            }
            sb.Append("\n  ]");
        }
        sb.Append("\n}\n");
        return sb.ToString();
    }

    /// <summary>A yaw as the file and the log lines write it: the shortest invariant text that reads back to the same
    /// float32.</summary>
    /// <param name="yaw">The yaw, radians.</param>
    /// <returns>The text.</returns>
    public static string FormatYaw(float yaw) => yaw.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Writes the file (its directory is created if missing).</summary>
    /// <param name="path">The file path.</param>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.GetFullPath(path);
        if (Path.GetDirectoryName(full) is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(full, ToJson(), new UTF8Encoding(false));
    }

    /// <summary>Reads a file, fail-closed.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The log.</returns>
    /// <exception cref="ArgumentException">Not a well-formed keyrun.input-log/1 or /2 file.</exception>
    public static InputLog Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Parses file text, fail-closed: an unknown schema, a missing or mistyped field, a field the shape does not
    /// define, an unknown key name, a tick that is negative or goes down, a camera list in a <c>/1</c> file or a <c>/2</c>
    /// file without one, and a <c>startHeading</c> that is present but not a finite number are all refused. The structure
    /// is <see cref="InputLogDocument.Parse"/> (Godot-free); this adds the key names (<see cref="ParseKey"/>).</summary>
    /// <param name="json">The text.</param>
    /// <param name="source">Where it came from (error messages).</param>
    /// <returns>The log.</returns>
    /// <exception cref="ArgumentException">Not a well-formed keyrun.input-log/1 or /2 document.</exception>
    public static InputLog Parse(string json, string source)
    {
        var doc = InputLogDocument.Parse(json, source);
        var events = new List<InputLogEvent>(doc.Events.Count);
        foreach (var e in doc.Events)
        {
            events.Add(new InputLogEvent(e.Tick, ParseKey(e.Key, $"{source}: event {events.Count}"), e.Pressed));
        }
        return new InputLog(doc.Seed, doc.KeyLevel, events, doc.Camera, doc.StartHeading, doc.RotationIndex, doc.ReadyKeys);
    }

    /// <summary>A Godot <see cref="Godot.Key"/> name (exact, case-sensitive, never a number) as the key.</summary>
    /// <param name="name">The name.</param>
    /// <param name="at">Where it came from (error messages).</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException">Not a named key, or <c>None</c>.</exception>
    public static Key ParseKey(string name, string at)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || char.IsDigit(name[0]) || !Enum.TryParse<Key>(name, ignoreCase: false, out var key) ||
            key == Key.None || !Enum.IsDefined(key))
        {
            throw new ArgumentException($"{at}: unknown key \"{name}\"");
        }
        return key;
    }
}

/// <summary>
/// <c>--record-input</c> (lane 3E-b): collects every key transition the game reads, stamped with the input-log clock
/// (SimDriver passes <see cref="ReplayClock.Stamp"/>, the value a replay reads; f-6b922005, R-CLOCK-1). Two
/// sources, so the log is complete whatever the event delivery does: <see cref="InputEventKey"/> events as the viewport delivers
/// them (every key PlayerInput, the between-runs screen, the run-end panel and SimDriver handle), and, before each step,
/// a diff of the keys PlayerInput polls (<see cref="PlayerInput.PolledKeys"/>, WASD) against what the events said, which
/// catches a release the engine applies without an event (focus loss). Echo repeats are skipped by the caller.
/// Lane 3E-c: before each step it also samples the camera heading WASD is relative to and records it whenever it differs
/// from the last one (<see cref="SyncCamera"/>; the first is compared with <see cref="BaselineCamera"/>).
/// </summary>
public sealed class InputRecorder
{
    private readonly List<InputLogEvent> _events = new();
    private readonly List<InputLogCamera> _camera = new();
    private readonly HashSet<Key> _down = new();
    private float? _lastYaw;

    /// <summary>The events recorded so far.</summary>
    public IReadOnlyList<InputLogEvent> Events => _events;

    /// <summary>The camera headings recorded so far.</summary>
    public IReadOnlyList<InputLogCamera> Camera => _camera;

    /// <summary>Records one delivered key event, as delivered: a second press with no release between (a scripted
    /// <c>Viewport.PushInput</c> press has none) is still a press every reader acts on, so it is kept.</summary>
    /// <param name="tick">The host tick.</param>
    /// <param name="key">The physical key.</param>
    /// <param name="pressed">Press or release.</param>
    public void OnKey(int tick, Key key, bool pressed)
    {
        if (key == Key.None)
        {
            return;
        }
        if (pressed)
        {
            _down.Add(key);
        }
        else
        {
            _down.Remove(key);
        }
        _events.Add(new InputLogEvent(tick, key, pressed));
    }

    /// <summary>Records the polled keys whose state differs from the events seen so far.</summary>
    /// <param name="tick">The host tick.</param>
    /// <param name="keys">The keys to poll.</param>
    /// <param name="isPressed">The poll (the engine's <c>Input.IsPhysicalKeyPressed</c>).</param>
    public void SyncPolled(int tick, IReadOnlyList<Key> keys, Func<Key, bool> isPressed)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(isPressed);
        foreach (var key in keys)
        {
            if (isPressed(key) != _down.Contains(key))
            {
                OnKey(tick, key, !_down.Contains(key));
            }
        }
    }

    /// <summary>The heading the rig starts at, sampled once before anything can turn it (SimDriver's first frame, after
    /// every node's <c>_Ready</c>). It is never a camera event; lane HOST-HEADING writes it as the log's
    /// <see cref="StartHeading"/> instead (SimDriver sets that).</summary>
    /// <param name="yaw">The rig's heading, radians.</param>
    public void BaselineCamera(float yaw) => _lastYaw ??= yaw;

    /// <summary>The <see cref="InputLog.StartHeading"/> the log is written with (lane HOST-HEADING): the sampled baseline
    /// on a plain recording, the replayed log's own value (null when it has none) while a replay is also recorded.</summary>
    public float? StartHeading { get; set; }

    /// <summary>Records the heading when it differs from the last recorded one (or the baseline). Called before each
    /// step with the value PlayerInput is about to read.</summary>
    /// <param name="tick">The host tick.</param>
    /// <param name="yaw">The rig's heading, radians.</param>
    public void SyncCamera(int tick, float yaw)
    {
        if (_lastYaw is { } last && last.Equals(yaw))
        {
            return;
        }
        _lastYaw = yaw;
        _camera.Add(new InputLogCamera(tick, yaw));
    }

    /// <summary>The log of what was recorded.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="keyLevel">The launch key level.</param>
    /// <param name="rotationIndex">The weekly affix rotation index the run was built with (<see cref="AffixWeek"/>).</param>
    /// <returns>The log.</returns>
    public InputLog ToLog(long seed, int keyLevel, int? rotationIndex = null) =>
        new(seed, keyLevel, _events.ToArray(), _camera.ToArray(), StartHeading, rotationIndex, ReadyKeys);

    /// <summary>Set once the human answers a ready check with a key while recording (SimDriver, from the Queue node):
    /// the log is written with <see cref="InputLog.ReadyKeys"/>.</summary>
    public bool ReadyKeys { get; set; }
}

/// <summary>
/// <c>--replay-input</c> (lane 3E-b): hands back a log's events once the replay clock reaches theirs, in order; lane 3E-c
/// does the same for its camera headings. The clock is the caller's: SimDriver passes <see cref="ReplayClock.Now"/>
/// (first-Screen frames before the launch's first run exists, those frames plus <see cref="SimDriver.TicksRun"/> after;
/// f-6a00609e, R-CLOCK-1). The due cursors are Godot-free <see cref="ReplayFeed{T}"/>s; SimDriver feeds each item to the
/// engine.
/// </summary>
public sealed class InputReplayer
{
    private readonly ReplayFeed<InputLogEvent> _events;
    private readonly ReplayFeed<InputLogCamera> _camera;

    /// <summary>Builds a replayer.</summary>
    /// <param name="log">The log to play.</param>
    public InputReplayer(InputLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _events = new ReplayFeed<InputLogEvent>(log.Events, e => e.Tick);
        _camera = new ReplayFeed<InputLogCamera>(log.Camera, c => c.Tick);
    }

    /// <summary>Key events applied so far.</summary>
    public int Applied => _events.Applied;

    /// <summary>Camera headings applied so far.</summary>
    public int CameraApplied => _camera.Applied;

    /// <summary>All key events and camera headings applied.</summary>
    public bool Done => _events.Done && _camera.Done;

    /// <summary>The tick of the last applied key event, or -1.</summary>
    public int LastTick => _events.LastTick;

    /// <summary>The events now due (tick at or before <paramref name="now"/>) that have not been handed out.</summary>
    /// <param name="now">The replay clock (<see cref="ReplayClock.Now"/>).</param>
    /// <returns>The due events, in log order.</returns>
    public IReadOnlyList<InputLogEvent> Due(int now) => _events.Due(now);

    /// <summary>The camera headings now due (tick at or before <paramref name="now"/>) that have not been handed out.</summary>
    /// <param name="now">The replay clock (<see cref="ReplayClock.Now"/>).</param>
    /// <returns>The due headings, in log order (the last one is the heading in force).</returns>
    public IReadOnlyList<InputLogCamera> DueCamera(int now) => _camera.Due(now);
}
