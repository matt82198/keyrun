using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// R-PACE-1 (2026-10-22, event store seq 201): a zero-death choirlesson run at key 2 must end between 20400 and 22800
/// ticks (17:00 to 19:00 at 20 Hz), par stays at 24000. Trash combat is about 54% of the run, at roughly 34 s per mob on
/// the foundry-floor packs (about 680 ticks). The lever is trash MaxHealth only; bosses, abilities and pack composition
/// are untouched.
/// Derivation of the bound: the heaviest pool pack today sums to 29000 trash HP (slag-haulers, pour-line). Taking 34%
/// off trash HP (the 0.66 factor, the tuned value) brings a per-mob time-to-kill from about 34 s to about 22 s, and
/// 29000 * 0.66 = 19140, rounded up to 19200. The bound is re-derived from the applied factor, not loosened.
/// </summary>
public sealed class SunkenBellfoundryTrashTtkTests
{
    private const int MaxPackTrashHealth = 19200;

    private static readonly ContentRegistry Reg = ContentLoader.Load();

    [Fact(DisplayName = "R-PACE-1: every Sunken Bellfoundry pool pack sums to at most 19200 trash MaxHealth")]
    public void Every_pool_pack_trash_health_is_at_most_the_pace_bound()
    {
        foreach (var id in Reg.Dungeons["sunken-bellfoundry"].PackPool)
        {
            var total = Reg.Roster[id].Enemies.Sum(g => Reg.Enemies[g.EnemyId].MaxHealth * g.Count);
            Assert.True(total <= MaxPackTrashHealth, $"{id} sums to {total} trash HP, bound is {MaxPackTrashHealth}");
        }
    }
}
