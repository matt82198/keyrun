using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;

namespace Keyrun.Hud;

/// <summary>
/// The Screen's talent panel (BIBLE s6 "Talents: forks on the five": one fork of two or three mutually exclusive
/// options per rotational ability, and "Respec is free on the screen, always. The screen is a build lab"). Opened
/// with T on the between-runs <see cref="Screen"/> (out of combat by construction), it lays one row per fork over the
/// Character and Key bands: the ability's name, then a card per option with its name and what it changes. The cursor's
/// fork row reads green; a pick carries a gold PICKED tag with a check and a thick gold border, and its row header names
/// it (<see cref="TalentPanelMarks"/>, f-0a21646d). Up/Down select a fork, 1-3 (or a click) pick a side, Backspace (or the button) respecs for free.
/// The pick state is the pure <see cref="TalentPicks"/>, held by the owning <see cref="Screen"/>; this control only
/// draws it and maps clicks. The fork and option text come read-only from the class's content data.
/// </summary>
public partial class TalentPanel : Control
{
    /// <summary>The panel's node name under the Screen.</summary>
    public const string NodeName = "TalentPanel";

    /// <summary>Option description font size.</summary>
    public const int TextFont = 12;

    /// <summary>The line under the title. Since lane TALENT-PICKS the picks reach the next queued run.</summary>
    public const string Caption = "Pick one side of each fork. Respec is free. Picks apply to the next run you queue.";

    private const int TitleFont = 16;
    /// <summary>Option name and fork header font size.</summary>
    public const int NameFont = 14;
    private const float Inset = TalentCardLayout.Inset;
    private const float Header = TalentCardLayout.Header;
    private const float Footer = TalentCardLayout.Footer;
    private const float TextInset = TalentCardLayout.TextInset;
    private const float TextTop = TalentCardLayout.TextTop;
    private const float TextLineHeight = TalentCardLayout.TextLineHeight;

    private static readonly Color Gold = new(1f, 0.85f, 0.5f);
    private static readonly Color Calm = new(0.7f, 0.85f, 1f);
    // The cursor's green (the fork row under Up/Down) and the pick's gold (f-0a21646d: never one channel for both).
    private static readonly Color Cursor = new(0.5f, 1f, 0.6f);
    private static readonly Color PickGold = new(1f, 0.78f, 0.25f);
    private static readonly Color TagInk = new(0.1f, 0.07f, 0.03f);

    private Screen? OwningScreen => GetParent() as Screen;

    /// <summary>One option card in panel-local coordinates.</summary>
    /// <param name="Fork">Fork index.</param>
    /// <param name="Option">Option index.</param>
    /// <param name="Rect">The card.</param>
    public readonly record struct Card(int Fork, int Option, Rect2 Rect);

    /// <summary>
    /// The fork rows and option cards for a panel of <paramref name="size"/> (pure; draw, clicks and fit proof share it),
    /// from <see cref="TalentCardLayout"/>: each fork's cards share the whole row.
    /// </summary>
    /// <param name="size">The panel size.</param>
    /// <param name="picks">The build.</param>
    /// <returns>The cards, fork-major.</returns>
    public static IReadOnlyList<Card> Cards(Vector2 size, TalentPicks picks)
    {
        ArgumentNullException.ThrowIfNull(picks);
        var counts = new List<int>(picks.ForkCount);
        for (var f = 0; f < picks.ForkCount; f++)
        {
            counts.Add(picks.Forks[f].Options.Count);
        }
        var cards = new List<Card>();
        foreach (var c in TalentCardLayout.Cards((int)size.X, (int)size.Y, counts))
        {
            cards.Add(new Card(c.Fork, c.Option, new Rect2(c.Rect.X, c.Rect.Y, c.Rect.Width, c.Rect.Height)));
        }
        return cards;
    }

    /// <summary>The respec button in panel-local coordinates.</summary>
    /// <param name="size">The panel size.</param>
    /// <returns>The button.</returns>
    public static Rect2 RespecButton(Vector2 size) => new(Inset, size.Y - Footer + 8, 200, 26);

    /// <summary>The width an option description may use inside a card of <paramref name="cardWidth"/>.</summary>
    /// <param name="cardWidth">The card width.</param>
    /// <returns>The text bound.</returns>
    public static float TextWidthFor(float cardWidth) => Math.Max(0f, cardWidth - (2 * TextInset));

    /// <summary>The most description lines a card of <paramref name="cardHeight"/> holds (at least 1).</summary>
    /// <param name="cardHeight">The card height.</param>
    /// <returns>The line cap.</returns>
    public static int MaxLinesFor(float cardHeight) => TalentCardLayout.MaxLinesFor((int)cardHeight);

    /// <summary>The bottom of the last description line inside a card, card-local.</summary>
    /// <param name="lines">Lines drawn.</param>
    /// <returns>The baseline of the last line plus a descender.</returns>
    public static float TextBottom(int lines) => TalentCardLayout.TextBottom(lines);

    /// <summary>An option description wrapped to its card (the draw and the Screen's fit proof share it).</summary>
    /// <param name="text">The description.</param>
    /// <param name="card">The card.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> TextLines(string text, Rect2 card) =>
        Screen.Wrap(text, TextWidthFor(card.Size.X), TextFont, MaxLinesFor(card.Size.Y));

