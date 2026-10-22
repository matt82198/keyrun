using System.Collections.Generic;
using System.Linq;
using Keyrun.Hud;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Idle-pack name labels (finding f-1bf2dace; Matt 2026-10-21 vertical capture): a pack the party has not engaged draws ONE
/// label at the pack centre, so five idle Choir enemies stop piling their names at the screen edge. An engaged pack keeps
/// one label per living member, and a label whose box would be clipped by the viewport edge is hidden, never clamped into
/// a pile. Godot-free (game/src/Hud/IdlePackLabels.cs), driven with the same member and engagement inputs LabelView reads.
/// </summary>
public sealed class IdlePackLabelsTests
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int TextHeight = 20;

    private static int Measure(string text) => 8 * text.Length;

    private static IdlePackMember Member(int id, string name, string classId, string? pack, int x, int y, bool alive = true) =>
        new(id, name, classId, alive, x, y, pack);

    // Five Choir enemies: one lieutenant (Cantor) and four acolytes, all in pack "choir", clustered near screen centre.
    private static List<IdlePackMember> Choir(params bool[] alive)
    {
        var members = new List<IdlePackMember>
        {
            Member(10, "Choir Cantor", "enemy.choir.lieutenant", "choir", 900, 600),
            Member(11, "Choir Acolyte", "enemy.choir.acolyte", "choir", 880, 610),
            Member(12, "Choir Acolyte", "enemy.choir.acolyte", "choir", 920, 610),
            Member(13, "Choir Acolyte", "enemy.choir.acolyte", "choir", 890, 625),
            Member(14, "Choir Acolyte", "enemy.choir.acolyte", "choir", 910, 625),
        };
        for (var i = 0; i < alive.Length && i < members.Count; i++)
        {
            if (!alive[i])
            {
                members[i] = members[i] with { Alive = false };
            }
        }
        return members;
    }

    [Fact]
    public void IdlePack_DrawsExactlyOneLabel_AtTheCentre_WithLeaderNamePlusCount()
    {
        var labels = IdlePackLabels.Plan(Choir(), new HashSet<string>(), new Dictionary<string, string>(),
            Width, Height, Measure, TextHeight);

        var label = Assert.Single(labels);
        Assert.Equal("Choir Cantor +4", label.Text);
        Assert.True(label.Chevron, "an idle pack label carries the chevron glyph (shape, not colour alone)");
        Assert.Equal(10, label.LeaderId);
        Assert.Equal(new[] { 10, 11, 12, 13, 14 }, label.ActorIds);
        // Centroid of the five anchors: x (900+880+920+890+910)/5 = 900, y (600+610+610+625+625)/5 = 614.
        Assert.Equal(900, label.X);
        Assert.Equal(614, label.Y);
    }

    [Fact]
    public void IdlePack_WithAPackDisplayName_UsesIt()
    {
        var names = new Dictionary<string, string> { ["choir"] = "The Choir" };

        var label = Assert.Single(IdlePackLabels.Plan(Choir(), new HashSet<string>(), names, Width, Height, Measure, TextHeight));

        Assert.Equal("The Choir", label.Text);
    }

    [Fact]
    public void EngagedPack_DrawsOneLabelPerLivingMember_WithoutChevron()
    {
        // Member 13 is dead: a dead member never draws a label, and is not counted.
        var members = Choir(true, true, true, false, true);

        var labels = IdlePackLabels.Plan(members, new HashSet<string> { "choir" }, new Dictionary<string, string>(),
            Width, Height, Measure, TextHeight);

        Assert.Equal(4, labels.Count);
        Assert.All(labels, l => Assert.False(l.Chevron));
        Assert.All(labels, l => Assert.Single(l.ActorIds));
        Assert.Equal(new[] { 10, 11, 12, 14 }, labels.Select(l => l.ActorIds[0]).ToArray());
        Assert.Equal("Choir Cantor", labels[0].Text);
        Assert.Equal("Choir Acolyte", labels[1].Text);
    }

    [Fact]
    public void IdlePack_WithOnlyOneLivingMember_ShowsItsNameWithNoCount()
    {
        var members = Choir(false, false, false, false, true);

        var label = Assert.Single(IdlePackLabels.Plan(members, new HashSet<string>(), new Dictionary<string, string>(),
            Width, Height, Measure, TextHeight));

        Assert.Equal("Choir Acolyte", label.Text);
        Assert.Equal(new[] { 14 }, label.ActorIds);
    }

    [Fact]
    public void EdgeClippedLabel_IsHidden_NotClampedIntoAPile()
    {
        // The idle pack sits at the left edge: its box would start left of x=0, so it is hidden.
        var edge = new List<IdlePackMember>
        {
            Member(20, "Choir Cantor", "enemy.choir.lieutenant", "edge", 3, 600),
            Member(21, "Choir Acolyte", "enemy.choir.acolyte", "edge", 5, 605),
        };

        Assert.Empty(IdlePackLabels.Plan(edge, new HashSet<string>(), new Dictionary<string, string>(),
            Width, Height, Measure, TextHeight));

        // An idle pack whose box rises above y=0 is hidden the same way.
        var top = new List<IdlePackMember>
        {
            Member(30, "Choir Cantor", "enemy.choir.lieutenant", "top", 500, 5),
            Member(31, "Choir Acolyte", "enemy.choir.acolyte", "top", 500, 5),
        };
        Assert.Empty(IdlePackLabels.Plan(top, new HashSet<string>(), new Dictionary<string, string>(),
            Width, Height, Measure, TextHeight));
    }

    // Five Paired Warriors on one spot (the NameplateDeclutter "Pair"), all in one pack.
    private static List<IdlePackMember> Pair(string? pack) =>
        Enumerable.Range(0, 5).Select(i => Member(100 + i, "Paired Warrior", "enemy.pair.melee", pack, 900, 600)).ToList();

    [Fact]
    public void IdlePair_IsOneLabel_AndTheCrowdProofCountsFiveActors()
    {
        // NameplateDeclutterTest case c reads "HUD platecrowd ... crowd>=2"; LabelView.LogCrowds prints crowd=CrowdSize.
        var labels = IdlePackLabels.Plan(Pair("pair"), new HashSet<string>(), new Dictionary<string, string>(),
            Width, Height, Measure, TextHeight);

        var label = Assert.Single(labels);
        Assert.Equal(5, IdlePackLabels.CrowdSize(new[] { label }));
    }

    [Fact]
    public void EngagedPair_NearTheTopEdge_StillDrawsItsPlates_SoThePlateCrowdFoldsThem()
    {
        // Engaged (or pack-less) plates are folded by HudDeclutter.PlateCrowds and clamped by PlateStack.Layout as on main;
        // only an idle pack's single label is edge-hidden. Hiding these dropped the Pair crowd the declutter proof reads.
        var top = Enumerable.Range(0, 5).Select(i => Member(200 + i, "Paired Warrior", "enemy.pair.melee", null, 900, 5)).ToList();

        var labels = IdlePackLabels.Plan(top, new HashSet<string>(), new Dictionary<string, string>(),
            Width, Height, Measure, TextHeight);

        Assert.Equal(5, labels.Count);
        Assert.Equal(5, IdlePackLabels.CrowdSize(labels));
    }

    [Fact]
    public void NonPackMember_KeepsItsOwnLabel()
    {
        var lone = new List<IdlePackMember> { Member(40, "Hall Sentry", "enemy.hall.melee", null, 700, 500) };

        var label = Assert.Single(IdlePackLabels.Plan(lone, new HashSet<string>(), new Dictionary<string, string>(),
            Width, Height, Measure, TextHeight));

        Assert.Equal("Hall Sentry", label.Text);
        Assert.False(label.Chevron);
        Assert.Equal(new[] { 40 }, label.ActorIds);
    }
}
