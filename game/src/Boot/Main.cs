using System;
using Godot;
using Keyrun.Sim;

namespace Keyrun.Boot;

/// <summary>
/// Boot scene root. Reads the user arguments after <c>--</c> on the Godot command line.
/// </summary>
/// <remarks>
/// <c>--smoke</c>: let the main loop run a few frames, print one <c>SMOKE ok</c> line
/// naming the engine version and the loaded sim assembly, then quit with exit code 0.
/// CI (game.yml job game-e2e) asserts on that line. Later phases add --autorun, --seed,
/// --key, --ticks, --log and --input here (ENGINE-PLAN section 4).
/// </remarks>
public partial class Main : Node3D
{
    /// <summary>Frames the main loop must complete before the smoke line is printed.</summary>
    private const int SmokeFrames = 3;

    private bool _smoke;
    private int _frames;

    /// <inheritdoc />
    public override void _Ready()
    {
        var userArgs = OS.GetCmdlineUserArgs();
        _smoke = Array.IndexOf(userArgs, "--smoke") >= 0;
        GD.Print($"Keyrun boot: engine {Engine.GetVersionInfo()["string"]}, sim {SimAssemblyName()}");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_smoke)
        {
            return;
        }
        _frames++;
        if (_frames == SmokeFrames)
        {
            GD.Print($"SMOKE ok frames={_frames} sim={SimAssemblyName()}");
            GetTree().Quit(0);
        }
    }

    /// <summary>Forces the runtime to load Keyrun.Sim (a const would be inlined at compile time).</summary>
    private static string SimAssemblyName() =>
        typeof(SimInfo).Assembly.GetName().Name ?? "<unnamed>";
}
