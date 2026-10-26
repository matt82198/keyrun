using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// One gear mark's fixed definition: the skeleton bone it rides (so the run animation carries it, R-VIEW-HEAD-1), and its
/// offset and size in integer millimetres (the content assembly holds no floats) in the bone's frame. The frame is the
/// model's own: X lateral, Y up, Z forward (the KayKit pack faces +z). Offsets are from the bone origin; sizes are full
/// extents (a sphere uses <see cref="SizeXMm"/> as its diameter). Godot-free so xUnit can prove every bone exists in the
/// shipped rigs.
/// </summary>
/// <param name="Slot">The gear slot name.</param>
/// <param name="Bone">The Skeleton3D bone name the mark attaches to.</param>
/// <param name="Sphere">True for a sphere, false for a box.</param>
/// <param name="OffXMm">Bone-local lateral offset, millimetres.</param>
/// <param name="OffYMm">Bone-local up offset, millimetres.</param>
/// <param name="OffZMm">Bone-local forward offset, millimetres.</param>
/// <param name="SizeXMm">Lateral extent (or sphere diameter), millimetres.</param>
/// <param name="SizeYMm">Vertical extent, millimetres (0 for a sphere).</param>
/// <param name="SizeZMm">Forward extent, millimetres (0 for a sphere).</param>
public sealed record GearMarkDef(
    string Slot,
    string Bone,
    bool Sphere,
    int OffXMm,
    int OffYMm,
    int OffZMm,
    int SizeXMm,
    int SizeYMm,
    int SizeZMm);

/// <summary>The five gear mark definitions, in slot order weapon, armor, charm, ring, keystone.</summary>
public static class GearMarkDefs
{
    /// <summary>Every mark definition, in slot order.</summary>
    public static readonly IReadOnlyList<GearMarkDef> All = new[]
    {
        // A blade held forward from the right hand slot.
        new GearMarkDef("weapon", "handslot.r", false, 0, 0, 400, 80, 60, 900),
        // A torso band centred on the chest.
        new GearMarkDef("armor", "chest", false, 0, 50, 0, 500, 500, 320),
        // A bead at the throat, just below the head bone origin.
        new GearMarkDef("charm", "head", true, 0, -80, 100, 100, 0, 0),
        // A stone on the right hand.
        new GearMarkDef("ring", "hand.r", true, 0, 0, 40, 60, 0, 0),
        // A block at the belt, behind the body.
        new GearMarkDef("keystone", "hips", false, 0, 0, -200, 180, 180, 180),
    };
}