    /// <inheritdoc />
    public override void _Ready()
    {
        Name = NodeName;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var open = OwningScreen is { IsOpen: true, TalentsOpen: true };
        Visible = open;
        MouseFilter = open ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        if (open)
        {
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (OwningScreen is not { IsOpen: true, TalentsOpen: true } screen || screen.Talents is not { } picks ||
            @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }
        if (RespecButton(Size).HasPoint(click.Position))
        {
            screen.RespecTalents();
        }
        else
        {
            foreach (var card in Cards(Size, picks))
            {
                if (card.Rect.HasPoint(click.Position))
                {
                    screen.PickTalent(card.Fork, card.Option);
                    break;
                }
            }
        }
        AcceptEvent();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (OwningScreen is not { IsOpen: true, TalentsOpen: true } screen || screen.Talents is not { } picks)
        {
            return;
        }
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.085f, 0.072f, 0.06f, 0.99f));
        DrawRect(new Rect2(Vector2.Zero, Size), HudNodes.Edge, filled: false, width: 1f);
        HudNodes.Text(this, new Vector2(Inset, 24), FormattableString.Invariant($"Talents: {screen.TalentClassName()}  ({picks.Count}/{picks.ForkCount} picked)"),
            TitleFont, Gold);
        HudNodes.Text(this, new Vector2(Inset, 46), Caption, TextFont, Calm);

        // f-0a21646d: the cursor (green row tint, green row border, green name) and the pick (gold PICKED tag with a
        // drawn check, thick gold border, the header naming the pick) never share a channel; both read from the live
        // build every frame through TalentPanelMarks.
        var row = RowHeight(Size, picks.ForkCount);
        for (var f = 0; f < picks.ForkCount; f++)
        {
            var y = Header + (f * row);
            var selected = f == screen.TalentFork;
            if (selected)
            {
                var band = new Rect2(4, y + 2, Size.X - 8, row - 4);
                DrawRect(band, new Color(0.5f, 1f, 0.6f, 0.07f));
                DrawRect(band, Cursor, filled: false, width: 2f);
            }
            var header = TalentPanelMarks.Header(screen.TalentAbilityName(picks.Forks[f].AbilityId), picks, f);
            HudNodes.Text(this, new Vector2(Inset, y + 18), header, NameFont, selected ? Cursor : Colors.White);
        }
        var marks = TalentPanelMarks.Marks(picks, screen.TalentFork);
        var cards = Cards(Size, picks);
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var isPicked = marks[i].Picked;
            var option = picks.Forks[card.Fork].Options[card.Option];
            DrawRect(card.Rect, new Color(0.12f, 0.1f, 0.08f));
            DrawRect(card.Rect, isPicked ? PickGold : HudNodes.Edge, filled: false, width: isPicked ? TalentPanelMarks.PickedBorder : 1f);
            if (isPicked)
            {
                DrawPickedTag(card.Rect);
            }
            HudNodes.Text(this, card.Rect.Position + new Vector2(TextInset, 20),
                FormattableString.Invariant($"{card.Option + 1}  {option.Name}"), NameFont, isPicked ? PickGold : new Color(1f, 0.9f, 0.55f));
            var lines = TextLines(option.Text, card.Rect);
            for (var j = 0; j < lines.Count; j++)
            {
                HudNodes.Text(this, card.Rect.Position + new Vector2(TextInset, TextTop + (j * TextLineHeight)), lines[j], TextFont, Colors.White);
            }
        }
        var button = RespecButton(Size);
        DrawRect(button, new Color(0.18f, 0.2f, 0.26f));
        DrawRect(button, Gold, filled: false, width: 1f);
        HudNodes.Text(this, button.Position + new Vector2(10, 18), "Respec (free)  Backspace", TextFont, Colors.White);
        HudNodes.Text(this, new Vector2(button.End.X + 16, button.Position.Y + 18), "Up/Down fork    1-3 pick    T close", TextFont, Calm);
    }

    /// <summary>The PICKED tag of a card (panel-local), from <see cref="TalentPanelMarks.Tag"/>.</summary>
    /// <param name="card">The card.</param>
    /// <returns>The tag.</returns>
    public static Rect2 TagFor(Rect2 card)
    {
        var t = TalentPanelMarks.Tag(new PixelRect((int)card.Position.X, (int)card.Position.Y, (int)card.Size.X, (int)card.Size.Y));
        return new Rect2(t.X, t.Y, t.Width, t.Height);
    }

    // The pick marker: a filled gold tag with a dark drawn check (a shape, not a font glyph) and the word PICKED.
    private void DrawPickedTag(Rect2 card)
    {
        var tag = TagFor(card);
        DrawRect(tag, PickGold);
        var p = tag.Position;
        DrawPolyline(new[] { p + new Vector2(4, 8), p + new Vector2(8, 12), p + new Vector2(15, 4) }, TagInk, 2.5f, antialiased: true);
        HudNodes.Text(this, p + new Vector2(19, 12), "PICKED", 10, TagInk);
    }

    private static float RowHeight(Vector2 size, int forks) => TalentCardLayout.RowHeight((int)size.Y, forks);
}
