using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Finding f-0a21646d (D15, capture cap2-1312z frame cap2-001-t004s): the talent panel read "1/5 picked" but no card
/// carried a picked marker; the cursor and the pick shared one green channel, so a player could not tell them apart.
/// Ruling: both states show at once on separate channels. The cursor stays the green fork-row read; a picked option gets
/// a shape-plus-colour marker (a gold "PICKED" tag with a drawn check, and a thicker gold border), and the fork row's
/// header names the picked option. <see cref="TalentPanelMarks"/> is the pure state the Godot TalentPanel draws from and
/// the Screen prints its "SCREEN talent mark" proof from, recomputed from the live <see cref="TalentPicks"/> every call.
/// </summary>
public sealed class TalentPanelMarksTests
{
    // Screen.LayoutFor at the 1280x720 design viewport (TalentCardLayoutTests uses the same panel).
    private const int DesignWidth = 1216;
    private const int DesignHeight = 614;

    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static TalentPicks Warden() => TalentPicks.Start(Reg.Classes["warden"]);

    private static int ForkOf(TalentPicks picks, string abilityId) =>
        picks.Forks.Select((f, i) => (f, i)).Single(x => x.f.AbilityId == abilityId).i;

    // The capture's replay: the panel opens with the cursor on fork 1 (Strike) and Key1 picks option 1 of the cursor's
    // fork (Screen.HandleTalentKey: Key1 -> PickTalent(TalentFork, 0)), which is Rend.
    private static (TalentPicks Picks, int Cursor) RendPickedWithKey1OnStrike()
    {
        var picks = Warden();
        var cursor = ForkOf(picks, "warden.strike");
        return (picks.Pick(cursor, 0), cursor);
    }

    [Fact]
    public void Key1_on_strike_marks_rend_picked_and_the_proof_line_tells_cursor_from_pick()
    {
        var (picks, cursor) = RendPickedWithKey1OnStrike();
        Assert.Equal("rend", picks.PickedOption(cursor)); // the existing pick pin still holds

        Assert.Equal("SCREEN talent mark fork=1 cursor=warden.strike picked=rend marker=1", TalentPanelMarks.ProofLine(picks, 0, cursor));
        Assert.Equal("SCREEN talent mark fork=2 cursor=warden.strike picked=- marker=0", TalentPanelMarks.ProofLine(picks, 1, cursor));

        var marks = TalentPanelMarks.Marks(picks, cursor);
        var rend = marks.Single(m => m.Fork == 0 && m.Option == 0);
        Assert.True(rend.Picked, "Rend is picked but carries no marker");
        Assert.True(rend.Cursor, "Rend is on the cursor's fork, so it shows both states");
        var riposte = marks.Single(m => m.Fork == 0 && m.Option == 1);
        Assert.False(riposte.Picked);
        Assert.True(riposte.Cursor);
        Assert.Equal(1, marks.Count(m => m.Picked));
    }

    [Fact]
    public void After_the_cursor_moves_off_strike_rend_keeps_its_marker_and_the_cursor_card_has_none()
    {
        var (picks, _) = RendPickedWithKey1OnStrike();
        var cursor = ForkOf(picks, "warden.guardBreak"); // Down

        Assert.Equal("SCREEN talent mark fork=1 cursor=warden.guardBreak picked=rend marker=1", TalentPanelMarks.ProofLine(picks, 0, cursor));
        Assert.Equal("SCREEN talent mark fork=2 cursor=warden.guardBreak picked=- marker=0", TalentPanelMarks.ProofLine(picks, cursor, cursor));

        var marks = TalentPanelMarks.Marks(picks, cursor);
        var rend = marks.Single(m => m.Fork == 0 && m.Option == 0);
        Assert.True(rend.Picked);
        Assert.False(rend.Cursor);
        var focused = marks.Single(m => m.Fork == cursor && m.Option == 0); // the capture's green "Focused Break" card
        Assert.True(focused.Cursor);
        Assert.False(focused.Picked, "the cursor card must not read as a pick");
    }

    [Fact]
    public void The_marker_tracks_the_live_pick_through_a_repick_and_a_respec()
    {
        var (picks, cursor) = RendPickedWithKey1OnStrike();
        var riposte = picks.Pick(cursor, 1); // Key2 on the same fork replaces the pick

        Assert.Equal("SCREEN talent mark fork=1 cursor=warden.strike picked=riposte marker=1", TalentPanelMarks.ProofLine(riposte, 0, cursor));
        Assert.Equal(new[] { (0, 1) }, TalentPanelMarks.Marks(riposte, cursor).Where(m => m.Picked).Select(m => (m.Fork, m.Option)));

        var respec = riposte.Respec();
        Assert.Equal("SCREEN talent mark fork=1 cursor=warden.strike picked=- marker=0", TalentPanelMarks.ProofLine(respec, 0, cursor));
        Assert.DoesNotContain(TalentPanelMarks.Marks(respec, cursor), m => m.Picked);
    }

    [Fact]
    public void The_fork_header_reads_the_picked_options_name_and_an_unpicked_fork_reads_its_ability_only()
    {
        var (picks, cursor) = RendPickedWithKey1OnStrike();

        Assert.Equal("Strike - picked: Rend", TalentPanelMarks.Header("Strike", picks, cursor));
        Assert.Equal("Guard Break", TalentPanelMarks.Header("Guard Break", picks, ForkOf(picks, "warden.guardBreak")));
    }

    [Fact]
    public void A_cursor_outside_the_forks_marks_no_card_as_cursor_and_names_no_ability()
    {
        var (picks, _) = RendPickedWithKey1OnStrike();

        Assert.DoesNotContain(TalentPanelMarks.Marks(picks, -1), m => m.Cursor);
        Assert.Equal("SCREEN talent mark fork=1 cursor=- picked=rend marker=1", TalentPanelMarks.ProofLine(picks, 0, -1));
    }

    [Theory]
    [InlineData("warden")]
    [InlineData("chanter")]
    [InlineData("cinder")]
    public void At_the_design_viewport_every_picked_tag_sits_inside_its_card_above_the_description_and_right_of_the_name(string classId)
    {
        var counts = Reg.Classes[classId].Talents.Select(f => f.Options.Count).ToList();
        foreach (var card in TalentCardLayout.Cards(DesignWidth, DesignHeight, counts))
        {
            var r = card.Rect;
            var tag = TalentPanelMarks.Tag(r);
            Assert.True(tag.X >= r.X && tag.Right <= r.Right && tag.Y >= r.Y && tag.Bottom <= r.Bottom, $"tag {tag} leaves card {r}");
            // The first description line's glyph tops sit about a font size above its baseline (TextTop, font 12).
            Assert.True(tag.Bottom <= r.Y + TalentCardLayout.TextTop - 12, $"tag {tag} runs into the description of card {r}");
            // The option name starts at TextInset; leave it at least NameRoom pixels before the tag.
            Assert.True(tag.X - (r.X + TalentCardLayout.TextInset) >= TalentPanelMarks.NameRoom, $"tag {tag} crowds the option name in card {r}");
        }
    }
}
