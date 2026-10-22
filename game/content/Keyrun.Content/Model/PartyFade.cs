using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>Who a drawn body is to the camera's fade rule (<see cref="PartyFade"/>).</summary>
public enum FadeRole
{
    /// <summary>The player's own model: never faded.</summary>
    Self,

    /// <summary>A party member (a bot): faded while it stands between the camera and what the player must read.</summary>
    Ally,

    /// <summary>An enemy: never faded (its body is the thing being read).</summary>
    Enemy,
}

/// <summary>One drawn body for the fade rule.</summary>
/// <param name="Role">Self, ally or enemy.</param>
/// <param name="CentreMm">The body's centre (about chest height), plan mm.</param>
/// <param name="RadiusMm">The sphere that stands in for the body, mm.</param>
public readonly record struct FadeBody(FadeRole Role, (int X, int Y, int Z) CentreMm, int RadiusMm);

/// <summary>
/// Party-model camera fade (f-84d213a5, design/BIBLE.md section 3: cast bars and floor shapes must stay readable).
/// The WoW rule for the dungeon loop: an ally fades to <see cref="FadedPermille"/> while
/// <list type="bullet">
/// <item>its body (a sphere of <see cref="FadeBody.RadiusMm"/> at <see cref="FadeBody.CentreMm"/>) crosses the sight
/// line from the camera to a focus (the player's body, the player's target), between the camera and that focus and no
/// further than <see cref="NearMm"/> from the camera along the line, or</item>
/// <item>its body comes within <see cref="CameraCloseMm"/> of the camera lens (a bot beside the camera fills a corner of
/// the frame even when it covers no sight line).</item>
/// </list>
/// It is drawn opaque again the frame it stops. The player's own model (<see cref="FadeRole.Self"/>) and enemies are never
/// faded. No Godot types and integers only (the Keyrun.Content purity rule); unit-tested in
/// Keyrun.Content.Tests/PartyFadeTests.cs. PARTY-SOLID (Matt 2026-10-21, "i want to be able to see my party"): no longer
/// applied by game/src/View/ActorView.cs; every party body is drawn opaque (<see cref="CameraNearFade.AppliedPermille"/>).
/// Units: plan mm, Y up; alpha in permille.
/// </summary>
public static class PartyFade
{
    /// <summary>Alpha of a body drawn normally, permille.</summary>
    public const int OpaquePermille = 1000;

    /// <summary>Alpha of an ally that occludes, permille (WoW fades such a model to about a quarter).</summary>
    public const int FadedPermille = 250;

    /// <summary>Only an ally this close to the camera along a sight line fades, mm.</summary>
    public const int NearMm = 10000;

    /// <summary>An ally whose body comes this close to the camera lens fades whatever it covers, mm.</summary>
    public const int CameraCloseMm = 1500;

    /// <summary>A party body's centre above its feet, mm.</summary>
    public const int BodyCentreMm = 1000;

    /// <summary>The radius of the sphere standing in for a party body, mm.</summary>
    public const int BodyRadiusMm = 600;

    /// <summary>The alpha one body is drawn with this frame, permille.</summary>
    /// <param name="cameraMm">The camera's position, mm.</param>
    /// <param name="fociMm">Points the player must see (the player's own body, the current target), mm.</param>
    /// <param name="body">The body to rate.</param>
    /// <returns><see cref="FadedPermille"/> or <see cref="OpaquePermille"/>.</returns>
    public static int AlphaPermille((int X, int Y, int Z) cameraMm, IReadOnlyList<(int X, int Y, int Z)> fociMm, FadeBody body)
    {
        ArgumentNullException.ThrowIfNull(fociMm);
        if (body.Role != FadeRole.Ally)
        {
            return OpaquePermille;
        }
        var v = Sub(body.CentreMm, cameraMm);
        Int128 r = Math.Max(0, body.RadiusMm);
        var vv = Dot(v, v);
        Int128 close = CameraCloseMm + r;
        if (vv <= close * close)
        {
            return FadedPermille;
        }
        foreach (var focus in fociMm)
        {
            if (Occludes(Sub(focus, cameraMm), v, vv, r))
            {
                return FadedPermille;
            }
        }
        return OpaquePermille;
    }

    // d = camera to focus, v = camera to the body's centre. The body occludes when its centre projects strictly between
    // the camera and the focus, no further than NearMm from the camera along the line, and the line passes within its
    // radius. Compared squared in Int128 so no square root is taken and no product overflows.
    private static bool Occludes((long X, long Y, long Z) d, (long X, long Y, long Z) v, Int128 vv, Int128 r)
    {
        var dd = Dot(d, d);
        if (dd == 0)
        {
            return false;
        }
        var along = Dot(v, d); // |d| times the distance along the line
        if (along <= 0 || along >= dd)
        {
            return false; // behind the camera, or behind the focus
        }
        Int128 near = NearMm;
        if (along * along > near * near * dd)
        {
            return false; // too far down the line to fill the frame
        }
        // Squared distance from the line, times dd: |v|^2 dd - (v.d)^2.
        return vv * dd - along * along <= r * r * dd;
    }

    private static (long X, long Y, long Z) Sub((int X, int Y, int Z) a, (int X, int Y, int Z) b) =>
        ((long)a.X - b.X, (long)a.Y - b.Y, (long)a.Z - b.Z);

    private static Int128 Dot((long X, long Y, long Z) a, (long X, long Y, long Z) b) =>
        (Int128)a.X * b.X + (Int128)a.Y * b.Y + (Int128)a.Z * b.Z;
}
