using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// D66 "huge pulls are fun" (Matt 2026-10-22: "room design is lame, 2 mobs 4 mobs"): every trash pack of the Sunken
/// Bellfoundry's pool is a WoW-style big pull of 5 to 8 mobs of at least two kinds, and the fill adds no new mob kind.
/// </summary>
public sealed class SunkenBellfoundryPackSizeTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static int Mobs(RosterPack pack) => pack.Enemies.Sum(g => g.Count);

    [Fact(DisplayName = "Sunken Bellfoundry pool: every pack is a big pull of 5 to 8 mobs")]
    public void Every_pool_pack_has_five_to_eight_mobs()
    {
        var pool = Reg.Dungeons["sunken-bellfoundry"].PackPool.Select(id => Reg.Roster[id]).ToList();
        Assert.NotEmpty(pool);
        foreach (var pack in pool)
        {
            Assert.True(Mobs(pack) is >= 5 and <= 8, $"{pack.Id} has {Mobs(pack)} mobs");
        }
    }

    [Fact(DisplayName = "Sunken Bellfoundry pool: every pack mixes at least two mob kinds")]
    public void Every_pool_pack_mixes_kinds()
    {
        foreach (var id in Reg.Dungeons["sunken-bellfoundry"].PackPool)
        {
            var kinds = Reg.Roster[id].Enemies.Select(g => g.EnemyId).Distinct().Count();
            Assert.True(kinds >= 2, $"{id} has {kinds} kind");
        }
    }

    [Fact(DisplayName = "Sunken Bellfoundry pool: a pack fields only enemies content defines, each with a forces value")]
    public void Pool_packs_use_defined_priced_mobs()
    {
        foreach (var id in Reg.Dungeons["sunken-bellfoundry"].PackPool)
        {
            foreach (var group in Reg.Roster[id].Enemies)
            {
                Assert.True(Reg.Enemies.ContainsKey(group.EnemyId), $"{id}: unknown {group.EnemyId}");
                Assert.True(Reg.Forces.PointsOf(group.EnemyId) > 0, $"{id}: {group.EnemyId} is worth no forces");
            }
        }
    }
}
