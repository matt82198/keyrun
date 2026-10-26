// RED spec for finding f-e4fe5b24: OracleSetupWriter.cs line 139 passes Vec3.Zero for the boss actor (900), so a
// regenerated hollow-vault-k2 setup places the boss at the origin instead of at the boss layout position. The expected
// position is read from the same source the run uses (NavLayouts.PlaceDungeon: the layout box whose Pull is the boss pull,
// its Origin), never from a copied literal.
using System;
using System.IO;
using System.Linq;
using Keyrun.Bots.Oracle;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>Boss placement in the oracle setup the writer produces (f-e4fe5b24).</summary>
public sealed class OracleSetupBossPositionTests
{
    private const string K2Setup = "hollow-vault-k2.setup.json";
    private const long Seed = 7;

    [Fact(DisplayName = "oracle setup writer > hollow-vault-k2: boss actor 900 stands at the boss layout origin, not Vec3.Zero")]
    public void WritesBossActor900AtTheBossLayoutPosition()
    {
        var registry = ContentLoader.Load();
        var outDir = Path.Combine(AppContext.BaseDirectory, "oracle-boss-pos", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var rec = OracleSetupWriter.Write(registry, K2Setup, outDir);
        var setup = RunSetup.Load(rec.Path, Seed);

        // The position the run gives the boss: the Origin of the layout box holding the boss pull (NavLayouts.PlaceDungeon).
        var bossBox = registry.Layout.Boxes.Single(b => b.Pull == LayoutDef.BossPullId);
        var origin = bossBox.Origin ?? throw new InvalidOperationException("the boss layout box has no spawn origin");
        var expected = new Vec3(origin.X, 0, origin.Z);

        var boss = setup.Dungeon.Pulls.SelectMany(p => p.Spawns).Single(s => s.ActorId == HostRunSetup.BossActorId);
        Assert.Equal(expected, boss.Pos);
        Assert.NotEqual(Vec3.Zero, boss.Pos);
    }
}
