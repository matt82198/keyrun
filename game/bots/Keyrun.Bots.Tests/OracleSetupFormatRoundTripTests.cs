// RED spec for the oracle setup format extension (R-K4-17; game/sim/ORACLE.md section 4). One fact per C#-only effect op
// the TS-era setup format never encoded: OracleSetupWriter.Write must encode it, RunSetup.Load must decode it, and the
// decoded op must equal the production ToSim adapter's op from the live registry. A writer that drops or stubs the op
// fails the exact equality; a reader without the decoding fails closed. Production code is not changed in this lane.
//
// Each fact writes a registry narrowed to the op under test (IsolateOp): the writer throws on the first unencoded op it
// meets, so without the narrowing every fact would fail on whichever C#-only op comes first in the live content and the
// red would not name its own op.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Oracle;
using Keyrun.Bots.Shared;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Tools.Replay;
using Xunit;
using C = Keyrun.Content;

namespace Keyrun.Bots.Tests;

public sealed class OracleSetupFormatRoundTripTests
{
    /// <summary>The setup the writer writes for the round trip (key 2, manifest seed 7).</summary>
    private const string SetupName = "hollow-vault-k2.setup.json";
    private const long Seed = 7;

    private static string OutDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "oracle-setup-format", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>The C#-only effect ops (no oracle content, not in the TS-era setup format).</summary>
    private static bool IsCSharpOnly(C.EffectOp e) =>
        e is C.DisplaceOp or C.PullOp or C.PartyEffectOp or C.CloudOp or C.CloserBurnOp or C.DamagePctOp;

    /// <summary>
    /// The live registry narrowed so the writer reaches only <paramref name="underTest"/>: abilities carrying another
    /// C#-only op are dropped, and only the combo rider <paramref name="riderId"/> (or none) is kept.
    /// </summary>
    private static C.ContentRegistry IsolateOp(C.ContentRegistry live, Func<C.EffectOp, bool> underTest, string? riderId)
    {
        var abilities = live.Abilities.Where(kv => !kv.Value.Effects.Any(e => IsCSharpOnly(e) && !underTest(e)));
        var effects = live.Effects.Where(kv => kv.Value.Periodic is null || !IsCSharpOnly(kv.Value.Periodic.Op) || underTest(kv.Value.Periodic.Op));
        var riders = live.ComboRiders.Where(kv => kv.Key == riderId);
        return live with
        {
            Abilities = new C.ValueMap<C.AbilityDef>(abilities),
            Effects = new C.ValueMap<C.EffectDef>(effects),
            ComboRiders = new C.ValueMap<C.ComboRider>(riders),
        };
    }

    /// <summary>A live ability whose only C#-only op is the one under test.</summary>
    private static C.AbilityDef LiveAbilityWith(C.ContentRegistry live, Func<C.EffectOp, bool> underTest) =>
        live.Abilities.Values.First(a => a.Effects.Any(underTest) && !a.Effects.Any(e => IsCSharpOnly(e) && !underTest(e)));

    /// <summary>Writes the isolated setup through the production writer and reads it back through the reader.</summary>
    private static RunSetup WriteAndRead(C.ContentRegistry registry)
    {
        var rec = OracleSetupWriter.Write(registry, SetupName, OutDir());
        return RunSetup.Load(rec.Path, Seed);
    }

    /// <summary>
    /// A combo-rider op (BIBLE section 4) lives in the combo table, not on an ability: the live row is the production
    /// ContentCombos table built from the full live registry, and the read-back row is the setup's own table at the same key.
    /// </summary>
    private static void AssertRiderComboRoundTrips(Func<C.EffectOp, bool> underTest, Type opType)
    {
        var live = C.ContentLoader.Load();
        var rider = live.ComboRiders.Values.First(r => r.Effects.Any(underTest));
        var entry = live.Combos[rider.Id];
        var classes = live.Classes.Keys.ToList();
        var (burst, field) = ContentCombos.ToComboTables(live.Combos.Values, classes, live.ComboRiders);

        var setup = WriteAndRead(IsolateOp(live, underTest, rider.Id));

        // A "*" payoff row fans out to the field table once per other class; an exact row is in the burst table.
        var payoff = entry.PayoffClass == "*" ? classes.First(c => c != entry.SetupClass) : entry.PayoffClass;
        var expectedTable = entry.PayoffClass == "*" ? field : burst;
        var actualTable = entry.PayoffClass == "*" ? setup.Content.FieldCombos : setup.Content.Combos;

        var expected = expectedTable.Lookup(entry.SetupClass, payoff)
            ?? throw new InvalidOperationException($"{entry.Id}: the live registry has no combo row");
        var actual = actualTable?.Lookup(entry.SetupClass, payoff)
            ?? throw new InvalidOperationException($"{entry.Id}: the read-back setup has no combo row");

        Assert.Contains(expected.Effects, e => e.GetType() == opType);
        Assert.Equal(expected.Effects.Count, actual.Effects.Count);
        for (var i = 0; i < expected.Effects.Count; i++)
        {
            Assert.Equal(expected.Effects[i], actual.Effects[i]);
        }
    }

    [Fact(DisplayName = "oracle setup format > displace: a live content ability's DisplaceOp (cinder.blink or warden.charge) written into a setup reads back equal to its live ToSim op")]
    public void DisplaceRoundTrips()
    {
        var live = C.ContentLoader.Load();
        var ability = LiveAbilityWith(live, e => e is C.DisplaceOp);

        var setup = WriteAndRead(IsolateOp(live, e => e is C.DisplaceOp, riderId: null));

        var expected = HostRunSetup.ToSimAbility(ability).Effects;
        var actual = setup.Content.Abilities[ability.Id].Effects;
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
        Assert.Contains(actual, e => e is DisplaceOp);
    }

    [Fact(DisplayName = "oracle setup format > pull: a PullOp on a live content ability reads back equal to its live ToSim op")]
    public void PullRoundTrips()
    {
        // drowned.undertow (content/data/abilities/vault.json) and bellfoundry carry a pull; the first such ability is used.
        var live = C.ContentLoader.Load();
        var ability = LiveAbilityWith(live, e => e is C.PullOp);

        var setup = WriteAndRead(IsolateOp(live, e => e is C.PullOp, riderId: null));

        var expected = HostRunSetup.ToSimAbility(ability).Effects;
        var actual = setup.Content.Abilities[ability.Id].Effects;
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
        Assert.Contains(actual, e => e is PullOp);
    }

    [Fact(DisplayName = "oracle setup format > partyEffect: the Anthem combo's party haste reads back equal to its live ToSim op")]
    public void PartyEffectRoundTrips() =>
        AssertRiderComboRoundTrips(e => e is C.PartyEffectOp, typeof(PartyEffectOp));

    [Fact(DisplayName = "oracle setup format > cloud: the Steam combo's floor cloud reads back equal to its live ToSim op")]
    public void CloudRoundTrips() =>
        AssertRiderComboRoundTrips(e => e is C.CloudOp, typeof(CloudOp));

    [Fact(DisplayName = "oracle setup format > closerBurn: the Cinderfield closer burn reads back equal to its live ToSim op")]
    public void CloserBurnRoundTrips() =>
        AssertRiderComboRoundTrips(e => e is C.CloserBurnOp, typeof(CloserBurnOp));
}
