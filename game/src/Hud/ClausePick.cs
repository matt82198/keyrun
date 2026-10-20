using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The Key band's Clause pick (BIBLE s8 "the party is offered three modifiers and picks one; exactly one Clause runs"):
/// three cards with the Clause name and what it changes, the pick outlined. Keys 1-3 (or a click) pick through the
/// owning <see cref="Screen"/>, which holds the <see cref="ScreenState"/>; this control only draws it and maps clicks.
/// </summary>
public partial class ClausePick : Control
{
    private const float CardHeight = 64;
    private const float CardGap = 8;
    private const int NameFont = 16;
    private const float TextInset = 10;
    private const float TextLineHeight = 14;

    /// <summary>The Clause description's font size.</summary>
    public const int TextFont = 12;

    /// <summary>The width a Clause description may use inside a card: the card width less the inset on each side.</summary>
    /// <param name="cardWidth">The card's width (the control's width).</param>
    /// <returns>The text bound in pixels.</returns>
    public static float TextWidthFor(float cardWidth) => Math.Max(0f, cardWidth - (2 * TextInset));

    /// <summary>
    /// A Clause description wrapped to <paramref name="width"/> in at most two lines (lane SCREEN-OVERFLOW: the text
    /// stays inside its card at 1280x720). The draw and the Screen's "SCREEN fit clause" proof line share it.
    /// </summary>
    /// <param name="text">The description.</param>
    /// <param name="width">The text bound, <see cref="TextWidthFor"/> of the card width.</param>
    /// <returns>The lines to draw.</returns>
    public static IReadOnlyList<string> TextLines(string text, float width) => Screen.Wrap(text, width, TextFont, 2);

    private static readonly Color Picked = new(0.5f, 1f, 0.6f);

    /// <summary>The screen that owns the state (the parent).</summary>
    private Screen? OwningScreen => GetParent() as Screen;

    /// <inheritdoc />
    public override void _Ready() => MouseFilter = MouseFilterEnum.Pass;

    /// <inheritdoc />
    public override void _Process(double delta) => QueueRedraw();

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (OwningScreen is not { IsOpen: true } screen || screen.State is not { } state ||
            @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }
        var n = (int)(click.Position.Y / (CardHeight + CardGap));
        if (n >= 0 && n < state.Clause.Offers.Count)
        {
            screen.PickClause(n);
            AcceptEvent();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (OwningScreen is not { IsOpen: true } screen || screen.State is not { } state)
        {
            return;
        }
        for (var i = 0; i < state.Clause.Offers.Count; i++)
        {
            var id = state.Clause.Offers[i];
            var (name, text) = screen.ClauseText(id);
            var picked = id == state.Clause.Picked;
            var card = new Rect2(0, i * (CardHeight + CardGap), Size.X, CardHeight);
            DrawRect(card, new Color(0.12f, 0.13f, 0.17f, state.Locked && !picked ? 0.5f : 1f));
            DrawRect(card, picked ? Picked : HudNodes.Edge, filled: false, width: picked ? 3f : 1f);
            HudNodes.Text(this, card.Position + new Vector2(10, 22), FormattableString.Invariant($"{i + 1}  {name}"), NameFont,
                picked ? Picked : new Color(1f, 0.9f, 0.55f));
            var lines = TextLines(text, TextWidthFor(Size.X));
            for (var j = 0; j < lines.Count; j++)
            {
                HudNodes.Text(this, card.Position + new Vector2(TextInset, 44 + (j * TextLineHeight)), lines[j], TextFont, Colors.White);
            }
        }
    }
}
