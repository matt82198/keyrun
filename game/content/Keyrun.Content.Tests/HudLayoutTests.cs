using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// HUD polish (design/ART-PLAN.md section 8 lane 8): the top bar never puts the pull title under the Slack bar, the
/// title always fits its panel, and enemy name plates never overlap each other or leave the viewport. The layout is
/// pure integer pixel math (<see cref="HudLayout"/>); the Godot HUD and LabelView only apply it.
/// </summary>
public sealed class HudLayoutTests
{
    /// <summary>A fixed-width stand-in for the HUD font: 10 px per character.</summary>
    private static int Measure(string text) => text.Length * 10;

    private static void AssertNoOverlap(IReadOnlyList<PixelRect> rects)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].Intersects(rects[j]), $"plate {i} {rects[i]} overlaps plate {j} {rects[j]}");
            }
        }
    }

    private static void AssertInside(IReadOnlyList<PixelRect> rects, int width, int height)
    {
        foreach (var r in rects)
        {
            Assert.True(r.X >= 0 && r.Y >= 0 && r.Right <= width && r.Bottom <= height, $"plate {r} leaves the {width}x{height} viewport");
        }
    }

    private static PixelRect[] Placed(IReadOnlyList<PixelRect> plates, IReadOnlyList<PlateOffset> offsets) =>
        plates.Select((p, i) => p.Offset(offsets[i].Dx, offsets[i].Dy)).ToArray();

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1280, 800)]
    [InlineData(1920, 1080)]
    [InlineData(1024, 600)]
    [InlineData(640, 480)]
    public void Top_bar_objective_panel_and_slack_bar_do_not_intersect(int width, int height)
    {
        TopBarLayout bar = HudLayout.TopBar(width, height);
        Assert.False(bar.Objective.Intersects(bar.Slack), $"objective {bar.Objective} intersects slack {bar.Slack} at {width}x{height}");
        Assert.True(bar.Slack.X >= 0 && bar.Slack.Right <= width, $"slack {bar.Slack} leaves the {width} px viewport");
        Assert.True(bar.Objective.X >= 0 && bar.Objective.Right <= width, $"objective {bar.Objective} leaves the {width} px viewport");
        Assert.Equal(bar.Objective.Width - (2 * HudLayout.PanelPad), bar.TitleMaxWidth);
    }

    [Fact]
    public void Top_bar_at_1280_keeps_the_pull_4_title_whole()
    {
        TopBarLayout bar = HudLayout.TopBar(1280, 720);
        // The longest live title, at about 10 px per character of the 18 px HUD font.
        const string title = "Pull 4/4  The Hollow Warden - cleave, sundered-twins, the-lock";
        Assert.Equal(title, HudLayout.FitTitle(title, bar.TitleMaxWidth, Measure));
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1280, 800)]
    [InlineData(1920, 1080)]
    [InlineData(640, 480)]
    [InlineData(320, 240)]
    public void Title_text_width_never_exceeds_its_panel(int width, int height)
    {
        TopBarLayout bar = HudLayout.TopBar(width, height);
        var title = "Pull 3/4  The Seed - the boss's core rule with no stakes" + new string('x', 120);
        string fitted = HudLayout.FitTitle(title, bar.TitleMaxWidth, Measure);
        Assert.True(Measure(fitted) <= bar.TitleMaxWidth, $"'{fitted}' is {Measure(fitted)} px in a {bar.TitleMaxWidth} px title slot");
        Assert.EndsWith(HudLayout.Ellipsis, fitted);
        Assert.True(HudLayout.PanelWidth(Measure(fitted), Measure("Defeat the pull: 1 of 1 remain"), bar) <= bar.Objective.Width);
    }

    [Fact]
    public void Five_plates_stacked_on_one_body_are_separated()
    {
        // The Pair hall (run-040): five plates drawn on almost the same spot, nearest first.
        var plates = Enumerable.Range(0, 5).Select(i => new PixelRect(1000 + (i * 6), 290, 140, 30)).ToArray();
        IReadOnlyList<PlateOffset> offsets = HudLayout.Separate(plates, 1280, 720);
        PixelRect[] placed = Placed(plates, offsets);
        AssertNoOverlap(placed);
        AssertInside(placed, 1280, 720);
        Assert.Equal(new PlateOffset(0, 0), offsets[0]);
    }

    [Fact]
    public void Plates_off_the_right_edge_are_pulled_inside()
    {
        var plates = new[] { new PixelRect(1220, 290, 140, 30), new PixelRect(-60, 290, 140, 30) };
        PixelRect[] placed = Placed(plates, HudLayout.Separate(plates, 1280, 720));
        AssertInside(placed, 1280, 720);
        AssertNoOverlap(placed);
    }

    [Fact]
    public void Twin_plate_4_m_from_the_boss_at_arena_distance_does_not_overlap_it()
    {
        // run-058: the boss and a Twin 4 m apart, seen down the arena from about 14 m: the plate centres land about
        // 60 px apart while "Sundered Twin (A)" and "The Hollow Warden" are each about 170 px wide.
        var boss = new PixelRect(735 - 85, 290, 170, 30);
        var twin = new PixelRect(795 - 85, 292, 170, 30);
        var plates = new[] { twin, boss };
        PixelRect[] placed = Placed(plates, HudLayout.Separate(plates, 1280, 720));
        AssertNoOverlap(placed);
        AssertInside(placed, 1280, 720);
    }

    [Fact]
    public void Plates_keep_clear_of_hud_obstacles()
    {
        var frames = new PixelRect(16, 124, 240, 220);
        var plates = new[] { new PixelRect(100, 300, 120, 30) };
        PixelRect[] placed = Placed(plates, HudLayout.Separate(plates, 1280, 720, new[] { frames }));
        Assert.False(placed[0].Intersects(frames), $"plate {placed[0]} sits under the party frames {frames}");
    }

    [Fact]
    public void Separated_plates_that_never_touched_do_not_move()
    {
        var plates = new[] { new PixelRect(100, 300, 120, 30), new PixelRect(600, 300, 120, 30) };
        Assert.All(HudLayout.Separate(plates, 1280, 720), o => Assert.Equal(new PlateOffset(0, 0), o));
        // Assert.All passes vacuously on an empty result, so pin one offset per plate: a Separate() that returned
        // nothing would otherwise read as "nobody moved".
        Assert.Equal(plates.Length, HudLayout.Separate(plates, 1280, 720).Count);
    }

    [Fact]
    public void Separate_rejects_a_null_list()
    {
        Assert.Throws<ArgumentNullException>(() => HudLayout.Separate(null!, 1280, 720));
    }

    // HUD-POLISH-2: the SPREAD/STACK floor labels (src/Hud/Pings.cs) and the over-head cast bars (src/View/CastBarView.cs).

    [Fact]
    public void Five_floor_labels_on_one_body_are_separated_and_stay_near_it()
    {
        // run-041: five "STACK: group up" labels on the Pair's telegraph origins, a few pixels apart, one pile.
        var labels = Enumerable.Range(0, 5)
            .Select(i => HudLayout.PingLabelRect(new PixelPoint(760 + (i * 8), 427), Measure("STACK: group up")))
            .ToArray();
        IReadOnlyList<PixelRect> placed = HudLayout.PingLabels(labels, 1280, 720, Array.Empty<PixelRect>());
        AssertNoOverlap(placed);
        AssertInside(placed, 1280, 720);
        for (var i = 0; i < labels.Length; i++)
        {
            var dx = Math.Abs(placed[i].X - labels[i].X);
            var dy = Math.Abs(placed[i].Y - labels[i].Y);
            Assert.True(dx <= HudLayout.PingLabelReach && dy <= HudLayout.PingLabelReach,
                $"label {i} moved ({dx}, {dy}) px from its ping, more than {HudLayout.PingLabelReach} px");
        }
        Assert.Equal(labels[0], placed[0]);
    }

    // STACK-PING-LABEL: the Pair's casters each raise their own STACK telegraph at the same tick, a few pixels apart,
    // so one party call drew five "STACK: group up" rows over the pack. One call on one spot draws one label.

    [Fact]
    public void One_stack_call_from_five_casters_on_one_spot_draws_one_label()
    {
        var texts = Enumerable.Repeat("STACK: group up", 5).ToArray();
        var labels = Enumerable.Range(0, 5)
            .Select(i => HudLayout.PingLabelRect(new PixelPoint(760 + (i * 8), 427 + (i * 6)), Measure(texts[i])))
            .ToArray();
        IReadOnlyList<int> shown = HudLayout.DistinctPings(texts, labels);
        Assert.True(shown.Count == 1, $"one STACK call on one spot drew {shown.Count} labels: [{string.Join(", ", shown)}]");
        Assert.Equal(0, shown[0]);
    }

    [Fact]
    public void Different_calls_and_far_apart_spots_each_keep_their_label()
    {
        // A SPREAD and a STACK on one body are two calls; the same call on two bodies 300 px apart is two spots.
        var texts = new[] { "SPREAD: move apart", "STACK: group up", "SPREAD: move apart", "SPREAD: move apart" };
        var labels = new[]
        {
            HudLayout.PingLabelRect(new PixelPoint(400, 400), Measure(texts[0])),
            HudLayout.PingLabelRect(new PixelPoint(404, 402), Measure(texts[1])),
            HudLayout.PingLabelRect(new PixelPoint(700, 400), Measure(texts[2])),
            HudLayout.PingLabelRect(new PixelPoint(406, 404), Measure(texts[3])),
        };
        Assert.Equal(new[] { 0, 1, 2 }, HudLayout.DistinctPings(texts, labels));
    }

    [Fact]
    public void Floor_labels_keep_clear_of_cast_bars_and_the_callout()
    {
        var bar = HudLayout.CastBarRect(new PixelPoint(700, 400));
        var callout = new PixelRect(440, 100, 400, 32);
        var labels = new[] { HudLayout.PingLabelRect(new PixelPoint(700, 410), Measure("SPREAD: move apart")) };
        IReadOnlyList<PixelRect> placed = HudLayout.PingLabels(labels, 1280, 720, new[] { bar, callout });
        Assert.False(placed[0].Intersects(bar), $"label {placed[0]} sits under the cast bar {bar}");
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public void Four_cast_bars_are_disjoint_and_inside_the_viewport(int width, int height)
    {
        // run-048: two casters side by side mid-screen, one at the right edge (cut to "Se..."), one at the left edge.
        var anchors = new[]
        {
            new PixelPoint(width / 2, 300),
            new PixelPoint((width / 2) + 40, 304),
            new PixelPoint(width - 10, 260),
            new PixelPoint(5, 420),
        };
        var band = HudLayout.TopBar(width, height).Band(width);
        IReadOnlyList<PixelRect> bars = HudLayout.CastBars(anchors, width, height, new[] { band });
        Assert.Equal(anchors.Length, bars.Count);
        AssertNoOverlap(bars);
        foreach (var b in bars)
        {
            Assert.True(b.X >= HudLayout.Margin && b.Right <= width - HudLayout.Margin && b.Y >= 0 && b.Bottom <= height,
                $"cast bar {b} leaves the {width}x{height} viewport's {HudLayout.Margin} px gutter");
            Assert.Equal(HudLayout.CastBarWidth, b.Width);
            Assert.False(b.Intersects(band), $"cast bar {b} sits under the top bar {band}");
        }
    }

    [Fact]
    public void Long_caster_and_spell_title_never_exceeds_its_bar()
    {
        string fitted = HudLayout.FitCastLabel("The Hollow Warden of the Deep Vault", "Sundered Twins - the lock of ages", "  2.5s",
            HudLayout.CastBarWidth, Measure, casterPlateShown: false);
        Assert.True(Measure(fitted) <= HudLayout.CastBarWidth, $"'{fitted}' is {Measure(fitted)} px on a {HudLayout.CastBarWidth} px bar");
        Assert.EndsWith("...  2.5s", fitted);
        // 18 characters, exactly the 180 px bar at 10 px per character: a label that fits is kept whole.
        Assert.Equal("Cinder: Bolt  0.4s",
            HudLayout.FitCastLabel("Cinder", "Bolt", "  0.4s", HudLayout.CastBarWidth, Measure, casterPlateShown: false));
    }

    // f-b78d1567 (BIBLE s3 "Cast bars are the enemy's language"): the caster's plate already names it, so its cast label
    // spends the bar on the ability alone.
    [Fact]
    public void A_caster_with_a_drawn_plate_gets_the_ability_alone()
    {
        Assert.Equal("Bolt  0.4s",
            HudLayout.FitCastLabel("Cinder", "Bolt", "  0.4s", HudLayout.CastBarWidth, Measure, casterPlateShown: true));
        Assert.Equal("Bolt interrupted",
            HudLayout.FitCastLabel("Cinder", "Bolt interrupted", string.Empty, HudLayout.CastBarWidth, Measure, casterPlateShown: true));
    }

    // With a plate drawn, a too-long ability is cut on its own; the caster's name never comes back.
    [Fact]
    public void A_plated_caster_long_ability_is_cut_without_the_caster()
    {
        string fitted = HudLayout.FitCastLabel("Cinder", "Sundered Twins - the lock of ages", "  2.5s", HudLayout.CastBarWidth,
            Measure, casterPlateShown: true);
        Assert.Equal("Sundered...  2.5s", fitted);
        Assert.True(Measure(fitted) <= HudLayout.CastBarWidth);
    }

    // No plate drawn for the caster (off its plate, crowd edge, dead mid-cast): "Name: Ability" keeps it identifiable.
    [Fact]
    public void A_caster_without_a_plate_keeps_its_name()
    {
        Assert.Equal("Cinder: Bolt  0.4s",
            HudLayout.FitCastLabel("Cinder", "Bolt", "  0.4s", HudLayout.CastBarWidth, Measure, casterPlateShown: false));
        Assert.Equal("Bolt  0.4s",
            HudLayout.FitCastLabel(string.Empty, "Bolt", "  0.4s", HudLayout.CastBarWidth, Measure, casterPlateShown: false));
    }

    // Truncation favours the ability: the caster's name is cut first, so the whole ability survives beside it.
    [Fact]
    public void A_plateless_label_cuts_the_caster_before_the_ability()
    {
        // 120 px for the title at 10 px per character: ": Lock" takes 60, so the caster gets "The..." (60).
        string fitted = HudLayout.FitCastLabel("The Hollow Warden of the Deep Vault", "Lock", "  2.5s", HudLayout.CastBarWidth,
            Measure, casterPlateShown: false);
        Assert.Equal("The...: Lock  2.5s", fitted);
        Assert.True(Measure(fitted) <= HudLayout.CastBarWidth);
    }

    // When not one character of the caster fits beside the whole ability, the caster is dropped, not the ability.
    [Fact]
    public void A_plateless_label_drops_the_caster_when_the_ability_needs_the_bar()
    {
        Assert.Equal("Lock of Ages  2.5s",
            HudLayout.FitCastLabel("Warden", "Lock of Ages", "  2.5s", HudLayout.CastBarWidth, Measure, casterPlateShown: false));
        Assert.Equal("Sundered...  2.5s",
            HudLayout.FitCastLabel("Hollow Warden", "Sundered Twins", "  2.5s", HudLayout.CastBarWidth, Measure, casterPlateShown: false));
    }

    [Fact]
    public void A_bar_or_label_shows_only_when_part_of_it_is_on_screen()
    {
        Assert.True(HudLayout.Shows(HudLayout.CastBarRect(new PixelPoint(1300, 300)), 1280, 720));
        Assert.False(HudLayout.Shows(HudLayout.CastBarRect(new PixelPoint(1500, 300)), 1280, 720));
        Assert.False(HudLayout.Shows(HudLayout.CastBarRect(new PixelPoint(600, -40)), 1280, 720));
    }
}
