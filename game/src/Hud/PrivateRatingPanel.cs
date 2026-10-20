using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using SimTick = Keyrun.Sim.Core.Tick;

namespace Keyrun.Hud;

/// <summary>
/// The private Mythic+ rating and the last-3-runs strip (lane PRIVATE-RATING; BIBLE s10 "No exported score", Matt
/// 2026-10-20: "a private WoW-style Mythic+ rating, shown only on the player's own screen. It is never shown to others
/// and never used to filter groups"; s10 "Last-3-runs strip. A collapsed, private strip under the queue state shows the
/// character's last three runs: time, result, and what the clear moved"). A child of the between-runs
/// <see cref="Screen"/> (scenes/Screen.tscn), drawn only while that screen is open, in the right column under the Queue
/// box and its caption. No button, no key: it is information only (s9 "less button clutter").
/// <para>It records every run end from the driver (key, timed with its +1/+2/+3, untimed, or not cleared, the run timer
/// against par, player deaths) into the pure <see cref="PrivateRecord"/> and keeps it with <see cref="PrivateRatingStore"/>
/// in the directory the screen keeps character state in (<see cref="ProgressionSavePlace.Directory"/>). Nothing outside
/// this node and the local file reads the record: no queue, party frame or other seat sees it.</para>
/// <para>Under <c>--autorun</c> it prints "SCREEN rating ..." proof lines. With <c>--rating-shot PATH</c> (a windowed
/// run) it saves one PNG of the screen once the strip has been drawn open.</para>
/// </summary>
public partial class PrivateRatingPanel : Control
{
    /// <summary>The user argument naming the PNG to save once the screen is drawn open.</summary>
    public const string ShotFlag = "--rating-shot";

    /// <summary>The node name under the screen.</summary>
    public const string NodeName = "PrivateRating";

    private const int LabelFont = 12;
    private const int RatingFont = 30;
    private const int RowFont = 12;
    private const float Margin = 32;

    private static readonly Color Gold = new(1f, 0.85f, 0.5f);
    private static readonly Color Calm = new(0.7f, 0.85f, 1f);
    private static readonly Color Muted = new(1f, 1f, 1f, 0.5f);

    private SimDriver? _driver;
    private Screen? _screen;
    private PrivateRatingStore? _store;
    private PrivateRecord _record = PrivateRecord.Empty;
    private bool _recorded;
    private int _deaths;
    private int _openFrames;
    private string? _shotPath;
    private bool _shot;

    /// <summary>The character's private record.</summary>
    public PrivateRecord Record => _record;

    /// <summary>The driver to read run ends from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>
    /// WoW's rating colour bands (grey, green, blue, purple, orange), by the private rating. View only.
    /// </summary>
    /// <param name="rating">The rating.</param>
    /// <returns>The colour.</returns>
    public static Color RatingColor(int rating) => rating switch
    {
        <= 0 => new Color(0.62f, 0.62f, 0.62f),
        < 500 => new Color(1f, 1f, 1f),
        < 1000 => new Color(0.12f, 1f, 0f),
        < 1500 => new Color(0f, 0.44f, 0.87f),
        < 2000 => new Color(0.64f, 0.21f, 0.93f),
        _ => new Color(1f, 0.5f, 0f),
    };

