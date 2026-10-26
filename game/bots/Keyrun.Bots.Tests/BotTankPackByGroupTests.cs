using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// WoW M+ pack identity (S1 spec, finding f-892781c7, ruling R-BOSS-6 amendment ab, goal G7). PURPOSE: pin that the bot
/// tank's loose-enemy pick treats a pack by its content group (the Aggro group), not by a 3000 mm proximity proxy.
/// Board is pack 51 from the seed-3 trace (C:/Users/Mattt/AppData/Local/Temp/claude/C--Users-Mattt-aesop/0198324a-b702-41b5-882f-d2628aac95cc/scratchpad/lanes/s5-loose-mob-seed3.txt):
/// the Warden (seat 2) holds pack-mate 100 with a safe lead; loose pack-mate 101 stands 800 mm from 100 and is not hurting
/// its seat; lieutenant 102 stands about 4190 mm from 100 and is also not hurting its seat. Both 101 and 102 are aimed at
/// the Cinder (seat 1). The pack is one Aggro group, so the loose-enemy pick must rank 101 (nearer to the tank) ahead of
/// 102 as a fresh pack, never 102 ahead of 101 because 102 is beyond a proximity radius of the held member.
/// Expected API (S2 target): <c>TankEngage.PickLooseEnemy(actors, tank, groupOf)</c>, where <c>groupOf</c> is an
/// <see cref="IReadOnlyDictionary{TKey, TValue}"/> from enemy id to Aggro group id, built from <see cref="AggroGroup"/>
/// records (the shape <c>Aggro.GroupsOf</c> returns).
/// </summary>
public sealed class BotTankPackByGroupTests
{
    private const int CinderId = 1;
    private const int WardenId = 2;
    private const int HeldPackMateId = 100;
    private const int LooseMobId = 101;
    private const int LieutenantId = 102;
    private const string PackGroupId = "pack-51";
    private const string SecondPackGroupId = "pack-52";

    private static ActorState Warden() =>
        Actor(
            id: WardenId,
            classId: "warden",
            role: ActorRole.Tank,
            x: 17887,
            z: -570,
            health: 310,
            maxHealth: 400,
            maxResource: 100,
            targetId: HeldPackMateId,
            threat: Array.Empty<(int, long)>());

    private static ActorState Cinder() =>
        Actor(
            id: CinderId,
            classId: "cinder",
            role: ActorRole.Damage,
            x: 15753,
            z: 616,
            health: 110,
            maxHealth: 250,
            maxResource: 100,
            targetId: HeldPackMateId);

    // Pack-mate held by the Warden with a safe lead (threat 2600 vs the Cinder's 320).
    private static ActorState HeldPackMate()
    {
        var mob = Actor(
            id: HeldPackMateId,
            kind: ActorKind.Enemy,
            classId: "choir.melee",
            role: ActorRole.None,
            x: 18100,
            z: 435,
            health: 5200,
            maxHealth: 6180,
            maxResource: 100,
            targetId: WardenId,
            threat: new[] { (WardenId, 2600L), (CinderId, 320L) });
        mob.HolderId = WardenId;
        return mob;
    }

    // Loose pack-mate: 800 mm from the held pack-mate, aimed at the Cinder, not hurting it (about 3150 mm off the Cinder).
    private static ActorState LooseMob()
    {
        var mob = Actor(
            id: LooseMobId,
            kind: ActorKind.Enemy,
            classId: "choir.melee",
            role: ActorRole.None,
            x: 18900,
            z: 463,
            health: 3620,
            maxHealth: 6180,
            maxResource: 100,
            targetId: CinderId,
            threat: new[] { (CinderId, 900L), (WardenId, 300L) });
        mob.HolderId = CinderId;
        return mob;
    }

