using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Guides;

/// <summary>What a guide condition reads: the bot view, the deciding seat, and its effective kit.</summary>
/// <param name="View">The bot view of this tick.</param>
/// <param name="Self">The deciding seat.</param>
/// <param name="Kit">The seat's effective kit.</param>
public sealed record GuideContext(BotView View, ActorState Self, EffectiveKit Kit);

/// <summary>One guide rotation entry: a press, the condition under which the guide wants it, and the quoted reason.</summary>
/// <param name="Id">The entry id; a receipt's reason is <c>guide:&lt;class&gt;:&lt;Id&gt;</c>.</param>
/// <param name="Press">The ability id to cast (validated against content).</param>
/// <param name="When">The condition, compiled from the guide's <c>when</c> object.</param>
/// <param name="Reason">The guide's quoted reason; never empty.</param>
public sealed record GuideRule(string Id, string Press, Func<GuideContext, bool> When, string Reason);

/// <summary>One class's guide: its class id and its rotation in priority order.</summary>
/// <param name="ClassId">The class the guide plays.</param>
/// <param name="Rotation">The rotation entries, first castable and holding entry wins.</param>
public sealed record BotGuide(string ClassId, IReadOnlyList<GuideRule> Rotation);

/// <summary>
/// The per-class playtester guides (design/PLAYTESTER-GUIDE.md section 4). A guide file is
/// <c>game/content/data/guides/&lt;classId&gt;.json</c>. Parsing fails closed: any unknown key, unknown ability, unknown
/// condition or malformed value throws an <see cref="InvalidOperationException"/> that names the file's class and rule.
/// </summary>
public sealed class GuideSet
{
    private readonly IReadOnlyDictionary<string, BotGuide> _byClass;

    private GuideSet(IReadOnlyDictionary<string, BotGuide> byClass) => _byClass = byClass;

    /// <summary>No guides loaded: every <see cref="Require"/> throws.</summary>
    public static GuideSet Empty { get; } = new(new Dictionary<string, BotGuide>(StringComparer.Ordinal));

