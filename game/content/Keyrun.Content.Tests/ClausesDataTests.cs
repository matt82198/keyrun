using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// content/data/clauses.json: the four Clauses the queue screen offers (game/src/Hud/Screen.cs shows each one's name
/// and description). The file loads strictly, holds exactly one Clause per <see cref="ClauseKind"/> with exactly the
/// params the sim reads for that kind (game/sim/Keyrun.Sim/Encounter/Clauses.cs ApplyClause), and each description
/// says only what those params do. Behaviour the description claims beyond the params (Tide moving Windows) is
/// checked against the sim in game/sim/Keyrun.Sim.Tests/ClauseTextTests.cs.
/// </summary>
public sealed class ClausesDataTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    /// <summary>Ticks per second (game/sim/Keyrun.Sim/Core/Tick.cs TickHz).</summary>
    private const int TickHz = 20;

    /// <summary>The params Clauses.ApplyClause reads for each kind; a param outside this set is read by nothing.</summary>
    private static readonly Dictionary<ClauseKind, string[]> ReadParams = new()
    {
        [ClauseKind.Echo] = new[] { "addMaxHealth", "spawnDelayTicks" },
        [ClauseKind.Tide] = new[] { "driftMmPerTick" },
        [ClauseKind.Hush] = new[] { "intervalTicks", "silenceDurationTicks" },
        [ClauseKind.Pact] = new[] { "comboDamageBp", "soloDamageBp" },
    };

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    private static JsonObject ClauseNode(JsonNode root, string id) =>
        root.AsArray().Single(n => n!["id"]!.GetValue<string>() == id)!.AsObject();

    [Fact]
    public void Every_kind_has_exactly_one_clause_named_after_it()
    {
        var kinds = Enum.GetValues<ClauseKind>();
        Assert.Equal(kinds.Length, Reg.Clauses.Count);
        foreach (var kind in kinds)
        {
            var clause = Assert.Single(Reg.Clauses.Values, c => c.Kind == kind);
            Assert.Equal(kind.ToString().ToLowerInvariant(), clause.Id);
            Assert.False(string.IsNullOrWhiteSpace(clause.Name), clause.Id + " has no name");
            Assert.False(string.IsNullOrWhiteSpace(clause.Description), clause.Id + " has no description");
        }
    }

    [Fact]
    public void Every_clause_carries_exactly_the_params_the_sim_reads_and_none_is_zero()
    {
        foreach (var clause in Reg.Clauses.Values)
        {
            Assert.Equal(ReadParams[clause.Kind].OrderBy(p => p, StringComparer.Ordinal), clause.Params.Keys.OrderBy(p => p, StringComparer.Ordinal));
            // An absent param reads as 0 in the sim (Clauses.cs Param), so a 0 param is a Clause that does nothing.
            Assert.All(clause.Params, kv => Assert.NotEqual(0, kv.Value));
        }
    }

    [Fact]
    public void A_duplicate_clause_id_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("clauses.json", n => ClauseNode(n, "tide")["id"] = "echo"));
        Assert.Contains("duplicate clause id \"echo\"", ex.Message);
    }

    [Fact]
    public void An_unknown_clause_kind_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("clauses.json", n => ClauseNode(n, "tide")["kind"] = "flood"));
        Assert.Contains("clauses.json", ex.Message);
    }

    [Fact]
    public void A_clause_without_a_description_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("clauses.json", n => ClauseNode(n, "hush").Remove("description")));
        Assert.Contains("clauses.json", ex.Message);
    }

    /// <summary>A phrase a Clause description can carry and the param check it implies.</summary>
    private sealed record Rule(string Name, Regex Phrase, Func<ClauseDef, Match, bool> Holds);

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static int P(ClauseDef c, string name) => c.Params.TryGetValue(name, out var v) ? v : 0;

    private static readonly Rule[] Rules =
    {
        new("drift", Rx(@"\bdrift"), (c, _) => c.Kind == ClauseKind.Tide && P(c, "driftMmPerTick") > 0),
        // Slow means a party member walking (run.json movement) outpaces the drift.
        new("drifts slowly", Rx(@"\bdrift\w* slowly"), (c, _) => P(c, "driftMmPerTick") < Reg.Run.Movement.SpeedMmPerTick),
        new("every N seconds", Rx(@"\bevery (?<s>\d+) seconds"), (c, m) => P(c, "intervalTicks") == int.Parse(m.Groups["s"].Value) * TickHz),
        new("silence", Rx(@"\bsilence"), (c, _) => c.Kind == ClauseKind.Hush && P(c, "silenceDurationTicks") > 0),
        new("combos deal more", Rx(@"\bCombos deal more"), (c, _) => P(c, "comboDamageBp") > 0),
        new("outside a combo deal less", Rx(@"outside a Combo deal less"), (c, _) => P(c, "soloDamageBp") < 0),
        // Weak means below the weakest shipped enemy's health.
        new("weak add", Rx(@"\bspawns a weak add"),
            (c, _) => c.Kind == ClauseKind.Echo && P(c, "addMaxHealth") > 0 && P(c, "addMaxHealth") < Reg.Enemies.Values.Min(e => e.MaxHealth)),
        new("interrupted cast", Rx(@"\binterrupted cast"), (c, _) => c.Kind == ClauseKind.Echo && P(c, "spawnDelayTicks") > 0),
    };

    private static IEnumerable<string> Violations(ClauseDef c) =>
        Rules.Where(r => r.Phrase.Match(c.Description) is { Success: true } m && !r.Holds(c, m))
            .Select(r => "clause " + c.Id + ": \"" + c.Description + "\" fails [" + r.Name + "]");

    [Fact]
    public void Every_clause_description_is_backed_by_its_params()
    {
        var violations = Reg.Clauses.Values.SelectMany(Violations).ToList();
        Assert.True(violations.Count == 0, "Clause text the params do not back:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Every_clause_description_is_checked_by_some_rule()
    {
        Assert.All(Reg.Clauses.Values, c => Assert.Contains(Rules, r => r.Phrase.IsMatch(c.Description)));
    }

    private static ClauseDef Clause(string id) => Reg.Clauses[id];

    private static ClauseDef With(string id, string param, int value) =>
        Clause(id) with { Params = new ValueMap<int>(Clause(id).Params.Select(kv => new KeyValuePair<string, int>(kv.Key, kv.Key == param ? value : kv.Value))) };

    public static IEnumerable<object[]> FalseClauses()
    {
        yield return new object[] { With("tide", "driftMmPerTick", 0) };
        yield return new object[] { With("tide", "driftMmPerTick", 400) };
        yield return new object[] { With("hush", "intervalTicks", 600) };
        yield return new object[] { With("hush", "silenceDurationTicks", 0) };
        yield return new object[] { With("pact", "comboDamageBp", -500) };
        yield return new object[] { With("pact", "soloDamageBp", 500) };
        yield return new object[] { With("echo", "addMaxHealth", 9000) };
        yield return new object[] { With("echo", "spawnDelayTicks", 0) };
        yield return new object[] { Clause("pact") with { Description = "Floor patches drift slowly." } };
    }

    [Theory]
    [MemberData(nameof(FalseClauses))]
    public void A_description_its_params_do_not_back_is_rejected(ClauseDef clause)
    {
        Assert.NotEmpty(Violations(clause));
    }
}
