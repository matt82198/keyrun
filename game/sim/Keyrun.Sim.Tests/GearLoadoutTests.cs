using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s7 "an item is a Tier plus one Facet; Facets are the chase": the gear equipped in <see cref="Progression"/>
/// reaches the next run. <see cref="GearLoadout.Of"/> carries every equipped item (slot order) and patches the wearer's
/// abilities with each Facet's <see cref="GearPatch"/> (the TS content/apply-patch.ts semantics, in sim types), and
/// <see cref="GearLoadout.ToRunOptions"/> hands the patched kit to createRun as <see cref="RunOptions.Loadouts"/>, the
/// run root's existing facet patch layer. The real patches are read straight from content/data/facets and
/// content/data/abilities, so every value asserted below comes from the data.
/// </summary>
public sealed class GearLoadoutTests
{
    private const int HumanId = 1;

    [Fact]
    public void An_equipped_item_is_carried_into_the_run_loadout_with_its_facet()
    {
        var facet = ContentFacet("warden", "warden.weapon.keenEdge");
        var progression = Progression.Start(7).BankRun(RunResult.Timed).Take(ItemOf("weapon", 3, facet.Facet));

        var loadout = GearLoadout.Of(progression, HumanId, WardenAbilities(), ContentPatches("warden"));

        Assert.Equal(HumanId, loadout.ActorId);
        Assert.Equal(new[] { "warden.weapon.keenEdge" }, loadout.Facets.Select(f => f.Id));
        Assert.Equal(3, Assert.Single(loadout.Gear).Tier);
        var options = loadout.ToRunOptions(null);
        Assert.NotNull(options);
        var strike = options!.Loadouts![HumanId]["warden.strike"];
        Assert.Equal(facet.Patch.Shape, strike.Shape);
        Assert.Empty(loadout.UnappliedFacetIds);
    }

    [Fact]
    public void The_patch_replaces_only_the_fields_the_facet_names()
    {
        var baseStrike = WardenAbilities()["warden.strike"];
        var facet = ContentFacet("warden", "warden.weapon.keenEdge");
        var patched = facet.Patch.ApplyTo(baseStrike);
        Assert.Equal(baseStrike with { Shape = facet.Patch.Shape! }, patched);
        Assert.NotEqual(baseStrike.Shape, patched.Shape);
    }

    [Fact]
    public void A_cooldown_facet_sets_the_data_cooldown_and_keeps_the_rest()
    {
        var baseBreak = WardenAbilities()["warden.guardBreak"];
        var facet = ContentFacet("warden", "warden.weapon.wideningBreak");
        var patched = facet.Patch.ApplyTo(baseBreak);
        Assert.Equal(facet.Patch.CooldownTicks, patched.CooldownTicks);
        Assert.Equal(facet.Patch.Shape, patched.Shape);
        Assert.Equal(baseBreak.Effects, patched.Effects);
        Assert.Equal(baseBreak.CastTicks, patched.CastTicks);
    }

    [Fact]
    public void EffectsAppend_keeps_the_base_ops_and_adds_the_facet_ops_after_them()
    {
        var baseBash = WardenAbilities()["warden.shieldBash"];
        var facet = ContentFacet("warden", "warden.armor.chillingBash");
        var patched = facet.Patch.ApplyTo(baseBash);
        Assert.Equal(baseBash.Effects.Concat(facet.Patch.EffectsAppend!), patched.Effects);
    }

    [Fact]
    public void TagAdd_merges_without_duplicates_and_tagRemove_drops()
    {
        var a = RunTestKit.Ability("zap", new EffectOp[] { new DamageOp(1, "physical") }) with { Tags = new[] { "a", "b" } };
        Assert.Equal(new[] { "a", "b", "c" }, new GearPatch { TagAdd = new[] { "b", "c" } }.ApplyTo(a).Tags);
        Assert.Equal(new[] { "b" }, new GearPatch { TagRemove = new[] { "a" } }.ApplyTo(a).Tags);
        Assert.Equal(new[] { "x" }, new GearPatch { TagAdd = new[] { "x" } }.ApplyTo(a with { Tags = null }).Tags);
    }

