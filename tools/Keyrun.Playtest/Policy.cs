using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots.Shared;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>One kick assignment: who kicks <see cref="Cast"/>, in order (the first whose interrupt is ready and in range).</summary>
/// <param name="Cast">The enemy ability id to interrupt (as the run summary names it).</param>
/// <param name="Kickers">Class ids in kick order: the first is the kicker, the rest are backups.</param>
public sealed record KickRule(string Cast, IReadOnlyList<string> Kickers);

/// <summary>
/// A playtester's plan for one run (tools/Keyrun.Playtest/GUIDE.md "The plan file"). The bots do only what the plan says:
/// a field left out means the bots do not do that thing at all (no kicks, no dodging, no defensives, no utility), never
/// that a hidden script fills it in. No policy file at all is the old scripted party, unchanged.
/// </summary>
public sealed record Policy
{
    /// <summary>The pull ids a <see cref="Hold"/> entry may name, in dungeon order ("boss" is the boss pull).</summary>
    public static readonly IReadOnlyList<string> PullIds = new[] { "choir", "pair", "seed", "boss" };

    /// <summary>The one-line reason the playtester gave for this plan (copied into the summary).</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// The playtester mode (design/PLAYTESTER-GUIDE.md section 1; G2.4, K3a): "guided" or "blind". Left out, the plan is
    /// guided when the plan has no policy knob section (R-GUIDE-2 a), blind when it has one; an explicit field always wins.
    /// </summary>
    public PlaytesterMode Mode { get; init; } = PlaytesterMode.Guided;

    /// <summary>Class id to (fork ability id to option id). Unpicked forks keep the base ability.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Talents { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>Kick assignments. A cast no rule names is never kicked.</summary>
    public IReadOnlyList<KickRule> Kicks { get; init; } = Array.Empty<KickRule>();

    /// <summary>True: a seat standing in a danger telegraph walks out of it (after its reaction time).</summary>
    public bool DodgeTelegraphs { get; init; }

    /// <summary>True: a seat standing in a hostile floor patch walks out of it.</summary>
    public bool DodgeFloor { get; init; }

    /// <summary>Class id to the health percent (1-99) at or under which that seat presses its defensive (E).</summary>
    public IReadOnlyDictionary<string, int> Defensive { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Class ids that mitigate the way the scripted party's tank does (STATE.md D48): the seat presses its defensive (E)
    /// and its group mitigation when its role's script would (under pack pressure, or under the role's health floor),
    /// instead of only under a <see cref="Defensive"/> threshold. Left out, a seat never presses E by its script, so the
    /// script's pressure branch never reaches the group mitigation behind it either.
    /// </summary>
    public IReadOnlyList<string> Mitigate { get; init; } = Array.Empty<string>();

    /// <summary>Class ids allowed to press their utility (R: taunt, dispel) when their script would.</summary>
    public IReadOnlyList<string> Utility { get; init; } = Array.Empty<string>();

    /// <summary>Ability id to the pull ids it may be pressed in; any other pull it is held.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Hold { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>Enemy ids the damage dealers (every seat but the tank) attack first, in order.</summary>
    public IReadOnlyList<string> Focus { get; init; } = Array.Empty<string>();

    /// <summary>Before the leader walks into the next room it waits until every living seat is at or above this health
    /// percent (0 = never waits).</summary>
    public int RestHealthPct { get; init; }

    /// <summary>The kick order for <paramref name="castId"/>, or null when no rule names it.</summary>
    /// <param name="castId">Enemy ability id.</param>
    /// <returns>The kickers, or null.</returns>
    public IReadOnlyList<string>? KickersFor(string castId) =>
        Kicks.FirstOrDefault(k => string.Equals(k.Cast, castId, StringComparison.Ordinal))?.Kickers;
}

/// <summary>A policy file that cannot be played (unknown field, id or value). The message says which.</summary>
public sealed class PolicyException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong.</param>
    public PolicyException(string message) : base(message)
    {
    }
}

/// <summary>Reads and validates a policy file against the content. Fails closed: every unknown field or id throws.</summary>
public static class PolicyReader
{
    private static readonly HashSet<string> TopLevel = new(StringComparer.Ordinal)
    {
        "reason", "mode", "talents", "kicks", "dodge", "defensive", "mitigate", "utility", "hold", "focus", "pull",
    };

    /// <summary>Every top-level field a policy file may hold, sorted (the guide documents each one).</summary>
    public static IReadOnlyList<string> Fields { get; } = TopLevel.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>Reads <paramref name="path"/>.</summary>
    /// <param name="path">The policy JSON file.</param>
    /// <param name="registry">The content it is checked against.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="PolicyException">The file is not a valid policy.</exception>
    public static Policy Load(string path, C.ContentRegistry registry) => Parse(File.ReadAllText(path), registry);

