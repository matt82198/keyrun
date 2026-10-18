using System.Collections.Generic;
using System.Text.Json;

namespace Keyrun.Sim.Core;

/// <summary>
/// sim/core/commands.ts validateCommand over its real input domain, untrusted JSON: a total guard that returns
/// a well-formed <see cref="Command"/> or null, never throwing. Integer checks use <see cref="JsNumber"/>, so
/// <c>1.0</c> is an integer (as in JS) and <c>1.5</c> is not. Also the canonical JSON writer for commands, the
/// form a trace's <c>c</c> array records.
/// </summary>
/// <remarks>
/// Fail-closed divergences from JS: tick and actorId must fit in a 32-bit int (JS accepts any safe integer;
/// no sim id or tick comes close); a repeated JSON key keeps the last value, as JSON.parse does.
/// </remarks>
public static class CommandJson
{
    /// <summary>The value of <paramref name="name"/> in an object, last occurrence wins; false when absent.</summary>
    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        var found = false;
        value = default;
        foreach (var p in obj.EnumerateObject())
        {
            if (p.NameEquals(name))
            {
                value = p.Value;
                found = true;
            }
        }
        return found;
    }

    private static bool IsInt(JsonElement e, out long v)
    {
        v = 0;
        return e.ValueKind == JsonValueKind.Number && JsNumber.TryParseInteger(e.GetRawText(), out v);
    }

    private static bool IsInt32(JsonElement e, out int v)
    {
        v = 0;
        if (!IsInt(e, out var l) || l < int.MinValue || l > int.MaxValue)
        {
            return false;
        }
        v = (int)l;
        return true;
    }

    private static bool IsIntInRange(JsonElement obj, string name, long min, long max, out long v)
    {
        v = 0;
        return TryGet(obj, name, out var e) && IsInt(e, out v) && v >= min && v <= max;
    }

    /// <summary>validateCommand: the command, or null on anything malformed.</summary>
    public static Command? Parse(JsonElement c)
    {
        if (c.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!TryGet(c, "tick", out var tickEl) || !IsInt32(tickEl, out var tick) || tick < 0)
        {
            return null;
        }
        if (!TryGet(c, "actorId", out var actorEl) || !IsInt32(actorEl, out var actorId))
        {
            return null;
        }
        if (!TryGet(c, "kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        switch (kindEl.GetString())
        {
            case "move":
            case "dash":
            {
                if (!TryGet(c, "dir", out var dir) || dir.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                if (!IsIntInRange(dir, "x", Commands.DirMin, Commands.DirMax, out var x) ||
                    !IsIntInRange(dir, "z", Commands.DirMin, Commands.DirMax, out var z))
                {
                    return null;
                }
                if (!IsIntInRange(c, "yaw", 0, 1023, out var yaw))
                {
                    return null;
                }
                // Walk speed is actor state, never command data: a client asking for its own speed is malformed.
                if (TryGet(c, "speedMmPerTick", out _))
                {
                    return null;
                }
                return kindEl.GetString() == "move"
                    ? new MoveCommand(tick, actorId, new Dir2(x, z), (int)yaw)
                    : new DashCommand(tick, actorId, new Dir2(x, z), (int)yaw);
            }
            case "cast":
            {
                if (!TryGet(c, "abilityId", out var ab) || ab.ValueKind != JsonValueKind.String || ab.GetString()!.Length == 0)
                {
                    return null;
                }
                int? targetId = null;
                if (TryGet(c, "targetId", out var tEl))
                {
                    if (!IsInt32(tEl, out var t))
                    {
                        return null; // includes JSON null: null is not undefined, and isInt(null) is false
                    }
                    targetId = t;
                }
                Vec3? point = null;
                if (TryGet(c, "point", out var pEl))
                {
                    if (pEl.ValueKind != JsonValueKind.Object ||
                        !TryGet(pEl, "x", out var px) || !IsInt(px, out var vx) ||
                        !TryGet(pEl, "y", out var py) || !IsInt(py, out var vy) ||
                        !TryGet(pEl, "z", out var pz) || !IsInt(pz, out var vz))
                    {
                        return null;
                    }
                    point = new Vec3(vx, vy, vz);
                }
                return new CastCommand(tick, actorId, ab.GetString()!, targetId, point);
            }
            case "stopcast":
                return new StopCastCommand(tick, actorId);
            case "target":
            {
                if (!TryGet(c, "targetId", out var tEl))
                {
                    return null;
                }
                if (tEl.ValueKind == JsonValueKind.Null)
                {
                    return new TargetCommand(tick, actorId, null);
                }
                return IsInt32(tEl, out var t) ? new TargetCommand(tick, actorId, t) : null;
            }
            default:
                return null;
        }
    }

    /// <summary>The command as a JSON object with the TS field names (absent optionals omitted).</summary>
    public static JsObject ToJsObject(Command c)
    {
        var m = new List<KeyValuePair<string, JsValue>>
        {
            new("tick", JsValue.Int(c.Tick)),
            new("actorId", JsValue.Int(c.ActorId)),
            new("kind", JsValue.Str(c.Kind)),
        };
        switch (c)
        {
            case MoveCommand mv:
                m.Add(new("dir", JsValue.Obj(("x", JsValue.Int(mv.Dir.X)), ("z", JsValue.Int(mv.Dir.Z)))));
                m.Add(new("yaw", JsValue.Int(mv.Yaw)));
                break;
            case DashCommand d:
                m.Add(new("dir", JsValue.Obj(("x", JsValue.Int(d.Dir.X)), ("z", JsValue.Int(d.Dir.Z)))));
                m.Add(new("yaw", JsValue.Int(d.Yaw)));
                break;
            case CastCommand cast:
                m.Add(new("abilityId", JsValue.Str(cast.AbilityId)));
                if (cast.TargetId is { } t)
                {
                    m.Add(new("targetId", JsValue.Int(t)));
                }
                if (cast.Point is { } p)
                {
                    m.Add(new("point", JsValue.Vec(p)));
                }
                break;
            case TargetCommand tc:
                m.Add(new("targetId", tc.TargetId is { } id ? JsValue.Int(id) : JsValue.Null));
                break;
        }
        return JsValue.Obj(m);
    }

    /// <summary>Canonical JSON of the command (sorted keys), byte-identical to the oracle's <c>c</c> entries.</summary>
    public static string ToCanonicalJson(Command c) => ToJsObject(c).ToCanonicalJson();
}
