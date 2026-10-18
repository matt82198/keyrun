using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Keyrun.Sim.Core;

/// <summary>
/// An immutable JSON value restricted to sim data: null, booleans, integers (never floats), strings, arrays
/// and objects. It is the payload type of <see cref="SimEvent"/> fields and the reader/writer for the oracle's
/// canonical JSON (ORACLE.md section 5): compact, object keys sorted by UTF-16 code unit (ordinal), strings
/// escaped as JSON.stringify escapes them, integers in plain decimal with -0 as 0.
/// </summary>
public abstract class JsValue
{
    private protected JsValue()
    {
    }

    /// <summary>JSON null.</summary>
    public static readonly JsValue Null = new JsNull();

    /// <summary>A boolean.</summary>
    public static JsValue Bool(bool value) => value ? JsBool.True : JsBool.False;

    /// <summary>An integer.</summary>
    public static JsValue Int(long value) => new JsInt(value);

    /// <summary>A string.</summary>
    public static JsValue Str(string value) => new JsString(value);

    /// <summary>An array.</summary>
    public static JsValue Arr(params JsValue[] items) => new JsArray(items);

    /// <summary>An array.</summary>
    public static JsValue Arr(IEnumerable<JsValue> items) => new JsArray(items.ToArray());

    /// <summary>An object from (key, value) pairs; a repeated key keeps the last value.</summary>
    public static JsObject Obj(params (string Key, JsValue Value)[] members) =>
        new(members.Select(m => new KeyValuePair<string, JsValue>(m.Key, m.Value)));

    /// <summary>An object from members; a repeated key keeps the last value.</summary>
    public static JsObject Obj(IEnumerable<KeyValuePair<string, JsValue>> members) => new(members);

    /// <summary>A position as <c>{x, y, z}</c>.</summary>
    public static JsObject Vec(Vec3 v) => Obj(("x", Int(v.X)), ("y", Int(v.Y)), ("z", Int(v.Z)));

    /// <summary>Parses JSON text. Non-integer numbers throw: they are not sim data.</summary>
    /// <exception cref="ArgumentException">A non-integer number.</exception>
    public static JsValue Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FromJson(doc.RootElement);
    }

    /// <summary>Converts a parsed JSON element. Numbers go through <see cref="JsNumber.ToInteger"/> (exact).</summary>
    /// <exception cref="ArgumentException">A non-integer number.</exception>
    public static JsValue FromJson(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Null:
                return Null;
            case JsonValueKind.True:
                return JsBool.True;
            case JsonValueKind.False:
                return JsBool.False;
            case JsonValueKind.Number:
                return new JsInt(JsNumber.ToInteger(e.GetRawText()));
            case JsonValueKind.String:
                return new JsString(e.GetString()!);
            case JsonValueKind.Array:
                return new JsArray(e.EnumerateArray().Select(FromJson).ToArray());
            case JsonValueKind.Object:
                return new JsObject(e.EnumerateObject().Select(p => new KeyValuePair<string, JsValue>(p.Name, FromJson(p.Value))));
            default:
                throw new ArgumentException($"unsupported JSON value kind {e.ValueKind}", nameof(e));
        }
    }

    /// <summary>Canonical JSON text of this value.</summary>
    public string ToCanonicalJson()
    {
        var sb = new StringBuilder();
        Write(sb);
        return sb.ToString();
    }

    /// <summary>Appends this value's canonical JSON.</summary>
    internal abstract void Write(StringBuilder sb);

    /// <inheritdoc />
    public override string ToString() => ToCanonicalJson();

    /// <summary>Structural equality (same canonical JSON).</summary>
    public override bool Equals(object? obj) => obj is JsValue other && other.ToCanonicalJson() == ToCanonicalJson();

    /// <summary>A deterministic hash of the canonical JSON (never the per-process randomized string hash).</summary>
    public override int GetHashCode()
    {
        unchecked
        {
            var h = (int)2166136261u;
            foreach (var c in ToCanonicalJson())
            {
                h = (h ^ c) * 16777619;
            }
            return h;
        }
    }

    /// <summary>Writes a string literal escaped exactly as JSON.stringify does.</summary>
    internal static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20 || IsLoneSurrogate(s, i))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    private static bool IsLoneSurrogate(string s, int i)
    {
        var c = s[i];
        if (char.IsHighSurrogate(c))
        {
            return i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]);
        }
        if (char.IsLowSurrogate(c))
        {
            return i == 0 || !char.IsHighSurrogate(s[i - 1]);
        }
        return false;
    }
}

/// <summary>JSON null.</summary>
public sealed class JsNull : JsValue
{
    internal JsNull()
    {
    }

    internal override void Write(StringBuilder sb) => sb.Append("null");
}

/// <summary>A JSON boolean.</summary>
public sealed class JsBool : JsValue
{
    internal static readonly JsBool True = new(true);
    internal static readonly JsBool False = new(false);

    private JsBool(bool value) => Value = value;

    /// <summary>The value.</summary>
    public bool Value { get; }

    internal override void Write(StringBuilder sb) => sb.Append(Value ? "true" : "false");
}

/// <summary>A JSON integer.</summary>
public sealed class JsInt : JsValue
{
    internal JsInt(long value) => Value = value;

    /// <summary>The value.</summary>
    public long Value { get; }

    internal override void Write(StringBuilder sb) => sb.Append(Value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>A JSON string.</summary>
public sealed class JsString : JsValue
{
    internal JsString(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The value.</summary>
    public string Value { get; }

    internal override void Write(StringBuilder sb) => WriteString(sb, Value);
}

/// <summary>A JSON array.</summary>
public sealed class JsArray : JsValue
{
    private readonly JsValue[] _items;

    internal JsArray(JsValue[] items) => _items = items.Select(i => i ?? throw new ArgumentNullException(nameof(items))).ToArray();

    /// <summary>The items in order.</summary>
    public IReadOnlyList<JsValue> Items => _items;

    internal override void Write(StringBuilder sb)
    {
        sb.Append('[');
        for (var i = 0; i < _items.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            _items[i].Write(sb);
        }
        sb.Append(']');
    }
}

/// <summary>A JSON object. Members keep first-insertion order for reading; canonical output sorts keys ordinally.</summary>
public sealed class JsObject : JsValue
{
    private readonly List<string> _order = new();
    private readonly Dictionary<string, JsValue> _members = new(StringComparer.Ordinal);

    internal JsObject(IEnumerable<KeyValuePair<string, JsValue>> members)
    {
        foreach (var m in members)
        {
            if (!_members.ContainsKey(m.Key))
            {
                _order.Add(m.Key);
            }
            _members[m.Key] = m.Value ?? throw new ArgumentNullException(nameof(members));
        }
    }

    /// <summary>Members in first-insertion order.</summary>
    public IEnumerable<KeyValuePair<string, JsValue>> Members => _order.Select(k => new KeyValuePair<string, JsValue>(k, _members[k]));

    /// <summary>Number of members.</summary>
    public int Count => _order.Count;

    /// <summary>The member value, or null when absent (JS <c>undefined</c>).</summary>
    public JsValue? Get(string key) => _members.TryGetValue(key, out var v) ? v : null;

    /// <summary>True when the key is present (JS <c>key in obj</c>).</summary>
    public bool Has(string key) => _members.ContainsKey(key);

    internal override void Write(StringBuilder sb)
    {
        sb.Append('{');
        var first = true;
        foreach (var key in _order.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!first)
            {
                sb.Append(',');
            }
            first = false;
            WriteString(sb, key);
            sb.Append(':');
            _members[key].Write(sb);
        }
        sb.Append('}');
    }
}
