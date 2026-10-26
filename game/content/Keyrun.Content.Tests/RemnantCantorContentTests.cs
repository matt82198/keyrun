using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// f-73d9d4e2: the Warrior Remnant is a two-healer pack whose healers are their own enemy, <c>remnant.cantor</c>,
/// not the shared <c>choir.lieutenant</c>. Content facts only: the enemy, its heal cast, and the pack that places it.
/// Pinned red first (S1 spec); S2 adds the content to make these green.
/// </summary>
public sealed class RemnantCantorContentTests
{
    private const string CantorId = "remnant.cantor";
    private const string HealCastId = "remnant.healCast";
    private const string PackId = "warrior-remnant";
    private const string SharedLieutenantId = "choir.lieutenant";

    private static readonly ContentRegistry Reg = ContentLoader.Load();

    [Fact]
    public void Remnant_cantor_is_a_declared_enemy_distinct_from_the_shared_choir_lieutenant()
    {
        Assert.True(Reg.Enemies.ContainsKey(CantorId), $"no enemy \"{CantorId}\" in content");
        Assert.True(Reg.Enemies.ContainsKey(SharedLieutenantId), $"no enemy \"{SharedLieutenantId}\" in content");

        EnemyDef cantor = Reg.Enemies[CantorId];
        EnemyDef lieutenant = Reg.Enemies[SharedLieutenantId];

        // Squishy healer (WoW M+ role): the Remnant Cantor has less max health than the shared lieutenant.
        Assert.True(cantor.MaxHealth < lieutenant.MaxHealth,
            $"{CantorId} MaxHealth {cantor.MaxHealth} is not below {SharedLieutenantId} MaxHealth {lieutenant.MaxHealth}");
    }

    [Fact]
    public void Remnant_cantor_heals_on_its_own_interruptible_cast_with_a_bar_and_a_floor_telegraph()
    {
        Assert.True(Reg.Enemies.ContainsKey(CantorId), $"no enemy \"{CantorId}\" in content");
        Assert.True(Reg.Abilities.ContainsKey(HealCastId), $"no ability \"{HealCastId}\" in content");

        AbilityDef heal = Reg.Abilities[HealCastId];
        Assert.True(heal.Interruptible, $"{HealCastId} must be interruptible (the Warrior kick ledger depends on it)");
        Assert.True(heal.CastTicks > 0, $"{HealCastId} castTicks {heal.CastTicks} (no cast bar)");

        // Script: the heal fires on a ~300 tick cadence (the finding's remnant.healCast cadence).
        ScriptStep[] healSteps = Reg.Enemies[CantorId].Script.Where(s => s.Cast == HealCastId).ToArray();
        Assert.Single(healSteps);
        Assert.Equal(300, healSteps[0].Every);

        // BIBLE section 3 floor telegraph: the heal is drawn ahead of resolution.
        Assert.NotNull(healSteps[0].Telegraph);
        Assert.True(healSteps[0].Telegraph!.LeadTicks > 0,
            $"{HealCastId} telegraph leadTicks {healSteps[0].Telegraph!.LeadTicks} (not drawn ahead)");
    }

    [Fact]
    public void Warrior_remnant_pack_places_two_remnant_cantors_and_no_shared_lieutenant()
    {
        LayoutPack pack = Reg.Layout.Packs.Single(p => p.Id == PackId);

        Assert.Equal(2, pack.Enemies.Where(m => m.EnemyId == CantorId).Sum(m => m.Count));
        Assert.DoesNotContain(pack.Enemies, m => m.EnemyId == SharedLieutenantId);
    }
}
