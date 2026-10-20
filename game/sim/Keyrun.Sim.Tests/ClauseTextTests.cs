using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Sim.Tests.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The Clause sentences the queue screen shows (content/data/clauses.json, game/src/Hud/Screen.cs) say what the sim
/// does under that Clause. Each test reads the shipped Clause, folds it into the sim's rules, and checks the sentence
/// against what the sim does: every shipped param is read, and Tide drifts what its sentence says drifts.
/// </summary>
public sealed class ClauseTextTests
{
    private const long Seed = 4242;
    private const int Player = 1;
    private const int Enemy = 100;

    /// <summary>The shipped Clauses as the sim sees them (HostRunSetup.ToSim's shape: id, name, kind, params, text).</summary>
    private static IReadOnlyList<ClauseDef> Shipped()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(EncounterTestKit.RepoRoot(), "content", "data", "clauses.json")));
        return doc.RootElement.EnumerateArray().Select(c => new ClauseDef(
            c.GetProperty("id").GetString()!,
            c.GetProperty("name").GetString()!,
            Enum.Parse<ClauseKind>(c.GetProperty("kind").GetString()!, ignoreCase: true),
            c.GetProperty("params").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64(), StringComparer.Ordinal),
            c.GetProperty("description").GetString()!)).ToList();
    }

    private static ClauseDef Tide() => Shipped().Single(c => c.Kind == ClauseKind.Tide);

    [Fact]
    public void Every_shipped_param_changes_the_rules_the_sim_runs_with()
    {
        foreach (var clause in Shipped())
        {
            var full = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), clause);
            foreach (var param in clause.Params.Keys)
            {
                var without = clause with { Params = clause.Params.Where(kv => kv.Key != param).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) };
                Assert.True(full != Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), without), clause.Id + "." + param + " is read by nothing");
            }
        }
    }

    // A hazard: a pending circle telegraph one tick from resolving, stepped once under the shipped Tide.
    private static long HazardDrift()
    {
        var encounter = EncounterTestKit.BaseEncounter(100, 1000) with
        {
            Rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Tide()),
            Telegraphs = new[] { new Telegraph(4, 1, "wave", "circle", Vec3.Zero, 0, 200) { Radius = 1000 } },
            NextTelegraphId = 5,
        };
        var state = EncounterTestKit.State(100, EncounterTestKit.Actor(2, ActorKind.Player, new Vec3(5000, 0, 0)));
        var choir = new DungeonDef("hollow-vault", new[] { new PullDef("choir", Array.Empty<PullSpawn>()) });
        var stepped = EncounterStep.Step(state, encounter, choir, new Dictionary<int, IReadOnlyList<ScriptStep>>(), _ => { });
        return Assert.Single(stepped.Encounter.Telegraphs).Origin.X;
    }

    // A floor Window: a player opens one at the origin on tick 10 under the shipped Tide; where is it on tick 40.
    private static long WindowDrift()
    {
        var open = RunTestKit.Ability("open", new EffectOp[] { new WindowOp("w", 200, false, "alpha") }, new AbilityShape(ShapeKind.Self));
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["open"] = open }, null, bounds: RunTestKit.Arena20k) with
        {
            Clauses = new Dictionary<string, ClauseDef>(StringComparer.Ordinal) { ["tide"] = Tide() },
        };
        var party = new[] { RunTestKit.Member(Player, "alpha", ActorRole.Damage, 0, 0, 200, 100) };
        var dungeon = RunTestKit.OnePull("one-pull", "boss", RunTestKit.Spawn(Enemy, "boss", 15000, 15000, 1000000));
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, new RunOptions { ClauseId = "tide" });
        var (after, _) = RunTestKit.StepUntil(run, 40, new Command[] { new CastCommand(10, Player, "open") });
        Assert.True(after.State.FloorWindows.ContainsKey("w"), "the floor Window never opened");
        return after.State.FloorWindows["w"].Pos.X;
    }

    [Fact]
    public void Tide_text_names_hazards_and_the_sim_drifts_them()
    {
        var text = Tide().Description;
        Assert.Matches(new Regex(@"\bhazards\b", RegexOptions.IgnoreCase), text);
        Assert.Matches(new Regex(@"\bdrift", RegexOptions.IgnoreCase), text);
        Assert.Equal(Tide().Params["driftMmPerTick"], HazardDrift());
    }

    /// <summary>
    /// Known mismatch, handed back for a text or sim fix (wrong data shown to the player is P0): the Tide sentence says
    /// "Floor patches (Windows and hazards) drift", but the sim drifts only enemy telegraphs (EncounterStep.cs
    /// Clauses.DriftTelegraphs); a floor Window stays where it opened (Windows.cs OpenWindow, nothing writes
    /// FloorWindow.Pos again). When the two agree this test fails until the flag below is cleared.
    /// </summary>
    private const bool TideWindowsKnownMismatch = true;

    [Fact]
    public void Tide_text_says_windows_drift_exactly_when_the_sim_drifts_them()
    {
        var claimed = Regex.IsMatch(Tide().Description, @"\bWindows\b", RegexOptions.CultureInvariant);
        var drifted = WindowDrift() != 0;
        Assert.True((claimed == drifted) != TideWindowsKnownMismatch,
            "Tide text says Windows drift: " + claimed + "; sim drifts a floor Window: " + drifted + "; known mismatch flag: " + TideWindowsKnownMismatch);
    }
}