    [Fact]
    public void Two_items_patching_the_same_ability_both_apply_in_slot_order()
    {
        // Three warden items all patch Strike: keenEdge (weapon) reshapes it, momentumEngine (ring) appends an op, and
        // sunderedStorm (keystone) reshapes it again and adds tags. They stack in GearSlots order: the ring's op
        // survives the keystone, and the keystone (the later slot) wins the shape.
        var weapon = ContentFacet("warden", "warden.weapon.keenEdge");
        var ring = ContentFacet("warden", "warden.ring.momentumEngine");
        var keystone = ContentFacet("warden", "warden.keystone.sunderedStorm");
        Assert.All(new[] { weapon, ring, keystone }, f => Assert.Equal("warden.strike", f.Facet.AbilityId));
        var progression = Progression.Start(1)
            .Take(ItemOf("keystone", 2, keystone.Facet))
            .Take(ItemOf("ring", 2, ring.Facet))
            .Take(ItemOf("weapon", 2, weapon.Facet));

        var loadout = GearLoadout.Of(progression, HumanId, WardenAbilities(), ContentPatches("warden"));

        Assert.Equal(new[] { "warden.weapon.keenEdge", "warden.ring.momentumEngine", "warden.keystone.sunderedStorm" }, loadout.Facets.Select(f => f.Id));
        var baseStrike = WardenAbilities()["warden.strike"];
        var strike = loadout.Abilities["warden.strike"];
        Assert.Equal(Fingerprint(keystone.Patch.ApplyTo(ring.Patch.ApplyTo(weapon.Patch.ApplyTo(baseStrike)))), Fingerprint(strike));
        Assert.Equal(keystone.Patch.Shape, strike.Shape);
        Assert.Equal(baseStrike.Effects.Concat(ring.Patch.EffectsAppend!), strike.Effects);
        Assert.Subset(strike.Tags!.ToHashSet(), keystone.Patch.TagAdd!.ToHashSet());
    }

    [Fact]
    public void A_facet_without_a_patch_or_for_an_unknown_ability_is_carried_but_unapplied()
    {
        var known = ContentFacet("warden", "warden.weapon.keenEdge");
        var foreign = new Facet("cinder.weapon.sprayOfEmbers", "x", "cinder.emberBolt");
        var progression = Progression.Start(1).Take(ItemOf("weapon", 1, known.Facet)).Take(ItemOf("ring", 1, foreign));
        var noPatches = new Dictionary<string, GearPatch>(StringComparer.Ordinal);

        var loadout = GearLoadout.Of(progression, HumanId, WardenAbilities(), noPatches);

        Assert.Equal(2, loadout.Facets.Count);
        Assert.Equal(new[] { "warden.weapon.keenEdge", "cinder.weapon.sprayOfEmbers" }, loadout.UnappliedFacetIds);
        Assert.Empty(loadout.Abilities);
    }

    [Fact]
    public void No_gear_leaves_the_run_options_untouched()
    {
        var loadout = GearLoadout.Of(Progression.Start(1), HumanId, WardenAbilities(), ContentPatches("warden"));
        Assert.Empty(loadout.Gear);
        Assert.Null(loadout.ToRunOptions(null));
        var options = new RunOptions { ClauseId = "echo" };
        Assert.Same(options, loadout.ToRunOptions(options));
    }

    [Fact]
    public void Gear_keeps_the_clause_and_other_seats_loadouts()
    {
        var facet = ContentFacet("warden", "warden.weapon.wideningBreak");
        var loadout = GearLoadout.Of(Progression.Start(1).Take(ItemOf("weapon", 1, facet.Facet)), HumanId, WardenAbilities(), ContentPatches("warden"));
        var other = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["warden.strike"] = WardenAbilities()["warden.strike"] };
        var options = new RunOptions
        {
            ClauseId = "echo",
            Loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>> { [2] = other },
        };

        var merged = loadout.ToRunOptions(options)!;

