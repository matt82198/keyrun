using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// FORCES-ROUTE-PLAN M5 FORTIFIED-TYRANNICAL (BIBLE s8): keys 2-4 none, 5-9 one by rotation parity, 10+ both, with
/// Fortified on non-boss enemies and Tyrannical on the boss and its adds, and exactly one key-set Clause on top.
/// </summary>
public sealed class AffixesTests
{
    /// <summary>The bible section 8 values (the shipped run.json carries the same; RunAffixesConfigTests pins that).</summary>
    public static readonly AffixTable BibleTable = new(new AffixScale(2000, 3000), new AffixScale(2500, 1500), 5, 10);

    private static readonly string[] ClauseIds = { "tide", "echo", "pact", "hush" };

    [Theory]
    [InlineData(4, 0, false, false)]
    [InlineData(4, 1, false, false)]
    [InlineData(5, 0, true, false)]
    [InlineData(5, 1, false, true)]
    [InlineData(9, 2, true, false)]
    [InlineData(9, -1, false, true)]
    [InlineData(10, 0, true, true)]
    [InlineData(10, 1, true, true)]
    public void Keys_4_5_and_10_under_rotations_0_and_1_give_none_one_then_the_other_and_both(int key, int rotation, bool fortified, bool tyrannical)
    {
        Assert.Equal(new KeyAffixes(fortified, tyrannical), Affixes.AffixesFor(BibleTable, key, rotation));
    }

    [Fact]
    public void A_key_10_trash_spawn_has_12000_bp_health_and_13000_bp_damage_and_the_boss_12500_and_11500()
    {
        var both = Affixes.AffixesFor(BibleTable, 10, 0);
        Assert.Equal(12000, Affixes.HealthBp(BibleTable, both, bossSide: false));
        Assert.Equal(13000, Affixes.DamageBp(BibleTable, both, bossSide: false));
        Assert.Equal(12500, Affixes.HealthBp(BibleTable, both, bossSide: true));
        Assert.Equal(11500, Affixes.DamageBp(BibleTable, both, bossSide: true));
        Assert.Equal(1200, Affixes.ScaleHealth(1000, Affixes.HealthBp(BibleTable, both, bossSide: false)));
    }

    [Fact]
    public void Fortified_alone_leaves_the_boss_side_at_10000_and_Tyrannical_alone_leaves_trash_at_10000()
    {
        var fortified = Affixes.AffixesFor(BibleTable, 5, 0);
        Assert.Equal(12000, Affixes.HealthBp(BibleTable, fortified, bossSide: false));
        Assert.Equal(10000, Affixes.HealthBp(BibleTable, fortified, bossSide: true));
        Assert.Equal(10000, Affixes.DamageBp(BibleTable, fortified, bossSide: true));
        var tyrannical = Affixes.AffixesFor(BibleTable, 5, 1);
        Assert.Equal(10000, Affixes.HealthBp(BibleTable, tyrannical, bossSide: false));
        Assert.Equal(12500, Affixes.HealthBp(BibleTable, tyrannical, bossSide: true));
    }

    [Fact]
    public void ScaleDamage_scales_every_damage_op_and_variant_and_leaves_other_ops_alone()
    {
        var ability = new AbilityDef
        {
            Id = "choir.meleeSwing",
            CastTicks = 0,
            GcdTicks = 0,
            CooldownTicks = 0,
            Charges = 1,
            Cost = new AbilityCost("none", 0),
            Range = 3000,
            Shape = new AbilityShape(ShapeKind.Target),
            Interruptible = false,
            Effects = new EffectOp[] { new DamageOp(40, "physical"), new HealOp(10) },
            Variants = new Dictionary<string, IReadOnlyList<EffectOp>>(StringComparer.Ordinal) { ["mend"] = new EffectOp[] { new DamageOp(7, "magic") } },
        };
        var scaled = Affixes.ScaleDamage(ability, 13000);
        Assert.Equal(new EffectOp[] { new DamageOp(52, "physical"), new HealOp(10) }, scaled.Effects);
        Assert.Equal(new EffectOp[] { new DamageOp(9, "magic") }, scaled.Variants!["mend"]);
        Assert.Same(ability, Affixes.ScaleDamage(ability, 10000));
    }

    [Fact]
    public void The_key_sets_exactly_one_Clause_from_the_start_key_and_none_below_it()
    {
        Assert.Null(ClauseRotation.ForKey(ClauseIds, 4, 0, 5));
        var sorted = ClauseIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        for (var r = 0; r < 8; r++)
        {
            Assert.Equal(sorted[r % 4], ClauseRotation.ForKey(ClauseIds, 10, r, 5));
        }
        Assert.Equal(sorted[3], ClauseRotation.ForKey(ClauseIds, 5, -1, 5));
        var options = RunOptions.ForKey(ClauseIds, 10, 1, 5);
        Assert.Equal(sorted[1], options.ClauseId);
        Assert.Equal(1, options.RotationIndex);
        Assert.Null(RunOptions.ForKey(ClauseIds, 2, 1, 5).ClauseId);
    }

