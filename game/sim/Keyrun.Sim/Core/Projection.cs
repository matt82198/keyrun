using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Keyrun.Sim.Core;

/// <summary>
/// The oracle's per-tick projection line and hashes (ORACLE.md sections 1 and 2):
/// <c>t=&lt;tick&gt;;pull=&lt;pullIndex&gt;;slack=&lt;slackTicks&gt;;a=&lt;actor&gt;|...</c> with each actor
/// <c>id,hp,x,z,yaw,alive,res,castAbility,castEnd</c>, actors ascending by id, dead ones included. Like the TS
/// writer it refuses values outside the safe-integer range and cast ids that are not projection-safe tokens,
/// so a corrupt value fails loudly instead of being recorded as exact.
/// </summary>
public static class Projection
{
    private static string Int(long n, string what)
    {
        if (n > IntMath.MaxSafeInteger || n < -IntMath.MaxSafeInteger)
        {
            throw new InvalidOperationException($"oracle: {what} is not a safe integer: {n.ToString(CultureInfo.InvariantCulture)}");
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsToken(string s)
    {
        if (s.Length == 0 || s == "-")
        {
            return false;
        }
        foreach (var c in s)
        {
            var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                     c == '.' || c == '_' || c == ':' || c == '-';
            if (!ok)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>One actor's field group.</summary>
    public static string Actor(ActorState a)
    {
        var id = Int(a.Id, "actor id");
        string cast;
        if (a.CastId is null)
        {
            cast = "-";
        }
        else if (IsToken(a.CastId))
        {
            cast = a.CastId;
        }
        else
        {
            throw new InvalidOperationException($"oracle: actor {id} castId \"{a.CastId}\" is not a projection-safe token");
        }
        return string.Join(",",
            id,
            Int(a.Health, $"actor {id} hp"),
            Int(a.Pos.X, $"actor {id} x"),
            Int(a.Pos.Z, $"actor {id} z"),
            Int(a.Yaw, $"actor {id} yaw"),
            a.Alive ? "1" : "0",
            Int(a.Resource, $"actor {id} res"),
            cast,
            a.CastEndsTick is { } end ? Int(end, $"actor {id} castEnd") : "-");
    }

    /// <summary>The projection line for one tick.</summary>
    public static string Line(int tick, int pullIndex, long slackTicks, IEnumerable<ActorState> actors)
    {
        var sb = new StringBuilder();
        sb.Append("t=").Append(Int(tick, "tick"))
          .Append(";pull=").Append(Int(pullIndex, "pullIndex"))
          .Append(";slack=").Append(Int(slackTicks, "slackTicks"))
          .Append(";a=");
        var first = true;
        foreach (var a in actors.OrderBy(x => x.Id))
        {
            if (!first)
            {
                sb.Append('|');
            }
            first = false;
            sb.Append(Actor(a));
        }
        return sb.ToString();
    }

    /// <summary>The projection line of a state, given the encounter's pull index and slack.</summary>
    public static string Line(SimState state, int pullIndex, long slackTicks) =>
        Line(state.Tick, pullIndex, slackTicks, state.Actors.Values);

    /// <summary>Record hash: FNV-1a 32 over the projection line (no newline).</summary>
    public static uint RecordHash(string line) => Fnv1a.Hash(line);

    /// <summary>Trace hash: one FNV state chained over every line, each followed by a single <c>\n</c>.</summary>
    public static uint TraceHash(IEnumerable<string> lines)
    {
        var h = Fnv1a.OffsetBasis;
        foreach (var line in lines)
        {
            h = Fnv1a.Append(h, line);
            h = Fnv1a.Append(h, "\n");
        }
        return h;
    }
}
