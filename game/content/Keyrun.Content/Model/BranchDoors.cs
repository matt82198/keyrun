using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// A one-way door's arrow (D45 forward-only): drawn flat on the floor of the corridor box a branch begins with, pointing
/// +x (into the branch), mm.
/// </summary>
/// <param name="BoxId">The one-way corridor box.</param>
/// <param name="X">Arrow centre x.</param>
/// <param name="Z">Arrow centre z.</param>
/// <param name="LengthMm">Tip-to-tail length along +x.</param>
/// <param name="WidthMm">Width across z.</param>
public sealed record DoorArrow(string BoxId, int X, int Z, int LengthMm, int WidthMm);

/// <summary>
/// What the view draws for a branching layout (STATE.md D45, f-46043bc6), pure and integer so xUnit pins it: every box,
/// the walked chain then the other branch (<see cref="LayoutDef.SideBoxes"/>), with one shell point per box (route points
/// for the chain, box centres for the side), and an arrow on every one-way door. A layout with no side boxes and no
/// one-way box (the hand-built Hollow Vault) draws exactly as before.
/// </summary>
public static class BranchDoors
{
    /// <summary>An arrow's length, mm (it stands this far in from the corridor's west end, so it reads at the door).</summary>
    public const int ArrowLengthMm = 1600;

    /// <summary>The walked chain's boxes, then the side boxes, as the dungeon shell reads them.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>Plan boxes.</returns>
    public static IReadOnlyList<PlanBox> ShellBoxes(LayoutDef layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return DungeonPlan.BoxesOf(layout).Concat(SideBoxes(layout)).ToList();
    }

    /// <summary>One shell point per <see cref="ShellBoxes"/> box: the route, then each side box's centre.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>Points, mm.</returns>
    public static IReadOnlyList<(int X, int Z)> ShellPoints(LayoutDef layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.Route.Select(p => (p.X, p.Z)).Concat(SidePoints(layout)).ToList();
    }

    /// <summary>The side branch's boxes alone (never gated), or none for a null layout.</summary>
    /// <param name="layout">The layout, or null.</param>
    /// <returns>Plan boxes.</returns>
    public static IReadOnlyList<PlanBox> SideBoxes(LayoutDef? layout) =>
        layout is null
            ? Array.Empty<PlanBox>()
            : layout.SideBoxes.Select(b => new PlanBox(b.Id, b.Kind == LayoutBoxKind.Room, b.MinX, b.MaxX, b.MinZ, b.MaxZ, false)).ToList();

    /// <summary>Each side box's centre (its shell point), or none for a null layout.</summary>
    /// <param name="layout">The layout, or null.</param>
    /// <returns>Points, mm.</returns>
    public static IReadOnlyList<(int X, int Z)> SidePoints(LayoutDef? layout) =>
        layout is null
            ? Array.Empty<(int, int)>()
            : layout.SideBoxes.Select(b => (b.MinX + ((b.MaxX - b.MinX) / 2), b.MinZ + ((b.MaxZ - b.MinZ) / 2))).ToList();

    /// <summary>An arrow on every one-way box of the chain and the side, in that order.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>Arrows.</returns>
    public static IReadOnlyList<DoorArrow> Arrows(LayoutDef layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.Boxes.Concat(layout.SideBoxes)
            .Where(b => b.OneWay)
            .Select(b =>
            {
                var length = Math.Min(ArrowLengthMm, b.MaxX - b.MinX);
                var width = (b.MaxZ - b.MinZ) * 2 / 3;
                return new DoorArrow(b.Id, b.MinX + ArrowLengthMm + (length / 2), b.MinZ + ((b.MaxZ - b.MinZ) / 2), length, width);
            })
            .ToList();
    }
}
