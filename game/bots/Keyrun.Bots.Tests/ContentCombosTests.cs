using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Xunit;
using C = Keyrun.Content;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Finding f-89b0825e: the combo tables the game's run plays (game/src/Host/HostRunSetup.cs Build calls
/// <see cref="ContentCombos.ToComboTables"/> with exactly these arguments) checked against the real content
/// (content/data/combos.json plus game/content/data/combo-riders.json, loaded by <see cref="C.ContentLoader"/>). Every
/// declared row lands at the cell the sim looks up (Payoff.TryClosePayoff reads the burst table at (Window setupClass,
/// Payoff actor classId), TryClosePayoffOnFloor reads the field table the same way), no cell exists that content did
/// not declare, a <c>"*"</c> row fans out to every class but its own, and a rider replaces the frozen row's effects.
/// </summary>
public sealed class ContentCombosTests
{
    private const string AnyClass = "*";

    private static readonly C.ContentRegistry Registry = C.ContentLoader.Load();

    // HostRunSetup.Build: ContentCombos.ToComboTables(registry.Combos.Values, registry.Classes.Keys.ToList(), registry.ComboRiders).
    private static (ComboTable Combos, ComboTable Field) Tables() =>
        ContentCombos.ToComboTables(Registry.Combos.Values, Registry.Classes.Keys.ToList(), Registry.ComboRiders);

    private static IReadOnlyList<string> ClassIds => Registry.Classes.Keys.ToList();

    private static IReadOnlyList<C.ComboEntry> ExactRows => Registry.Combos.Values.Where(e => e.PayoffClass != AnyClass).ToList();

    private static IReadOnlyList<C.ComboEntry> FieldRows => Registry.Combos.Values.Where(e => e.PayoffClass == AnyClass).ToList();

    private static IReadOnlyList<C.EffectOp> SourceOps(C.ComboEntry entry) =>
        Registry.ComboRiders.TryGetValue(entry.Id, out var rider) ? rider.Effects : entry.Effects;

    // Every key a lookup can be made with: the classes, every setup class in content (the "boss" pseudo-class) and "*".
    private static IReadOnlyList<string> AllKeys =>
        ClassIds.Concat(Registry.Combos.Values.Select(e => e.SetupClass)).Append(AnyClass).Distinct(StringComparer.Ordinal).ToList();

    // The sim ops of a combo match its content source op for op, checked here without ContentCombos.ToSimOp: the same
    // count, the same op kind in order, and a damage op's amount and school carried through.
    private static void AssertOpsMatch(C.ComboEntry entry, ComboDef def)
    {
        var source = SourceOps(entry);
        Assert.Equal(source.Count, def.Effects.Count);
        for (var i = 0; i < source.Count; i++)
        {
            Assert.Equal(source[i].GetType().Name, def.Effects[i].GetType().Name);
            if (source[i] is C.DamageOp d)
            {
                var sim = Assert.IsType<DamageOp>(def.Effects[i]);
                Assert.Equal(d.Amount, sim.Amount);
                Assert.Equal(d.School, sim.School);
            }
        }
    }

    [Fact(DisplayName = "ContentCombos: every exact combos.json row is in the burst table at its (setupClass, payoffClass) cell")]
    public void Every_exact_row_is_at_its_cell()
    {
        var (combos, _) = Tables();
        Assert.NotEmpty(ExactRows);
        foreach (var entry in ExactRows)
        {
            var def = combos.Lookup(entry.SetupClass, entry.PayoffClass);
            Assert.True(def is not null, $"burst table has no cell ({entry.SetupClass}, {entry.PayoffClass}) for combo {entry.Id}");
            Assert.Equal(entry.Id, def!.Id);
            AssertOpsMatch(entry, def);
        }
    }

    [Fact(DisplayName = "ContentCombos: a \"*\" row is in the field table for every class except its own setup class")]
    public void Every_field_row_fans_out_to_every_other_class()
    {
        var (_, field) = Tables();
        Assert.NotEmpty(FieldRows);
        foreach (var entry in FieldRows)
        {
            foreach (var payoff in ClassIds)
            {
                var def = field.Lookup(entry.SetupClass, payoff);
                if (payoff == entry.SetupClass)
                {
                    Assert.True(def is null, $"field row {entry.Id} closes on its own setup class {payoff}");
                    continue;
                }
                Assert.True(def is not null, $"field table has no cell ({entry.SetupClass}, {payoff}) for combo {entry.Id}");
                Assert.Equal(entry.Id, def!.Id);
                AssertOpsMatch(entry, def);
            }
        }
    }

