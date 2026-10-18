using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Keyrun.Sim.Core;

/// <summary>
/// A simulation event (sim/core/events.ts SimEvent): a tick, a free-form type tag ("damage", "telegraph",
/// "run_end", ...) and any extra fields. Core emits none itself; rules emit through the <c>emit</c> callback
/// of <see cref="Tick.Step"/>. A TS field whose value is <c>undefined</c> is simply not added here, which is
/// what canonical JSON does with it (omitted).
/// </summary>
public sealed class SimEvent
{
    /// <summary>Builds an event. <paramref name="fields"/> must not repeat "tick" or "type".</summary>
    /// <exception cref="ArgumentException">A field named tick or type, or a repeated field.</exception>
    public SimEvent(int tick, string type, params (string Key, JsValue Value)[] fields)
        : this(tick, type, fields.Select(f => new KeyValuePair<string, JsValue>(f.Key, f.Value)))
    {
    }

    /// <summary>Builds an event. <paramref name="fields"/> must not repeat "tick" or "type".</summary>
    /// <exception cref="ArgumentException">A field named tick or type, or a repeated field.</exception>
    public SimEvent(int tick, string type, IEnumerable<KeyValuePair<string, JsValue>> fields)
    {
        Tick = tick;
        Type = type ?? throw new ArgumentNullException(nameof(type));
        var list = fields.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in list)
        {
            if (f.Key == "tick" || f.Key == "type")
            {
                throw new ArgumentException($"event field '{f.Key}' is reserved", nameof(fields));
            }
            if (!seen.Add(f.Key))
            {
                throw new ArgumentException($"event field '{f.Key}' repeated", nameof(fields));
            }
        }
        Fields = JsValue.Obj(list);
    }

    /// <summary>The tick the event was emitted on (the pre-increment state tick).</summary>
    public int Tick { get; }

    /// <summary>The type tag.</summary>
    public string Type { get; }

    /// <summary>The extra fields (everything except tick and type).</summary>
    public JsObject Fields { get; }

    /// <summary>A field value, or null when absent.</summary>
    public JsValue? Get(string key) => Fields.Get(key);

    /// <summary>The whole event as one JSON object (tick, type and every field).</summary>
    public JsObject ToJsObject() =>
        JsValue.Obj(Fields.Members.Prepend(new KeyValuePair<string, JsValue>("type", JsValue.Str(Type)))
            .Prepend(new KeyValuePair<string, JsValue>("tick", JsValue.Int(Tick))));

    /// <summary>Canonical JSON, as the oracle records it in a trace's <c>e</c> array.</summary>
    public string ToCanonicalJson() => ToJsObject().ToCanonicalJson();

    /// <inheritdoc />
    public override string ToString() => ToCanonicalJson();

    /// <summary>Reads an event object (must carry an integer tick and a string type).</summary>
    /// <exception cref="ArgumentException">Not an event object, or a non-integer number.</exception>
    public static SimEvent FromJson(JsonElement e)
    {
        if (JsValue.FromJson(e) is not JsObject obj)
        {
            throw new ArgumentException("an event must be a JSON object", nameof(e));
        }
        if (obj.Get("tick") is not JsInt tick || tick.Value < int.MinValue || tick.Value > int.MaxValue)
        {
            throw new ArgumentException("event tick must be an integer", nameof(e));
        }
        if (obj.Get("type") is not JsString type)
        {
            throw new ArgumentException("event type must be a string", nameof(e));
        }
        return new SimEvent((int)tick.Value, type.Value, obj.Members.Where(m => m.Key != "tick" && m.Key != "type"));
    }
}
