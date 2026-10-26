using System;

namespace Keyrun.Content;

/// <summary>Which frame a gear mark is placed in: a skeleton bone, or the actor root when there is no skeleton.</summary>
public enum GearMountKind
{
    /// <summary>The mark rides a Skeleton3D bone; its offset is bone-local.</summary>
    Bone,

    /// <summary>The capsule fallback: the mark sits in the actor root frame at the bone's rest height.</summary>
    Root,
}

/// <summary>
/// A gear mark's resolved placement, in integer millimetres. For <see cref="GearMountKind.Bone"/> the position is
/// bone-local (the model frame: X lateral, Y up, Z forward) and <see cref="Bone"/> names the bone. For
/// <see cref="GearMountKind.Root"/> the position is in the actor root frame (root X forward, Z lateral) and
/// <see cref="Bone"/> names the rest-height anchor it was measured from. ActorView converts to metres at the Godot boundary.
/// </summary>
/// <param name="Kind">Whether the mark rides a bone or sits in the root frame.</param>
/// <param name="Bone">The bone name (the skeleton bone, or the anchor for the root fallback).</param>
/// <param name="X">Position X, millimetres.</param>
/// <param name="Y">Position Y, millimetres.</param>
/// <param name="Z">Position Z, millimetres.</param>
public readonly record struct GearMountPlacement(GearMountKind Kind, string Bone, int X, int Y, int Z);

/// <summary>
/// The Godot-free mount and visibility rule for gear marks (R-VIEW-HEAD-1, keyrun item 11). ActorView resolves each mark
/// through this rule instead of holding its own fallback arithmetic, so the head-height and dead-body behaviour can be
/// proven by xUnit. The rule works entirely in integer millimetres; no floating-point appears in Keyrun.Content.
/// </summary>
public static class GearMountRule
{
    /// <summary>
    /// Resolves one mark's placement. With a skeleton the mark rides its bone with the bone-local offset. Without one
    /// (the capsule fallback) it sits in the root frame at the bone's rest height plus the offset Y, with root X forward
    /// from the offset Z and root Z lateral from the negated offset X.
    /// </summary>
    /// <param name="def">The mark's fixed definition.</param>
    /// <param name="hasSkeleton">True when the model's skeleton has the mark's bone.</param>
    public static GearMountPlacement Resolve(GearMarkDef def, bool hasSkeleton)
    {
        ArgumentNullException.ThrowIfNull(def);
        var x = def.OffXMm;
        var y = def.OffYMm;
        var z = def.OffZMm;
        if (hasSkeleton)
        {
            return new GearMountPlacement(GearMountKind.Bone, def.Bone, x, y, z);
        }
        return new GearMountPlacement(GearMountKind.Root, def.Bone, z, RootAnchorY(def.Bone) + y, -x);
    }

    /// <summary>
    /// Whether a mark is drawn. A dead actor hides every mark, bone-mounted ones included: they sit under the Skeleton3D,
    /// outside the gear root, so the rule itself must hide them while the body keeps its death clip (HoldsDeath).
    /// </summary>
    /// <param name="actorAlive">True when the actor is alive.</param>
    /// <param name="placement">The mark's resolved placement. Every mount kind follows the body the same way.</param>
    public static bool Visible(bool actorAlive, GearMountPlacement placement) => actorAlive;

    /// <summary>Root-frame height of a bone's rest position, for the no-skeleton fallback only, millimetres.</summary>
    /// <param name="bone">The bone name.</param>
    public static int RootAnchorY(string bone) => bone switch
    {
        "head" => 1500,
        "chest" => 1150,
        "handslot.r" => 1050,
        _ => 950,
    };
}
