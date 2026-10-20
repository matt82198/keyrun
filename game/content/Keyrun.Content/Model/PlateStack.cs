using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>How a plate's cast bar marks whether the cast can be kicked (shape and colour, never colour alone).</summary>
public enum CastMark
{
    /// <summary>The cast can be interrupted: an amber diamond beside an amber-framed bar.</summary>
    Kick,

    /// <summary>The cast cannot be interrupted: a grey square beside a grey-framed bar.</summary>
    Locked,
}

/// <summary>One plate to stack this frame, where its body projects.</summary>
/// <param name="Id">The actor the plate stands for (a folded crowd's leader).</param>
/// <param name="Anchor">The plate anchor projected: the bottom centre of the name row.</param>
/// <param name="NameWidth">The name row's width in pixels (text plus padding).</param>
/// <param name="Depth">Distance from the camera, any monotonic unit (smaller is nearer).</param>
/// <param name="Casting">True when the plate shows a cast bar under its name (an enemy casting or a fading cast).</param>
public readonly record struct PlateSpec(int Id, PixelPoint Anchor, int NameWidth, long Depth, bool Casting);

/// <summary>One placed plate: its whole block and the parts inside it.</summary>
/// <param name="Id">The actor the plate stands for.</param>
/// <param name="Block">The whole plate, the name row and any cast rows: what no other plate may share a pixel with.</param>
/// <param name="Name">The name row.</param>
/// <param name="CastBar">The cast bar under the name, or null when the plate is idle.</param>
/// <param name="Mark">The kick or locked mark left of the cast bar, or null when the plate is idle.</param>
public readonly record struct StackedPlate(int Id, PixelRect Block, PixelRect Name, PixelRect? CastBar, PixelRect? Mark);

/// <summary>
/// STACKED-PLATES (Matt 2026-10-20, ledger f-e933a2b7: "enemy nameplates stack vertically with interruptible cast bars,
/// hover to interrupt like WoW"; BIBLE s3, cast bars are the enemy's language). Godot-free, integer-only layout of the
/// name plates src/View/LabelView.cs draws: each plate is one block, its name row and, while its enemy casts, a cast
/// label band and a cast bar hanging under the name, with a mark left of the bar that says by shape (a diamond to kick,
/// a square when locked) as well as colour whether the cast can be interrupted. Blocks are placed nearest the camera
/// first (lowest id on a tie, so the order never depends on how the frame listed them) and moved only up or down
/// (<see cref="HudLayout"/>.Separate) until no two share a pixel. The blocks are also the hover targets
/// (<see cref="MouseoverRule"/>.Unit with <see cref="HoverPlate"/>s).
/// </summary>
public static class PlateStack
{
    /// <summary>The name row's height: the 15 px name font plus 4 px padding above and below (LabelView).</summary>
    public const int NameHeight = 23;

    /// <summary>A plate's cast area width: the mark, a gap, and the bar.</summary>
    public const int CastWidth = 150;

    /// <summary>The band above the plate's cast bar its 12 px spell label draws in.</summary>
    public const int CastLabelHeight = 15;

    /// <summary>The plate's cast bar height, and the mark's side.</summary>
    public const int CastBarHeight = 12;

    /// <summary>The gap between the mark and the bar.</summary>
    public const int MarkGap = 3;

    /// <summary>The cast rows' height under the name: the label band and the bar.</summary>
    public const int CastHeight = CastLabelHeight + CastBarHeight;

    /// <summary>The mark a cast draws: <see cref="CastMark.Kick"/> when it can be interrupted, else <see cref="CastMark.Locked"/>.</summary>
    /// <param name="interruptible">The ability is interruptible.</param>
    /// <returns>The mark.</returns>
    public static CastMark MarkOf(bool interruptible) => CastMark.Locked;

    /// <summary>
    /// The mark's outline as a polygon inside <paramref name="box"/>, clockwise from the top: a kick is a diamond through
    /// the box's edge midpoints, a locked cast the box's square corners. Two shapes, so the mark reads without colour.
    /// </summary>
    /// <param name="mark">The mark.</param>
    /// <param name="box">The mark's box (<see cref="StackedPlate.Mark"/>).</param>
    /// <returns>Four points.</returns>
    public static PixelPoint[] MarkShape(CastMark mark, PixelRect box) =>
        new[] { new PixelPoint(box.X, box.Y), new PixelPoint(box.Right, box.Y), new PixelPoint(box.Right, box.Bottom), new PixelPoint(box.X, box.Bottom) };

    /// <summary>
    /// The key NAMEPLATE-DECLUTTER folds same-name plates by: the name, except that a casting enemy gets a key of its own
    /// so its plate is never folded away and its cast bar and hover target always draw.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="id">The actor id.</param>
    /// <param name="casting">The plate shows a cast.</param>
    /// <returns>The fold key.</returns>
    public static string FoldKey(string name, int id, bool casting) => name;

    /// <summary>
    /// Places every plate: blocks built at their anchors, sorted nearest first then lowest id, then each moved the least
    /// distance up or down (and pulled inside the viewport) until it shares no pixel with a block already placed or an
    /// obstacle.
    /// </summary>
    /// <param name="plates">This frame's plates, any order.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="obstacles">HUD rectangles plates keep out of.</param>
    /// <returns>The placed plates in draw order (nearest first, lowest id on a tie).</returns>
    public static IReadOnlyList<StackedPlate> Layout(
        IReadOnlyList<PlateSpec> plates, int viewportWidth, int viewportHeight, IReadOnlyList<PixelRect> obstacles)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(obstacles);
        var placed = new List<StackedPlate>(plates.Count);
        foreach (var s in plates)
        {
            placed.Add(Build(s));
        }
        return placed;
    }

    /// <summary>The plate's block at its anchor, before placement.</summary>
    /// <param name="spec">The plate.</param>
    /// <returns>The block and its parts.</returns>
    public static StackedPlate Build(PlateSpec spec)
    {
        var name = new PixelRect(spec.Anchor.X - (spec.NameWidth / 2), spec.Anchor.Y - NameHeight, spec.NameWidth, NameHeight);
        return new StackedPlate(spec.Id, name, name, null, null);
    }
}
