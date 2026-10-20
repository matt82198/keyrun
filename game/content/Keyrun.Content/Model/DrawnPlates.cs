using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// One name plate as LabelView draws it this frame (lane PLATE-RECTS, finding f-9d4a31a5): its box after the same-name
/// crowd fold (NAMEPLATE-DECLUTTER) and <see cref="HudLayout"/> separation, and every actor folded into it (the leader
/// first). LabelView publishes the list; CombatText reads it, so floating numbers avoid the plates where they draw.
/// </summary>
/// <param name="Rect">The plate's drawn box, viewport pixels.</param>
/// <param name="ActorIds">The actors this plate stands for, the leader first.</param>
public readonly record struct DrawnPlate(PixelRect Rect, IReadOnlyList<int> ActorIds);

/// <summary>
/// Lane PLATE-RECTS (finding f-9d4a31a5): the Godot-free reads of LabelView's drawn plates that src/Hud/CombatText.cs
/// places its numbers with, so what is drawn and the proof line it prints come from the same decision. Display only;
/// never a rule.
/// </summary>
public static class DrawnPlates
{
    /// <summary>
    /// The drawn plate an actor's number goes beside: the plate whose <see cref="DrawnPlate.ActorIds"/> holds the
    /// actor, its own or the crowd plate it is folded into; null when no plate draws for it (dead, off screen, behind
    /// the camera, or the Warden).
    /// </summary>
    /// <param name="plates">This frame's drawn plates (LabelView.PlateRects).</param>
    /// <param name="actorId">The number's target.</param>
    /// <returns>The plate's drawn box, or null.</returns>
    public static PixelRect? PlateOf(IReadOnlyList<DrawnPlate> plates, int actorId)
    {
        ArgumentNullException.ThrowIfNull(plates);
        foreach (var p in plates)
        {
            foreach (var id in p.ActorIds)
            {
                if (id == actorId)
                {
                    return p.Rect;
                }
            }
        }
        return null;
    }

    /// <summary>Every drawn plate's box, in draw order: the rectangles a number keeps clear of.</summary>
    /// <param name="plates">This frame's drawn plates.</param>
    /// <returns>One box per drawn plate.</returns>
    public static IReadOnlyList<PixelRect> Rects(IReadOnlyList<DrawnPlate> plates)
    {
        ArgumentNullException.ThrowIfNull(plates);
        var rects = new PixelRect[plates.Count];
        for (var i = 0; i < plates.Count; i++)
        {
            rects[i] = plates[i].Rect;
        }
        return rects;
    }

    /// <summary>True when <paramref name="box"/> shares a pixel with any drawn plate.</summary>
    /// <param name="box">A number's box.</param>
    /// <param name="plates">This frame's drawn plates.</param>
    /// <returns>True on a hit.</returns>
    public static bool Hits(PixelRect box, IReadOnlyList<DrawnPlate> plates)
    {
        ArgumentNullException.ThrowIfNull(plates);
        foreach (var p in plates)
        {
            if (box.Intersects(p.Rect))
            {
                return true;
            }
        }
        return false;
    }
}
