using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;

namespace Keyrun.Hud;

/// <summary>
/// The ability bar's tooltip (Matt 2026-10-22: "can we please get some ability tooltips and cooler names"). A slot shows
/// its tooltip while the mouse hovers it (the mouse is free) or while its key has been held longer than
/// <see cref="AbilityTooltipLayout.HoldMs"/>; a held key wins over the hover, so one tooltip shows at a time. The text is
/// generated from content (<see cref="AbilityTooltip.Build"/>) for the run's own ability (talents and Facets applied,
/// <see cref="RunAbilityFacts.FromSim"/>), and the box sits above the bar, inside the viewport and clear of the party
/// frames and the top bar (<see cref="AbilityTooltipLayout.Place"/>). It hides while the player's own cast bar is up and
/// once the run has ended. Under --autorun or --replay-input it prints one line each time the shown tooltip changes:
/// "HUD tooltip slot=K name=NAME rect=X,Y,W,H viewport=WxH lines=N fits=0|1 by=hold|hover" and "HUD tooltip hidden".
/// </summary>
public partial class AbilityBar
{
    private static readonly Color TipBack = new(0.06f, 0.07f, 0.09f, 0.94f);
    private static readonly Color TipTitle = new(1f, 0.85f, 0.45f, 1f);
    private static readonly Color TipRole = new(0.6f, 0.85f, 1f, 1f);
    private static readonly Color TipStats = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Color TipFlavor = new(0.7f, 0.7f, 0.62f, 1f);

    // The ability-text table, read once against the driver's registry.
    private AbilityTextTable? _abilityText;

    // The key held down on an ability slot (its physical keycode and action) and for how long, seconds.
    private Key _holdKey = Key.None;
    private KeyAction _holdAction;
    private double _holdSeconds;

    // The tooltip drawn this frame: the slot index, its box and its lines (text, font size, colour), or null.
    private (int Slot, PixelRect Box, List<(string Text, int Font, Color Color)> Lines)? _tip;
    private string? _lastTipLine;

    // True once the pointer has moved at least once this session. Until then the cursor position is the engine's
    // default (0,0), not a real hover, so the hover path must not read it (headless runs have no pointer at all).
    private bool _pointerMoved;

