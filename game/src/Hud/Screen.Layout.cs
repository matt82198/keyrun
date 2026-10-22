using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

public partial class Screen
{
    /// <inheritdoc />
    public override void _Draw()
    {
        if (!IsOpen || _lobby is not { } lobby)
        {
            return;
        }
        var l = Layout();
        DrawRect(new Rect2(Vector2.Zero, Size), LobbyBg);
        DrawTabBar(l, lobby);
        HudNodes.Text(this, new Vector2(Margin, Size.Y - 18), HintFor(lobby.Tab), SmallFont, Dim);
        switch (lobby.Tab)
        {
            case LobbyTab.Character:
                DrawClasses(l);
                DrawSheet(l.Gear);
                DrawKit(l.Kit);
                break;
            case LobbyTab.Gear:
                DrawGear(l.GearFull);
                DrawGearDetail(l.GearDetail);
                break;
            case LobbyTab.Build:
                break; // the TalentPanel child draws the Build tab
            case LobbyTab.Key:
                DrawSummary(l.Summary);
                DrawKey(l.Key, lobby.Key);
                DrawKeyNote(l.Note, lobby.Key);
                break;
            default:
                DrawSummary(l.Summary);
                DrawSeats(l.Key, lobby.Key);
                DrawQueue(l.Queue, lobby.Key);
                break;
        }
        if (_status.Length > 0)
        {
            HudNodes.Text(this, new Vector2(Size.X - Margin - HudNodes.Width(_status, BodyFont), Size.Y - 18), _status, BodyFont, Warn);
        }
    }

    private void SetHelpVisible(bool visible)
    {
        if (GetNodeOrNull<CanvasItem>(HelpPath) is not { } help)
        {
            return;
        }
        help.Visible = visible;
        Proof(FormattableString.Invariant($"SCREEN help visible={(help.Visible ? 1 : 0)}"));
    }

    // The Queue caption wrapped to the Queue box's width, so it never runs past the window edge.
    private static IReadOnlyList<string> CaptionLines(Rect2 queue) => Wrap(QueueCaption, queue.Size.X, SmallFont, 3);

