using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// BranchDoors (D45, f-46043bc6): the view's shell is the walked chain plus the side branch, with an arrow on every
/// one-way door. The hand-built Hollow Vault has neither, so it draws exactly as before; a one-way box gets one arrow
/// inside it, pointing into the branch.
/// </summary>
public sealed class BranchDoorsTests
{
    [Fact(DisplayName = "Branch doors: the hand-built layout's shell is its route boxes and it has no arrows")]
    public void Hand_layout_draws_as_before()
    {
        var layout = ContentLoader.Load().Layout;
        Assert.Empty(layout.SideBoxes);
        Assert.Equal(DungeonPlan.BoxesOf(layout), BranchDoors.ShellBoxes(layout));
        Assert.Equal(layout.Route.Select(p => (p.X, p.Z)), BranchDoors.ShellPoints(layout));
        Assert.Empty(BranchDoors.Arrows(layout));
    }

    [Fact(DisplayName = "Branch doors: side boxes follow the route boxes and every one-way box carries one arrow inside it")]
    public void Side_boxes_and_arrows()
    {
        var hand = ContentLoader.Load().Layout;
        var side = new LayoutBox { Id = "side-hall", Kind = LayoutBoxKind.Corridor, MinX = 5000, MaxX = 11000, MinZ = 3000, MaxZ = 6000, OneWay = true };
        var oneWayRoute = hand.Boxes.Select((b, i) => i == 1 ? b with { OneWay = true } : b).ToList();
        var layout = hand with { Boxes = new ValueList<LayoutBox>(oneWayRoute), SideBoxes = new ValueList<LayoutBox>(new[] { side }) };

        var shell = BranchDoors.ShellBoxes(layout);
        Assert.Equal(hand.Boxes.Count + 1, shell.Count);
        Assert.Equal("side-hall", shell[^1].Id);
        Assert.Equal((8000, 4500), BranchDoors.ShellPoints(layout)[^1]);

        var arrows = BranchDoors.Arrows(layout);
        Assert.Equal(new[] { hand.Boxes[1].Id, "side-hall" }, arrows.Select(a => a.BoxId));
        foreach (var a in arrows)
        {
            var b = oneWayRoute.Append(side).Single(x => x.Id == a.BoxId);
            Assert.InRange(a.X - (a.LengthMm / 2), b.MinX, b.MaxX);
            Assert.InRange(a.X + (a.LengthMm / 2), b.MinX, b.MaxX);
            Assert.InRange(a.Z, b.MinZ, b.MaxZ);
            Assert.True(a.WidthMm > 0 && a.WidthMm < b.MaxZ - b.MinZ);
        }
    }
}
