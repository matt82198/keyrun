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
        ApplyViewSize(userArgs);
        if (!_smoke)
        {
            // Lane 1E: everything except the smoke check boots into the arena (SimDriver reads
            // --autorun, --ticks, --log and --write-movie there).
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, ArenaScene);
        }
    }

    // --view-size WxH (GATE-FOREGROUND): the root viewport is sized to WxH px. A headless run's window is 64x64, so a
    // headless scene test that projects world geometry onto the HUD passes the shipped window size (1280x720) to frame
    // and project exactly as the desktop game does. Render-only; nothing the sim reads.
    private void ApplyViewSize(string[] userArgs)
    {
        var i = Array.IndexOf(userArgs, "--view-size");
        if (i < 0 || i + 1 >= userArgs.Length)
        {
            return;
        }
        var parts = userArgs[i + 1].Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h) || w < 1 || h < 1)
        {
            GD.PrintErr($"--view-size expects WxH, got '{userArgs[i + 1]}'");
            return;
        }
        GetTree().Root.Size = new Vector2I(w, h);
        GD.Print(FormattableString.Invariant($"VIEW size={GetTree().Root.GetVisibleRect().Size.X}x{GetTree().Root.GetVisibleRect().Size.Y}"));
    }

    /// <summary>The phase 1 arena scene (lane 1E).</summary>
    public const string ArenaScene = "res://scenes/Arena.tscn";

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
