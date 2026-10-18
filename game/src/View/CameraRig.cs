using Godot;

namespace Keyrun.View;

/// <summary>
/// Third-person camera: a pivot that follows the target at shoulder height, turned by mouse look
/// (yaw unbounded, pitch clamped), with its Camera3D child held <see cref="Distance"/> metres behind
/// the pivot along the pivot's +Z, so the camera looks along the pivot's -Z at the target.
/// </summary>
public partial class CameraRig : Node3D
{
    private const float MinPitch = -1.2f;
    private const float MaxPitch = -0.05f;

    /// <summary>The node to follow (the Warden's ActorView).</summary>
    [Export]
    public NodePath TargetPath { get; set; } = new("../Actors/Warden");

    /// <summary>Camera distance behind the pivot, metres.</summary>
    [Export]
    public float Distance { get; set; } = 7.5f;

    /// <summary>Pivot height above the target's feet, metres.</summary>
    [Export]
    public float PivotHeight { get; set; } = 1.4f;

    /// <summary>Heading, radians. Pi looks along world +z (the Warden's spawn facing).</summary>
    public float Yaw { get; private set; } = Mathf.Pi;

    /// <summary>Pitch, radians (negative looks down).</summary>
    public float Pitch { get; private set; } = -0.42f;

    private Node3D? Target { get; set; }

    private Camera3D? Camera { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Target = GetNodeOrNull<Node3D>(TargetPath);
        Camera = GetNodeOrNull<Camera3D>("Camera");
        Apply();
    }

    /// <summary>Turns the rig by a mouse delta, radians.</summary>
    public void Look(float yawDelta, float pitchDelta)
    {
        Yaw = Mathf.Wrap(Yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
        Pitch = Mathf.Clamp(Pitch + pitchDelta, MinPitch, MaxPitch);
    }

    /// <summary>
    /// Puts the heading back to a value this rig reported as <see cref="Yaw"/> (lane 3E-c, <c>--replay-input</c>): the
    /// recorded yaw is restored bit for bit, with no second wrap, so WASD (PlayerInput reads <see cref="Yaw"/>) walks the
    /// recorded route. Pitch is untouched (movement never reads it).
    /// </summary>
    /// <param name="yaw">A heading in [-Pi, Pi], radians, as <see cref="Yaw"/> reported it.</param>
    public void RestoreYaw(float yaw) => Yaw = yaw;

    /// <inheritdoc />
    public override void _Process(double delta) => Apply();

    private void Apply()
    {
        if (Target is not null)
        {
            GlobalPosition = Target.GlobalPosition + new Vector3(0, PivotHeight, 0);
        }
        Rotation = new Vector3(Pitch, Yaw, 0);
        if (Camera is not null)
        {
            Camera.Position = new Vector3(0, 0, Distance);
        }
    }
}
