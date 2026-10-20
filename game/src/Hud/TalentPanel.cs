using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;

namespace Keyrun.Hud;

/// <summary>
/// The Screen's talent panel (BIBLE s6 "Talents: forks on the five": one fork of two or three mutually exclusive
/// options per rotational ability, and "Respec is free on the screen, always. The screen is a build lab"). Opened
/// with T on the between-runs <see cref="Screen"/> (out of combat by construction), it lays one row per fork over the
/// Character and Key bands: the ability's name, then a card per option with its name and what it changes, the pick
/// outlined. Up/Down select a fork, 1-3 (or a click) pick a side, Backspace (or the button) respecs for free.
/// The pick state is the pure <see cref="TalentPicks"/>, held by the owning <see cref="Screen"/>; this control only
/// draws it and maps clicks. The fork and option text come read-only from the class's content data.
/// </summary>
public partial class TalentPanel : Control
{
    /// <summary>The panel's node name under the Screen.</summary>
    public const string NodeName = "TalentPanel";

    /// <summary>Option description font size.</summary>
    public const int TextFont = 12;

    /// <summary>The line under the title; says plainly that picks do not reach a run yet (see the PR's missing hook).</summary>
    public const string Caption = "Pick one side of each fork. Respec is free. Picks are not applied to runs yet.";

    private const int TitleFont = 16;
    private const int NameFont = 14;
    private const float Inset = 14;
    private const float Header = 60;
    private const float Footer = 42;
    private const float CardGap = 8;
    private const float MaxRow = 104;
    private const float RowLabel = 24;
    private const float TextInset = 10;
    private const float TextTop = 36;
    private const float TextLineHeight = 14;

    private static readonly Color Gold = new(1f, 0.85f, 0.5f);
    private static readonly Color Calm = new(0.7f, 0.85f, 1f);
    private static readonly Color Picked = new(0.5f, 1f, 0.6f);

    private Screen? OwningScreen => GetParent() as Screen;

    /// <summary>One option card in panel-local coordinates.</summary>
    /// <param name="Fork">Fork index.</param>
    /// <param name="Option">Option index.</param>
    /// <param name="Rect">The card.</param>
    public readonly record struct Card(int Fork, int Option, Rect2 Rect);

    /// <summary>The fork rows and option cards for a panel of <paramref name="size"/> (pure; draw, clicks and fit proof share it).</summary>
    /// <param name="size">The panel size.</param>
    /// <param name="picks">The build.</param>
    /// <returns>The cards, fork-major.</returns>
    public static IReadOnlyList<Card> Cards(Vector2 size, TalentPicks picks)
    {
        ArgumentNullException.ThrowIfNull(picks);
        var cards = new List<Card>();
        var row = RowHeight(size, picks.ForkCount);
        var width = CardWidth(size);
        for (var f = 0; f < picks.ForkCount; f++)
        {
            var y = Header + (f * row) + RowLabel;
            for (var o = 0; o < picks.Forks[f].Options.Count; o++)
            {
                cards.Add(new Card(f, o, new Rect2(Inset + (o * (width + CardGap)), y, width, row - RowLabel - 6)));
            }
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
    public static int MaxLinesFor(float cardHeight) => Math.Max(1, (int)((cardHeight - TextTop + TextLineHeight - 4) / TextLineHeight));

    /// <summary>The bottom of the last description line inside a card, card-local.</summary>
    /// <param name="lines">Lines drawn.</param>
    /// <returns>The baseline of the last line plus a descender.</returns>
    public static float TextBottom(int lines) => TextTop + ((lines - 1) * TextLineHeight) + 4;

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
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.07f, 0.08f, 0.1f, 0.99f));
        DrawRect(new Rect2(Vector2.Zero, Size), HudNodes.Edge, filled: false, width: 1f);
        HudNodes.Text(this, new Vector2(Inset, 24), FormattableString.Invariant($"Talents: {screen.TalentClassName()}  ({picks.Count}/{picks.ForkCount} picked)"),
            TitleFont, Gold);
        HudNodes.Text(this, new Vector2(Inset, 46), Caption, TextFont, Calm);

        var row = RowHeight(Size, picks.ForkCount);
        for (var f = 0; f < picks.ForkCount; f++)
        {
            var y = Header + (f * row);
            var selected = f == screen.TalentFork;
            if (selected)
            {
                DrawRect(new Rect2(4, y + 2, Size.X - 8, row - 4), new Color(0.5f, 1f, 0.6f, 0.07f));
            }
            HudNodes.Text(this, new Vector2(Inset, y + 18), screen.TalentAbilityName(picks.Forks[f].AbilityId), NameFont,
                selected ? Picked : Colors.White);
        }
        foreach (var card in Cards(Size, picks))
        {
            var option = picks.Forks[card.Fork].Options[card.Option];
            var isPicked = picks.PickedIndex(card.Fork) == card.Option;
            DrawRect(card.Rect, new Color(0.12f, 0.13f, 0.17f));
            DrawRect(card.Rect, isPicked ? Picked : HudNodes.Edge, filled: false, width: isPicked ? 3f : 1f);
            HudNodes.Text(this, card.Rect.Position + new Vector2(TextInset, 20),
                FormattableString.Invariant($"{card.Option + 1}  {option.Name}"), NameFont, isPicked ? Picked : new Color(1f, 0.9f, 0.55f));
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

    private static float RowHeight(Vector2 size, int forks) =>
        forks <= 0 ? 0 : Math.Min(MaxRow, (size.Y - Header - Footer) / forks);

    private static float CardWidth(Vector2 size) => Math.Max(0f, (size.X - (2 * Inset) - (2 * CardGap)) / 3f);
}