        Assert.Equal("echo", merged.ClauseId);
        Assert.Same(other, merged.Loadouts![2]);
        Assert.Equal(facet.Patch.CooldownTicks, merged.Loadouts[HumanId]["warden.guardBreak"].CooldownTicks);
    }

    // Behaviour, not shape: the equipped item's cooldown reaches the created run and refuses the second cast.
    [Fact]
    public void An_equipped_cooldown_facet_changes_what_the_run_lets_the_wearer_cast()
    {
        var baseZap = RunTestKit.Ability("zap", new EffectOp[] { new DamageOp(1, "physical") }, cooldownTicks: 100);
        var facet = new Facet("test.weapon.slowZap", "Zap recovers slower.", "zap");
        var patches = new Dictionary<string, GearPatch>(StringComparer.Ordinal) { [facet.Id] = new GearPatch { CooldownTicks = 250 } };
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = baseZap };
        var party = new[]
        {
            RunTestKit.Member(HumanId, "warrior", ActorRole.Damage, -2000, 0, 200, 100),
            RunTestKit.Member(2, "target-dummy", ActorRole.Damage, 3000, 0, 300, 0),
        };
        var dungeon = RunTestKit.OnePull("empty", "p", RunTestKit.Spawn(11, "boss", 0, 0, 1));
        var content = RunTestKit.Content() with { Abilities = abilities };

        int Damages(Progression p)
        {
            var options = GearLoadout.Of(p, HumanId, abilities, patches).ToRunOptions(null);
            var run = RunRoot.CreateRun(42, party, dungeon, content, options);
            var commands = new[] { 0, 150, 260 }.Select(t => (Command)new CastCommand(t, HumanId, "zap", 2)).ToList();
            var (_, events) = RunTestKit.StepUntil(run, 300, commands);
            return events.Count(e => e.Type == "damage");
        }

        Assert.Equal(3, Damages(Progression.Start(1)));
        Assert.Equal(2, Damages(Progression.Start(1).Take(ItemOf("weapon", 1, facet))));
    }

    // Every shipped facet patch applies to its class's base ability without throwing and changes it.
    [Theory]
    [InlineData("warden")]
    [InlineData("cinder")]
    [InlineData("chanter")]
    public void Every_content_facet_patch_applies_to_its_base_ability(string classId)
    {
        var abilities = ContentAbilities(classId);
        var facets = ContentFacets(classId);
        Assert.Equal(20, facets.Count);
        foreach (var (facet, patch) in facets)
        {
            Assert.True(abilities.ContainsKey(facet.AbilityId), facet.Id);
            Assert.NotEqual(Fingerprint(abilities[facet.AbilityId]), Fingerprint(patch.ApplyTo(abilities[facet.AbilityId])));
        }
    }

    // Structural view of an ability (the record's list members compare by reference).
    private static string Fingerprint(AbilityDef a) => string.Join(
        "|",
        a with { Effects = Array.Empty<EffectOp>(), Tags = null, Variants = null },
        string.Join(",", a.Effects),
        string.Join(",", a.Tags ?? Array.Empty<string>()),
        string.Join(",", (a.Variants ?? new Dictionary<string, IReadOnlyList<EffectOp>>()).Select(kv => kv.Key + ":" + string.Join(",", kv.Value))));

    private static Item ItemOf(string slot, int tier, Facet facet) =>
        new(FormattableString.Invariant($"reward-{slot}-{facet.Id}-t{tier}-0"), slot, tier, facet, Rewards.StartingCharges);

    private static IReadOnlyDictionary<string, AbilityDef> WardenAbilities() => ContentAbilities("warden");

    private static (Facet Facet, GearPatch Patch) ContentFacet(string classId, string id) => ContentFacets(classId).Single(f => f.Facet.Id == id);

    private static IReadOnlyDictionary<string, GearPatch> ContentPatches(string classId) =>
        ContentFacets(classId).ToDictionary(f => f.Facet.Id, f => f.Patch, StringComparer.Ordinal);

    // content/data/facets/<class>.json read the way HostRunSetup.GearPatches converts it (a line's full width becomes
    // the sim half-width radius; an appended Window op without setupClass is the wearer's class Window).
    private static List<(Facet Facet, GearPatch Patch)> ContentFacets(string classId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(DataPath("facets", classId + ".json")));
        var list = new List<(Facet, GearPatch)>();
        foreach (var f in doc.RootElement.EnumerateArray())
        {
            var id = f.GetProperty("id").GetString()!;
            var p = f.GetProperty("patch");
            var patch = new GearPatch
            {
                CastTicks = p.TryGetProperty("castTicks", out var ct) ? ct.GetInt32() : null,
                GcdTicks = p.TryGetProperty("gcdTicks", out var gt) ? gt.GetInt32() : null,
                CooldownTicks = p.TryGetProperty("cooldownTicks", out var cd) ? cd.GetInt32() : null,
                Charges = p.TryGetProperty("charges", out var ch) ? ch.GetInt64() : null,
                Range = p.TryGetProperty("range", out var rg) ? rg.GetInt64() : null,
                Cost = p.TryGetProperty("cost", out var co) ? new AbilityCost(co.GetProperty("resource").GetString()!, co.GetProperty("amount").GetInt64()) : null,
                Shape = p.TryGetProperty("shape", out var sh) ? Shape(sh) : null,
                TagAdd = p.TryGetProperty("tagAdd", out var ta) ? ta.EnumerateArray().Select(t => t.GetString()!).ToList() : null,
                EffectsAppend = p.TryGetProperty("effectsAppend", out var ea) ? Ops(ea, id, classId) : null,
            };
            list.Add((new Facet(id, f.GetProperty("text").GetString()!, f.GetProperty("abilityId").GetString()!), patch));
        }
        return list;
    }

    private static IReadOnlyDictionary<string, AbilityDef> ContentAbilities(string classId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(DataPath("abilities", classId + ".json")));
        var map = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        foreach (var a in doc.RootElement.EnumerateArray())
        {
            var id = a.GetProperty("id").GetString()!;
            var effects = a.TryGetProperty("variants", out var vs) && vs.TryGetProperty("mend", out var mend) ? mend : a.GetProperty("effects");
            var cost = a.GetProperty("cost");
            map[id] = new AbilityDef
            {
                Id = id,
                CastTicks = a.GetProperty("castTicks").GetInt32(),
                GcdTicks = a.GetProperty("gcdTicks").GetInt32(),
                CooldownTicks = a.GetProperty("cooldownTicks").GetInt32(),
                Charges = a.GetProperty("charges").GetInt64(),
                Cost = new AbilityCost(cost.GetProperty("resource").GetString()!, cost.GetProperty("amount").GetInt64()),
                Range = a.GetProperty("range").GetInt64(),
                Shape = Shape(a.GetProperty("shape")),
                Interruptible = a.GetProperty("interruptible").GetBoolean(),
                Effects = Ops(effects, id, classId),
                Tags = a.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToList(),
            };
        }
        return map;
    }

    // A width-bearing line (twinStrike, infernoBolt) is Abilities.LineShape: radius = half the full width.
    private static AbilityShape Shape(JsonElement s) => s.TryGetProperty("width", out var w)
        ? Abilities.LineShape(s.GetProperty("length").GetInt64(), w.GetInt64())
        : new(
        s.GetProperty("kind").GetString() switch
        {
            "target" => ShapeKind.Target,
            "self" => ShapeKind.Self,
            "cone" => ShapeKind.Cone,
            "circle" => ShapeKind.Circle,
            "line" => ShapeKind.Line,
            var k => throw new InvalidOperationException("shape " + k),
        },
        s.TryGetProperty("radius", out var r) ? r.GetInt64() : null,
        s.TryGetProperty("angle1024", out var g) ? g.GetInt64() : null,
        s.TryGetProperty("length", out var l) ? l.GetInt64() : null);

    private static IReadOnlyList<EffectOp> Ops(JsonElement arr, string where, string classId) => arr.EnumerateArray().Select(o => (EffectOp)(o.GetProperty("op").GetString() switch
    {
        "heal" => new HealOp(o.GetProperty("amount").GetInt64()),
        "damage" => new DamageOp(o.GetProperty("amount").GetInt64(), o.GetProperty("school").GetString()!),
        "applyEffect" => new ApplyEffectOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("stacks").GetInt64()),
        "removeEffect" => new RemoveEffectOp(o.GetProperty("effectId").GetString()!),
        "resource" => new ResourceOp(o.GetProperty("delta").GetInt64()),
        "threat" => new ThreatOp(o.TryGetProperty("amount", out var am) ? am.GetInt64() : null, o.TryGetProperty("multiplierBp", out var m) ? m.GetInt64() : null),
        "payoff" => new PayoffOp(),
        "interrupt" => new InterruptOp(),
        "displace" => new DisplaceOp(
            o.GetProperty("distanceMm").GetInt64(),
            o.TryGetProperty("freesBinds", out var fb) && fb.GetBoolean(),
            o.TryGetProperty("towardTarget", out var tt) && tt.GetBoolean()),
        "combo" => new ComboOp(o.GetProperty("comboTable").GetString()!),
        "partyEffect" => new PartyEffectOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("stacks").GetInt64()),
        "cloud" => new CloudOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("radiusMm").GetInt64()),
        "window" => new WindowOp(
            o.GetProperty("windowId").GetString()!,
            o.GetProperty("durationTicks").GetInt32(),
            o.GetProperty("onTarget").GetBoolean(),
            o.TryGetProperty("setupClass", out var sc) ? sc.GetString()! : classId,
            o.TryGetProperty("self", out var se) ? se.GetBoolean() : null,
            o.TryGetProperty("alternate", out var al) ? al.GetBoolean() : null,
            o.TryGetProperty("radiusMm", out var rm) ? rm.GetInt64() : null),
        var op => throw new InvalidOperationException($"{where}: op {op} not mapped by this test"),
    })).ToList();

    private static string DataPath(string kind, string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content", "data", kind, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException($"content/data/{kind}/{file} not found above " + AppContext.BaseDirectory);
    }
}