    /// <summary>Records that the pointer has moved, which arms the hover path.</summary>
    /// <param name="event">The input event.</param>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion)
        {
            _pointerMoved = true;
        }
    }

    /// <summary>The themed display name of an ability (ability-text.json), else its content name.</summary>
    /// <param name="abilityId">The ability id.</param>
    /// <returns>The name.</returns>
    private string SlotName(string abilityId) =>
        AbilityText()?.NameOr(abilityId, Driver!.AbilityName(abilityId)) ?? Driver!.AbilityName(abilityId);

    private AbilityTextTable? AbilityText()
    {
        if (_abilityText is null && Driver?.Registry is { } registry)
        {
            _abilityText = AbilityTextLoader.Read(EmbeddedContentSource.Instance, registry);
        }
        return _abilityText;
    }

    // Called from _UnhandledInput for every non-echo key event bound to an action: a press on a bar action starts the
    // hold timer, its release (or any other key's press) ends it.
    private void TrackHold(InputEventKey key, KeyAction action)
    {
        if (key.Pressed)
        {
            _holdKey = key.PhysicalKeycode;
            _holdAction = action;
            _holdSeconds = 0;
        }
        else if (key.PhysicalKeycode == _holdKey)
        {
            _holdKey = Key.None;
        }
    }

    private void UpdateTooltip(double delta)
    {
        if (_holdKey != Key.None)
        {
            if (Input.IsPhysicalKeyPressed(_holdKey))
            {
                _holdSeconds += delta;
            }
            else
            {
                _holdKey = Key.None;
            }
        }
        _tip = BuildTip();
        if (Driver?.Options is { } o && (o.Autorun || o.ReplayInput is not null))
        {
            PrintTip();
        }
    }

    private (int, PixelRect, List<(string, int, Color)>)? BuildTip()
    {
        if (Driver?.Setup is not { } setup || Driver.Run is not { } run || Driver.Registry is not { } registry
            || !run.State.Actors.TryGetValue(setup.HumanActorId, out var me) || me.CastId is not null || run.RunEnd.Result is not null)
        {
            return null;
        }
        var vw = (int)Size.X;
        var vh = (int)Size.Y;
        var rects = ActionBarLayout.SlotRects(vw, vh, setup.Slots.Count + 1);
        var index = -1;
        var by = "hover";
        if (_holdKey != Key.None && AbilityTooltipLayout.HoldShows((int)(_holdSeconds * 1000)))
        {
            index = SlotIndex(setup, _holdAction);
            by = "hold";
        }
        if (index < 0 && _pointerMoved && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            var m = GetLocalMousePosition();
            for (var i = 0; i < setup.Slots.Count; i++)
            {
                var r = rects[i];
                if (m.X >= r.X && m.X < r.Right && m.Y >= r.Y && m.Y < r.Bottom)
                {
                    index = i;
                }
            }
        }
        if (index < 0 || AbilityText()?.For(setup.Slots[index].AbilityId) is not { } row
            || !run.Content.Abilities.TryGetValue(setup.Slots[index].AbilityId, out var def))
        {
            return null;
        }
        var text = AbilityTooltip.Build(RunAbilityFacts.FromSim(def), row, registry);
        var font = ThemeDB.FallbackFont;
        var inner = Math.Min(AbilityTooltipLayout.Width, vw - (2 * HudLayout.Margin)) - (2 * AbilityTooltipLayout.Pad);
        var lines = new List<(string, int, Color)> { (text.Title, AbilityTooltipLayout.TitleFont, TipTitle) };
        foreach (var (part, color) in new[] { (text.Role, TipRole), (text.Body, Colors.White), (text.Stats, TipStats), (text.Flavor, TipFlavor) })
        {
            foreach (var l in AbilityTooltipLayout.Wrap(part, inner, s => (int)Math.Ceiling(font.GetStringSize(s, HorizontalAlignment.Left, -1, AbilityTooltipLayout.BodyFont).X)))
            {
                lines.Add((l, AbilityTooltipLayout.BodyFont, color));
            }
        }
        var bar = ActionBarLayout.Footprint(vw, vh, setup.Slots.Count + 1);
        var box = AbilityTooltipLayout.Place(vw, vh, rects[index], bar, AbilityTooltipLayout.Height(lines.Count - 1), Avoid(vw, vh));
        _tipBy = by;
        return (index, box, lines);
    }

    private string _tipBy = "hover";

    private static int SlotIndex(HostRunSetup setup, KeyAction action)
    {
        for (var i = 0; i < setup.Slots.Count; i++)
        {
            if (setup.Slots[i].Action == action)
            {
                return i;
            }
        }
        return -1;
    }

    // The rects the box keeps clear of: the party frames control (read from the scene, Hud/HudRoot/PartyFrames, else its
    // Hud.tscn offsets) and the top bar band with the forces bar under it (objective, timer).
    private List<PixelRect> Avoid(int vw, int vh)
    {
        var party = GetNodeOrNull<Control>("../HudRoot/PartyFrames") is { } p
            ? new PixelRect((int)p.GlobalPosition.X, (int)p.GlobalPosition.Y, (int)p.Size.X, (int)p.Size.Y)
            : new PixelRect(HudLayout.Margin, ActionBarLayout.PartyTop, ActionBarLayout.PartyWidth, 356);
        var top = HudLayout.TopBar(vw, vh);
        var forces = HudLayout.ForcesBar(top);
        var band = top.Band(vw);
        return new List<PixelRect> { party, band with { Height = Math.Max(band.Bottom, forces.Bottom) } };
    }

    private void PrintTip()
    {
        string line;
        if (_tip is not { } t || Driver?.Setup is not { } setup)
        {
            line = "HUD tooltip hidden";
        }
        else
        {
            var vw = (int)Size.X;
            var vh = (int)Size.Y;
            var fits = AbilityTooltipLayout.Fits(t.Box, vw, vh, Avoid(vw, vh)) && TextFits(t);
            line = FormattableString.Invariant(
                $"HUD tooltip slot={setup.Slots[t.Slot].Key} name={t.Lines[0].Text} rect={t.Box.X},{t.Box.Y},{t.Box.Width},{t.Box.Height} viewport={vw}x{vh} lines={t.Lines.Count} fits={(fits ? 1 : 0)} by={_tipBy}");
        }
        if (line == _lastTipLine || (_lastTipLine is null && _tip is null))
        {
            return;
        }
        _lastTipLine = line;
        GD.Print(FormattableString.Invariant($"{line} tick={Driver?.Run?.State.Tick ?? 0}"));
    }

    // Every wrapped line is no wider than the box's inner width and the lines stack inside the box's height.
    private static bool TextFits((int Slot, PixelRect Box, List<(string Text, int Font, Color Color)> Lines) t)
    {
        var font = ThemeDB.FallbackFont;
        var inner = t.Box.Width - (2 * AbilityTooltipLayout.Pad);
        var height = 2 * AbilityTooltipLayout.Pad;
        foreach (var (text, size, _) in t.Lines)
        {
            if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X > inner && text.Contains(' ', StringComparison.Ordinal))
            {
                return false;
            }
            height += size + AbilityTooltipLayout.LineGap;
        }
        return height <= t.Box.Height;
    }

    private void DrawTooltip(Font font)
    {
        if (_tip is not { } t)
        {
            return;
        }
        var box = new Rect2(t.Box.X, t.Box.Y, t.Box.Width, t.Box.Height);
        DrawRect(box, TipBack);
        DrawRect(box, SlotEdge, filled: false, width: 1f);
        var y = box.Position.Y + AbilityTooltipLayout.Pad;
        foreach (var (text, size, color) in t.Lines)
        {
            y += size;
            DrawString(font, new Vector2(box.Position.X + AbilityTooltipLayout.Pad, y), text, HorizontalAlignment.Left, -1, size, color);
            y += AbilityTooltipLayout.LineGap;
        }
    }
}
