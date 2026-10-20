using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane COMBO-BLAME (BIBLE s4 "when the party fails a Combo, the game shows which half of the pair was late, in the
/// run log, privately"): the run-end panel's private section lists the human's own denied Payoffs from the sim's
/// <c>combo_denied</c> events, one line per (Window, reason), counted and capped, and never another seat's.
/// </summary>
public sealed class ComboBlameTests
{
    private const int Human = 1;
    private const int Bot = 3;

    [Fact]
    public void A_denied_Payoff_by_the_human_yields_one_line_naming_the_Payoff_half()
    {
        var lines = ComboBlame.For(Human, new[] { new DeniedCombo(Human, Human, "sundered", "self_combo") });

        var line = Assert.Single(lines);
        Assert.Equal(Human, line.Seat);
        Assert.Equal("sundered", line.WindowId);
        Assert.Equal(1, line.Count);
        Assert.Equal(ComboHalf.Payoff, line.LateHalf);
        Assert.Contains("your Payoff", line.Text(), System.StringComparison.Ordinal);
        Assert.Equal("HUD runend combo seat=1 window=sundered reason=self_combo half=payoff count=1", line.LogLine());
    }

    [Fact]
    public void A_denied_Payoff_by_a_bot_yields_no_line_for_the_human()
    {
        var lines = ComboBlame.For(Human, new[] { new DeniedCombo(Bot, Bot, "sundered", "self_combo") });

        Assert.Empty(lines);
    }

    [Fact]
    public void A_bot_Payoff_denied_on_the_humans_Setup_is_not_shown_to_the_human()
    {
        // The bot's half failed here; the human's private log never shows another seat's blame.
        var lines = ComboBlame.For(Human, new[] { new DeniedCombo(Bot, Human, "sundered", "self_combo") });

        Assert.Empty(lines);
    }

    [Fact]
    public void Repeats_of_one_denial_dedupe_into_one_counted_line()
    {
        var one = new DeniedCombo(Human, Human, "sundered", "self_combo");

        var line = Assert.Single(ComboBlame.For(Human, new[] { one, one, one }));
        Assert.Equal(3, line.Count);
        Assert.EndsWith("(x3)", line.Text(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_are_capped_most_frequent_first_then_first_seen()
    {
        var denied = new[]
        {
            new DeniedCombo(Human, Human, "a", "self_combo"),
            new DeniedCombo(Human, Human, "b", "self_combo"),
            new DeniedCombo(Human, Human, "c", "self_combo"),
            new DeniedCombo(Human, Human, "d", "self_combo"),
            new DeniedCombo(Human, Human, "d", "self_combo"),
            new DeniedCombo(Human, Human, "e", "self_combo"),
        };

        var lines = ComboBlame.For(Human, denied);

        Assert.Equal(ComboBlame.MaxLines, lines.Count);
        Assert.Equal(new[] { "d", "a", "b" }, lines.Select(l => l.WindowId));
    }

    [Fact]
    public void A_Lock_alternation_denial_says_the_other_class_had_to_close_this_Lock()
    {
        var line = Assert.Single(ComboBlame.For(Human, new[] { new DeniedCombo(Human, 900, "lock", "lock_alternate") }));

        Assert.Equal("lock_alternate", line.Reason);
        Assert.Contains("Lock", line.Text(), System.StringComparison.Ordinal);
        Assert.Contains("your Payoff", line.Text(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Same_window_with_different_reasons_stays_two_lines()
    {
        var lines = ComboBlame.For(Human, new[]
        {
            new DeniedCombo(Human, Human, "sundered", "self_combo"),
            new DeniedCombo(Human, Human, "sundered", "lock_alternate"),
        });

        Assert.Equal(2, lines.Count);
    }
}