    // Lieutenant of the same pack: about 4190 mm from the held pack-mate, aimed at the Cinder, not hurting it.
    private static ActorState Lieutenant()
    {
        var mob = Actor(
            id: LieutenantId,
            kind: ActorKind.Enemy,
            classId: "choir.lieutenant",
            role: ActorRole.None,
            x: 21944,
            z: -1227,
            health: 6000,
            maxHealth: 6180,
            maxResource: 100,
            targetId: CinderId,
            threat: new[] { (CinderId, 1300L), (WardenId, 740L) });
        mob.HolderId = CinderId;
        return mob;
    }

    /// <summary>
    /// The group map the S2 pick consumes: every pack-51 enemy id mapped to its Aggro group id. The pack is one
    /// <see cref="AggroGroup"/> record (the shape <c>Aggro.GroupsOf</c> returns for a layout pack; the full
    /// <c>DungeonDef</c> is not needed to state the group membership this test pins).
    /// </summary>
    private static IReadOnlyDictionary<int, string> GroupMapOfPack51()
    {
        var pack = new AggroGroup(
            PackGroupId,
            -1,
            new[] { HeldPackMateId, LooseMobId, LieutenantId },
            new Vec3(18500, 0, 0),
            3000,
            Array.Empty<string>());
        return new[] { pack }
            .SelectMany(g => g.ActorIds.Select(id => (id, g.Id)))
            .ToDictionary(p => p.id, p => p.Id);
    }

    [Fact(DisplayName = "Bot tank pack identity > a loose pack-mate beyond 3000 mm of its held member is still the same pack: pick 101, not 102")]
    public void LooseMobOfHeldPackBeatsDistantLieutenant()
    {
        var warden = Warden();
        var actors = new SortedDictionary<int, ActorState>
        {
            [WardenId] = warden,
            [CinderId] = Cinder(),
            [HeldPackMateId] = HeldPackMate(),
            [LooseMobId] = LooseMob(),
            [LieutenantId] = Lieutenant(),
        };

        var pick = TankEngage.PickLooseEnemy(actors, warden, GroupMapOfPack51());

        Assert.NotNull(pick);
        Assert.Equal(LooseMobId, pick!.Id);
    }

    /// <summary>
    /// Two packs: the tank holds 100 of pack-51 (safe lead, aimed at the Warden); loose 101 is pack-51, 800 mm from 100
    /// and about 1450 mm from the tank; loose 102 is pack-52, about 4110 mm from the tank, and hurts no seat. The tank holds
    /// no member of pack-52, so 102 is taunted before the nearer 101 of the held pack.
    /// </summary>
    private static IReadOnlyDictionary<int, string> GroupMapTwoPacks()
    {
        var heldPack = new AggroGroup(
            PackGroupId,
            -1,
            new[] { HeldPackMateId, LooseMobId },
            new Vec3(18500, 0, 0),
            3000,
            Array.Empty<string>());
        var unheldPack = new AggroGroup(
            SecondPackGroupId,
            -1,
            new[] { LieutenantId },
            new Vec3(21944, 0, -1227),
            3000,
            Array.Empty<string>());
        return TankEngage.GroupMap(new[] { heldPack, unheldPack });
    }

    private static void AssertUnheldPackMobIsPicked(IReadOnlyDictionary<int, string> groupOf)
    {
        var warden = Warden();
        var actors = new SortedDictionary<int, ActorState>
        {
            [WardenId] = warden,
            [CinderId] = Cinder(),
            [HeldPackMateId] = HeldPackMate(),
            [LooseMobId] = LooseMob(),
            [LieutenantId] = Lieutenant(),
        };

        var pick = TankEngage.PickLooseEnemy(actors, warden, groupOf);

        Assert.NotNull(pick);
        Assert.Equal(LieutenantId, pick!.Id);
    }

    [Fact(DisplayName = "Bot tank pack identity > a loose mob of a pack the tank holds no member of is taken before a nearer loose mob of the held pack: pick 102, not 101")]
    public void LooseMobOfUnheldPackBeatsNearerMobOfHeldPack()
    {
        AssertUnheldPackMobIsPicked(GroupMapTwoPacks());
    }
}
