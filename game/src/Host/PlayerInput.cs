using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Sim.Core;
using Keyrun.View;

namespace Keyrun.Host;

/// <summary>
/// Turns the human's input into sim commands for the Warden. WASD moves relative to the camera's
/// heading (one <c>move</c> command per tick while a key is held, facing the walk direction); the mouse
/// turns the CameraRig while captured; Esc frees the mouse and a left click captures it again.
/// Under <c>--autorun</c> a fixed script replaces the keyboard so headless runs and proof frames
/// are reproducible: walk +z (toward the box) on ticks 0..19, then stand still in the Cleave cone.
/// </summary>
public partial class PlayerInput : Node, ICommandSource
{
    /// <summary>Last tick (exclusive) of the scripted autorun walk.</summary>
    public const int ScriptedWalkTicks = 20;

    /// <summary>Mouse look sensitivity, radians per pixel.</summary>
    private const float MouseRadPerPixel = 0.0035f;

    private bool _scripted;

    /// <summary>The camera rig the mouse turns and WASD is relative to.</summary>
    [Export]
    public NodePath CameraRigPath { get; set; } = new("../CameraRig");

    /// <summary>The driver (for the autorun flag).</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    private CameraRig? Rig { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Rig = GetNodeOrNull<CameraRig>(CameraRigPath);
        var args = OS.GetCmdlineUserArgs();
        _scripted = Array.IndexOf(args, "--autorun") >= 0 || Array.IndexOf(args, "--write-movie") >= 0;
        if (!_scripted && DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_scripted)
        {
            return;
        }
        switch (@event)
        {
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Rig?.Look(-motion.Relative.X * MouseRadPerPixel, -motion.Relative.Y * MouseRadPerPixel);
                break;
        }
    }

    /// <inheritdoc />
    public void Sample(int tick, SimState state, List<Command> into)
    {
        if (_scripted)
        {
            if (tick < ScriptedWalkTicks)
            {
                into.Add(new MoveCommand(tick, SimDriver.WardenId, new Dir2(0, 1000), 256));
            }
            return;
        }

        float forward = 0, right = 0;
        if (Input.IsPhysicalKeyPressed(Key.W)) forward += 1;
        if (Input.IsPhysicalKeyPressed(Key.S)) forward -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D)) right += 1;
        if (Input.IsPhysicalKeyPressed(Key.A)) right -= 1;
        if (forward == 0 && right == 0)
        {
            return;
        }

        // Camera heading on the floor. The rig looks along its -Z: forward = (-sin yaw, -cos yaw),
        // right = (cos yaw, -sin yaw) in world (x, z). Sim mm axes are Godot's x and z unchanged.
        var yaw = Rig?.Yaw ?? Mathf.Pi;
        var wx = (-Mathf.Sin(yaw) * forward) + (Mathf.Cos(yaw) * right);
        var wz = (-Mathf.Cos(yaw) * forward) - (Mathf.Sin(yaw) * right);
        var len = Mathf.Sqrt((wx * wx) + (wz * wz));
        wx /= len;
        wz /= len;
        var dir = new Dir2(Mathf.RoundToInt(wx * 1000), Mathf.RoundToInt(wz * 1000));
        into.Add(new MoveCommand(tick, SimDriver.WardenId, dir, ToYaw1024(wx, wz)));
    }

    /// <summary>World XZ direction to sim yaw (1/1024 turn, 0 = +x, 256 = +z; the Facing.cs convention).</summary>
    public static int ToYaw1024(float x, float z)
    {
        var turns = Mathf.Atan2(z, x) / Mathf.Tau;
        var yaw = Mathf.RoundToInt(turns * 1024) % 1024;
        return yaw < 0 ? yaw + 1024 : yaw;
    }
}
