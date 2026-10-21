using System;

namespace Keyrun.Content;

/// <summary>
/// CAMERA-FG (receipt demo-5e34e26b: two untextured party bodies, Chanter and Cinder, stood between the camera and the
/// Hollow Warden and were drawn solid over the floor and the HUD's lower edge). The WoW rule for an actor body near the
/// lens: a body whose distance to the camera is under <see cref="FadeDistanceMm"/> is drawn with alpha that falls linearly
/// from opaque at the fade distance to <see cref="FloorPermille"/> at the lens, and eases back to opaque as it leaves.
/// The rule applies to every actor body, the player's own model included. World geometry is not faded: it is cut
/// away or pulled in front of by the camera rule (CameraBoom). No Godot types and integers only (the Keyrun.Content
/// purity rule); unit-tested in Keyrun.Content.Tests/CameraNearFadeTests.cs, applied by game/src/View/ActorView.cs.
/// Units: mm; alpha in permille.
/// CAMERA-FG occlusion (ruling, receipt demo-0e560876: the bots Chanter and Cinder stand beside the Warden between the
/// lens and the boss, off the PartyFade sight line, and were drawn solid; their distances were not measured): a body that occludes the pivot
/// (<see cref="Occludes"/>) is also held to <see cref="PartyFade.FadedPermille"/>, whatever its distance.
/// </summary>
public static class CameraNearFade
{
    /// <summary>A body at or beyond this distance from the camera is drawn opaque, mm (the default follow boom is 5 m).</summary>
    public const int FadeDistanceMm = 5000;

    /// <summary>The alpha of a body at the lens, permille: near zero, so a body that fills the frame stops blocking it.</summary>
    public const int FloorPermille = 50;

    /// <summary>The alpha of a body drawn normally, permille.</summary>
    public const int OpaquePermille = PartyFade.OpaquePermille;

    /// <summary>
    /// The alpha one actor body is drawn with at this distance from the camera, permille: opaque at or beyond
    /// <see cref="FadeDistanceMm"/>, <see cref="FloorPermille"/> at the lens and at any distance behind it, linear between.
    /// </summary>
    /// <param name="distanceMm">The distance from the camera to the body's centre, mm.</param>
    /// <returns>An alpha from <see cref="FloorPermille"/> to <see cref="OpaquePermille"/>, never falling as the distance grows.</returns>
    public static int AlphaPermille(int distanceMm)
    {
        if (distanceMm >= FadeDistanceMm)
        {
            return OpaquePermille;
        }
        if (distanceMm <= 0)
        {
            return FloorPermille;
        }
        return FloorPermille + (OpaquePermille - FloorPermille) * distanceMm / FadeDistanceMm;
    }

    /// <summary>The radius of the sight-line tube around lens-to-pivot, mm.</summary>
    public const int OcclusionRadiusMm = 1500;

    /// <summary>
    /// Whether a body occludes the Warden: its centre lies strictly between the lens and the pivot and within
    /// <see cref="OcclusionRadiusMm"/> of the sight line between them (a body beside the Warden that covers its floor
    /// ring, or stands in the frame between the lens and the boss). The player's own model never occludes, and a body
    /// at or beyond the pivot does not. The test is squared and scaled by the squared line length, so no root is taken
    /// and nothing overflows (Int128, as in <see cref="PartyFade"/>).
    /// </summary>
    /// <param name="role">Who the body is.</param>
    /// <param name="cameraMm">The camera lens, mm.</param>
    /// <param name="pivotMm">The pivot the camera looks at (the Warden's shoulder), mm.</param>
    /// <param name="bodyMm">The body's centre, mm.</param>
    /// <returns>True when the body occludes the pivot from the lens.</returns>
    public static bool Occludes(FadeRole role, (int X, int Y, int Z) cameraMm, (int X, int Y, int Z) pivotMm, (int X, int Y, int Z) bodyMm)
    {
        if (role == FadeRole.Self)
        {
            return false;
        }
        var d = Sub(pivotMm, cameraMm);
        var w = Sub(bodyMm, cameraMm);
        Int128 dd = Dot(d, d);
        if (dd == 0)
        {
            return false;
        }
        Int128 along = Dot(w, d);
        if (along <= 0 || along >= dd)
        {
            return false; // behind the lens, or at or beyond the pivot
        }
        Int128 r = OcclusionRadiusMm;
        // Squared distance from the sight line, times dd: |w|^2 dd - along^2.
        return Dot(w, w) * dd - along * along <= r * r * dd;
    }

