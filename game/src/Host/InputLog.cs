using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

namespace Keyrun.Host;

/// <summary>
/// One recorded key transition of a <c>keyrun.input-log/1</c> file (lane 3E-b): at host tick <paramref name="Tick"/> the
/// physical key <paramref name="Key"/> went down (<paramref name="Pressed"/> true) or up.
/// </summary>
/// <param name="Tick">The host's stepped-tick count when the key changed (<see cref="SimDriver.TicksRun"/>: ticks stepped
/// since launch, across runs; 0 on the first between-runs screen). The event is applied before the next step.</param>
/// <param name="Key">The Godot physical key (its <see cref="Godot.Key"/> name in the file, e.g. <c>W</c>, <c>Enter</c>,
/// <c>Key1</c>, <c>Tab</c>).</param>
/// <param name="Pressed">True for a press, false for a release.</param>
public sealed record InputLogEvent(int Tick, Key Key, bool Pressed);

/// <summary>
/// One recorded camera heading of a <c>keyrun.input-log/2</c> file (lane 3E-c): from host tick <paramref name="Tick"/>
/// on, the CameraRig's <see cref="Keyrun.View.CameraRig.Yaw"/> (the heading PlayerInput's WASD is relative to) was
/// <paramref name="Yaw"/>.
/// </summary>
/// <param name="Tick">The host tick (<see cref="SimDriver.TicksRun"/>) of the step the heading was first sampled for.
/// The heading is restored before that step.</param>
/// <param name="Yaw">The heading, radians in [-Pi, Pi], the float32 the rig held. The file writes it in the shortest form
/// that reads back to the same float32 (<c>"R"</c>, invariant), so a replay restores it bit for bit.</param>
public sealed record InputLogCamera(int Tick, float Yaw);

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
/// </summary>
public sealed class InputLog
{
    /// <summary>The key-only schema id (lane 3E-b).</summary>
    public const string SchemaV1 = "keyrun.input-log/1";

    /// <summary>The schema id with camera headings (lane 3E-c).</summary>
    public const string SchemaV2 = "keyrun.input-log/2";

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

    /// <summary>Parses file text, fail-closed: an unknown schema, a missing or mistyped field, an unknown key name, a tick
    /// that goes down, a camera list in a <c>/1</c> file or a <c>/2</c> file without one, and a <c>startHeading</c> that
    /// is present but not a finite number are all refused.</summary>
    /// <param name="json">The text.</param>
    /// <param name="source">Where it came from (error messages).</param>
    /// <returns>The log.</returns>
    /// <exception cref="ArgumentException">Not a well-formed keyrun.input-log/1 or /2 document.</exception>
    public static InputLog Parse(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schemaEl) ||
                schemaEl.ValueKind != JsonValueKind.String || schemaEl.GetString() is not (SchemaV1 or SchemaV2))
            {
                throw new ArgumentException($"{source}: not a {SchemaV1} or {SchemaV2} file (schema field missing or different)");
            }
            var schema = schemaEl.GetString()!;
            var seed = Field(root, "seed", source, schema).GetInt64();
            var key = Field(root, "key", source, schema).GetInt32();
            float? startHeading = null;
            if (root.TryGetProperty("startHeading", out var headingEl))
            {
                if (headingEl.ValueKind != JsonValueKind.Number || !headingEl.TryGetSingle(out var heading) || !float.IsFinite(heading))
                {
                    throw new ArgumentException($"{source}: {schema} startHeading must be a finite number (radians)");
                }
                startHeading = heading;
            }
            var readyKeys = false;
            if (root.TryGetProperty("readyKeys", out var readyEl))
            {
                if (readyEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new ArgumentException($"{source}: {schema} readyKeys must be true or false");
                }
                readyKeys = readyEl.GetBoolean();
            }
            if (!root.TryGetProperty("events", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException($"{source}: {schema} needs an events array");
            }
            var events = new List<InputLogEvent>();
            foreach (var item in list.EnumerateArray())
            {
                var at = $"{source}: event {events.Count}";
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("tick", out var tick) || tick.ValueKind != JsonValueKind.Number ||
                    !item.TryGetProperty("key", out var name) || name.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("pressed", out var pressed) || pressed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new ArgumentException($"{at} needs tick (number), key (string) and pressed (true|false)");
                }
                events.Add(new InputLogEvent(tick.GetInt32(), ParseKey(name.GetString()!, at), pressed.GetBoolean()));
            }
            var hasCamera = root.TryGetProperty("camera", out var camList);
            if (schema == SchemaV1 && hasCamera)
            {
                throw new ArgumentException($"{source}: a {SchemaV1} file has no camera list (camera headings are {SchemaV2})");
            }
            if (schema == SchemaV2 && (!hasCamera || camList.ValueKind != JsonValueKind.Array))
            {
                throw new ArgumentException($"{source}: {SchemaV2} needs a camera array");
            }
            var camera = new List<InputLogCamera>();
            if (schema == SchemaV2)
            {
                foreach (var item in camList.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("tick", out var tick) || tick.ValueKind != JsonValueKind.Number ||
                        !item.TryGetProperty("yaw", out var yaw) || yaw.ValueKind != JsonValueKind.Number)
                    {
                        throw new ArgumentException($"{source}: camera {camera.Count} needs tick (number) and yaw (number)");
                    }
                    camera.Add(new InputLogCamera(tick.GetInt32(), yaw.GetSingle()));
                }
            }
            return new InputLog(seed, key, events, camera, startHeading, AffixWeek.ReadRecorded(root, source), readyKeys);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException($"{source}: not a well-formed {SchemaV1} or {SchemaV2} file ({ex.Message})", ex);
        }
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

    private static JsonElement Field(JsonElement root, string name, string source, string schema) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v
            : throw new ArgumentException($"{source}: {schema} needs a numeric {name}");
}

