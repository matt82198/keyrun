using Godot;
using Keyrun.Host;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// Draws one sim actor: a capsule (with a small nose showing facing) or a box, coloured per instance,
/// placed each frame by interpolating the actor's mm position and yaw between SimDriver's previous and
/// current state. mm become metres as floats here and only here (game/CLAUDE.md purity rule).
/// </summary>
public partial class ActorView : Node3D
{
    private const float MmToM = 0.001f;
    private const float RadPerYaw = Mathf.Tau / 1024f;

    /// <summary>The sim actor id this view follows.</summary>
    [Export]
    public int ActorId { get; set; } = SimDriver.WardenId;

    /// <summary>Draw a box instead of a capsule.</summary>
    [Export]
    public bool BoxShape { get; set; }

    /// <summary>Body colour.</summary>
    [Export]
    public Color BodyColor { get; set; } = new(0.2f, 0.4f, 0.9f);

    /// <summary>The driver to read state from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../SimDriver");

    private SimDriver? Driver { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        var body = GetNode<MeshInstance3D>("Body");
        var nose = GetNode<MeshInstance3D>("Nose");
        var material = new StandardMaterial3D { AlbedoColor = BodyColor, Roughness = 0.7f };
        if (BoxShape)
        {
            body.Mesh = new BoxMesh { Size = new Vector3(1.4f, 1.4f, 1.4f) };
            body.Position = new Vector3(0, 0.7f, 0);
            nose.Visible = false;
        }
        else
        {
            body.Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
            body.Position = new Vector3(0, 0.9f, 0);
            nose.Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.15f, 0.2f) };
            nose.Position = new Vector3(0.42f, 1.45f, 0); // local +x = facing (see Place)
            nose.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.9f, 1f) };
        }
        body.MaterialOverride = material;
        Place();
    }

    /// <inheritdoc />
    public override void _Process(double delta) => Place();

    private void Place()
    {
        if (Driver is null ||
            !Driver.State.Actors.TryGetValue(ActorId, out var now))
        {
            return;
        }
        var before = Driver.PreviousState.Actors.TryGetValue(ActorId, out var prev) ? prev : now;
        var t = Driver.Alpha;
        var x = Mathf.Lerp((float)before.Pos.X, (float)now.Pos.X, t) * MmToM;
        var y = Mathf.Lerp((float)before.Pos.Y, (float)now.Pos.Y, t) * MmToM;
        var z = Mathf.Lerp((float)before.Pos.Z, (float)now.Pos.Z, t) * MmToM;
        Position = new Vector3(x, y, z);
        Visible = now.Alive;
        Rotation = new Vector3(0, -YawRadians(before.Yaw, now.Yaw, t), 0);
    }

    // Sim yaw theta faces (cos theta, sin theta) in (x, z). A Y rotation of -theta turns local +x onto
    // that direction, so views model "forward" as local +x. Interpolates along the shorter arc.
    private static float YawRadians(int from, int to, float t)
    {
        var d = ((to - from) % 1024 + 1024 + 512) % 1024 - 512;
        return (from + (d * t)) * RadPerYaw;
    }

    /// <summary>Sim mm position to a Godot position on the floor plane, metres.</summary>
    public static Vector3 ToMetres(Vec3 mm) => new(mm.X * MmToM, mm.Y * MmToM, mm.Z * MmToM);
}