    /// <summary>Parses one class guide file.</summary>
    /// <param name="json">The guide file's text.</param>
    /// <param name="abilityIds">Every ability id in content; a press outside it is rejected.</param>
    /// <returns>A set holding the one class.</returns>
    /// <exception cref="InvalidOperationException">The guide is malformed or names an unknown ability or condition.</exception>
    public static GuideSet Parse(string json, IReadOnlySet<string> abilityIds)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(abilityIds);
        using var doc = ParseOrThrow(json);
        var root = doc.RootElement;
        RequireObject(root, "guide");
        RejectUnknownKeys(root, "guide", "classId", "version", "rotation");
        var classId = RequireString(root, "classId", "guide");
        var where = $"guide '{classId}'";
        if (RequireInt(root, "version", where) != 1)
        {
            throw Fail(where, "version must be 1");
        }
        var rules = new List<GuideRule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in RequireArray(root, "rotation", where).EnumerateArray())
        {
            RequireObject(row, where);
            var id = RequireString(row, "id", where);
            var rw = $"{where} rule '{id}'";
            if (!seen.Add(id))
            {
                throw Fail(rw, "duplicate rule id");
            }
            RejectUnknownKeys(row, rw, "id", "press", "when", "reason");
            var press = RequireString(row, "press", rw);
            if (!abilityIds.Contains(press))
            {
                throw Fail(rw, $"press '{press}' is not an ability in content");
            }
            var reason = RequireString(row, "reason", rw);
            var when = ParseWhen(RequireProperty(row, "when", rw), rw, abilityIds);
            rules.Add(new GuideRule(id, press, when, reason));
        }
        return new GuideSet(new Dictionary<string, BotGuide>(StringComparer.Ordinal) { [classId] = new BotGuide(classId, rules) });
    }

    /// <summary>The guide for <paramref name="classId"/>; a Guided seat with no guide fails here, at plan load.</summary>
    /// <param name="classId">The class id.</param>
    /// <returns>The guide.</returns>
    /// <exception cref="InvalidOperationException">No guide is loaded for the class.</exception>
    public BotGuide Require(string classId)
    {
        ArgumentNullException.ThrowIfNull(classId);
        return _byClass.TryGetValue(classId, out var guide)
            ? guide
            : throw new InvalidOperationException($"no playtester guide for class '{classId}' (guided bots need game/content/data/guides/{classId}.json)");
    }

    private static JsonDocument ParseOrThrow(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"guide is not valid JSON: {ex.Message}", ex);
        }
    }

    private static InvalidOperationException Fail(string where, string why) => new($"{where}: {why}");

    private static void RequireObject(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            throw Fail(where, "expected an object");
        }
    }

    private static void RejectUnknownKeys(JsonElement e, string where, params string[] allowed)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (!allowed.Contains(p.Name, StringComparer.Ordinal))
            {
                throw Fail(where, $"unknown key '{p.Name}'");
            }
        }
    }

    private static JsonElement RequireProperty(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) ? v : throw Fail(where, $"missing '{name}'");

    private static JsonElement RequireArray(JsonElement e, string name, string where)
    {
        var v = RequireProperty(e, name, where);
        return v.ValueKind == JsonValueKind.Array ? v : throw Fail(where, $"'{name}' must be an array");
    }

    private static string RequireString(JsonElement e, string name, string where)
    {
        var v = RequireProperty(e, name, where);
        var text = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return string.IsNullOrWhiteSpace(text) ? throw Fail(where, $"'{name}' must be a non-empty string") : text;
    }

    private static int RequireInt(JsonElement e, string name, string where) =>
        IntOf(RequireProperty(e, name, where), name, where);

    private static int IntOf(JsonElement v, string name, string where) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : throw Fail(where, $"'{name}' must be an integer");

    private static Func<GuideContext, bool> ParseWhen(JsonElement cond, string where, IReadOnlySet<string> abilityIds)
    {
        if (cond.ValueKind != JsonValueKind.Object || cond.EnumerateObject().Count() != 1)
        {
            throw Fail(where, "a condition is an object with exactly one key");
        }
        var p = cond.EnumerateObject().First();
        switch (p.Name)
        {
            case "all":
                var all = Conditions(p.Value, p.Name, where, abilityIds);
                return ctx => all.All(f => f(ctx));
            case "any":
                var any = Conditions(p.Value, p.Name, where, abilityIds);
                return ctx => any.Any(f => f(ctx));
            case "not":
                var inner = ParseWhen(p.Value, where, abilityIds);
                return ctx => !inner(ctx);
            case "resourceAtLeast":
                var resource = IntOf(p.Value, p.Name, where);
                return ctx => ctx.Self.Resource >= resource;
            case "healthBelowPct":
                var pct = IntOf(p.Value, p.Name, where);
                return ctx => ctx.Self.Health * 100 < (long)pct * ctx.Self.MaxHealth;
            case "attackersOnSelfAtLeast":
                var attackers = IntOf(p.Value, p.Name, where);
                return ctx => TankPressure.AttackersOf(ctx.View, ctx.Self) >= attackers;
            case "abilityReady":
                if (p.Value.ValueKind != JsonValueKind.String || !abilityIds.Contains(p.Value.GetString() ?? string.Empty))
                {
                    throw Fail(where, "'abilityReady' must name an ability in content");
                }
                var ability = p.Value.GetString()!;
                return ctx => RoleShared.ReadyAndAffordable(ctx.Self, ability, ctx.View.State.Tick, ctx.Kit);
            case "kickDuty":
                if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw Fail(where, "'kickDuty' must be true or false");
                }
                var wantKick = p.Value.ValueKind == JsonValueKind.True;
                return ctx => (RoleShared.LedgerKickCommand(ctx.View, ctx.Self, ctx.Kit) is not null) == wantKick;
            default:
                throw Fail(where, $"'{p.Name}' is not in the guide condition vocabulary");
        }
    }

    private static List<Func<GuideContext, bool>> Conditions(JsonElement list, string op, string where, IReadOnlySet<string> abilityIds)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            throw Fail(where, $"'{op}' must be an array");
        }
        return list.EnumerateArray().Select(c => ParseWhen(c, where, abilityIds)).ToList();
    }
}
