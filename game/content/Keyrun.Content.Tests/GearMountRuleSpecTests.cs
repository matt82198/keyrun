using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// S1 spec for R-VIEW-HEAD-1 (keyrun item 11, character head glitches while moving). The mount and visibility rule that
/// ActorView applies to a gear mark (ActorView.cs:662, 694-713, 742-749, 827) must be a Godot-free helper so xUnit can
/// prove it. <c>GearMountRule</c> does not exist yet; S2 creates it in game/content/Keyrun.Content beside
/// <c>GearMarkDefs</c>. Until then this file is the red spec: the compile error names the missing helper.
/// </summary>
public sealed class GearMountRuleSpecTests
{
    private static GearMarkDef Charm() => GearMarkDefs.All.Single(d => d.Slot == "charm");

    [Fact]
    public void CharmWithoutSkeletonMountsAtHeadHeightNotTheFloor()
    {
        // R-HEAD-1 (d) seq 56: unit restated mm.
        // Root-frame fallback (ActorView.cs:705-713): root Y = RootAnchorY("head") 1500 mm + offset Y (-80 mm) = 1420 mm.
        // The pre-#710 rule put the mark at the bone offset alone, -80 mm, which is the floor.
        GearMountPlacement placement = GearMountRule.Resolve(Charm(), hasSkeleton: false);

        Assert.Equal(GearMountKind.Root, placement.Kind);
        Assert.True(placement.Y >= 1200, $"charm root Y is {placement.Y} mm; the head sits near 1500 mm, so under 1200 mm is at the floor");
    }

    [Fact]
    public void CharmWithSkeletonRidesTheHeadBoneWithItsOffset()
    {
        // R-HEAD-1 (d) seq 56: unit restated mm.
        // Bone-local offset (0, -80, 100) mm from GearMarkDefs.cs:43, under the head bone.
        GearMountPlacement placement = GearMountRule.Resolve(Charm(), hasSkeleton: true);

        Assert.Equal(GearMountKind.Bone, placement.Kind);
        Assert.Equal("head", placement.Bone);
        Assert.Equal(0, placement.X);
        Assert.Equal(-80, placement.Y);
        Assert.Equal(100, placement.Z);
    }

    [Fact]
    public void DeadActorHidesEveryMarkIncludingBoneMountedOnes()
    {
        // ActorView.cs:662 hid the gear root for a dead actor; after #710 the bone-mounted marks sit under the Skeleton3D,
        // outside that root, so the rule itself must hide them (ActorView.cs:827 keeps the body visible while HoldsDeath).
        GearMountPlacement bone = GearMountRule.Resolve(Charm(), hasSkeleton: true);
        GearMountPlacement root = GearMountRule.Resolve(Charm(), hasSkeleton: false);

        Assert.False(GearMountRule.Visible(actorAlive: false, bone));
        Assert.False(GearMountRule.Visible(actorAlive: false, root));
        Assert.True(GearMountRule.Visible(actorAlive: true, bone));
        Assert.True(GearMountRule.Visible(actorAlive: true, root));
    }
}