    /// <summary>
    /// Greedy word wrap of <paramref name="text"/> into lines no wider than <paramref name="width"/> at font
    /// <paramref name="size"/>. A word wider than the bound is clipped with "...", and text past
    /// <paramref name="maxLines"/> is clipped into the last line with "...". The wording itself never changes.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The bound in pixels.</param>
    /// <param name="size">Font size.</param>
    /// <param name="maxLines">The most lines to return (at least 1).</param>
    /// <returns>The lines, empty for empty text.</returns>
    public static IReadOnlyList<string> Wrap(string text, float width, int size, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || HudNodes.Width(candidate, size) <= width)
            {
                current = candidate;
                continue;
            }
            lines.Add(current);
            current = word;
        }
        if (current.Length > 0)
        {
            lines.Add(current);
        }
        var cap = Math.Max(1, maxLines);
        if (lines.Count > cap)
        {
            lines[cap - 1] = string.Join(' ', lines.Skip(cap - 1));
            lines.RemoveRange(cap, lines.Count - cap);
        }
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i] = Clip(lines[i], width, size);
        }
        return lines;
    }

    // The rects of every tab at a screen of `size`: the tab bar, then per tab its panels. Only the open tab's panels
    // are drawn; the talent panel is a child control placed here and shown on the Build tab only. The Queue tab reuses
    // the Key tab's left and middle columns (summary, seats) and puts the Queue box in the right one.
    private readonly record struct ScreenLayout(
        IReadOnlyList<Rect2> Tabs,
        Rect2 Classes,
        IReadOnlyList<Rect2> ClassCards,
        Rect2 Gear,
        Rect2 Kit,
        Rect2 GearFull,
        Rect2 GearDetail,
        Rect2 Summary,
        Rect2 Key,
        Rect2 Note,
        Rect2 Queue,
        Rect2 KeyDown,
        Rect2 KeyUp,
        Rect2 Talents);

    private ScreenLayout Layout()
    {
        var l = LayoutFor(Size);
        if (GetNodeOrNull<TalentPanel>(TalentPanel.NodeName) is { } talents)
        {
            talents.Position = l.Talents.Position;
            talents.Size = l.Talents.Size;
        }
        return l;
    }

    // The Clause row's text width on the Key band: the band less the 14px inset each side.
    private static float ClauseTextWidth(Rect2 key) => Math.Max(0f, key.Size.X - 28);

    // The layout for a screen of `size` (pure): the draw uses the node's Size, the fit proof lines the design size. The
    // tab bar sits in the title row, so every tab's panels start at the same top as the old three bands did, and each
    // panel is at least as wide as the band it replaced (the Clause cards, the Queue caption and the talent cards wrap
    // into no more lines than before).
    private static ScreenLayout LayoutFor(Vector2 size)
    {
        var top = Margin + 30;
        var height = size.Y - top - 44;
        var width = size.X - (2 * Margin);
        var tabs = Enumerable.Range(0, Lobby.Tabs.Count)
            .Select(i => new Rect2(TabsLeft + (i * (TabWidth + 6)), 12, TabWidth, top - 18))
            .ToList();

        // Key: summary | key band | a note on what Queue does. Queue: summary | the seats | the Queue box.
        var summary = new Rect2(Margin, top, width * 0.28f, height);
        var key = new Rect2(summary.End.X + 16, top, width * 0.40f, height);
        var note = new Rect2(key.End.X + 16, top, size.X - Margin - key.End.X - 16, height);
        var queue = new Rect2(key.End.X + 16, top, size.X - Margin - key.End.X - 16, 120);
        var keyDown = new Rect2(key.Position.X + 14, key.Position.Y + 44, 40, 40);
        var keyUp = new Rect2(key.Position.X + 150, key.Position.Y + 44, 40, 40);

        // Character: class cards | the character sheet (key, Ascension, the five slots) | the class kit.
        var classes = new Rect2(Margin, top, width * 0.28f, height);
        var cards = Enumerable.Range(0, 3)
            .Select(i => new Rect2(classes.Position.X + 12, classes.Position.Y + 40 + (i * 92), classes.Size.X - 24, 84))
            .ToList();
        var gear = new Rect2(classes.End.X + 16, top, width * 0.42f, height);
        var kit = new Rect2(gear.End.X + 16, top, size.X - Margin - gear.End.X - 16, height);

        // Gear: the five slots in detail | the selected item.
        var gearFull = new Rect2(Margin, top, width * 0.58f, height);
        var gearDetail = new Rect2(gearFull.End.X + 16, top, size.X - Margin - gearFull.End.X - 16, height);

        // Build: the talent panel over the whole content area.
        var talents = new Rect2(Margin, top, width, height);
        return new ScreenLayout(tabs, classes, cards, gear, kit, gearFull, gearDetail, summary, key, note, queue, keyDown, keyUp, talents);
    }

    // project.godot display/window/size/viewport_width and _height (1280x720).
    private static Vector2 DesignSize() => new(
        ProjectSettings.GetSetting("display/window/size/viewport_width", 1280).AsInt32(),
        ProjectSettings.GetSetting("display/window/size/viewport_height", 720).AsInt32());

    // A dark lobby panel: near-black fill, a thin bronze edge, a short gold rule under the small-caps title.
    private void Band(Rect2 r, string title)
    {
        DrawRect(r, PanelBg);
        DrawRect(r, Bronze, filled: false, width: 1f);
        HudNodes.Text(this, r.Position + new Vector2(14, 24), title.ToUpperInvariant(), 15, Gold);
        DrawRect(new Rect2(r.Position.X + 14, r.Position.Y + 31, Math.Min(120, r.Size.X - 28), 1), BronzeDim);
    }

    // The title and the five tabs in the title row (the Dark and Darker lobby shape: one bar of tabs over one screen),
    // the active tab lit with a gold rule under it, and the character's Ash and private rating on the right.
    private void DrawTabBar(ScreenLayout l, Lobby lobby)
    {
        DrawRect(new Rect2(0, 0, Size.X, l.Tabs[0].End.Y + 4), BarBg);
        DrawRect(new Rect2(0, l.Tabs[0].End.Y + 4, Size.X, 1), Bronze);
        HudNodes.Text(this, new Vector2(Margin, Margin + 4), "KEYRUN", TitleFont, Gold);
        for (var i = 0; i < Lobby.Tabs.Count; i++)
        {
            var r = l.Tabs[i];
            var tab = Lobby.Tabs[i];
            var on = tab == lobby.Tab;
            DrawRect(r, on ? TabOn : TabOff);
            if (on)
            {
                DrawRect(new Rect2(r.Position.X, r.End.Y - 3, r.Size.X, 3), Gold);
            }
            var label = TabLabel(tab);
            var x = r.Position.X + ((r.Size.X - HudNodes.Width(label, 16)) / 2);
            HudNodes.Text(this, new Vector2(x, r.Position.Y + 26), label, 16, on ? Gold : Dim);
        }
        var ash = FormattableString.Invariant($"Ash {_progression.AshTotal}");
        var rating = _rating.ScreenText();
        var right = Size.X - Margin;
        HudNodes.Text(this, new Vector2(right - HudNodes.Width(ash, BodyFont), Margin - 2), ash, BodyFont, Gold);
        HudNodes.Text(this, new Vector2(right - HudNodes.Width(rating, SmallFont), Margin + 14), rating, SmallFont, Dim);
    }

    private static string TabLabel(LobbyTab tab) => tab switch
    {
        LobbyTab.Character => "CHARACTER",
        LobbyTab.Gear => "GEAR",
        LobbyTab.Build => "BUILD",
        LobbyTab.Key => "KEY",
        _ => "QUEUE",
    };

    private static string HintFor(LobbyTab tab) => tab switch
    {
        LobbyTab.Character => "Q/E or F1-F5 tabs    Up/Down class    T talents    Enter queue",
        LobbyTab.Gear => "Q/E tabs    Up/Down or Tab item    R reroll Facet    Enter queue",
        LobbyTab.Build => "Q/E tabs    Up/Down fork    1-3 pick    Backspace respec    T back    Enter queue",
        LobbyTab.Key => "Q/E tabs    Left/Right key    Enter queue",
        _ => "Q/E tabs    Enter or click Queue",
    };

    private static string Clip(string text, float width, int size)
    {
        if (HudNodes.Width(text, size) <= width)
        {
            return text;
        }
        var s = text;
        while (s.Length > 1 && HudNodes.Width(s + "...", size) > width)
        {
            s = s[..^1];
        }
        return s + "...";
    }
}