    /// <summary>
    /// The alpha one body is drawn with from the near-camera rules: the distance rule, and when the body occludes the
    /// pivot (<see cref="Occludes"/>), no higher than <see cref="PartyFade.FadedPermille"/>.
    /// </summary>
    /// <param name="distanceMm">The distance from the lens to the body's centre, mm.</param>
    /// <param name="occludes">Whether the body occludes the pivot.</param>
    /// <returns>An alpha from <see cref="FloorPermille"/> to <see cref="OpaquePermille"/>.</returns>
    public static int AlphaPermille(int distanceMm, bool occludes) =>
        occludes ? Math.Min(AlphaPermille(distanceMm), PartyFade.FadedPermille) : AlphaPermille(distanceMm);

    /// <summary>
    /// The height a body is measured at for the screen-height rule, mm: the capsule the actor view draws (1.8 m). The view
    /// projects the feet and this height above them.
    /// </summary>
    public const int BodyHeightMm = 1800;

    /// <summary>
    /// A party body drawn taller than this many permille of the viewport height is capped at <see cref="PartyFade.FadedPermille"/>.
    /// One third of the viewport is 333.33 permille, so 333 is within it and 334 exceeds it.
    /// </summary>
    public const int ScreenHeightLimitPermille = 333;

    /// <summary>
    /// The projected height of a body on screen, permille of the viewport height: the span from its top to its bottom
    /// pixel over the viewport height, floored. Zero when there is no viewport or the span is not positive (a body behind
    /// the lens projects to no height, so the cap does not apply).
    /// </summary>
    /// <param name="topPx">The screen y of the body's top, pixels (smaller y is higher on the screen).</param>
    /// <param name="bottomPx">The screen y of the body's feet, pixels.</param>
    /// <param name="viewportHeightPx">The viewport height, pixels.</param>
    /// <returns>The projected height, permille of the viewport height.</returns>
    public static int ScreenHeightPermille(int topPx, int bottomPx, int viewportHeightPx)
    {
        var span = (long)bottomPx - topPx;
        if (viewportHeightPx <= 0 || span <= 0)
        {
            return 0;
        }
        return (int)(span * OpaquePermille / viewportHeightPx);
    }

    /// <summary>
    /// The alpha cap the screen-height rule puts on one body: <see cref="PartyFade.FadedPermille"/> for a party body
    /// (<see cref="FadeRole.Ally"/>) drawn taller than <see cref="ScreenHeightLimitPermille"/>, otherwise opaque. Only a
    /// party body can be capped; the player's own model and every enemy (the boss and the twins) always return opaque.
    /// </summary>
    /// <param name="role">Who the body is.</param>
    /// <param name="screenHeightPermille">The body's projected height, permille of the viewport height (see <see cref="ScreenHeightPermille"/>).</param>
    /// <returns><see cref="PartyFade.FadedPermille"/> or <see cref="OpaquePermille"/>.</returns>
    public static int ScreenHeightCapPermille(FadeRole role, int screenHeightPermille) =>
        role == FadeRole.Ally && screenHeightPermille > ScreenHeightLimitPermille ? PartyFade.FadedPermille : OpaquePermille;

    /// <summary>
    /// ENEMY-BODY-STABLE: the single alpha entry point the actor view draws a body with, from every near-camera rule.
    /// An enemy (<see cref="FadeRole.Enemy"/>) is always opaque, whatever its distance, occlusion or screen height. A party
    /// body (<see cref="FadeRole.Self"/>, <see cref="FadeRole.Ally"/>) is the lowest of the distance rule
    /// (<see cref="AlphaPermille(int, bool)"/>) and the screen-height cap (<see cref="ScreenHeightCapPermille"/>).
    /// </summary>
    /// <param name="role">Who the body is.</param>
    /// <param name="distanceMm">The distance from the lens to the body's centre, mm.</param>
    /// <param name="occludes">Whether the body occludes the pivot (<see cref="Occludes"/>).</param>
    /// <param name="screenHeightPermille">The body's projected height, permille of the viewport height.</param>
    /// <returns>An alpha in permille: <see cref="OpaquePermille"/> for every enemy.</returns>
    public static int AppliedPermille(FadeRole role, int distanceMm, bool occludes, int screenHeightPermille) =>
        role == FadeRole.Enemy
            ? OpaquePermille
            : Math.Min(AlphaPermille(distanceMm, occludes), ScreenHeightCapPermille(role, screenHeightPermille));

    private static (long X, long Y, long Z) Sub((int X, int Y, int Z) a, (int X, int Y, int Z) b) =>
        ((long)a.X - b.X, (long)a.Y - b.Y, (long)a.Z - b.Z);

    private static Int128 Dot((long X, long Y, long Z) a, (long X, long Y, long Z) b) =>
        (Int128)a.X * b.X + (Int128)a.Y * b.Y + (Int128)a.Z * b.Z;
}
