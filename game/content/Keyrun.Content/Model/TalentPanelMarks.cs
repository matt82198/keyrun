using System;
using System.Collections.Generic;
using System.Globalization;

namespace Keyrun.Content;

/// <summary>What one talent option card shows: whether it sits on the cursor's fork, and whether it is the pick.</summary>
/// <param name="Fork">Fork index.</param>
/// <param name="Option">Option index within the fork.</param>
/// <param name="Cursor">The card is on the fork row under the cursor (Up/Down): drawn with the green row read.</param>
/// <param name="Picked">The card is its fork's picked option: drawn with the gold PICKED tag, check and thick gold border.</param>
public readonly record struct TalentCardMark(int Fork, int Option, bool Cursor, bool Picked);

/// <summary>
/// The talent panel's two reads, kept on separate channels (finding f-0a21646d, D15): the cursor is the green fork-row
/// read (row tint, green row border, green ability name); a pick is a shape-plus-colour marker on the card (a gold
/// "PICKED" tag with a drawn check, and a thicker gold border), never colour alone (BIBLE s3, research/06), and the fork
/// row's header names the picked option. A card that is both shows both. Pure: every call reads the live
/// <see cref="TalentPicks"/> it is given, so the marker can never show a cached pick.
/// </summary>
public static class TalentPanelMarks
{
    /// <summary>Width of the PICKED tag.</summary>
    public const int TagWidth = 72;

    /// <summary>Height of the PICKED tag.</summary>
    public const int TagHeight = 16;

    /// <summary>Gap between the tag and the card's top and right edges.</summary>
    public const int TagInset = 5;

    /// <summary>Border width of a picked card (an unpicked card keeps its 1 px edge).</summary>
    public const int PickedBorder = 3;

    /// <summary>Pixels the option name keeps free left of the tag (from the name's TextInset start).</summary>
    public const int NameRoom = 160;

    /// <summary>The per-card marks for the build <paramref name="picks"/> with the cursor on fork <paramref name="cursorFork"/>.</summary>
    /// <param name="picks">The live build.</param>
    /// <param name="cursorFork">The cursor's fork index (out of range marks no card as cursor).</param>
    /// <returns>One mark per option card, fork-major (the order of <see cref="TalentCardLayout.Cards"/>).</returns>
    public static IReadOnlyList<TalentCardMark> Marks(TalentPicks picks, int cursorFork)
    {
        ArgumentNullException.ThrowIfNull(picks);
        var marks = new List<TalentCardMark>();
        for (var f = 0; f < picks.ForkCount; f++)
        {
            var picked = picks.PickedIndex(f);
            for (var o = 0; o < picks.Forks[f].Options.Count; o++)
            {
                marks.Add(new TalentCardMark(f, o, f == cursorFork, o == picked));
            }
        }
        return marks;
    }

    /// <summary>The fork row's header: the ability name, then the picked option's name when the fork has a pick.</summary>
    /// <param name="abilityName">The forked ability's display name.</param>
    /// <param name="picks">The live build.</param>
    /// <param name="fork">The fork index.</param>
    /// <returns>"Strike - picked: Rend", or "Strike" when unpicked.</returns>
    public static string Header(string abilityName, TalentPicks picks, int fork)
    {
        ArgumentNullException.ThrowIfNull(picks);
        var i = picks.PickedIndex(fork);
        return i < 0 ? abilityName : abilityName + " - picked: " + picks.Forks[fork].Options[i].Name;
    }

    /// <summary>The PICKED tag in the top-right corner of <paramref name="card"/> (same coordinate space as the card).</summary>
    /// <param name="card">The card.</param>
    /// <returns>The tag rectangle.</returns>
    public static PixelRect Tag(PixelRect card) =>
        new(card.Right - TagInset - TagWidth, card.Y + TagInset, TagWidth, TagHeight);

    /// <summary>
    /// The proof line for fork <paramref name="fork"/>: the cursor's ability, the fork's picked option, and how many of
    /// the fork's cards draw the picked marker (from <see cref="Marks"/>, the draw's own state).
    /// </summary>
    /// <param name="picks">The live build.</param>
    /// <param name="fork">The fork index.</param>
    /// <param name="cursorFork">The cursor's fork index.</param>
    /// <returns>"SCREEN talent mark fork=1 cursor=warden.strike picked=rend marker=1".</returns>
    public static string ProofLine(TalentPicks picks, int fork, int cursorFork)
    {
        ArgumentNullException.ThrowIfNull(picks);
        var cursor = cursorFork >= 0 && cursorFork < picks.ForkCount ? picks.Forks[cursorFork].AbilityId : "-";
        var marker = 0;
        foreach (var m in Marks(picks, cursorFork))
        {
            if (m.Fork == fork && m.Picked) marker++;
        }
        return string.Format(CultureInfo.InvariantCulture, "SCREEN talent mark fork={0} cursor={1} picked={2} marker={3}",
            fork + 1, cursor, picks.PickedOption(fork) ?? "-", marker);
    }
}
