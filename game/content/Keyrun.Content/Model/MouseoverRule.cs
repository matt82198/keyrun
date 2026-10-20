using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>One body the cursor may hover: its screen box this frame and what the pick needs to know about it.</summary>
/// <param name="Id">The sim actor id.</param>
/// <param name="Rect">The body's screen box (<see cref="MouseoverRule.BodyRect"/>).</param>
/// <param name="Depth">Distance from the camera, any monotonic unit (smaller is nearer).</param>
/// <param name="Hostile">True for an enemy of the human.</param>
/// <param name="Alive">True while the actor is alive.</param>
public readonly record struct HoverBody(int Id, PixelRect Rect, long Depth, bool Hostile, bool Alive);

/// <summary>One drawn name plate the cursor may hover (STACKED-PLATES): its block this frame, in draw order.</summary>
/// <param name="Id">The actor the plate stands for (a folded crowd's leader).</param>
/// <param name="Rect">The plate's drawn block (<see cref="StackedPlate.Block"/>).</param>
/// <param name="Hostile">True for an enemy of the human.</param>
/// <param name="Alive">True while the actor is alive.</param>
public readonly record struct HoverPlate(int Id, PixelRect Rect, bool Hostile, bool Alive);

/// <summary>
/// MOUSEOVER (Matt 2026-10-20, "like wow you should be able to hover and interrupt"; BIBLE s3, cast bars are the enemy's
/// language): the WoW input model, Godot-free and integer-only. The cursor is free by default and captured only while
/// the right mouse button is held (mouselook: the camera turns and the Warden faces its heading). The mouseover unit is
/// the live enemy whose body box is under the cursor, and there is none during mouselook. The class kick (the Q slot)
/// goes at the mouseover unit when one exists, else wherever the cast would go without it; every other cast keeps its
/// target. src/Host/PlayerInput.cs calls these each frame and tick; the sim's interrupt path is unchanged (the kick is
/// an ordinary <c>CastCommand</c> with a different target id).
/// </summary>
public static class MouseoverRule
{
    /// <summary>The narrowest half width a body box has, px, so a far body can still be hovered.</summary>
    public const int MinHalfWidthPx = 14;

    /// <summary>The shortest a body box is, px.</summary>
    public const int MinHeightPx = 24;

    /// <summary>True when the cursor is captured for mouselook: only while the right mouse button is held.</summary>
    /// <param name="rightHeld">The right mouse button is down.</param>
    /// <returns>True while captured.</returns>
    public static bool CursorCaptured(bool rightHeld) => rightHeld;

    /// <summary>
    /// The screen box a body is hovered by: from the projected head top down to the projected feet, centred on the
    /// feet, half as wide as it is tall (at least <see cref="MinHalfWidthPx"/> each side, at least
    /// <see cref="MinHeightPx"/> tall, grown upward from the feet).
    /// </summary>
    /// <param name="feet">The feet projected.</param>
    /// <param name="head">The head top projected.</param>
    /// <returns>The box.</returns>
    public static PixelRect BodyRect(PixelPoint feet, PixelPoint head)
    {
        var height = Math.Max(MinHeightPx, feet.Y - head.Y);
        var half = Math.Max(MinHalfWidthPx, height / 4);
        return new PixelRect(feet.X - half, feet.Y - height, 2 * half, height);
    }

    /// <summary>
    /// The mouseover unit: of the live hostile bodies whose box holds <paramref name="cursor"/>, the nearest the camera
    /// (lowest id on a tie); null when none is under it or while the right mouse button is held (mouselook).
    /// </summary>
    /// <param name="cursor">The cursor, viewport pixels.</param>
    /// <param name="rightHeld">The right mouse button is down.</param>
    /// <param name="bodies">This frame's bodies.</param>
    /// <returns>The hovered actor id, or null.</returns>
    public static int? Unit(PixelPoint cursor, bool rightHeld, IReadOnlyList<HoverBody> bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        if (rightHeld)
        {
            return null;
        }
        HoverBody? best = null;
        foreach (var b in bodies)
        {
            if (!b.Alive || !b.Hostile || !Holds(b.Rect, cursor))
            {
                continue;
            }
            if (best is not { } cur || b.Depth < cur.Depth || (b.Depth == cur.Depth && b.Id < cur.Id))
            {
                best = b;
            }
        }
        return best?.Id;
    }

    /// <summary>
    /// STACKED-PLATES (ledger f-e933a2b7, "hover to interrupt like WoW"): the mouseover unit with name plates. A live
    /// enemy's plate under <paramref name="cursor"/> wins (plates draw over the 3D view; where two still overlap, the one
    /// drawn last, which is on top); a friendly or dead plate is not a target and the pick falls through to
    /// <see cref="Unit(PixelPoint, bool, IReadOnlyList{HoverBody})"/>. Null while the right mouse button is held.
    /// </summary>
    /// <param name="cursor">The cursor, viewport pixels.</param>
    /// <param name="rightHeld">The right mouse button is down.</param>
    /// <param name="bodies">This frame's bodies.</param>
    /// <param name="plates">This frame's drawn plates, in draw order.</param>
    /// <returns>The hovered actor id, or null.</returns>
    public static int? Unit(PixelPoint cursor, bool rightHeld, IReadOnlyList<HoverBody> bodies, IReadOnlyList<HoverPlate> plates)
    {
        ArgumentNullException.ThrowIfNull(plates);
        if (rightHeld)
        {
            return null;
        }
        // Last drawn is on top: walk the plates backwards and take the first live enemy plate under the cursor.
        for (var i = plates.Count - 1; i >= 0; i--)
        {
            var p = plates[i];
            if (p.Alive && p.Hostile && Holds(p.Rect, cursor))
            {
                return p.Id;
            }
        }
        return Unit(cursor, rightHeld, bodies);
    }

    /// <summary>The target a human cast goes at: the kick at the mouseover unit when one exists, else the fallback.</summary>
    /// <param name="interrupt">The cast is the class kick (the Q slot).</param>
    /// <param name="mouseover">The mouseover unit, or null.</param>
    /// <param name="fallback">The target the cast goes at without a mouseover (<c>CastTargeting.For</c>).</param>
    /// <returns>The target id.</returns>
    public static int CastTarget(bool interrupt, int? mouseover, int fallback) =>
        interrupt && mouseover is { } m ? m : fallback;

    /// <summary>
    /// The yaw a move command carries this tick, or null for no move: walking faces the walk direction; during
    /// mouselook the Warden faces the camera heading, walking or standing (standing, only when he does not already).
    /// </summary>
    /// <param name="rightHeld">The right mouse button is down.</param>
    /// <param name="moving">A walk key is down.</param>
    /// <param name="walkYaw">The walk direction's yaw, 1/1024 turn.</param>
    /// <param name="cameraYaw">The camera's forward yaw, 1/1024 turn.</param>
    /// <param name="actorYaw">The actor's current yaw, 1/1024 turn.</param>
    /// <returns>The yaw, or null.</returns>
    public static int? FacingYaw(bool rightHeld, bool moving, int walkYaw, int cameraYaw, int actorYaw)
    {
        if (rightHeld)
        {
            return moving || actorYaw != cameraYaw ? cameraYaw : null;
        }
        return moving ? walkYaw : null;
    }

    private static bool Holds(PixelRect r, PixelPoint p) => p.X >= r.X && p.X < r.Right && p.Y >= r.Y && p.Y < r.Bottom;
}