/// <summary>
/// <c>--record-input</c> (lane 3E-b): collects every key transition the game reads, stamped with the host tick. Two
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
/// <c>--replay-input</c> (lane 3E-b): hands back a log's events once the host tick reaches theirs, in order; lane 3E-c
/// does the same for its camera headings. Godot-free bookkeeping; SimDriver feeds each one to the engine.
/// </summary>
public sealed class InputReplayer
{
    private readonly InputLog _log;
    private int _next;
    private int _nextCamera;

    /// <summary>Builds a replayer.</summary>
    /// <param name="log">The log to play.</param>
    public InputReplayer(InputLog log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Key events applied so far.</summary>
    public int Applied => _next;

    /// <summary>Camera headings applied so far.</summary>
    public int CameraApplied => _nextCamera;

    /// <summary>All key events and camera headings applied.</summary>
    public bool Done => _next >= _log.Events.Count && _nextCamera >= _log.Camera.Count;

    /// <summary>The tick of the last applied key event, or -1.</summary>
    public int LastTick => _next == 0 ? -1 : _log.Events[_next - 1].Tick;

    /// <summary>The events now due (tick at or before <paramref name="tick"/>) that have not been handed out.</summary>
    /// <param name="tick">The host tick.</param>
    /// <returns>The due events, in log order.</returns>
    public IReadOnlyList<InputLogEvent> Due(int tick)
    {
        var due = new List<InputLogEvent>();
        while (_next < _log.Events.Count && _log.Events[_next].Tick <= tick)
        {
            due.Add(_log.Events[_next]);
            _next++;
        }
        return due;
    }

    /// <summary>The camera headings now due (tick at or before <paramref name="tick"/>) that have not been handed out.</summary>
    /// <param name="tick">The host tick.</param>
    /// <returns>The due headings, in log order (the last one is the heading in force).</returns>
    public IReadOnlyList<InputLogCamera> DueCamera(int tick)
    {
        var due = new List<InputLogCamera>();
        while (_nextCamera < _log.Camera.Count && _log.Camera[_nextCamera].Tick <= tick)
        {
            due.Add(_log.Camera[_nextCamera]);
            _nextCamera++;
        }
        return due;
    }
}
