// RED spec for the oracle setup writer (R-K4-17; game/sim/ORACLE.md sections 4 and 6; recon acc226c7).
// The four committed oracle setups are frozen snapshots of a TypeScript createRun that no longer exists, so each
// setup.json is re-written from the live content registry through the same ToSim adapter production uses
// (game/src/HostCore/HostRunSetup.cs:219-226). Production code is NOT written in this lane. Specified API:
//
//   namespace Keyrun.Bots.Oracle;
//   public static class OracleSetupWriter
//   {
//       /// Writes <outputDir>/<setupName> from the live registry at the setup's key and tempo, through the production
//       /// ToSim adapter. Deterministic: same registry, same bytes. Returns the file it wrote.
//       public static OracleSetupRecord Write(ContentRegistry registry, string setupName, string outputDir);
//   }
//   public sealed record OracleSetupRecord(string Path, long Bytes, string Fnv);
//
//   /// The setups KEYRUN_RERECORD re-writes, in the order OracleTraceRecorderTests.Rerecord would write them.
//   public static class OracleRerecordPlan
//   {
//       public static IReadOnlyList<string> SetupFiles { get; }
//   }
//
//   /// Public seam needed by the scripts check (today ContentToSim.Dungeon.cs:144-146 ToSim(IEnumerable<C.ScriptStep>)
//   /// is private): maps a registry script to the sim's ScriptStep list, exactly as production does.
//   public static partial class HostRunSetup { public static IReadOnlyList<ScriptStep> ToSimScript(IEnumerable<C.ScriptStep> script); }
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Oracle;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Bots.Tests;

public sealed class OracleSetupWriterTests
{
    /// <summary>The hollow-vault-k2 setup: key 2 (manifest bots-hv-k2-s7 runs seed 7).</summary>
    private const string K2Setup = "hollow-vault-k2.setup.json";
    private const int KeyLevel = 2;
    private const long Seed = 7;

    private static string OutDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "oracle-setup-writer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact(DisplayName = "oracle setup writer > hollow-vault-k2: the written abilities are the live registry's ids, and each spawn's health and scripts equal the tempo-scaled raw content")]
    public void WritesK2SetupFromLiveRegistry()
    {
        var registry = ContentLoader.Load();
        var rec = OracleSetupWriter.Write(registry, K2Setup, OutDir());
        var setup = RunSetup.Load(rec.Path, Seed);

        // (1) abilities: the keys of the written RunContent are exactly the live registry's ability ids after ToSim.
        Assert.Equal(
            registry.Abilities.Keys.OrderBy(k => k, StringComparer.Ordinal),
            setup.Content.Abilities.Keys.OrderBy(k => k, StringComparer.Ordinal));

        // (2) scripts and health: TempoTests.Oracle_setup_scripts_and_health_equal_tempo_scaled_raw_content's comparison,
        // with the raw content read from the live registry (not from a fixture file). The scripts are the written
        // content.enemyScripts, read through the sim's own EncounterJson parser, keyed by actor id.
        var written = JsValue.Parse(File.ReadAllText(rec.Path)) as JsObject
            ?? throw new InvalidOperationException($"{K2Setup}: the written setup is not a JSON object");
        var writtenContent = (JsObject)written.Get("content")!;
        var scripts = EncounterJson.ParseScripts(writtenContent.Get("enemyScripts")!);

        var compared = 0;
        foreach (var spawn in setup.Dungeon.Pulls.SelectMany(p => p.Spawns))
        {
            var enemy = registry.Enemies[spawn.ClassId];
            Assert.Equal(Tempo.ScaleHealth(enemy.MaxHealth, KeyLevel), spawn.Health);
            Assert.Equal(Tempo.ScaleScript(HostRunSetup.ToSimScript(enemy.Script), KeyLevel), scripts[spawn.ActorId]);
            compared++;
        }
        Assert.Equal(10, compared); // 3 choir + 5 pair + seed + boss
        Assert.Equal(scripts.Count, compared);
    }

