using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// C#/TS parity for the talent patch path. The TS half (<c>tests/content/talent-patch-parity.test.ts</c>) runs every
/// shipped talent option and facet through the frozen TS patch engine (<c>content/apply-patch.ts applyPatch</c>, the
/// engine <c>content/apply.ts applyLoadout</c> calls) and commits the resulting ability definitions to
/// <c>Fixtures/talent-patch-parity.json</c>, byte-checked against a fresh TS run inside <c>npm test</c>. This class
/// reads that snapshot, parses each TS ability through the real <see cref="ContentLoader"/> (so both sides are the
/// same C# record type, compared with record equality), and asserts <see cref="TalentPicks.Resolve"/> produces the
/// same name, cast, GCD, cooldown, charges, cost, range, shape and radius, interruptible flag, tags, effects and
/// variants for every option. A drift in either implementation turns this red; a deliberate C# deviation is listed
/// in <see cref="KnownTalentMismatches"/> with its reason, and an entry that stops mismatching fails too.
/// </summary>
public sealed class TalentPatchParityTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static readonly Lazy<JsonObject> Snapshot = new(() =>
        JsonNode.Parse(File.ReadAllText(SnapshotPath()))!.AsObject());

    /// <summary>
    /// Deliberate C# deviations, keyed "option@ability:field". Each value is the reason. The C# talent layer replays
    /// an Effects-only patch onto every Chanter verse variant (TalentPicks.PatchVariants, TalentVariantPatchTests,
    /// BIBLE s5 "Every other ability behaves differently per verse"); the frozen TS applyPatch replaces Effects only and
    /// leaves the base Variants, so in TS the talent is dead in both verses (the sim plays the variant).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownTalentMismatches = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["amplify@chanter.dissonance:variants"] = VerseReason,
        ["grandHymn@chanter.hymn:variants"] = VerseReason,
        ["crescendo@chanter.refrain:variants"] = VerseReason,
    };

    private const string VerseReason =
        "C# replays an Effects-only talent patch onto each verse variant (TalentPicks.PatchVariants); frozen TS keeps the base variants";

    [Fact]
    public void The_snapshot_covers_every_shipped_talent_option_and_facet()
    {
        var talents = Rows("talents").Select(r => (string)r["optionId"]! + "@" + (string)r["abilityId"]!).ToList();
        var shipped = Reg.Classes.Values.SelectMany(c => c.Talents.SelectMany(f => f.Options.Select(o => o.Id + "@" + f.AbilityId))).ToList();
        Assert.NotEmpty(shipped);
        Assert.Equal(shipped.OrderBy(s => s, StringComparer.Ordinal), talents.OrderBy(s => s, StringComparer.Ordinal));

        var facets = Rows("facets").Select(r => (string)r["facetId"]!).OrderBy(s => s, StringComparer.Ordinal);
        Assert.NotEmpty(Reg.Facets);
        Assert.Equal(Reg.Facets.Keys.OrderBy(s => s, StringComparer.Ordinal), facets);
    }

    [Fact]
    public void Every_talent_option_patches_its_ability_the_same_in_CSharp_and_TS()
    {
        var mismatches = new List<string>();
        var compared = 0;
        foreach (var row in Rows("talents"))
        {
            string classId = (string)row["classId"]!, abilityId = (string)row["abilityId"]!, optionId = (string)row["optionId"]!;
            Assert.Equal("ok", (string)row["loadout"]!);
            var ts = ParseThroughLoader(classId, abilityId, row["ability"]!);
            var cs = Talented(classId, abilityId, optionId);
            foreach (var (field, tsValue, csValue) in Fields(ts, cs))
            {
                compared++;
                if (!Equals(tsValue, csValue))
                    mismatches.Add(optionId + "@" + abilityId + ":" + field + " | TS=" + Show(tsValue) + " | C#=" + Show(csValue));
            }
        }
        Assert.Equal(Rows("talents").Count * 12, compared);

        var unexpected = mismatches.Where(m => !KnownTalentMismatches.ContainsKey(Key(m))).ToList();
        Assert.True(unexpected.Count == 0, "C#/TS talent patch drift:\n" + string.Join("\n", unexpected));
        var stale = KnownTalentMismatches.Keys.Where(k => !mismatches.Any(m => Key(m) == k)).ToList();
        Assert.True(stale.Count == 0, "known mismatch no longer mismatches (remove it): " + string.Join(", ", stale));
    }

    /// <summary>
    /// Facets the frozen TS applyLoadout refuses to equip, each with its reason. The C# facet patch path is not on main
    /// yet (open PR 233, Keyrun.Sim.Runs.GearPatch in Run/Ash.cs), so the facet rows of the snapshot are recorded for
    /// that comparison and only this divergence is pinned here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownFacetLoadoutRefusals = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["warden.charm.vigilantsMark"] = SecondPayoffReason,
        ["warden.keystone.eternalVigil"] = SecondPayoffReason,
        ["cinder.charm.overloadedBolt"] = SecondPayoffReason,
        ["cinder.keystone.eternalFlare"] = SecondPayoffReason,
        ["chanter.charm.refrainsEdge"] = SecondPayoffReason,
        ["chanter.keystone.eternalHymn"] = SecondPayoffReason,
    };

    private const string SecondPayoffReason =
        "the facet adds a Payoff (BIBLE s7; C# Combat/Combos.cs ComboOp); frozen TS applyLoadout enforces exactly 2 Payoff rotational abilities and throws";

    [Fact]
    public void The_facets_TS_refuses_to_equip_are_exactly_the_known_second_payoff_facets()
    {
        var refused = Rows("facets").Where(r => (string)r["loadout"]! != "ok").Select(r => (string)r["facetId"]!);
        Assert.Equal(KnownFacetLoadoutRefusals.Keys.OrderBy(s => s, StringComparer.Ordinal), refused.OrderBy(s => s, StringComparer.Ordinal));
        foreach (var id in KnownFacetLoadoutRefusals.Keys)
            Assert.Contains("payoff", (IEnumerable<string>?)Reg.Facets[id].Patch.TagAdd ?? Array.Empty<string>());
    }

    private static string Key(string mismatch) => mismatch[..mismatch.IndexOf(" | ", StringComparison.Ordinal)];

    private static AbilityDef Talented(string classId, string abilityId, string optionId)
    {
        var cls = Reg.Classes[classId];
        var fork = cls.Talents.Select((f, i) => (f, i)).Single(x => x.f.AbilityId == abilityId).i;
        var option = cls.Talents[fork].Options.Select((o, i) => (o, i)).Single(x => x.o.Id == optionId).i;
        return TalentPicks.Start(cls).Pick(fork, option).Resolve(Reg.Abilities)[abilityId];
    }

    // The TS ability replaces the base entry in its abilities file and the whole content loads through the real
    // loader and validator, so the TS JSON becomes the same C# record the talent layer returns.
    private static AbilityDef ParseThroughLoader(string classId, string abilityId, JsonNode tsAbility)
    {
        var source = new MutableSource().Edit("abilities/" + classId + ".json", node =>
        {
            var array = node.AsArray();
            var index = array.Select((a, i) => (a, i)).Single(x => (string)x.a!["id"]! == abilityId).i;
            array[index] = JsonNode.Parse(tsAbility.ToJsonString());
        });
        return ContentLoader.Load(source.Build()).Abilities[abilityId];
    }

    private static IEnumerable<(string Field, object? Ts, object? Cs)> Fields(AbilityDef ts, AbilityDef cs)
    {
        yield return ("name", ts.Name, cs.Name);
        yield return ("castTicks", ts.CastTicks, cs.CastTicks);
        yield return ("gcdTicks", ts.GcdTicks, cs.GcdTicks);
        yield return ("cooldownTicks", ts.CooldownTicks, cs.CooldownTicks);
        yield return ("charges", ts.Charges, cs.Charges);
        yield return ("cost", ts.Cost, cs.Cost);
        yield return ("range", ts.Range, cs.Range);
        yield return ("shape", ts.Shape, cs.Shape);
        yield return ("interruptible", ts.Interruptible, cs.Interruptible);
        yield return ("tags", ts.Tags, cs.Tags);
        yield return ("effects", ts.Effects, cs.Effects);
        yield return ("variants", ts.Variants, cs.Variants);
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        IDictionary map => "{" + string.Join(", ", map.Keys.Cast<object>().Select(k => k + ": " + Show(map[k]))).TrimEnd() + "}",
        IEnumerable list => "[" + string.Join(", ", list.Cast<object?>().Select(Show)) + "]",
        _ when value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>) =>
            value.GetType().GetProperty("Key")!.GetValue(value) + ": " + Show(value.GetType().GetProperty("Value")!.GetValue(value)),
        _ => value.ToString() ?? "?",
    };

    private static List<JsonObject> Rows(string section) =>
        Snapshot.Value[section]!.AsArray().Select(n => n!.AsObject()).ToList();

    private static string SnapshotPath()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var path = Path.Combine(d.FullName, "game", "content", "Keyrun.Content.Tests", "Fixtures", "talent-patch-parity.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Fixtures/talent-patch-parity.json not found above " + AppContext.BaseDirectory);
    }
}
