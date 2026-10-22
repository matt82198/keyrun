using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;
using SimPull = Keyrun.Sim.Encounter.PullDef;
using SimPullSpawn = Keyrun.Sim.Encounter.PullSpawn;
using SimScriptStep = Keyrun.Sim.Encounter.ScriptStep;

namespace Keyrun.Bots.Tests;

/// <summary>
/// PURPOSE: this artifact exists to build N1 PACK-LINKS (game/design/MPLUS-DUNGEON-PLAN.md; the rest of FORCES-ROUTE-PLAN
/// S3 PACK-SCHEMA) so a layout pack's <c>links</c> reach the sim the way the host arms a run (BIBLE section 9.1 chain
/// pulls; finding f-82d9390e); status: real.
/// <see cref="LayoutPackGroups.Build"/> copies each content pack's <see cref="LayoutPack.Links"/> onto its sim
/// <see cref="Keyrun.Sim.Encounter.PackGroup.Links"/>, and an unlinked pack carries none.
/// </summary>
public sealed class PackLinkAdapterTests
{
    [Fact(DisplayName = "Pack links (N1): LayoutPackGroups.Build carries each content pack's links onto its sim PackGroup")]
    public void Build_carries_the_content_links_onto_the_sim_pack()
    {
        LayoutDef shipped = ContentLoader.Load().Layout;
        var member = new ValueList<PullEnemy>(new[] { new PullEnemy { EnemyId = "choir.melee", Count = 1, Spawn = new SpawnPoint { X = 0, Z = 0 } } });
        var packs = shipped.Packs.Select(p => p with
        {
            Enemies = member,
            Links = p.Id == "cantor-vigil" ? new ValueList<string>(new[] { "hall-sentries" }) : new ValueList<string>(),
        }).ToList();
        LayoutDef layout = shipped with { Packs = new ValueList<LayoutPack>(packs) };
        var dungeon = new SimDungeon("d", new[] { new SimPull("choir", new[] { new SimPullSpawn(101, "choir.melee", Vec3.Zero, 0, 10) }) });
        var scripts = new Dictionary<int, IReadOnlyList<SimScriptStep>> { [101] = new List<SimScriptStep>() };

        var (built, _) = LayoutPackGroups.Build(layout, dungeon, scripts);

        Assert.Equal(new[] { "hall-sentries" }, built.Single(p => p.Id == "cantor-vigil").Links);
        Assert.All(built.Where(p => p.Id != "cantor-vigil"), p => Assert.Empty(p.Links));
    }
}
