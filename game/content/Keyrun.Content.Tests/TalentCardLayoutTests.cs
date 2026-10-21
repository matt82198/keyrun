using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The Screen's talent panel (BIBLE s6 "Talents: forks on the five") lays one card per option. PR #445 (ruling
/// R-445-CI): on the design viewport the Warden's two-option Reckoning fork drew Overwhelm's description in a card one
/// third of the row wide, so it wrapped past the three lines a card holds and was clipped with "..." while the third
/// slot stood empty. Each fork's cards now share the whole row. <see cref="TalentCardLayout"/> is the pure geometry the
/// Godot TalentPanel draws, clicks and fit-proves from.
/// </summary>
public sealed class TalentCardLayoutTests
{
    // Screen.LayoutFor at the 1280x720 design viewport: Margin 32, top 62, height 720 - 62 - 44.
    private const int DesignWidth = 1216;
    private const int DesignHeight = 614;

    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static IReadOnlyList<int> OptionCounts(string classId) =>
        Reg.Classes[classId].Talents.Select(f => f.Options.Count).ToList();

    private static int RowInner(int width) => width - (2 * TalentCardLayout.Inset);

    [Fact]
    public void A_two_option_fork_splits_the_whole_row_between_its_two_cards()
    {
        var cards = TalentCardLayout.Cards(DesignWidth, DesignHeight, new[] { 3, 2 });

        var two = cards.Where(c => c.Fork == 1).Select(c => c.Rect).ToList();
        Assert.Equal(2, two.Count);
        Assert.Equal((RowInner(DesignWidth) - TalentCardLayout.CardGap) / 2, two[0].Width);
        Assert.InRange(two[1].Right, DesignWidth - TalentCardLayout.Inset - 1, DesignWidth - TalentCardLayout.Inset); // the row ends at the right inset
    }

    [Fact]
    public void The_warden_reckoning_fork_gives_overwhelm_a_wider_card_than_a_three_option_fork()
    {
        var talents = Reg.Classes["warden"].Talents;
        var reckoning = talents.Select((f, i) => (f, i)).Single(x => x.f.AbilityId == "warden.reckoning").i;
        var three = talents.Select((f, i) => (f, i)).First(x => x.f.Options.Count == 3).i;
        var overwhelm = talents[reckoning].Options.Select((o, i) => (o, i)).Single(x => x.o.Id == "overwhelm").i;

        var cards = TalentCardLayout.Cards(DesignWidth, DesignHeight, OptionCounts("warden"));
        var wide = cards.Single(c => c.Fork == reckoning && c.Option == overwhelm).Rect;
        var narrow = cards.First(c => c.Fork == three).Rect;

        Assert.True(2 * wide.Width >= 3 * narrow.Width,
            $"overwhelm card {wide.Width} px is not at least 1.5x a three-option card {narrow.Width} px");
    }

    [Theory]
    [InlineData("warden")]
    [InlineData("chanter")]
    [InlineData("cinder")]
    public void At_the_design_viewport_every_card_is_inside_the_panel_disjoint_and_holds_three_lines(string classId)
    {
        var counts = OptionCounts(classId);
        Assert.NotEmpty(counts);
        var cards = TalentCardLayout.Cards(DesignWidth, DesignHeight, counts);

        Assert.Equal(counts.Sum(), cards.Count);
        foreach (var c in cards)
        {
            var r = c.Rect;
            Assert.True(r.X >= TalentCardLayout.Inset && r.Right <= DesignWidth - TalentCardLayout.Inset,
                $"fork {c.Fork} option {c.Option} {r} leaves the {DesignWidth} px panel");
            Assert.True(r.Y >= TalentCardLayout.Header && r.Bottom <= DesignHeight - TalentCardLayout.Footer,
                $"fork {c.Fork} option {c.Option} {r} runs into the header or the respec footer");
            Assert.True(TalentCardLayout.MaxLinesFor(r.Height) >= 3, $"card {r} holds fewer than three description lines");
            Assert.True(TalentCardLayout.TextBottom(3) <= r.Height, $"a three-line description runs past card {r}");
        }
        for (var i = 0; i < cards.Count; i++)
        {
            for (var j = i + 1; j < cards.Count; j++)
            {
                Assert.False(cards[i].Rect.Intersects(cards[j].Rect), $"card {cards[i]} overlaps card {cards[j]}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_fork_of_n_options_fills_its_row_from_inset_to_inset(int options)
    {
        var row = TalentCardLayout.Cards(DesignWidth, DesignHeight, new[] { options }).Select(c => c.Rect).ToList();

        Assert.Equal(options, row.Count);
        Assert.Equal(TalentCardLayout.Inset, row[0].X);
        Assert.InRange(DesignWidth - TalentCardLayout.Inset - row[^1].Right, 0, options - 1); // integer split remainder only
        for (var o = 1; o < row.Count; o++)
        {
            Assert.Equal(row[o - 1].Right + TalentCardLayout.CardGap, row[o].X);
            Assert.Equal(row[0].Width, row[o].Width);
        }
    }
}