    private static ClauseDef Def(string id, ClauseKind kind, params (string, long)[] ps)
    {
        var d = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (k, v) in ps)
        {
            d[k] = v;
        }
        return new ClauseDef(id, id, kind, d, id);
    }

    private static readonly IReadOnlyDictionary<string, ClauseDef> ClauseDefs = new Dictionary<string, ClauseDef>(StringComparer.Ordinal)
    {
        ["echo"] = Def("echo", ClauseKind.Echo, ("addMaxHealth", 600), ("spawnDelayTicks", 40)),
        ["tide"] = Def("tide", ClauseKind.Tide, ("driftMmPerTick", 50)),
        ["hush"] = Def("hush", ClauseKind.Hush, ("intervalTicks", 900), ("silenceDurationTicks", 40)),
        ["pact"] = Def("pact", ClauseKind.Pact, ("comboDamageBp", 2000), ("soloDamageBp", -1000)),
    };

    [Theory]
    [InlineData(5, 0)]
    [InlineData(5, 1)]
    [InlineData(9, 2)]
    [InlineData(10, 0)]
    [InlineData(10, 3)]
    [InlineData(15, 1)]
    public void The_key_set_Clause_layers_on_top_of_the_affixes_and_a_second_Clause_never_stacks(int key, int rotation)
    {
        var affixes = Affixes.AffixesFor(BibleTable, key, rotation);
        Assert.Equal(key >= 10 ? 2 : 1, affixes.Names.Count);

        var options = RunOptions.ForKey(ClauseIds, key, rotation, 5);
        Assert.NotNull(options.ClauseId);
        Assert.Equal(rotation, options.RotationIndex);
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(key), ClauseDefs[options.ClauseId!]);
        Assert.Equal(options.ClauseId, rules.ActiveClauseId);

        // The Clause does not change the affixes: same key and rotation, same Fortified/Tyrannical set.
        Assert.Equal(affixes, Affixes.AffixesFor(BibleTable, key, rotation));
        var other = ClauseDefs.Keys.First(id => id != options.ClauseId);
        Assert.Throws<ClauseAlreadyActiveException>(() => Clauses.ApplyClause(rules, ClauseDefs[other]));
    }

    // Finding f-319aced5 (Matt 2026-10-20, "affixes follow current WoW"): the game's run must actually rotate. The host
    // derives the rotation index from the run seed (Affixes.RotationIndexFor), so a replay with the same seed and key
    // rebuilds the same affixes and Clause, and consecutive seeds take turns, as WoW's weeks do. The host now takes the
    // ISO week instead (Keyrun.Host.AffixWeek, Host/AffixWeekTests.cs); these pin the seed function itself.
    [Theory]
    [InlineData(1L, 4, false, false)]
    [InlineData(1L, 5, false, true)]
    [InlineData(1L, 9, false, true)]
    [InlineData(1L, 10, true, true)]
    [InlineData(2L, 4, false, false)]
    [InlineData(2L, 5, true, false)]
    [InlineData(2L, 9, true, false)]
    [InlineData(2L, 10, true, true)]
    public void A_seeds_rotation_gives_none_below_5_one_from_5_to_9_and_both_from_10(long seed, int key, bool fortified, bool tyrannical)
    {
        Assert.Equal(new KeyAffixes(fortified, tyrannical), Affixes.AffixesFor(BibleTable, key, Affixes.RotationIndexFor(seed)));
    }

    [Theory]
    [InlineData(1L, 1)]
    [InlineData(2L, 2)]
    [InlineData(7L, 7)]
    [InlineData(0L, 0)]
    [InlineData(-1L, int.MaxValue)]
    [InlineData(long.MaxValue, int.MaxValue)]
    [InlineData(0x1_0000_0003L, 3)]
    public void The_rotation_index_is_a_pure_non_negative_function_of_the_seed(long seed, int expected)
    {
        Assert.Equal(expected, Affixes.RotationIndexFor(seed));
        Assert.Equal(Affixes.RotationIndexFor(seed), Affixes.RotationIndexFor(seed));
    }

    [Fact]
    public void Consecutive_seeds_alternate_Fortified_and_Tyrannical_on_a_key_7_and_cycle_every_Clause()
    {
        var seen = new List<string>();
        var clauses = new HashSet<string>(StringComparer.Ordinal);
        for (long seed = 1; seed <= 8; seed++)
        {
            var rotation = Affixes.RotationIndexFor(seed);
            seen.Add(string.Join("+", Affixes.AffixesFor(BibleTable, 7, rotation).Names));
            clauses.Add(RunOptions.ForKey(ClauseIds, 7, rotation, 5).ClauseId!);
        }
        Assert.Equal(new[] { "Tyrannical", "Fortified", "Tyrannical", "Fortified", "Tyrannical", "Fortified", "Tyrannical", "Fortified" }, seen);
        Assert.Equal(4, clauses.Count);
    }

    [Fact]
    public void Key_4_carries_neither_an_affix_nor_a_Clause()
    {
        for (var r = 0; r < 4; r++)
        {
            Assert.Empty(Affixes.AffixesFor(BibleTable, 4, r).Names);
            Assert.Null(RunOptions.ForKey(ClauseIds, 4, r, 5).ClauseId);
        }
    }
}
