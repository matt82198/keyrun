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
}
