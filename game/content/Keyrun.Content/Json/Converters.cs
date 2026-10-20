using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Keyrun.Content;

/// <summary>Reads and writes <see cref="ValueList{T}"/> as a JSON array.</summary>
internal sealed class ValueListConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ValueList<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class Converter<T> : JsonConverter<ValueList<T>>
    {
        public override ValueList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("expected a JSON array");
            var items = new List<T>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                items.Add(JsonSerializer.Deserialize<T>(ref reader, options)!);
            return new ValueList<T>(items);
        }

        public override void Write(Utf8JsonWriter writer, ValueList<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (T item in value) JsonSerializer.Serialize(writer, item, options);
            writer.WriteEndArray();
        }
    }
}

/// <summary>Reads and writes <see cref="ValueMap{TValue}"/> as a JSON object; a duplicate key fails.</summary>
internal sealed class ValueMapConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ValueMap<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class Converter<TValue> : JsonConverter<ValueMap<TValue>>
    {
        public override ValueMap<TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("expected a JSON object");
            var entries = new List<KeyValuePair<string, TValue>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string key = reader.GetString()!;
                if (!seen.Add(key)) throw new JsonException("duplicate key \"" + key + "\"");
                reader.Read();
                entries.Add(new KeyValuePair<string, TValue>(key, JsonSerializer.Deserialize<TValue>(ref reader, options)!));
            }
            return new ValueMap<TValue>(entries);
        }

        public override void Write(Utf8JsonWriter writer, ValueMap<TValue> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, TValue> entry in value)
            {
                writer.WritePropertyName(entry.Key);
                JsonSerializer.Serialize(writer, entry.Value, options);
            }
            writer.WriteEndObject();
        }
    }
}

/// <summary>
/// Strict string enums for every enum in Keyrun.Content: each member declares its exact JSON
/// spelling with <see cref="EnumMemberAttribute"/>; matching is ordinal (case-sensitive) and a
/// JSON number is rejected, unlike the lenient built-in JsonStringEnumConverter.
/// </summary>
internal sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum && typeToConvert.Assembly == typeof(StrictEnumConverterFactory).Assembly;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private readonly Dictionary<string, TEnum> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<TEnum, string> _byValue = new();

        public Converter()
        {
            foreach (FieldInfo field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                string name = field.GetCustomAttribute<EnumMemberAttribute>()?.Value
                    ?? throw new InvalidOperationException(typeof(TEnum).Name + "." + field.Name + " lacks [EnumMember(Value = ...)]");
                var value = (TEnum)field.GetValue(null)!;
                _byName.Add(name, value);
                _byValue.Add(value, name);
            }
        }

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException(typeof(TEnum).Name + " must be a JSON string");
            string text = reader.GetString()!;
            if (!_byName.TryGetValue(text, out TEnum value))
                throw new JsonException("unknown " + typeof(TEnum).Name + " \"" + text + "\" (expected one of: " + string.Join(", ", _byName.Keys) + ")");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(_byValue[value]);
    }
}

/// <summary>
/// Picks the concrete <see cref="EffectOp"/> record from the JSON <c>"op"</c> field, wherever it
/// sits in the object, then binds the remaining properties strictly to that record.
/// </summary>
internal sealed class EffectOpConverter : JsonConverter<EffectOp>
{
    private static readonly (string Name, Type Type)[] Ops =
    {
        ("damage", typeof(DamageOp)),
        ("heal", typeof(HealOp)),
        ("applyEffect", typeof(ApplyEffectOp)),
        ("removeEffect", typeof(RemoveEffectOp)),
        ("interrupt", typeof(InterruptOp)),
        ("resource", typeof(ResourceOp)),
        ("threat", typeof(ThreatOp)),
        ("window", typeof(WindowOp)),
        ("payoff", typeof(PayoffOp)),
        ("combo", typeof(ComboOp)),
        ("partyEffect", typeof(PartyEffectOp)),
        ("cloud", typeof(CloudOp)),
    };

    /// <summary>The JSON <c>op</c> name of a concrete op record.</summary>
    internal static string NameOf(EffectOp op) => Ops.First(o => o.Type == op.GetType()).Name;

    public override EffectOp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject obj) throw new JsonException("an effect op must be a JSON object");
        if (!obj.TryGetPropertyValue("op", out JsonNode? opNode) || opNode is not JsonValue opValue || !opValue.TryGetValue(out string? name))
            throw new JsonException("an effect op needs a string \"op\" field");
        Type? type = Ops.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.Ordinal)).Type;
        if (type is null) throw new JsonException("unknown effect op \"" + name + "\"");
        obj.Remove("op");
        return (EffectOp)(obj.Deserialize(type, options) ?? throw new JsonException("effect op \"" + name + "\" is null"));
    }

    public override void Write(Utf8JsonWriter writer, EffectOp value, JsonSerializerOptions options)
    {
        JsonObject body = JsonSerializer.SerializeToNode(value, value.GetType(), options)!.AsObject();
        writer.WriteStartObject();
        writer.WriteString("op", NameOf(value));
        foreach (KeyValuePair<string, JsonNode?> property in body)
        {
            writer.WritePropertyName(property.Key);
            if (property.Value is null) writer.WriteNullValue();
            else property.Value.WriteTo(writer, options);
        }
        writer.WriteEndObject();
    }
}

/// <summary>Reads a script step's <c>at</c>: <c>"loop"</c>, <c>"onDeath"</c> or an integer tick.</summary>
internal sealed class ScriptAtConverter : JsonConverter<ScriptAt>
{
    public override ScriptAt Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (!reader.TryGetInt32(out int tick)) throw new JsonException("script \"at\" must be an integer tick");
                return new ScriptAt { Kind = ScriptAtKind.Tick, Tick = tick };
            case JsonTokenType.String:
                string? text = reader.GetString();
                if (text == "loop") return new ScriptAt { Kind = ScriptAtKind.Loop, Tick = 0 };
                if (text == "onDeath") return new ScriptAt { Kind = ScriptAtKind.OnDeath, Tick = 0 };
                throw new JsonException("unknown script \"at\" value \"" + text + "\"");
            default:
                throw new JsonException("script \"at\" must be \"loop\", \"onDeath\" or an integer");
        }
    }

    public override void Write(Utf8JsonWriter writer, ScriptAt value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case ScriptAtKind.Tick: writer.WriteNumberValue(value.Tick); break;
            case ScriptAtKind.Loop: writer.WriteStringValue("loop"); break;
            default: writer.WriteStringValue("onDeath"); break;
        }
    }
}
