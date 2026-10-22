using Godot;
using Keyrun.Content;

namespace Keyrun.View;

/// <summary>
/// A one-way door's arrow (D45 forward-only; <see cref="BranchDoors.Arrows"/>): a flat, unshaded gold triangle on the
/// corridor floor, its tip pointing +x into the branch, readable from the vertical camera. Render-only.
/// </summary>
public static class DoorArrowView
{
    private const float MmToM = 0.001f;
    private const float ThicknessM = 0.04f;
    private const float LiftM = 0.03f;

    private static readonly Color ArrowColor = new(1.0f, 0.78f, 0.25f);

    /// <summary>The arrow's mesh node, named after its box.</summary>
    /// <param name="arrow">The arrow, mm.</param>
    /// <returns>The node.</returns>
    public static MeshInstance3D Make(DoorArrow arrow)
    {
        // PrismMesh: a triangle in its XY plane (base along x at -y, apex at +y) extruded along z. Rotate +90 about x
        // (apex to +z, flat on the floor) then +90 about y (apex to +x).
        var basis = new Basis(Vector3.Up, Mathf.Pi / 2) * new Basis(Vector3.Right, Mathf.Pi / 2);
        return new MeshInstance3D
        {
            Name = arrow.BoxId + "-oneway",
            Mesh = new PrismMesh { Size = new Vector3(arrow.WidthMm * MmToM, arrow.LengthMm * MmToM, ThicknessM) },
            Transform = new Transform3D(basis, new Vector3(arrow.X * MmToM, LiftM, arrow.Z * MmToM)),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = ArrowColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
