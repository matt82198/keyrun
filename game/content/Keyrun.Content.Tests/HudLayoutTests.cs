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
    }

    [Fact]
    public void Separate_rejects_a_null_list()
    {
        Assert.Throws<ArgumentNullException>(() => HudLayout.Separate(null!, 1280, 720));
    }
}