    /// <summary>The strip box for a screen of <paramref name="size"/>: the Screen's right column under the Queue box and
    /// its caption (Screen.LayoutFor: Queue at x = character + key bands, 120 tall; caption up to 3 lines).</summary>
    /// <param name="size">The screen size.</param>
    /// <returns>The box.</returns>
    public static Rect2 BoxFor(Vector2 size)
    {
        var top = Margin + 30;
        var width = size.X - (2 * Margin);
        var keyEnd = Margin + (width * 0.46f) + 16 + (width * 0.32f);
        var x = keyEnd + 16;
        var y = top + 120 + 24 + (3 * (12 + 4)) + 12;
        var bottom = size.Y - 44;
        return new Rect2(x, y, size.X - Margin - x, Math.Max(0, bottom - y));
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _screen = GetParent() as Screen;
        _driver = HudNodes.FindDriver(this, DriverPath);
        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == ShotFlag && args[i + 1].Length > 0)
            {
                _shotPath = args[i + 1];
            }
        }
        var dir = ProgressionSavePlace.Directory(args, DisplayServer.GetName() == "headless", ProjectSettings.GlobalizePath("user://"));
        if (dir is not null)
        {
            _store = new PrivateRatingStore(dir);
            _record = _store.Load();
        }
        if (_driver is not null)
        {
            _driver.Stepped += OnStepped;
            _driver.RunStarted += OnRunStarted;
            Proof(FormattableString.Invariant($"SCREEN rating load store={(_store is null ? 0 : 1)} rating={_record.Rating} recent={_record.Recent.Count}"));
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_driver is not null)
        {
            _driver.Stepped -= OnStepped;
            _driver.RunStarted -= OnRunStarted;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var open = _screen?.IsOpen ?? false;
        _openFrames = open ? _openFrames + 1 : 0;
        QueueRedraw();
        if (open && _openFrames == 4 && _shotPath is { } path && !_shot)
        {
            _shot = true;
            SaveShot(path);
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (_screen is not { IsOpen: true })
        {
            return;
        }
        var box = BoxFor(Size);
        DrawRect(box, HudNodes.Back);
        DrawRect(box, HudNodes.Edge, filled: false, width: 1f);
        var x = box.Position.X + 12;
        var y = box.Position.Y + 22;
        HudNodes.Text(this, new Vector2(x, y), "Mythic+ Rating", 14, Gold);
        y += 34;
        var rating = _record.Rating;
        HudNodes.Text(this, new Vector2(x, y), rating.ToString(System.Globalization.CultureInfo.InvariantCulture), RatingFont, RatingColor(rating));
        y += 18;
        HudNodes.Text(this, new Vector2(x, y), "Private: only you see this", LabelFont, Muted);
        y += 28;
        HudNodes.Text(this, new Vector2(x, y), "Last runs", 14, Gold);
        y += 20;
        if (_record.Recent.Count == 0)
        {
            HudNodes.Text(this, new Vector2(x, y), "No runs yet", RowFont, Muted);
            return;
        }
        foreach (var e in _record.Recent)
        {
            var color = e.Run.Clear switch
            {
                RunClear.Timed => new Color(0.5f, 1f, 0.6f),
                RunClear.Untimed => new Color(1f, 0.75f, 0.45f),
                _ => Muted,
            };
            foreach (var line in Wrap(PrivateRecord.RowText(e, SimTick.TickHz), box.Size.X - 24))
            {
                HudNodes.Text(this, new Vector2(x, y), line, RowFont, color);
                y += RowFont + 4;
            }
            y += 6;
        }
    }

    // Splits a strip row at its double-space fields so it fits the column; a field never breaks.
    private static List<string> Wrap(string row, float width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var field in row.Split("  ", StringSplitOptions.RemoveEmptyEntries))
        {
            var next = current.Length == 0 ? field : current + "  " + field;
            if (current.Length > 0 && HudNodes.Width(next, RowFont) > width)
            {
                lines.Add(current);
                current = field;
            }
            else
            {
                current = next;
            }
        }
        if (current.Length > 0)
        {
            lines.Add(current);
        }
        return lines;
    }

    private void OnRunStarted()
    {
        _recorded = false;
        _deaths = 0;
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_driver?.Run is not { } run || _recorded)
        {
            return;
        }
        foreach (var e in events)
        {
            if (e.Type == "death" && e.Get("actorId") is JsInt id && id.Value is >= int.MinValue and <= int.MaxValue &&
                run.State.Actors.TryGetValue((int)id.Value, out var a) && a.Kind == ActorKind.Player)
            {
                _deaths++;
            }
        }
        if (run.RunEnd.Result is not { } result)
        {
            return;
        }
        _recorded = true;
        var cleared = run.Encounter.PullIndex >= run.Dungeon.Pulls.Count && result != Keyrun.Sim.Encounter.RunResult.Abandoned;
        var clear = !cleared ? RunClear.NotCleared : result == Keyrun.Sim.Encounter.RunResult.Timed ? RunClear.Timed : RunClear.Untimed;
        var rated = new RatedRun(run.Dungeon.Id, run.Encounter.KeyLevel, clear, run.State.RunTimerTicks, run.Encounter.ParTicks, _deaths);
        _record = _record.Add(rated);
        var ok = _store?.Save(_record);
        Proof(FormattableString.Invariant(
            $"SCREEN rating add key={rated.KeyLevel} clear={clear} upgrade={rated.Upgrade} timer={rated.ClearTicks} par={rated.ParTicks} deaths={rated.Deaths} score={rated.Score} gain={_record.Recent[0].Gain} rating={_record.Rating} recent={_record.Recent.Count} saved={(ok is null ? "-" : ok.Value ? "1" : "0")}"));
        foreach (var e in _record.Recent)
        {
            var lines = Wrap(PrivateRecord.RowText(e, SimTick.TickHz), BoxFor(DesignSize()).Size.X - 24);
            var widest = lines.Max(l => HudNodes.Width(l, RowFont));
            Proof(FormattableString.Invariant(
                $"SCREEN rating row \"{PrivateRecord.RowText(e, SimTick.TickHz)}\" lines={lines.Count} widest={Math.Ceiling(widest)} box={Math.Floor(BoxFor(DesignSize()).Size.X - 24)}"));
        }
    }

    private static Vector2 DesignSize() => new(
        ProjectSettings.GetSetting("display/window/size/viewport_width", 1280).AsInt32(),
        ProjectSettings.GetSetting("display/window/size/viewport_height", 720).AsInt32());

    private async void SaveShot(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var err = GetViewport().GetTexture().GetImage().SavePng(path);
        Proof(FormattableString.Invariant($"SCREEN rating shot path=\"{path}\" ok={(err == Error.Ok ? 1 : 0)}"));
    }

    private void Proof(string line)
    {
        if (_driver is not null)
        {
            HudNodes.Proof(_driver, line);
        }
    }
}
