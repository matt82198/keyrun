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
/// The <c>keyrun.input-log/1</c> file (lane 3E-b, ENGINE-PLAN row 3E): the keys a human pressed during an interactive
/// run, tick-stamped, so <c>--replay-input</c> can play them back through the real input path. Shape:
/// <c>{ "schema": "keyrun.input-log/1", "seed": S, "key": K, "events": [ { "tick": N, "key": "W", "pressed": true } ] }</c>
/// (UTF-8, no BOM, LF, one event per line). Seed and key are the launch's <c>--seed</c> and <c>--key</c>; a replay
/// under a different seed or key is refused, because the same keys would not produce the same run.
/// </summary>
public sealed class InputLog
{
    /// <summary>The schema id.</summary>
    public const string Schema = "keyrun.input-log/1";

    /// <summary>Builds a log.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="keyLevel">The launch key level.</param>
    /// <param name="events">The events, ticks never decreasing.</param>
    /// <exception cref="ArgumentException">A tick is negative or goes down.</exception>
    public InputLog(long seed, int keyLevel, IReadOnlyList<InputLogEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Tick < 0 || (i > 0 && events[i].Tick < events[i - 1].Tick))
            {
                throw new ArgumentException($"{Schema}: event {i} tick {events[i].Tick} is negative or before the previous event");
            }
        }
        Seed = seed;
        KeyLevel = keyLevel;
        Events = events;
    }

    /// <summary>The run seed the log was recorded at.</summary>
    public long Seed { get; }

    /// <summary>The launch key level the log was recorded at.</summary>
    public int KeyLevel { get; }

    /// <summary>The key transitions in order.</summary>
    public IReadOnlyList<InputLogEvent> Events { get; }

    /// <summary>The file text (deterministic: fixed key order, invariant numbers, LF, trailing newline).</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"schema\": \"{Schema}\",\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"seed\": {Seed},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"key\": {KeyLevel},\n");
        sb.Append("  \"events\": [");
        for (var i = 0; i < Events.Count; i++)
        {
            var e = Events[i];
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append(CultureInfo.InvariantCulture, $"    {{ \"tick\": {e.Tick}, \"key\": \"{e.Key}\", \"pressed\": {(e.Pressed ? "true" : "false")} }}");
        }
        sb.Append(Events.Count == 0 ? "]\n" : "\n  ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

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
    /// <exception cref="ArgumentException">Not a well-formed keyrun.input-log/1 file.</exception>
    public static InputLog Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Parses file text, fail-closed: wrong schema, a missing or mistyped field, an unknown key name or a tick
    /// that goes down are all refused.</summary>
    /// <param name="json">The text.</param>
    /// <param name="source">Where it came from (error messages).</param>
    /// <returns>The log.</returns>
    /// <exception cref="ArgumentException">Not a well-formed keyrun.input-log/1 document.</exception>
    public static InputLog Parse(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) ||
                schema.ValueKind != JsonValueKind.String || schema.GetString() != Schema)
            {
                throw new ArgumentException($"{source}: not a {Schema} file (schema field missing or different)");
            }
            var seed = Field(root, "seed", source).GetInt64();
            var key = Field(root, "key", source).GetInt32();
            if (!root.TryGetProperty("events", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException($"{source}: {Schema} needs an events array");
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
            return new InputLog(seed, key, events);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException($"{source}: not a well-formed {Schema} file ({ex.Message})", ex);
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

    private static JsonElement Field(JsonElement root, string name, string source) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v
            : throw new ArgumentException($"{source}: {Schema} needs a numeric {name}");
}

/// <summary>
/// <c>--record-input</c> (lane 3E-b): collects every key transition the game reads, stamped with the host tick. Two
/// sources, so the log is complete whatever the event delivery does: <see cref="InputEventKey"/> events as the viewport delivers
/// them (every key PlayerInput, the between-runs screen, the run-end panel and SimDriver handle), and, before each step,
/// a diff of the keys PlayerInput polls (<see cref="PlayerInput.PolledKeys"/>, WASD) against what the events said, which
/// catches a release the engine applies without an event (focus loss). Echo repeats are skipped by the caller.
/// </summary>
public sealed class InputRecorder
{
    private readonly List<InputLogEvent> _events = new();
    private readonly HashSet<Key> _down = new();

    /// <summary>The events recorded so far.</summary>
    public IReadOnlyList<InputLogEvent> Events => _events;

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

    /// <summary>The log of what was recorded.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="keyLevel">The launch key level.</param>
    /// <returns>The log.</returns>
    public InputLog ToLog(long seed, int keyLevel) => new(seed, keyLevel, _events.ToArray());
}

/// <summary>
/// <c>--replay-input</c> (lane 3E-b): hands back a log's events once the host tick reaches theirs, in order. Godot-free
/// bookkeeping; SimDriver feeds each one to the engine.
/// </summary>
public sealed class InputReplayer
{
    private readonly InputLog _log;
    private int _next;

    /// <summary>Builds a replayer.</summary>
    /// <param name="log">The log to play.</param>
    public InputReplayer(InputLog log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Events applied so far.</summary>
    public int Applied => _next;

    /// <summary>All events applied.</summary>
    public bool Done => _next >= _log.Events.Count;

    /// <summary>The tick of the last applied event, or -1.</summary>
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
}