    /// <summary>Parses policy JSON.</summary>
    /// <param name="json">The text.</param>
    /// <param name="registry">The content it is checked against.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="PolicyException">The text is not a valid policy.</exception>
    public static Policy Parse(string json, C.ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(registry);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            throw new PolicyException($"policy: not valid JSON ({e.Message})");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new PolicyException("policy: the file must be one JSON object");
            }
            foreach (var p in root.EnumerateObject())
            {
                if (!TopLevel.Contains(p.Name))
                {
                    throw new PolicyException($"policy: unknown field \"{p.Name}\" (fields: {string.Join(", ", TopLevel.OrderBy(n => n, StringComparer.Ordinal))})");
                }
            }
            var policy = new Policy
            {
                Reason = root.TryGetProperty("reason", out var r) ? Str(r, "reason") : string.Empty,
                Talents = root.TryGetProperty("talents", out var t) ? Talents(t, registry) : new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal),
                Kicks = root.TryGetProperty("kicks", out var k) ? Kicks(k, registry) : Array.Empty<KickRule>(),
                Defensive = root.TryGetProperty("defensive", out var d) ? Defensive(d, registry) : new Dictionary<string, int>(StringComparer.Ordinal),
                Mitigate = root.TryGetProperty("mitigate", out var m) ? ClassList(m, "mitigate", registry) : Array.Empty<string>(),
                Utility = root.TryGetProperty("utility", out var u) ? ClassList(u, "utility", registry) : Array.Empty<string>(),
                Hold = root.TryGetProperty("hold", out var h) ? Hold(h, registry) : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
                Focus = root.TryGetProperty("focus", out var f) ? Focus(f, registry) : Array.Empty<string>(),
                Mode = root.TryGetProperty("mode", out var mode) ? ModeOf(mode) : DefaultMode(root),
            };
            if (root.TryGetProperty("dodge", out var dodge))
            {
                var (telegraphs, floor) = Dodge(dodge);
                policy = policy with { DodgeTelegraphs = telegraphs, DodgeFloor = floor };
            }
            if (root.TryGetProperty("pull", out var pull))
            {
                policy = policy with { RestHealthPct = Pull(pull) };
            }
            return policy;
        }
    }

    /// <summary>
    /// R-GUIDE-2 (a): a plan with no explicit mode is Blind when it carries any policy knob section (every field but
    /// reason and mode), and Guided otherwise. An explicit mode always wins (see <see cref="ModeOf"/>).
    /// </summary>
    private static PlaytesterMode DefaultMode(JsonElement root)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (p.Name is not ("reason" or "mode"))
            {
                return PlaytesterMode.Blind;
            }
        }
        return PlaytesterMode.Guided;
    }

    // K3a: the mode is exactly "guided" or "blind". Anything else (a case change, a number, null) is a plan error, refused.
    private static PlaytesterMode ModeOf(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String && PlaytesterModeWire.TryParse(e.GetString(), out var mode))
        {
            return mode;
        }
        throw new PolicyException($"policy: mode must be \"guided\" or \"blind\" (got {e.GetRawText()})");
    }

    private static string Str(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new PolicyException($"policy: {where} must be a string");

    private static void RequireObject(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            throw new PolicyException($"policy: {where} must be an object");
        }
    }

    private static List<string> Strings(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array)
        {
            throw new PolicyException($"policy: {where} must be an array of strings");
        }
        return e.EnumerateArray().Select(x => Str(x, where + " entry")).ToList();
    }

    private static C.ClassDef Class(string id, string where, C.ContentRegistry registry) =>
        registry.Classes.TryGetValue(id, out var cls) ? cls : throw new PolicyException($"policy: {where}: unknown class \"{id}\" (classes: {string.Join(", ", registry.Classes.Keys)})");

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Talents(JsonElement e, C.ContentRegistry registry)
    {
        RequireObject(e, "talents");
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var byClass in e.EnumerateObject())
        {
            var cls = Class(byClass.Name, "talents", registry);
            RequireObject(byClass.Value, $"talents.{cls.Id}");
            var picks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pick in byClass.Value.EnumerateObject())
            {
                var fork = cls.Talents.FirstOrDefault(f => string.Equals(f.AbilityId, pick.Name, StringComparison.Ordinal))
                    ?? throw new PolicyException($"policy: talents.{cls.Id}: \"{pick.Name}\" is not one of {cls.Id}'s talent forks (forks: {string.Join(", ", cls.Talents.Select(f => f.AbilityId))})");
                var option = Str(pick.Value, $"talents.{cls.Id}.{pick.Name}");
                if (!fork.Options.Any(o => string.Equals(o.Id, option, StringComparison.Ordinal)))
                {
                    throw new PolicyException($"policy: talents.{cls.Id}.{pick.Name}: unknown option \"{option}\" (options: {string.Join(", ", fork.Options.Select(o => o.Id))})");
                }
                picks[pick.Name] = option;
            }
            result[cls.Id] = picks;
        }
        return result;
    }

    private static IReadOnlyList<KickRule> Kicks(JsonElement e, C.ContentRegistry registry)
    {
        if (e.ValueKind != JsonValueKind.Array)
        {
            throw new PolicyException("policy: kicks must be an array of {\"cast\": ..., \"kickers\": [...]}");
        }
        var rules = new List<KickRule>();
        foreach (var rule in e.EnumerateArray())
        {
            RequireObject(rule, "kicks entry");
            foreach (var p in rule.EnumerateObject())
            {
                if (p.Name is not ("cast" or "kickers"))
                {
                    throw new PolicyException($"policy: kicks entry: unknown field \"{p.Name}\" (fields: cast, kickers)");
                }
            }
            if (!rule.TryGetProperty("cast", out var castEl) || !rule.TryGetProperty("kickers", out var kickersEl))
            {
                throw new PolicyException("policy: kicks entry needs both \"cast\" and \"kickers\"");
            }
            var cast = Str(castEl, "kicks.cast");
            if (!registry.Abilities.TryGetValue(cast, out var ability) || !registry.Enemies.Values.Any(en => en.Abilities.Contains(cast)))
            {
                throw new PolicyException($"policy: kicks: \"{cast}\" is not an enemy ability");
            }
            if (!ability.Interruptible)
            {
                throw new PolicyException($"policy: kicks: \"{cast}\" cannot be interrupted");
            }
            if (rules.Any(x => x.Cast == cast))
            {
                throw new PolicyException($"policy: kicks: \"{cast}\" is listed twice");
            }
            var kickers = Strings(kickersEl, "kicks.kickers");
            if (kickers.Count == 0)
            {
                throw new PolicyException($"policy: kicks: \"{cast}\" names no kicker");
            }
            foreach (var k in kickers)
            {
                Class(k, "kicks.kickers", registry);
            }
            if (kickers.Distinct(StringComparer.Ordinal).Count() != kickers.Count)
            {
                throw new PolicyException($"policy: kicks: \"{cast}\" names a kicker twice");
            }
            rules.Add(new KickRule(cast, kickers));
        }
        return rules;
    }

    private static (bool Telegraphs, bool Floor) Dodge(JsonElement e)
    {
        RequireObject(e, "dodge");
        bool telegraphs = false, floor = false;
        foreach (var p in e.EnumerateObject())
        {
            var on = p.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new PolicyException($"policy: dodge.{p.Name} must be true or false"),
            };
            switch (p.Name)
            {
                case "telegraphs": telegraphs = on; break;
                case "floor": floor = on; break;
                default: throw new PolicyException($"policy: dodge: unknown field \"{p.Name}\" (fields: telegraphs, floor)");
            }
        }
        return (telegraphs, floor);
    }

    private static IReadOnlyDictionary<string, int> Defensive(JsonElement e, C.ContentRegistry registry)
    {
        RequireObject(e, "defensive");
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject())
        {
            var cls = Class(p.Name, "defensive", registry);
            if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out var pct) || pct is < 1 or > 99)
            {
                throw new PolicyException($"policy: defensive.{cls.Id} must be a whole health percent 1-99");
            }
            result[cls.Id] = pct;
        }
        return result;
    }

    private static IReadOnlyList<string> ClassList(JsonElement e, string where, C.ContentRegistry registry)
    {
        var list = Strings(e, where);
        foreach (var id in list)
        {
            Class(id, where, registry);
        }
        return list.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Hold(JsonElement e, C.ContentRegistry registry)
    {
        RequireObject(e, "hold");
        var kitAbilities = registry.Classes.Values.SelectMany(KitIds).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject())
        {
            if (!kitAbilities.Contains(p.Name))
            {
                throw new PolicyException($"policy: hold: \"{p.Name}\" is not a player ability (see `playtest kit`)");
            }
            var pulls = Strings(p.Value, $"hold.{p.Name}");
            foreach (var pull in pulls)
            {
                if (!Policy.PullIds.Contains(pull))
                {
                    throw new PolicyException($"policy: hold.{p.Name}: unknown pull \"{pull}\" (pulls: {string.Join(", ", Policy.PullIds)})");
                }
            }
            result[p.Name] = pulls;
        }
        return result;
    }

    private static IReadOnlyList<string> Focus(JsonElement e, C.ContentRegistry registry)
    {
        var list = Strings(e, "focus");
        foreach (var id in list)
        {
            if (!registry.Enemies.ContainsKey(id))
            {
                throw new PolicyException($"policy: focus: unknown enemy \"{id}\"");
            }
        }
        return list;
    }

    private static int Pull(JsonElement e)
    {
        RequireObject(e, "pull");
        var rest = 0;
        foreach (var p in e.EnumerateObject())
        {
            if (p.Name != "restHealthPct")
            {
                throw new PolicyException($"policy: pull: unknown field \"{p.Name}\" (fields: restHealthPct)");
            }
            if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out rest) || rest is < 0 or > 100)
            {
                throw new PolicyException("policy: pull.restHealthPct must be a whole percent 0-100");
            }
        }
        return rest;
    }

    /// <summary>The nine kit ability ids of <paramref name="cls"/> in bar order (1-5, Q, E, R, F).</summary>
    /// <param name="cls">The class.</param>
    /// <returns>The ids.</returns>
    public static IReadOnlyList<string> KitIds(C.ClassDef cls)
    {
        ArgumentNullException.ThrowIfNull(cls);
        var a = cls.Abilities;
        return new[] { a.Rot1, a.Rot2, a.Rot3, a.Rot4, a.Rot5, a.Interrupt, a.Defensive, a.Utility, a.Movement };
    }
}
