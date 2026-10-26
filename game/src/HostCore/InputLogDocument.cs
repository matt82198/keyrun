using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Keyrun.Host;

/// <summary>
/// One recorded camera heading of a <c>keyrun.input-log/2</c> file (lane 3E-c): from replay clock value
/// <paramref name="Tick"/> on, the CameraRig's <c>Yaw</c> (the heading PlayerInput's WASD is relative to) was
/// <paramref name="Yaw"/>.
/// </summary>
/// <param name="Tick">The input-log clock value (<see cref="ReplayClock"/>, R-CLOCK-1: a first-Screen frame before the
/// launch's first run exists, the screen frames at the run start plus <c>SimDriver.TicksRun</c> after) the heading was
/// first sampled for. The heading is restored before that step.</param>
/// <param name="Yaw">The heading, radians in [-Pi, Pi], the float32 the rig held. The file writes it in the shortest form
/// that reads back to the same float32 (<c>"R"</c>, invariant), so a replay restores it bit for bit.</param>
public sealed record InputLogCamera(int Tick, float Yaw);

/// <summary>One key event of an input log as the file spells it, before the key name is resolved to a Godot key
/// (<c>InputLog.ParseKey</c>, the Godot half).</summary>
/// <param name="Tick">The replay clock value (<see cref="ReplayClock"/>).</param>
/// <param name="Key">The key name as written (non-empty).</param>
/// <param name="Pressed">True for a press, false for a release.</param>
public sealed record InputLogRawEvent(int Tick, string Key, bool Pressed);

