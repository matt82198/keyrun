using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>One talent option card in panel-local whole pixels.</summary>
/// <param name="Fork">Fork index.</param>
/// <param name="Option">Option index within the fork.</param>
/// <param name="Rect">The card.</param>
public readonly record struct TalentCardRect(int Fork, int Option, PixelRect Rect);

/// <summary>
/// The Screen's talent panel geometry (BIBLE s6 "Talents: forks on the five"), as pure integer pixel math so it can be
/// proven headless. One row per fork: the ability name, then one card per option. The Godot <c>TalentPanel</c> draws,
/// maps clicks and prints its fit proof from these rectangles; it never lays cards out on its own.
/// </summary>
public static class TalentCardLayout
{
    /// <summary>Left and right panel inset.</summary>
    public const int Inset = 14;

    /// <summary>Title and caption band above the first fork row.</summary>
    public const int Header = 60;

    /// <summary>Respec button band below the last fork row.</summary>
    public const int Footer = 42;

    /// <summary>Horizontal gap between two cards of one fork.</summary>
    public const int CardGap = 8;

    /// <summary>Tallest a fork row grows on a tall panel.</summary>
    public const int MaxRow = 104;

    /// <summary>The ability-name strip at the top of a fork row, above its cards.</summary>
    public const int RowLabel = 24;

    /// <summary>Vertical gap between a row's cards and the next row's name strip.</summary>
    public const int RowGap = 6;

    /// <summary>Left inset of the option name and description inside a card.</summary>
    public const int TextInset = 10;

    /// <summary>Baseline of the first description line, card-local.</summary>
    public const int TextTop = 36;

    /// <summary>Baseline-to-baseline step of the description lines.</summary>
    public const int TextLineHeight = 14;

    /// <summary>The height of one fork row on a panel <paramref name="height"/> tall holding <paramref name="forks"/> forks.</summary>
    /// <param name="height">Panel height in pixels.</param>
    /// <param name="forks">Fork count.</param>
    /// <returns>The row height, 0 when there are no forks.</returns>
    public static int RowHeight(int height, int forks) =>
        forks <= 0 ? 0 : Math.Min(MaxRow, Math.Max(0, height - Header - Footer) / forks);

    /// <summary>
    /// The width of each card in a fork of <paramref name="options"/> options on a panel <paramref name="width"/> wide:
    /// the fork's cards share the whole row, so a two-option fork's descriptions wrap into fewer lines instead of
    /// leaving an empty third slot (PR #445: Overwhelm clipped in a one-third card).
    /// </summary>
    /// <param name="width">Panel width in pixels.</param>
    /// <param name="options">The fork's option count (at least 1 counted).</param>
    /// <returns>The card width, at least 0.</returns>
    public static int CardWidth(int width, int options)
    {
        var n = Math.Max(1, options);
        return Math.Max(0, width - (2 * Inset) - ((n - 1) * CardGap)) / n;
    }

    /// <summary>The option cards for a panel of <paramref name="width"/> by <paramref name="height"/>, fork-major.</summary>
    /// <param name="width">Panel width in pixels.</param>
    /// <param name="height">Panel height in pixels.</param>
    /// <param name="optionCounts">Each fork's option count, in fork order.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<TalentCardRect> Cards(int width, int height, IReadOnlyList<int> optionCounts)
    {
        ArgumentNullException.ThrowIfNull(optionCounts);
        var cards = new List<TalentCardRect>();
        var row = RowHeight(height, optionCounts.Count);
        var cardHeight = Math.Max(0, row - RowLabel - RowGap);
        for (var f = 0; f < optionCounts.Count; f++)
        {
            var y = Header + (f * row) + RowLabel;
            var cardWidth = CardWidth(width, optionCounts[f]);
            for (var o = 0; o < optionCounts[f]; o++)
            {
                cards.Add(new TalentCardRect(f, o, new PixelRect(Inset + (o * (cardWidth + CardGap)), y, cardWidth, cardHeight)));
            }
        }
        return cards;
    }

    /// <summary>The most description lines a card <paramref name="cardHeight"/> tall holds (at least 1).</summary>
    /// <param name="cardHeight">The card height.</param>
    /// <returns>The line cap.</returns>
    public static int MaxLinesFor(int cardHeight) => Math.Max(1, (cardHeight - TextTop + TextLineHeight - 4) / TextLineHeight);

    /// <summary>The bottom of the last of <paramref name="lines"/> description lines, card-local.</summary>
    /// <param name="lines">Lines drawn (at least 1 counted).</param>
    /// <returns>The last baseline plus a descender.</returns>
    public static int TextBottom(int lines) => TextTop + ((Math.Max(1, lines) - 1) * TextLineHeight) + 4;
}