    [Fact(DisplayName = "ContentCombos: no cell exists in either table that content did not declare")]
    public void No_cell_is_invented()
    {
        var (combos, field) = Tables();
        var declaredBurst = 0;
        var declaredField = 0;
        foreach (var setup in AllKeys)
        {
            foreach (var payoff in AllKeys)
            {
                var exact = ExactRows.SingleOrDefault(e => e.SetupClass == setup && e.PayoffClass == payoff);
                var fieldRow = FieldRows.SingleOrDefault(e => e.SetupClass == setup);
                var fieldExpected = fieldRow is not null && payoff != setup && ClassIds.Contains(payoff);
                declaredBurst += exact is null ? 0 : 1;
                declaredField += fieldExpected ? 1 : 0;

                Assert.True(
                    (combos.Lookup(setup, payoff)?.Id) == exact?.Id,
                    $"burst ({setup}, {payoff}): table has {combos.Lookup(setup, payoff)?.Id ?? "nothing"}, content declares {exact?.Id ?? "nothing"}");
                Assert.True(
                    (field.Lookup(setup, payoff)?.Id) == (fieldExpected ? fieldRow!.Id : null),
                    $"field ({setup}, {payoff}): table has {field.Lookup(setup, payoff)?.Id ?? "nothing"}, content declares {(fieldExpected ? fieldRow!.Id : "nothing")}");
            }
        }

        // The equivalence above is only meaningful over a non-empty declaration: the content declares both kinds.
        Assert.Equal(ExactRows.Count, declaredBurst);
        Assert.Equal(FieldRows.Sum(r => ClassIds.Count(c => c != r.SetupClass)), declaredField);
        Assert.True(declaredBurst > 0 && declaredField > 0);
    }

    [Fact(DisplayName = "ContentCombos: a combo-riders.json rider replaces the frozen combos.json effects (Anthem, Steam)")]
    public void Riders_replace_the_frozen_effects()
    {
        var (combos, _) = Tables();

        var anthem = combos.Lookup("chanter", "warden");
        Assert.NotNull(anthem);
        Assert.Equal("anthem", anthem!.Id);
        var haste = Assert.IsType<PartyEffectOp>(Assert.Single(anthem.Effects));
        Assert.Equal("hasted", haste.EffectId);

        var steam = combos.Lookup("cinder", "chanter");
        Assert.NotNull(steam);
        Assert.Equal("steam", steam!.Id);
        Assert.Contains(steam.Effects, op => op is CloudOp { EffectId: "blinded" });

        // Every rider wins over its frozen row wherever that row lands.
        Assert.NotEmpty(Registry.ComboRiders);
        foreach (var (id, rider) in Registry.ComboRiders)
        {
            var entry = Registry.Combos[id];
            var def = entry.PayoffClass == AnyClass
                ? Tables().Field.Lookup(entry.SetupClass, ClassIds.First(c => c != entry.SetupClass))
                : combos.Lookup(entry.SetupClass, entry.PayoffClass);
            Assert.NotNull(def);
            Assert.Equal(rider.Effects.Select(o => ContentCombos.ToSimOp(o, id)), def!.Effects);
        }
    }

    [Fact(DisplayName = "ContentCombos: the BIBLE section 4 cells resolve to their named combos, self-combos resolve to nothing")]
    public void Named_cells_resolve()
    {
        var (combos, field) = Tables();
        Assert.Equal("shatter", combos.Lookup("warden", "cinder")?.Id);
        Assert.Equal("consecrate", combos.Lookup("warden", "chanter")?.Id);
        Assert.Equal("forge", combos.Lookup("cinder", "warden")?.Id);
        Assert.Equal("flashfire", combos.Lookup("chanter", "cinder")?.Id);
        Assert.Equal("lock-warden", combos.Lookup("boss", "warden")?.Id);
        Assert.Equal("bulwark-field", field.Lookup("warden", "chanter")?.Id);
        Assert.Equal("seedbloom-field", field.Lookup("boss", "chanter")?.Id);
        Assert.Null(combos.Lookup("warden", "warden"));
        Assert.Null(field.Lookup("cinder", "cinder"));
        Assert.Null(combos.Lookup("warden", AnyClass));
    }
}
