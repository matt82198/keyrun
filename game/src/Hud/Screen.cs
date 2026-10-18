using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The between-runs screen (BIBLE s10 "One screen, three bands"; s1 loop "End-of-run choice of three, then Screen";
/// ENGINE-PLAN 3D). Opened by the run-end panel when a reward is taken (<see cref="OpenFrom"/>), it shows:
/// <list type="bullet">
/// <item><b>Character</b>: the class, the "since last run" line, banked Ash, the five abilities with their keybinds,
/// and the equipped gear with tier, Facet and crafting charges left (Tab selects an item, R rerolls its Facet).</item>
/// <item><b>Key</b>: the key stepper (Left/Right, 2-20) and the <see cref="ClausePick"/> of three (1-3).</item>
/// <item><b>Queue</b>: one button (Enter or a click), which locks the Key band and raises <see cref="Queued"/>.</item>
/// </list>
/// Progression (Ash, gear, the crafting PRNG) is the pure <see cref="Progression"/> and the Key band the pure
/// <see cref="ScreenState"/>; this node renders them and maps input. Ash is banked when the run ends, win or lose,
/// whether or not a reward is taken. Under <c>--autorun</c> every change prints a "SCREEN ..." proof line and a short
/// script drives the screen through the real input path (take reward 1, reroll three times, key up, Clause 2, Queue).
/// </summary>
public partial class Screen : Control
{
    /// <summary>The scene-tree group the screen joins, so <see cref="OpenFrom"/> can find it.</summary>
    public const string GroupName = "keyrun_screen";

    private const int TitleFont = 22;
    private const int BodyFont = 14;
    private const int SmallFont = 12;
    private const float Margin = 32;

    /// <summary>
    /// The one line under the Queue box. Since lane 3I SimDriver starts the queued run at the queued key with the
    /// queued Clause (<see cref="SimDriver.StartQueuedRun"/>), and since 3J that request leaves the Queue node.
    /// </summary>
    public const string QueueCaption = "Enter starts the next run at this key and Clause.";

    private static readonly Color Gold = new(1f, 0.85f, 0.5f);
    private static readonly Color Calm = new(0.7f, 0.85f, 1f);
    private static readonly Color Good = new(0.5f, 1f, 0.6f);

    // The autorun script: one key per frame once the run has ended (keys 1, R, R, R, Right, 2, Enter).
    private static readonly Key[] AutorunKeys = { Key.Key1, Key.R, Key.R, Key.R, Key.Right, Key.Key2, Key.Enter };

    private SimDriver? _driver;
    private Progression _progression = Progression.Start(1);
    private ScreenState? _state;
    private string? _selected;
    private bool _banked;
    private int _autorunStep = -1;
    private int _autorunWait;
    private string _status = string.Empty;

    /// <summary>Raised when Queue is pressed, with the one key and one Clause the party queued.</summary>
    public event Action<QueueRequest>? Queued;

    /// <summary>The driver to read the run and content from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../SimDriver");

    /// <summary>True while the screen is up.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The Key band, or null before the first open.</summary>
    public ScreenState? State => _state;

    /// <summary>The character's progression.</summary>
    public Progression Progression => _progression;

    /// <summary>The last request Queue handed out, or null.</summary>
    public QueueRequest? LastRequest { get; private set; }

    /// <summary>
    /// Opens the screen in <paramref name="from"/>'s scene tree with <paramref name="taken"/> as the end-of-run reward
    /// the player chose (the run-end panel's call site).
    /// </summary>
    /// <param name="from">Any node in the tree.</param>
    /// <param name="taken">The reward taken.</param>
    /// <returns>True when a screen was found and opened.</returns>
    public static bool OpenFrom(Node from, Item taken)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (from.GetTree().GetFirstNodeInGroup(GroupName) is not Screen screen)
        {
            return false;
        }
        screen.Open(taken);
        return true;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        AddToGroup(GroupName);
        MouseFilter = MouseFilterEnum.Ignore;
        _driver = HudNodes.FindDriver(this, DriverPath);
        if (_driver is not null)
        {
            _progression = Progression.Start(_driver.Options.Seed);
            _driver.Stepped += OnStepped;
            _driver.RunStarted += OnRunStarted;
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
        QueueRedraw();
        RunAutorunScript();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && @event is InputEventKey { Pressed: true, Echo: false } key && HandleKey(key.Keycode))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }
        var l = Layout();
        if (l.Queue.HasPoint(click.Position))
        {
            Queue();
        }
        else if (l.KeyDown.HasPoint(click.Position))
        {
            StepKey(-1);
        }
        else if (l.KeyUp.HasPoint(click.Position))
        {
            StepKey(1);
        }
        AcceptEvent();
    }

    /// <summary>Picks Clause offer <paramref name="n"/> (0-based).</summary>
    /// <param name="n">The offer index.</param>
    public void PickClause(int n)
    {
        if (_state is not { } s || n < 0 || n >= s.Clause.Offers.Count)
        {
            return;
        }
        var next = s.PickClause(s.Clause.Offers[n]);
        _status = ReferenceEquals(next, s) ? "The Clause is locked in." : string.Empty;
        _state = next;
        ProofKeyBand();
    }

    /// <summary>The display name and description of a Clause.</summary>
    /// <param name="clauseId">The id.</param>
    /// <returns>Name and text.</returns>
    public (string Name, string Text) ClauseText(string clauseId) =>
        _driver?.Registry is { } reg && reg.Clauses.TryGetValue(clauseId, out var c) ? (c.Name, c.Description) : (clauseId, string.Empty);

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!IsOpen || _state is not { } state)
        {
            return;
        }
        var l = Layout();
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.05f, 0.06f, 0.08f, 0.97f));
        HudNodes.Text(this, new Vector2(Margin, Margin + 4), "Between runs", TitleFont, Gold);
        HudNodes.Text(this, new Vector2(Margin, Size.Y - 18),
            "1-3 Clause    Left/Right key    Tab item    R reroll Facet    Enter queue", SmallFont, Calm);

        DrawCharacter(l.Character);
        DrawKey(l.Key, state);
        DrawQueue(l.Queue, state);
    }

    private void Open(Item taken)
    {
        _progression = _progression.Take(taken);
        _selected = taken.Slot;
        var key = _state?.KeyLevel ?? _driver?.Options.KeyLevel ?? ScreenState.MinKey;
        _state = ScreenState.Open(SimDriver.DungeonId, ClauseIds(), key);
        _status = string.Empty;
        IsOpen = true;
        MouseFilter = MouseFilterEnum.Stop;
        if (_driver is { } d && !d.Options.Autorun && DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        if (GetNodeOrNull<Control>("ClausePick") is { } pick)
        {
            pick.MouseFilter = MouseFilterEnum.Pass;
        }
        Proof(FormattableString.Invariant(
            $"SCREEN open ash={_progression.AshTotal} runs={_progression.Runs} since=\"{_progression.LastRun?.Text() ?? string.Empty}\""));
        Proof($"SCREEN character class={ClassName()}");
        foreach (var (keyLabel, name) in Abilities())
        {
            Proof($"SCREEN ability key={keyLabel} name=\"{name}\"");
        }
        foreach (var slot in Rewards.GearSlots)
        {
            ProofItem(slot);
        }
        ProofKeyBand();
        Proof($"SCREEN queue caption=\"{QueueCaption}\"");
    }

    private void Close()
    {
        IsOpen = false;
        MouseFilter = MouseFilterEnum.Ignore;
        if (GetNodeOrNull<Control>("ClausePick") is { } pick)
        {
            pick.MouseFilter = MouseFilterEnum.Ignore;
        }
    }

    // Returns true when the key was the screen's (Enter is not consumed, so the host's restart still sees it).
    private bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Key1 or Key.Kp1:
                PickClause(0);
                return true;
            case Key.Key2 or Key.Kp2:
                PickClause(1);
                return true;
            case Key.Key3 or Key.Kp3:
                PickClause(2);
                return true;
            case Key.Left or Key.Minus or Key.KpSubtract:
                StepKey(-1);
                return true;
            case Key.Right or Key.Equal or Key.Plus or Key.KpAdd:
                StepKey(1);
                return true;
            case Key.Tab:
                CycleSelected();
                return true;
            case Key.R:
                Reroll();
                return true;
            case Key.Enter or Key.KpEnter:
                Queue();
                return false;
            default:
                return false;
        }
    }

    private void StepKey(int delta)
    {
        if (_state is not { } s)
        {
            return;
        }
        var next = s.StepKey(delta);
        _status = s.Locked ? "The key is locked in." : string.Empty;
        _state = next;
        ProofKeyBand();
    }

    private void CycleSelected()
    {
        var owned = Rewards.GearSlots.Where(s => _progression.Equipped.ContainsKey(s)).ToList();
        if (owned.Count == 0)
        {
            return;
        }
        var i = _selected is null ? -1 : owned.IndexOf(_selected);
        _selected = owned[(i + 1) % owned.Count];
    }

    private void Reroll()
    {
        if (_selected is not { } slot)
        {
            return;
        }
        var (next, result) = _progression.Reroll(slot, SlotPool(slot));
        _progression = next;
        if (result is null)
        {
            return;
        }
        _status = result.Ok ? string.Empty : FormattableString.Invariant($"Reroll refused: {result.Refusal}.");
        Proof(result.Ok
            ? FormattableString.Invariant($"SCREEN reroll slot={slot} ok=1 facet={result.Item.Facet.Id} charges={result.Item.Charges}")
            : FormattableString.Invariant($"SCREEN reroll slot={slot} ok=0 charges={result.Item.Charges} refused=\"{result.Refusal}\""));
        ProofItem(slot);
    }

    private void Queue()
    {
        if (_state is not { } s)
        {
            return;
        }
        var (locked, request) = s.Queue();
        _state = locked;
        if (request is null)
        {
            return;
        }
        LastRequest = request;
        Proof(FormattableString.Invariant($"SCREEN queue dungeon={request.DungeonId} key={request.KeyLevel} clause={request.ClauseId}"));
        Queued?.Invoke(request);
        Close();
    }

    private void OnStepped(int tick, IReadOnlyList<Keyrun.Sim.Core.SimEvent> events)
    {
        if (_banked || _driver?.Run?.RunEnd.Result is not { } result)
        {
            return;
        }
        _banked = true;
        _progression = _progression.BankRun(result);
        Proof(FormattableString.Invariant($"SCREEN bank result={result.ToJs()} ash={Ash.ForResult(result)} total={_progression.AshTotal}"));
        if (_driver.Options.Autorun)
        {
            _autorunStep = 0;
            _autorunWait = 2;
        }
    }

    private void OnRunStarted()
    {
        _banked = false;
        Close();
        if (_state is { } s)
        {
            // A new run: the Key band unlocks for the next queue, keeping the key the party chose.
            _state = ScreenState.Open(s.DungeonId, s.ClauseIds, s.KeyLevel);
        }
    }

    private void RunAutorunScript()
    {
        if (_autorunStep < 0 || _autorunStep >= AutorunKeys.Length)
        {
            return;
        }
        if (_autorunWait > 0)
        {
            _autorunWait--;
            return;
        }
        var key = AutorunKeys[_autorunStep];
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        if (_autorunStep == 0 && !IsOpen)
        {
            Proof("SCREEN error the run-end take did not open the screen");
            _autorunStep = AutorunKeys.Length;
            return;
        }
        _autorunStep++;
    }

    private void Proof(string line)
    {
        if (_driver is not null)
        {
            HudNodes.Proof(_driver, line);
        }
    }

    private void ProofItem(string slot)
    {
        if (!_progression.Equipped.TryGetValue(slot, out var item))
        {
            return;
        }
        Proof(FormattableString.Invariant(
            $"SCREEN item slot={slot} tier={item.Tier} facet={item.Facet.Id} charges={item.Charges} selected={(slot == _selected ? 1 : 0)}"));
    }

    private void ProofKeyBand()
    {
        if (_state is not { } s)
        {
            return;
        }
        Proof(FormattableString.Invariant($"SCREEN key level={s.KeyLevel} locked={(s.Locked ? 1 : 0)}"));
        for (var i = 0; i < s.Clause.Offers.Count; i++)
        {
            var id = s.Clause.Offers[i];
            Proof(FormattableString.Invariant($"SCREEN clause n={i + 1} id={id} name=\"{ClauseText(id).Name}\" picked={(id == s.Clause.Picked ? 1 : 0)}"));
        }
    }

    private IReadOnlyList<string> ClauseIds() =>
        _driver?.Registry is { } reg ? reg.Clauses.Select(kv => kv.Key).ToList() : Array.Empty<string>();

    private string ClassName() =>
        _driver?.Registry is { } reg && reg.Classes.TryGetValue(SimDriver.HumanClassId, out var c) ? c.Name : SimDriver.HumanClassId;

    private IEnumerable<(string Key, string Name)> Abilities() =>
        _driver?.Setup is { } setup
            ? setup.Slots.Take(5).Select(s => (s.Key, _driver.AbilityName(s.AbilityId)))
            : Enumerable.Empty<(string, string)>();

    // That slot's listed pool for the human's class: content Facets of the slot that patch one of the class kit's
    // abilities, ordered by id so the draw does not depend on content load order.
    private IReadOnlyList<Facet> SlotPool(string slot)
    {
        if (_driver?.Registry is not { } reg || _driver.Setup is not { } setup)
        {
            return Array.Empty<Facet>();
        }
        var kit = new HashSet<string>(setup.Slots.Select(s => s.AbilityId), StringComparer.Ordinal);
        return reg.Facets
            .Select(kv => kv.Value)
            .Where(f => f.Slot.ToString().ToLowerInvariant() == slot && kit.Contains(f.AbilityId))
            .OrderBy(f => f.Id, StringComparer.Ordinal)
            .Select(f => new Facet(f.Id, f.Text, f.AbilityId))
            .ToList();
    }

    private (Rect2 Character, Rect2 Key, Rect2 Queue, Rect2 KeyDown, Rect2 KeyUp) Layout()
    {
        var top = Margin + 30;
        var height = Size.Y - top - 44;
        var width = Size.X - (2 * Margin);
        var character = new Rect2(Margin, top, width * 0.46f, height);
        var key = new Rect2(character.End.X + 16, top, width * 0.32f, height);
        var queue = new Rect2(key.End.X + 16, top, Size.X - Margin - key.End.X - 16, 120);
        var keyDown = new Rect2(key.Position.X + 14, key.Position.Y + 44, 40, 40);
        var keyUp = new Rect2(key.Position.X + 150, key.Position.Y + 44, 40, 40);
        if (GetNodeOrNull<Control>("ClausePick") is { } pick)
        {
            pick.Position = new Vector2(key.Position.X + 14, key.Position.Y + 128);
            pick.Size = new Vector2(key.Size.X - 28, 3 * 72);
        }
        return (character, key, queue, keyDown, keyUp);
    }

    private void Band(Rect2 r, string title)
    {
        DrawRect(r, HudNodes.Back);
        DrawRect(r, HudNodes.Edge, filled: false, width: 1f);
        HudNodes.Text(this, r.Position + new Vector2(14, 24), title, 16, Gold);
    }

    private void DrawCharacter(Rect2 r)
    {
        Band(r, "Character");
        var x = r.Position.X + 14;
        var y = r.Position.Y + 50;
        HudNodes.Text(this, new Vector2(x, y), ClassName(), 18, Colors.White);
        HudNodes.Text(this, new Vector2(x + 140, y), FormattableString.Invariant($"Ash {_progression.AshTotal}"), BodyFont, Gold);
        y += 22;
        var since = _progression.LastRun?.Text() ?? "First run";
        HudNodes.Text(this, new Vector2(x, y), "Since last run: " + since, BodyFont, Calm);
        y += 28;
        foreach (var (keyLabel, name) in Abilities())
        {
            DrawRect(new Rect2(x, y - 15, 22, 20), new Color(0.2f, 0.22f, 0.28f));
            HudNodes.Text(this, new Vector2(x + 6, y), keyLabel, BodyFont, Gold);
            HudNodes.Text(this, new Vector2(x + 32, y), name, BodyFont, Colors.White);
            y += 24;
        }
        y += 10;
        HudNodes.Text(this, new Vector2(x, y), "Gear", 16, Gold);
        y += 22;
        foreach (var slot in Rewards.GearSlots)
        {
            var label = char.ToUpperInvariant(slot[0]) + slot[1..];
            if (!_progression.Equipped.TryGetValue(slot, out var item))
            {
                HudNodes.Text(this, new Vector2(x, y), label + ": empty", BodyFont, new Color(1, 1, 1, 0.45f));
                y += 22;
                continue;
            }
            var selected = slot == _selected;
            if (selected)
            {
                DrawRect(new Rect2(x - 6, y - 16, r.Size.X - 16, 40), new Color(0.5f, 1f, 0.6f, 0.12f));
            }
            var charges = item.Charges == 1 ? "1 charge left" : FormattableString.Invariant($"{item.Charges} charges left");
            HudNodes.Text(this, new Vector2(x, y), FormattableString.Invariant($"{label}  Tier {item.Tier}    {charges}"), BodyFont,
                selected ? Good : Colors.White);
            HudNodes.Text(this, new Vector2(x + 12, y + 18), Clip(item.Facet.Text, r.Size.X - 40, SmallFont), SmallFont, Calm);
            y += 42;
        }
        if (_status.Length > 0)
        {
            HudNodes.Text(this, new Vector2(x, r.End.Y - 14), _status, BodyFont, new Color(1f, 0.6f, 0.5f));
        }
    }

    private void DrawKey(Rect2 r, ScreenState state)
    {
        Band(r, "Key");
        var l = Layout();
        foreach (var (rect, glyph) in new[] { (l.KeyDown, "<"), (l.KeyUp, ">") })
        {
            DrawRect(rect, new Color(0.18f, 0.2f, 0.26f, state.Locked ? 0.4f : 1f));
            HudNodes.Text(this, rect.Position + new Vector2(14, 27), glyph, 20, Colors.White);
        }
        HudNodes.Text(this, new Vector2(r.Position.X + 74, r.Position.Y + 76), FormattableString.Invariant($"Key {state.KeyLevel}"), TitleFont, Colors.White);
        HudNodes.Text(this, new Vector2(r.Position.X + 14, r.Position.Y + 116), state.Locked ? "Clause (locked)" : "Clause: pick one", BodyFont, Gold);
    }

    private void DrawQueue(Rect2 r, ScreenState state)
    {
        DrawRect(r, state.Locked ? new Color(0.2f, 0.3f, 0.22f) : new Color(0.32f, 0.26f, 0.12f));
        DrawRect(r, Gold, filled: false, width: 2f);
        HudNodes.Text(this, r.Position + new Vector2(18, 48), state.Locked ? "Queued" : "Queue", 28, Colors.White);
        var (name, _) = ClauseText(state.Clause.Picked);
        HudNodes.Text(this, r.Position + new Vector2(18, 80), FormattableString.Invariant($"Key {state.KeyLevel} with {name}"), BodyFont, Calm);
        HudNodes.Text(this, r.Position + new Vector2(18, 102), "Enter or click", SmallFont, Colors.White);
        HudNodes.Text(this, new Vector2(r.Position.X, r.End.Y + 24), QueueCaption, SmallFont, Calm);
    }

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
