using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>The rig's camera preset (<c>--camera chase|vertical</c>, toggled in play with V).</summary>
public enum CameraModeKind
{
    /// <summary>The WoW over-the-shoulder follow camera (<see cref="CameraFraming"/>), the default.</summary>
    Chase,

    /// <summary>A high-pitch view looking down on the party and the floor telegraphs (WoW max zoom-out style).</summary>
    Vertical,
}

/// <summary>A preset's starting pitch (mrad, negative looks down) and boom (mm) behind the pivot.</summary>
/// <param name="PitchMrad">Pitch, mrad.</param>
/// <param name="BoomMm">Boom from the pivot to the camera, mm.</param>
public readonly record struct CameraModeView(int PitchMrad, int BoomMm);

/// <summary>
/// CAMERA-VERTICAL (Matt 2026-10-21 "run the playthroughs vertical camera"; capture kf2-kick-1f8fed7f frame 007: the low
/// chase camera let the party hide the telegraph circle and left the back of the room unreadable). The run argument
/// <c>--camera chase|vertical</c> (default chase, unchanged) picks the preset; V flips it in play. Vertical is
/// <see cref="VerticalPitchMrad"/> (68.75 degrees down, the rig's own pitch clamp) at <see cref="VerticalBoomMm"/>, so
/// at 1280x720 the party, the current pull and a 4 m telegraph around it fit inside the HUD bands. Above the 1.6 m walls
/// the chase-only rules (the full-height wall clamp, the boss headroom pull-back) are off (<see cref="ChaseRules"/>).
/// No Godot types and integers only; unit-tested in Keyrun.Content.Tests/CameraModeTests.cs, applied by
/// game/src/View/CameraRig.cs, parsed by ArenaOptions (game/src/Host/SimDriver.cs).
/// </summary>
public static class CameraMode
{
    /// <summary>The run argument.</summary>
    public const string Flag = "--camera";

    /// <summary>Vertical pitch, mrad: 68.75 degrees down (CameraRig's MinPitch, so the mouse cannot go past it).</summary>
    public const int VerticalPitchMrad = -1200;

    /// <summary>Vertical boom, mm: the camera about 16.8 m above the floor and 6.0 m behind the player.</summary>
    public const int VerticalBoomMm = 16500;

    /// <summary>The in-play toggle key's label (a view key like the wheel zoom, not a keybinds.json action).</summary>
    public const string ToggleKeyLabel = "V";

    /// <summary>Parses a mode name.</summary>
    /// <param name="raw">"chase" or "vertical".</param>
    /// <returns>The mode.</returns>
    /// <exception cref="ArgumentException">Any other value (fail closed).</exception>
    public static CameraModeKind Parse(string raw) => raw switch
    {
        "chase" => CameraModeKind.Chase,
        "vertical" => CameraModeKind.Vertical,
        _ => throw new ArgumentException($"{Flag} needs chase or vertical, got '{raw}'"),
    };

    /// <summary>The mode the user arguments ask for: the last <see cref="Flag"/> value, else chase.</summary>
    /// <param name="args">The user arguments.</param>
    /// <returns>The mode.</returns>
    /// <exception cref="ArgumentException">The flag has no value or an unknown one.</exception>
    public static CameraModeKind FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var mode = CameraModeKind.Chase;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != Flag)
            {
                continue;
            }
            if (i + 1 >= args.Count)
            {
                throw new ArgumentException($"{Flag} needs a value");
            }
            mode = Parse(args[++i]);
        }
        return mode;
    }

    /// <summary>The mode's name as the argument spells it.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>"chase" or "vertical".</returns>
    public static string Name(CameraModeKind mode) => mode == CameraModeKind.Vertical ? "vertical" : "chase";

    /// <summary>The preset a mode starts at.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>Pitch and boom.</returns>
    public static CameraModeView For(CameraModeKind mode) => mode == CameraModeKind.Vertical
        ? new CameraModeView(VerticalPitchMrad, VerticalBoomMm)
        : new CameraModeView(CameraFraming.DefaultPitchMrad, CameraFraming.DefaultBoomMm);

    /// <summary>The other mode (the V toggle).</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The other mode.</returns>
    public static CameraModeKind Toggle(CameraModeKind mode) =>
        mode == CameraModeKind.Vertical ? CameraModeKind.Chase : CameraModeKind.Vertical;

    /// <summary>True when the chase-only rules run: the full-height wall clamp, the outside audit and the headroom fit.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>True for chase.</returns>
    public static bool ChaseRules(CameraModeKind mode) => mode == CameraModeKind.Chase;

    /// <summary>The wheel zoom in a mode: <see cref="CameraFraming.Zoom"/>, out to <see cref="VerticalBoomMm"/> in vertical.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="boomMm">The boom before the notches, mm.</param>
    /// <param name="notches">Wheel notches (negative zooms in).</param>
    /// <returns>The new boom, mm.</returns>
    public static int Zoom(CameraModeKind mode, int boomMm, int notches)
    {
        var max = mode == CameraModeKind.Vertical ? VerticalBoomMm : CameraFraming.MaxZoomMm;
        return (int)Math.Clamp((long)boomMm + (long)notches * CameraFraming.ZoomStepMm, CameraFraming.MinZoomMm, max);
    }

    /// <summary>The run-start proof line: <c>CAMERA mode=M pitch=P dist=D</c> (pitch mrad, dist mm, as the rig holds them).</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="pitchMrad">The rig's pitch, mrad.</param>
    /// <param name="boomMm">The rig's zoom, mm.</param>
    /// <returns>The line.</returns>
    public static string ProofLine(CameraModeKind mode, int pitchMrad, int boomMm) =>
        FormattableString.Invariant($"CAMERA mode={Name(mode)} pitch={pitchMrad} dist={boomMm}");
}