    [Fact(DisplayName = "oracle setup writer > hollow-vault-k2: the written setup reads back through RunSetup and its abilities, effects, enemy scripts and spawns equal the live registry ToSim output at key 2 tempo")]
    public void WrittenK2SetupReadsBackToTheLiveRegistryToSimOutput()
    {
        // No byte comparison with the committed fixture: it is a retired flat-arena TS snapshot (R-K4-17), not the live content.
        var registry = ContentLoader.Load();
        var rec = OracleSetupWriter.Write(registry, K2Setup, OutDir());
        var setup = RunSetup.Load(rec.Path, Seed);

        // (1) abilities: the read-back ability for each live id equals the production ToSim ability at key 2.
        Assert.Equal(
            registry.Abilities.Keys.OrderBy(k => k, StringComparer.Ordinal),
            setup.Content.Abilities.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (id, ability) in registry.Abilities)
        {
            AssertAbilityEqual(HostRunSetup.ToSimAbility(ability), setup.Content.Abilities[id]);
        }

        // (2) effects: the read-back status effect for each live id equals the production ToSim effect.
        foreach (var (id, effect) in registry.Effects)
        {
            Assert.Equal(HostRunSetup.ToSimEffect(effect), setup.Content.Effects[id]);
        }

        // (3) enemy scripts and spawn health: the written enemyScripts, read through the sim's own parser, equal the
        // production ToSim script scaled to key 2 tempo; spawn health equals the tempo-scaled raw content.
        var written = JsValue.Parse(File.ReadAllText(rec.Path)) as JsObject
            ?? throw new InvalidOperationException($"{K2Setup}: the written setup is not a JSON object");
        var writtenContent = (JsObject)written.Get("content")!;
        var scripts = EncounterJson.ParseScripts(writtenContent.Get("enemyScripts")!);

        var compared = 0;
        foreach (var spawn in setup.Dungeon.Pulls.SelectMany(p => p.Spawns))
        {
            var enemy = registry.Enemies[spawn.ClassId];
            Assert.Equal(Tempo.ScaleHealth(enemy.MaxHealth, KeyLevel), spawn.Health);
            Assert.Equal(Tempo.ScaleScript(HostRunSetup.ToSimScript(enemy.Script), KeyLevel), scripts[spawn.ActorId]);
            compared++;
        }
        Assert.Equal(10, compared); // 3 choir + 5 pair + seed + boss
        Assert.Equal(scripts.Count, compared);
    }

    /// <summary>Compares every field of a sim ability, element-wise for its lists (AbilityDef's record equality compares lists by reference).</summary>
    private static void AssertAbilityEqual(Keyrun.Sim.Combat.AbilityDef expected, Keyrun.Sim.Combat.AbilityDef actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.CastTicks, actual.CastTicks);
        Assert.Equal(expected.GcdTicks, actual.GcdTicks);
        Assert.Equal(expected.CooldownTicks, actual.CooldownTicks);
        Assert.Equal(expected.Charges, actual.Charges);
        Assert.Equal(expected.Cost, actual.Cost);
        Assert.Equal(expected.Range, actual.Range);
        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Interruptible, actual.Interruptible);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.Effects, actual.Effects);
        Assert.Equal(expected.Variants is null, actual.Variants is null);
        if (expected.Variants is { } variants)
        {
            Assert.Equal(
                variants.Keys.OrderBy(k => k, StringComparer.Ordinal),
                actual.Variants!.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var key in variants.Keys)
            {
                Assert.Equal(variants[key], actual.Variants![key]);
            }
        }
    }

    [Fact(DisplayName = "oracle setup writer > KEYRUN_RERECORD: the re-record plan writes exactly hollow-vault-k2 then hollow-vault-k10")]
    public void RerecordPlanWritesExactlyTheTwoRegistryDerivedSetups()
    {
        // The hand-authored pull fixtures (content-pull, scripted-pull) are not derived from the registry and are not re-recorded.
        Assert.Equal(
            new List<string> { "hollow-vault-k2.setup.json", "hollow-vault-k10.setup.json" },
            OracleRerecordPlan.SetupFiles.ToList());
    }
}