/// <summary>
/// The Godot-free half of reading a <c>keyrun.input-log/1</c> or <c>/2</c> file (finding f-6a00609e): every structural
/// rule of <c>InputLog.Parse</c> except resolving key names, so Keyrun.Sim.Tests can drive it. Fail-closed: an unknown
/// schema, a missing or mistyped field, a field the shape does not define (header, event or camera), a tick that is
/// negative or goes down, a camera list in a <c>/1</c> file or a <c>/2</c> file without one, a <c>startHeading</c> that
/// is present but not a finite number and a bad <c>rotationIndex</c> are all refused with <see cref="ArgumentException"/>.
/// </summary>
/// <param name="Schema">The schema id (<see cref="SchemaV1"/> or <see cref="SchemaV2"/>).</param>
/// <param name="Seed">The run seed.</param>
/// <param name="KeyLevel">The launch key level.</param>
/// <param name="RotationIndex">The recorded weekly rotation index, or null (<see cref="AffixWeek.ReadRecorded"/>).</param>
/// <param name="StartHeading">The recorded start heading (radians), or null.</param>
/// <param name="ReadyKeys">The file's <c>readyKeys</c> (false when absent).</param>
/// <param name="Events">The key events in file order.</param>
/// <param name="Camera">The camera headings in file order (empty for <c>/1</c>).</param>
public sealed record InputLogDocument(
    string Schema,
    long Seed,
    int KeyLevel,
    int? RotationIndex,
    float? StartHeading,
    bool ReadyKeys,
    IReadOnlyList<InputLogRawEvent> Events,
    IReadOnlyList<InputLogCamera> Camera)
{
    /// <summary>The key-only schema id (lane 3E-b).</summary>
    public const string SchemaV1 = "keyrun.input-log/1";

    /// <summary>The schema id with camera headings (lane 3E-c).</summary>
    public const string SchemaV2 = "keyrun.input-log/2";

    /// <summary>Parses file text, fail-closed (see the type summary).</summary>
    /// <param name="json">The text.</param>
    /// <param name="source">Where it came from (error messages).</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentException">Not a well-formed keyrun.input-log/1 or /2 document.</exception>
    public static InputLogDocument Parse(string json, string source)
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
            OnlyKnown(root, HeaderFields, $"{source}: {schema} header");
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
            var events = new List<InputLogRawEvent>();
            foreach (var item in list.EnumerateArray())
            {
                var at = $"{source}: event {events.Count}";
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("tick", out var tick) || tick.ValueKind != JsonValueKind.Number ||
                    !item.TryGetProperty("key", out var name) || name.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("pressed", out var pressed) || pressed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new ArgumentException($"{at} needs tick (number), key (string) and pressed (true|false)");
                }
                OnlyKnown(item, EventFields, at);
                var keyName = name.GetString()!;
                if (keyName.Length == 0)
                {
                    throw new ArgumentException($"{at}: unknown key \"\"");
                }
                var t = tick.GetInt32();
                if (t < 0 || (events.Count > 0 && t < events[^1].Tick))
                {
                    throw new ArgumentException($"{at}: tick {t} is negative or before the previous event");
                }
                events.Add(new InputLogRawEvent(t, keyName, pressed.GetBoolean()));
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
                    var at = $"{source}: camera {camera.Count}";
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("tick", out var tick) || tick.ValueKind != JsonValueKind.Number ||
                        !item.TryGetProperty("yaw", out var yaw) || yaw.ValueKind != JsonValueKind.Number)
                    {
                        throw new ArgumentException($"{at} needs tick (number) and yaw (number)");
                    }
                    OnlyKnown(item, CameraFields, at);
                    var t = tick.GetInt32();
                    var y = yaw.GetSingle();
                    if (t < 0 || (camera.Count > 0 && t < camera[^1].Tick) || !float.IsFinite(y))
                    {
                        throw new ArgumentException($"{at}: tick {t} is negative or before the previous one, or its yaw is not finite");
                    }
                    camera.Add(new InputLogCamera(t, y));
                }
            }
            return new InputLogDocument(schema, seed, key, AffixWeek.ReadRecorded(root, source), startHeading, readyKeys, events, camera);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException($"{source}: not a well-formed {SchemaV1} or {SchemaV2} file ({ex.Message})", ex);
        }
    }

    /// <summary>
    /// The file text, Godot-free, byte-compatible with <c>InputLog.ToJson</c> (the Host writer): fixed key order, invariant
    /// numbers, LF, a trailing newline. A <c>/2</c> document always writes its <c>camera</c> array (empty when it has no
    /// headings, so <see cref="Parse"/> reads it back); a <c>/1</c> document writes none. <see cref="Parse"/> of the text
    /// returns an equal document.
    /// </summary>
    /// <returns>The JSON text.</returns>
    public string ToJson()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\n");
        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"  \"schema\": \"{Schema}\",\n");
        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"  \"seed\": {Seed},\n");
        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"  \"key\": {KeyLevel},\n");
        if (RotationIndex is { } rotation)
        {
            sb.Append(AffixWeek.HeaderLine(rotation));
        }
        if (StartHeading is { } heading)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"  \"startHeading\": {FormatYaw(heading)},\n");
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
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"    {{ \"tick\": {e.Tick}, \"key\": \"{e.Key}\", \"pressed\": {(e.Pressed ? "true" : "false")} }}");
        }
        sb.Append(Events.Count == 0 ? "]" : "\n  ]");
        if (Schema == SchemaV2)
        {
            sb.Append(",\n  \"camera\": [");
            for (var i = 0; i < Camera.Count; i++)
            {
                var c = Camera[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"    {{ \"tick\": {c.Tick}, \"yaw\": {FormatYaw(c.Yaw)} }}");
            }
            sb.Append(Camera.Count == 0 ? "]" : "\n  ]");
        }
        sb.Append("\n}\n");
        return sb.ToString();
    }

    /// <summary>A yaw as the file writes it: the shortest invariant text that reads back to the same float32.</summary>
    /// <param name="yaw">The yaw, radians.</param>
    /// <returns>The text.</returns>
    private static string FormatYaw(float yaw) => yaw.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    // Every field the writer (InputLog.ToJson) can emit; anything else is refused, so a misspelt or invented field (a
    // "frame" on an event, say) can never be silently ignored by a replay.
    private static readonly string[] HeaderFields =
        { "schema", "seed", "key", AffixWeek.HeaderField, "startHeading", "readyKeys", "events", "camera" };

    private static readonly string[] EventFields = { "tick", "key", "pressed" };

    private static readonly string[] CameraFields = { "tick", "yaw" };

    private static void OnlyKnown(JsonElement obj, string[] known, string at)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (Array.IndexOf(known, p.Name) < 0)
            {
                throw new ArgumentException($"{at}: unknown field \"{p.Name}\" (known: {string.Join(", ", known)})");
            }
        }
    }

    private static JsonElement Field(JsonElement root, string name, string source, string schema) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v
            : throw new ArgumentException($"{source}: {schema} needs a numeric {name}");
}
