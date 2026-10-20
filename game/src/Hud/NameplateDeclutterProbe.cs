using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HudLayout = Keyrun.Content.HudLayout;
using PixelRect = Keyrun.Content.PixelRect;

namespace Keyrun.Hud;

/// <summary>
/// Test-only probe for lane NAMEPLATE-DECLUTTER, run by NameplateDeclutterTest.ps1 (run P) as its own scene
/// (res://src/Hud/NameplateDeclutterProbe.tscn). Drives the Godot-free rules LabelView and Pings draw from:
///   crowd-target - five "Paired Warrior" plates at one anchor, the third the target: one plate draws, the target's,
///                  as "&gt; Paired Warrior x5 &lt;";
///   crowd-plain  - the same five with no target: one plate, the nearest, as "Paired Warrior x5";
///   two-names    - a Twin and the boss at one spot: both draw, and after HudLayout.Separate they share no pixel;
///   far-apart    - two same-name plates that do not touch: both draw, no count;
///   ping-world   - SPREAD draws no world label (the banner is its one text), STACK keeps one.
/// Prints "NAMEPLATE-DECLUTTER PASS cases=5" or one "NAMEPLATE-DECLUTTER FAIL ..." line per failed case, then quits 0
/// or 1.
/// </summary>
public partial class NameplateDeclutterProbe : Node
{
    /// <inheritdoc />
    public override void _Ready()
    {
        List<string> failures;
        try
        {
            failures = Check();
        }
        catch (Exception ex)
        {
            failures = new List<string> { "probe threw " + ex.GetType().Name + ": " + ex.Message };
        }
        foreach (var f in failures)
        {
            GD.Print("NAMEPLATE-DECLUTTER FAIL " + f);
        }
        if (failures.Count == 0)
        {
            GD.Print("NAMEPLATE-DECLUTTER PASS cases=5");
        }
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }

    /// <summary>Runs the five cases.</summary>
    /// <returns>One message per failed case; empty when all pass.</returns>
    public static List<string> Check()
    {
        var failures = new List<string>();
        var spot = new PixelRect(600, 300, 130, 23);

        // crowd-target: five same-name plates on one spot, index 2 is the Warden's target.
        var five = Enumerable.Range(0, 5).Select(i => new PlateCandidate("Paired Warrior", spot, i == 2)).ToList();
        var crowds = HudDeclutter.PlateCrowds(five);
        if (crowds.Count != 1 || crowds[0].Leader != 2 || crowds[0].Members.Count != 5)
        {
            failures.Add($"crowd-target: {Describe(crowds)} (want one crowd led by 2 holding 5)");
        }
        var text = HudDeclutter.PlateText("Paired Warrior", 5, targeted: true);
        if (text != "> Paired Warrior x5 <")
        {
            failures.Add($"crowd-target: plate text '{text}' (want '> Paired Warrior x5 <')");
        }

        // crowd-plain: the same five, no target.
        var plain = HudDeclutter.PlateCrowds(five.Select(p => p with { Targeted = false }).ToList());
        if (plain.Count != 1 || plain[0].Leader != 0 || plain[0].Members.Count != 5 ||
            HudDeclutter.PlateText("Paired Warrior", 5, targeted: false) != "Paired Warrior x5")
        {
            failures.Add($"crowd-plain: {Describe(plain)} (want one crowd led by 0 holding 5, 'Paired Warrior x5')");
        }

        // two-names: a Twin and the boss on one spot keep both plates, separated.
        var two = new List<PlateCandidate> { new("Sundered Twin (A)", spot, false), new("The Hollow Warden", spot, true) };
        var twoCrowds = HudDeclutter.PlateCrowds(two);
        if (twoCrowds.Count != 2)
        {
            failures.Add($"two-names: {Describe(twoCrowds)} (want two crowds)");
        }
        else
        {
            var rects = twoCrowds.Select(c => two[c.Leader].Rect).ToList();
            var offsets = HudLayout.Separate(rects, 1280, 720);
            var placed = rects.Select((r, i) => r.Offset(offsets[i].Dx, offsets[i].Dy)).ToList();
            if (placed[0].Intersects(placed[1]))
            {
                failures.Add($"two-names: separated plates {placed[0]} and {placed[1]} share a pixel");
            }
        }

        // far-apart: same name, boxes that do not touch.
        var far = new List<PlateCandidate> { new("Choir Acolyte", spot, false), new("Choir Acolyte", spot.Offset(400, 0), false) };
        var farCrowds = HudDeclutter.PlateCrowds(far);
        if (farCrowds.Count != 2 || farCrowds.Any(c => c.Members.Count != 1) ||
            HudDeclutter.PlateText("Choir Acolyte", 1, targeted: false) != "Choir Acolyte")
        {
            failures.Add($"far-apart: {Describe(farCrowds)} (want two crowds of one, plain name)");
        }

        // ping-world: SPREAD's one text is the banner; STACK marks its spot.
        if (HudDeclutter.WorldPingLabel("SPREAD") || !HudDeclutter.WorldPingLabel("STACK"))
        {
            failures.Add($"ping-world: SPREAD world={HudDeclutter.WorldPingLabel("SPREAD")} STACK world={HudDeclutter.WorldPingLabel("STACK")} (want False, True)");
        }
        return failures;
    }

    private static string Describe(IReadOnlyList<PlateCrowd> crowds) =>
        crowds.Count + " crowd(s): " + string.Join("; ", crowds.Select(c => $"leader {c.Leader} members [{string.Join(",", c.Members)}]"));
}
