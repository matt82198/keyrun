using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// Readers for the encounter shapes from JSON, as they appear both in content/data (enemy <c>script</c> arrays)
/// and in the oracle setup files (<c>dungeon</c>, <c>content.enemyScripts</c>, <c>content.abilities</c>;
/// game/sim/ORACLE.md section 4). Fail closed: a missing or mistyped field throws, never defaults silently; the only
/// defaults are the TS optionals (every, telegraph, target, radius/angle1024/length/width, speed, margin).
/// </summary>
public static class EncounterJson
{
    private static JsObject Obj(JsValue? v, string what) =>
        v as JsObject ?? throw new FormatException($"{what} must be an object");

    private static JsValue Req(JsObject o, string key, string what) =>
        o.Get(key) ?? throw new FormatException($"{what} lacks '{key}'");

    private static long Long(JsValue v, string what) =>
        v is JsInt i ? i.Value : throw new FormatException($"{what} must be an integer");

    private static int Int(JsValue v, string what)
    {
        var n = Long(v, what);
        return n >= int.MinValue && n <= int.MaxValue ? (int)n : throw new FormatException($"{what} out of int range");
    }

    private static string Str(JsValue v, string what) =>
        v is JsString s ? s.Value : throw new FormatException($"{what} must be a string");

    private static long? OptLong(JsObject o, string key, string what) => o.Get(key) is { } v ? Long(v, $"{what}.{key}") : null;

    private static int? OptInt(JsObject o, string key, string what) => o.Get(key) is { } v ? Int(v, $"{what}.{key}") : null;

    private static TelegraphSpec ParseTelegraph(JsValue v, string what)
    {
        var o = Obj(v, what);
        return new TelegraphSpec(Str(Req(o, "shape", what), what + ".shape"), Int(Req(o, "leadTicks", what), what + ".leadTicks"))
        {
            Radius = OptLong(o, "radius", what),
            Angle1024 = OptInt(o, "angle1024", what),
            Length = OptLong(o, "length", what),
            Width = OptLong(o, "width", what),
            SpansCast = o.Get("spansCast") switch
            {
                null => false,
                JsBool b => b.Value,
                _ => throw new FormatException($"{what}.spansCast must be a boolean"),
            },
        };
    }

    private static ScriptTargetOverride? ParseTarget(JsObject o, string what) => o.Get("target") switch
    {
        null => null,
        JsString { Value: "self" } => ScriptTargetOverride.Self,
        JsString { Value: "threat" } => ScriptTargetOverride.Threat,
        JsString { Value: "random" } => ScriptTargetOverride.Random,
        _ => throw new FormatException($"{what}.target must be self, threat or random"),
    };

    private static ScriptStep ParseStep(JsValue v, string what)
    {
        var o = Obj(v, what);
        var cast = Str(Req(o, "cast", what), what + ".cast");
        var at = Req(o, "at", what);
        var step = at switch
        {
            JsString { Value: "loop" } => new ScriptStep(StepTrigger.Loop, cast),
            JsString { Value: "onDeath" } => new ScriptStep(StepTrigger.OnDeath, cast),
            JsInt t => new ScriptStep(StepTrigger.AtTick, cast) { AtTick = Int(t, what + ".at") },
            _ => throw new FormatException($"{what}.at must be \"loop\", \"onDeath\" or an integer tick"),
        };
        return step with
        {
            Every = OptInt(o, "every", what),
            Telegraph = o.Get("telegraph") is { } tg ? ParseTelegraph(tg, what + ".telegraph") : null,
            Target = ParseTarget(o, what),
        };
    }

    /// <summary>An enemy script: a JSON array of steps (<c>{at, every?, cast, telegraph?, target?}</c>).</summary>
    public static IReadOnlyList<ScriptStep> ParseScript(JsValue json) =>
        json is JsArray a
            ? a.Items.Select((s, i) => ParseStep(s, string.Create(CultureInfo.InvariantCulture, $"script[{i}]"))).ToList()
            : throw new FormatException("a script must be an array");

    /// <summary>An actor-id-keyed object of scripts (<c>content.enemyScripts</c>).</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> ParseScripts(JsValue json)
    {
        var result = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var kv in Obj(json, "enemyScripts").Members)
        {
            if (!int.TryParse(kv.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new FormatException($"enemyScripts key '{kv.Key}' is not an actor id");
            }
            result.Add(id, ParseScript(kv.Value));
        }
        return result;
    }

    private static Vec3 ParseVec(JsValue v, string what)
    {
        var o = Obj(v, what);
        return new Vec3(Long(Req(o, "x", what), what + ".x"), Long(Req(o, "y", what), what + ".y"), Long(Req(o, "z", what), what + ".z"));
    }

    private static PullSpawn ParseSpawn(JsValue v, string what)
    {
        var o = Obj(v, what);
        return new PullSpawn(
            Int(Req(o, "actorId", what), what + ".actorId"),
            Str(Req(o, "classId", what), what + ".classId"),
            ParseVec(Req(o, "pos", what), what + ".pos"),
            Int(Req(o, "yaw", what), what + ".yaw"),
            Long(Req(o, "health", what), what + ".health"))
        {
            SpeedMmPerTick = OptLong(o, "speedMmPerTick", what),
            ChaseMarginMm = OptLong(o, "chaseMarginMm", what),
        };
    }

    /// <summary>A sim DungeonDef: <c>{id, pulls:[{id, spawns:[{actorId, classId, pos, yaw, health, ...}]}]}</c>.</summary>
    public static DungeonDef ParseDungeon(JsValue json)
    {
        var o = Obj(json, "dungeon");
        if (Req(o, "pulls", "dungeon") is not JsArray pulls)
        {
            throw new FormatException("dungeon.pulls must be an array");
        }
        var parsed = pulls.Items.Select((p, i) =>
        {
            var what = string.Create(CultureInfo.InvariantCulture, $"dungeon.pulls[{i}]");
            var po = Obj(p, what);
            if (Req(po, "spawns", what) is not JsArray spawns)
            {
                throw new FormatException(what + ".spawns must be an array");
            }
            return new PullDef(Str(Req(po, "id", what), what + ".id"),
                spawns.Items.Select((s, j) => ParseSpawn(s, string.Create(CultureInfo.InvariantCulture, $"{what}.spawns[{j}]"))).ToList());
        }).ToList();
        return new DungeonDef(Str(Req(o, "id", "dungeon"), "dungeon.id"), parsed);
    }

    /// <summary>
    /// An id-keyed object of AbilityDefs reduced to <see cref="ScriptAbility"/>: id, range, shape.kind, and whether
    /// any effect op is "heal".
    /// </summary>
    public static IReadOnlyDictionary<string, ScriptAbility> ParseAbilities(JsValue json)
    {
        var result = new Dictionary<string, ScriptAbility>(StringComparer.Ordinal);
        foreach (var kv in Obj(json, "abilities").Members)
        {
            var what = "abilities." + kv.Key;
            var o = Obj(kv.Value, what);
            var shape = Obj(Req(o, "shape", what), what + ".shape");
            if (Req(o, "effects", what) is not JsArray effects)
            {
                throw new FormatException(what + ".effects must be an array");
            }
            var heals = effects.Items.Any(op => op is JsObject eo && eo.Get("op") is JsString { Value: "heal" });
            result.Add(kv.Key, new ScriptAbility(
                Str(Req(o, "id", what), what + ".id"),
                Long(Req(o, "range", what), what + ".range"),
                Str(Req(shape, "kind", what + ".shape"), what + ".shape.kind"),
                heals));
        }
        return result;
    }
}
