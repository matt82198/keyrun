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
/// ENGINE-PLAN 3D), drawn as one lobby with three tabs (lane LOBBY-TABS, Matt 2026-10-20 "just one screen with tabs
/// should be enough before entering a game/queue"): <b>Character</b> (class pick and the five gear slots with tier),
/// <b>Build</b> (the class's talent forks) and <b>Play</b> (key, Clause and Queue). The tab, the class, its build and the
/// Key band are the pure <see cref="Lobby"/>; Q/E, F1-F3 or a click switch tabs, Up/Down or a click picks the class on
/// the Character tab, and every older key works on every tab (Enter queues from anywhere). Opened by the run-end panel when a reward is taken (<see cref="OpenFrom"/>), and at launch before
/// the first run in the interactive game (<see cref="OpenFirst"/>, lane 3D-b), and again by <see cref="OpenFirst"/> each
/// time a run ends in the interactive game and Enter is pressed on the run-end panel (the SimDriver reopen path, lane
/// 3D-c, wipe included), it shows:
/// <list type="bullet">
/// <item><b>Character</b>: the class, the "since last run" line, banked Ash, the five abilities with their keybinds,
/// and the equipped gear with tier, Facet and crafting charges left (Tab selects an item, R rerolls its Facet).</item>
/// <item><b>Key</b>: the key stepper (Left/Right, 2-20) and the key's Clause, read-only (BIBLE s8, R1 2026-10-21: the key sets it).</item>
/// <item><b>Queue</b>: one button (Enter or a click), which locks the Key band and raises <see cref="Queued"/>.</item>
/// </list>
/// Progression (Ash, gear, the crafting PRNG) is the pure <see cref="Progression"/> and the Key band the pure
/// <see cref="ScreenState"/>; this node renders them and maps input. Ash is banked when the run ends, win or lose,
/// whether or not a reward is taken. Under <c>--autorun</c> every change prints a "SCREEN ..." proof line and a short
/// script drives the screen through the real input path (take reward 1, reroll three times, key up, the
/// talent panel: T, pick fork 1 side 1, Down, pick side 2, free respec, pick again, T, then Queue).
/// <para>Talents (BIBLE s6, lane BIBLE-TALENTS-UI): T opens the <see cref="TalentPanel"/> over the Character and Key
/// bands; the build is the pure <see cref="TalentPicks"/> for the human's class, kept across opens, and Backspace
/// respecs for free (Ash is untouched). Queue hands the picks to the run (lane TALENT-PICKS): they are resolved with
/// <see cref="TalentPicks.Resolve"/>, converted to sim AbilityDefs by the host's own content conversion
/// (<see cref="HostRunSetup.Build"/>), and queued on the <see cref="QueueRequest"/> as the human's loadout, which
/// <see cref="QueueRequest.ToRunOptions"/> passes to the run as RunOptions.Loadouts. The panel's <c>applied=</c> count is
/// the number of talented abilities the next Queue hands over. Every pick and respec is kept in the progression save
/// (lane TALENT-SAVE, f-83c5e369: <see cref="Progression.Talents"/>, saved with reason=talent), and the first open after
/// a launch restores the saved build with <see cref="Keyrun.Content.TalentPicks.Restore"/>, so the next launch's run
/// plays it too.</para>
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

    // LOBBY-TABS palette (the Dark and Darker lobby read: warm near-black, bronze edges, parchment text, gold accents).
    private static readonly Color LobbyBg = new(0.05f, 0.042f, 0.035f, 0.98f);
    private static readonly Color BarBg = new(0.075f, 0.062f, 0.05f, 1f);
    private static readonly Color PanelBg = new(0.085f, 0.072f, 0.06f, 0.96f);
    private static readonly Color CardBg = new(0.12f, 0.1f, 0.08f, 1f);
    private static readonly Color TabOff = new(0.1f, 0.085f, 0.07f, 1f);
    private static readonly Color TabOn = new(0.2f, 0.16f, 0.1f, 1f);
    private static readonly Color Bronze = new(0.45f, 0.35f, 0.2f, 1f);
    private static readonly Color BronzeDim = new(0.3f, 0.24f, 0.15f, 1f);
    private static readonly Color Parchment = new(0.88f, 0.83f, 0.72f, 1f);
    private static readonly Color Dim = new(0.58f, 0.53f, 0.45f, 1f);
    private static readonly Color Warn = new(1f, 0.6f, 0.5f);

    // The tab bar: three tabs from this x in the title row.
    private const float TabsLeft = 200;
    private const float TabWidth = 150;

    // The autorun script: one key per frame once the run has ended (keys 1, R, R, R, Right, then the talent panel
    // T, 1, Down, 2, Backspace, 1, T, then Enter).
    private static readonly Key[] AutorunKeys =
    {
        Key.Key1, Key.R, Key.R, Key.R, Key.Right,
        Key.T, Key.Key1, Key.Down, Key.Key2, Key.Backspace, Key.Key1, Key.T,
        Key.Enter,
    };

    private SimDriver? _driver;
    private Progression _progression = Progression.Start(1);
    private ProgressionStore? _store;
    private Keyrun.Content.PrivateRecord _rating = Keyrun.Content.PrivateRecord.Empty;
    private PrivateRatingStore? _ratingStore;
    private Lobby? _lobby;
    private int? _pendingKey;
    private string? _selected;
    private bool _banked;
    private int _autorunStep = -1;
    private int _autorunWait;
    private string _status = string.Empty;

    /// <summary>The human's talent build, or null before the first open (or with no content).</summary>
    public Keyrun.Content.TalentPicks? Talents => _lobby?.Talents;

    /// <summary>True while the talent panel is up: the Build tab of the open screen.</summary>
    public bool TalentsOpen => IsOpen && _lobby?.Tab == LobbyTab.Build;

    /// <summary>The open tab (Play before the first open).</summary>
    public LobbyTab Tab => _lobby?.Tab ?? LobbyTab.Play;

    /// <summary>The human's class for the next Queue (the Character tab's pick).</summary>
    public string ClassId => _lobby?.ClassId ?? SimDriver.HumanClassId;

    /// <summary>The lobby (tab, class, build, Key band), or null before the first open; SimDriver reads its class.</summary>
    public Lobby? CurrentLobby => _lobby;

    /// <summary>The selected fork row in the talent panel (Up/Down).</summary>
    public int TalentFork { get; private set; }

    /// <summary>Raised when Queue is pressed, with the one key and one Clause the party queued.</summary>
    public event Action<QueueRequest>? Queued;

    /// <summary>The driver to read the run and content from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../SimDriver");

    /// <summary>
    /// The in-run help line (Arena.tscn <c>Hud/Help</c>), hidden while the screen is up so it does not show through
    /// behind the title, and shown again when the screen closes (lane SCREEN-OVERFLOW).
    /// </summary>
    [Export]
    public NodePath HelpPath { get; set; } = new("../Help");

    /// <summary>True while the screen is up.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The Key band, or null before the first open.</summary>
    public ScreenState? State => _lobby?.Key;

    /// <summary>The character's progression.</summary>
    public Progression Progression => _progression;

    /// <summary>
    /// True once the ended run is banked into <see cref="Progression"/> (its Ash and its <see cref="RunRecord"/>), until
    /// the next run starts. The run-end panel waits for it before reading the Ascension state, so it shows the banked one.
    /// </summary>
    public bool Banked => _banked;

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

    /// <summary>
    /// Opens the screen with no reward taken, in the interactive game only. It is called at launch, before the first run
    /// (lane 3D-b, ENGINE-PLAN phase 3 "the screen and Clause pick come up before the run starts"), and again after every
    /// run end when Enter is pressed on the run-end panel without taking a reward (SimDriver reopen path, lane 3D-c,
    /// wipe included). The Key band opens at the current key (the driver's <c>--key</c> on the first open) with the first
    /// Clause offer picked, and Queue (<see cref="Queued"/>) starts the next run. It reads only the driver's Registry and
    /// Setup, never its Run, so it works whether no run exists yet or the previous one has ended.
    /// </summary>
    public void OpenFirst() => Open(null);

    /// <inheritdoc />
    public override void _Ready()
    {
        AddToGroup(GroupName);
        MouseFilter = MouseFilterEnum.Ignore;
        if (GetNodeOrNull<TalentPanel>(TalentPanel.NodeName) is null)
        {
            AddChild(new TalentPanel { Name = TalentPanel.NodeName });
        }
        _driver = HudNodes.FindDriver(this, DriverPath);
        if (_driver is not null)
        {
            LoadProgression(_driver.Options.Seed);
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
        if (l.Tabs.Select((r, i) => (r, i)).FirstOrDefault(t => t.r.HasPoint(click.Position)) is { r.Size.X: > 0 } hit)
        {
            SelectTab(Lobby.Tabs[hit.i]);
        }
        else if (Tab == LobbyTab.Character)
        {
            var ids = ClassIds();
            for (var i = 0; i < ids.Count && i < l.ClassCards.Count; i++)
            {
                if (l.ClassCards[i].HasPoint(click.Position))
                {
                    PickClass(ids[i]);
                }
            }
        }
        else if (Tab == LobbyTab.Play && l.Queue.HasPoint(click.Position))
        {
            Queue();
        }
        else if (Tab == LobbyTab.Play && l.KeyDown.HasPoint(click.Position))
        {
            StepKey(-1);
        }
        else if (Tab == LobbyTab.Play && l.KeyUp.HasPoint(click.Position))
        {
            StepKey(1);
        }
        AcceptEvent();
    }

    /// <summary>
    /// Opens a tab. Leaving or entering the Build tab is the talent panel closing or opening, so it prints the same
    /// "SCREEN talents open=..." line the T key always has.
    /// </summary>
    /// <param name="tab">The tab.</param>
    public void SelectTab(LobbyTab tab)
    {
        if (!IsOpen || _lobby is not { } lobby)
        {
            return;
        }
        var next = lobby.SelectTab(tab);
        if (ReferenceEquals(next, lobby))
        {
            return;
        }
        var wasBuild = lobby.Tab == LobbyTab.Build;
        _lobby = next;
        Proof(FormattableString.Invariant($"SCREEN tab {next.Tab.ToString().ToLowerInvariant()} class={next.ClassId}"));
        if (wasBuild == (next.Tab == LobbyTab.Build))
        {
            return;
        }
        var picks = next.Talents;
        TalentFork = Math.Clamp(TalentFork, 0, Math.Max(0, picks.ForkCount - 1));
        Proof(FormattableString.Invariant(
            $"SCREEN talents open={(TalentsOpen ? 1 : 0)} class={picks.ClassId} forks={picks.ForkCount} picks={picks.Count} applied={AppliedCount(TalentLoadouts())}"));
        if (TalentsOpen)
        {
            ProofTalents();
        }
    }

    /// <summary>Opens the Build tab, or goes back to Play from it (T). Only while the screen is up.</summary>
    public void ToggleTalents() => SelectTab(Tab == LobbyTab.Build ? LobbyTab.Play : LobbyTab.Build);

    /// <summary>
    /// Plays <paramref name="classId"/> on the next Queue (the Character tab). Refused while queued; the build becomes
    /// that class's saved build (<see cref="Lobby.PickClass"/>).
    /// </summary>
    /// <param name="classId">The class.</param>
    public void PickClass(string classId)
    {
        if (!IsOpen || _lobby is not { } lobby || _driver?.Registry is not { } reg)
        {
            return;
        }
        var next = lobby.PickClass(reg, _progression, classId);
        _status = ReferenceEquals(next, lobby) && lobby.Locked ? "The class is locked in." : string.Empty;
        if (ReferenceEquals(next, lobby))
        {
            return;
        }
        _lobby = next;
        TalentFork = 0;
        Proof(FormattableString.Invariant(
            $"SCREEN class pick class={next.ClassId} name=\"{ClassName()}\" picks={next.Talents.Count} forks={next.Talents.ForkCount}"));
        foreach (var (keyLabel, name) in Abilities())
        {
            Proof($"SCREEN class ability key={keyLabel} name=\"{name}\"");
        }
    }

    // Up/Down on the Character tab: the next or previous class in roster order.
    private void CycleClass(int delta)
    {
        var ids = ClassIds();
        if (ids.Count == 0)
        {
            return;
        }
        var i = Math.Max(0, ids.ToList().IndexOf(ClassId));
        PickClass(ids[(((i + delta) % ids.Count) + ids.Count) % ids.Count]);
    }

    private IReadOnlyList<string> ClassIds() =>
        _driver?.Registry is { } reg ? Lobby.ClassIds(reg) : Array.Empty<string>();

    /// <summary>Picks side <paramref name="option"/> (0-based) of fork <paramref name="fork"/>; out-of-range input is ignored.</summary>
    /// <param name="fork">The fork index.</param>
    /// <param name="option">The option index.</param>
    public void PickTalent(int fork, int option)
    {
        if (!TalentsOpen || _lobby is not { } lobby || lobby.Talents is not { } picks || fork < 0 || fork >= picks.ForkCount ||
            option < 0 || option >= picks.Forks[fork].Options.Count)
        {
            return;
        }
        _lobby = lobby.WithTalents(picks.Pick(fork, option));
        TalentFork = fork;
        var f = _lobby.Talents.Forks[fork];
        Proof(FormattableString.Invariant(
            $"SCREEN talent pick fork={fork + 1} ability={f.AbilityId} option={f.Options[option].Id} picks={_lobby.Talents.Count}"));
        ProofTalentMarks(fork);
        SaveTalents();
    }

    /// <summary>Clears every talent pick for free (BIBLE s6 "Respec is free on the screen, always"): Ash is untouched.</summary>
    public void RespecTalents()
    {
        if (!TalentsOpen || _lobby is not { } lobby)
        {
            return;
        }
        var ash = _progression.AshTotal;
        _lobby = lobby.WithTalents(lobby.Talents.Respec());
        Proof(FormattableString.Invariant($"SCREEN talent respec picks={_lobby.Talents.Count} ash={ash} ash_after={_progression.AshTotal}"));
        ProofTalentMarks();
        SaveTalents();
    }

    // TALENT-SAVE (f-83c5e369): the build rides the progression, so a pick or a respec is saved like a reroll is. Only
    // this class's forks are written; the other classes' saved builds are kept (Lobby.SaveTalents).
    private void SaveTalents()
    {
        if (_lobby is not { } lobby)
        {
            return;
        }
        var picks = lobby.Talents;
        _progression = lobby.SaveTalents(_progression);
        SaveProgression("talent");
        if (_store is not null)
        {
            Proof(FormattableString.Invariant($"SCREEN talents saved picks={picks.Count} choices={ChoicesText(picks)}"));
        }
    }

    // "warden.strike:rend,..." in fork order, or "none".
    private static string ChoicesText(Keyrun.Content.TalentPicks picks) =>
        picks.Count == 0 ? "none" : string.Join(",", picks.Choices.Select(kv => kv.Key + ":" + kv.Value));

    /// <summary>The human class's display name (the talent panel's title).</summary>
    /// <returns>The name.</returns>
    public string TalentClassName() => ClassName();

    /// <summary>An ability's display name (the talent panel's fork rows).</summary>
    /// <param name="abilityId">The ability id.</param>
    /// <returns>The name.</returns>
    public string TalentAbilityName(string abilityId) => _driver?.AbilityName(abilityId) ?? abilityId;

    /// <summary>The display name and description of a Clause.</summary>
    /// <param name="clauseId">The id.</param>
    /// <returns>Name and text.</returns>
    public (string Name, string Text) ClauseText(string clauseId) =>
        _driver?.Registry is { } reg && reg.Clauses.TryGetValue(clauseId, out var c) ? (c.Name, c.Description) : (clauseId, string.Empty);

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
                DrawGear(l.Gear);
                DrawKit(l.Kit);
                break;
            case LobbyTab.Build:
                break; // the TalentPanel child draws the Build tab
            default:
                DrawSummary(l.Summary);
                DrawKey(l.Key, lobby.Key);
                DrawQueue(l.Queue, lobby.Key);
                break;
        }
        if (_status.Length > 0)
        {
            HudNodes.Text(this, new Vector2(Size.X - Margin - HudNodes.Width(_status, BodyFont), Size.Y - 18), _status, BodyFont, Warn);
        }
    }

    // taken null: OpenFirst, both the launch open and the run-end reopen (no run is required); no reward to equip.
    private void Open(Item? taken)
    {
        if (taken is not null)
        {
            _progression = _progression.Take(taken);
            _selected = taken.Slot;
        }
        // The key: the lobby's own (moved by the last run end, Lobby.ReturnFrom), else the key a run end before the first
        // open moved to, else the driver's --key.
        var key = _lobby?.Key.KeyLevel ?? _pendingKey ?? _driver?.Options.KeyLevel ?? ScreenState.MinKey;
        _pendingKey = null;
        _status = string.Empty;
        if (_lobby is { } open)
        {
            _lobby = open.Reopen(key);
        }
        else if (_driver?.Registry is { } reg && reg.Classes.ContainsKey(SimDriver.HumanClassId))
        {
            // TALENT-SAVE: the saved build comes back; a pick the content no longer has is dropped (free respec).
            // R1: the key's Clause is read from the same rotation index the run is built with (SimDriver.RotationIndex) and
            // content's clauseStartKey, so the band never shows a Clause the run does not carry.
            _lobby = Lobby.Open(reg, _progression, SimDriver.DungeonId, ClauseIds(), key, _driver.RotationIndex, reg.Run.Affixes.ClauseStartKey, SimDriver.HumanClassId);
            var talents = _lobby.Talents;
            Proof(FormattableString.Invariant(
                $"SCREEN talents restore class={talents.ClassId} saved={_progression.Talents.Count} picks={talents.Count} choices={ChoicesText(talents)}"));
        }
        else
        {
            return; // no content: nothing to show or queue
        }
        IsOpen = true;
        MouseFilter = MouseFilterEnum.Stop;
        if (_driver is { } d && !d.Options.Autorun && DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        Proof(FormattableString.Invariant(
            $"SCREEN open ash={_progression.AshTotal} runs={_progression.Runs} since=\"{_progression.LastRun?.Text() ?? string.Empty}\""));
        Proof(_rating.LogLine()); // never the rating's value: it is drawn on this screen only (f-a3230c59)
        SetHelpVisible(false);
        Proof($"SCREEN character class={ClassName()}");
        // f-275d47c9: the Character tab's private Ascension line, the same Ascension.Of read DrawAscension draws.
        var ascension = Ascension.Of(_progression);
        Proof(AscensionReadout.LogLine("SCREEN", ascension));
        Proof($"SCREEN ascension line=\"{AscensionReadout.CharacterLine(ascension)}\"");
        foreach (var (keyLabel, name) in Abilities())
        {
            Proof($"SCREEN ability key={keyLabel} name=\"{name}\"");
        }
        foreach (var slot in Rewards.GearSlots)
        {
            ProofItem(slot);
        }
        ProofGear();
        ProofKeyBand();
        Proof($"SCREEN queue caption=\"{QueueCaption}\"");
        // Measured at the design viewport (project.godot 1280x720): headless runs have a tiny dummy window.
        var design = DesignSize();
        Proof(FormattableString.Invariant($"SCREEN tab {_lobby.Tab.ToString().ToLowerInvariant()} class={_lobby.ClassId}"));
        var queueBox = LayoutFor(design).Queue;
        var caption = CaptionLines(queueBox);
        var right = queueBox.Position.X + caption.Max(line => HudNodes.Width(line, SmallFont));
        Proof(FormattableString.Invariant(
            $"SCREEN fit caption lines={caption.Count} right={Math.Ceiling(right)} viewport={Math.Floor(design.X)}"));
        if (taken is not null)
        {
            SaveProgression("take"); // the reward is equipped: keep it before anything else can end the launch
        }
    }

    private void Close()
    {
        IsOpen = false;
        SetHelpVisible(true);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    // Returns true when the key was the screen's (Enter is not consumed, so the host's restart still sees it).
    private bool HandleKey(Key key)
    {
        if (TabKey(key))
        {
            return true;
        }
        if (TalentsOpen)
        {
            return HandleTalentKey(key);
        }
        switch (key)
        {
            case Key.T:
                ToggleTalents();
                return true;
            case Key.Up when Tab == LobbyTab.Character:
                CycleClass(-1);
                return true;
            case Key.Down when Tab == LobbyTab.Character:
                CycleClass(1);
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

    // The tab bar's keys work on every tab: Q and E step along the bar, F1-F3 jump to a tab.
    private bool TabKey(Key key)
    {
        if (_lobby is not { } lobby)
        {
            return false;
        }
        switch (key)
        {
            case Key.Q:
                SelectTab(lobby.CycleTab(-1).Tab);
                return true;
            case Key.E:
                SelectTab(lobby.CycleTab(1).Tab);
                return true;
            case Key.F1:
                SelectTab(LobbyTab.Character);
                return true;
            case Key.F2:
                SelectTab(LobbyTab.Build);
                return true;
            case Key.F3:
                SelectTab(LobbyTab.Play);
                return true;
            default:
                return false;
        }
    }

    // The Build tab's keys (the talent panel; and Enter, which still queues).
    private bool HandleTalentKey(Key key)
    {
        switch (key)
        {
            case Key.T or Key.Escape:
                ToggleTalents();
                return true;
            case Key.Up:
                TalentFork = Math.Max(0, TalentFork - 1);
                ProofTalentMarks();
                return true;
            case Key.Down:
                TalentFork = Math.Min(Math.Max(0, (_lobby?.Talents.ForkCount ?? 1) - 1), TalentFork + 1);
                ProofTalentMarks();
                return true;
            case Key.Key1 or Key.Kp1:
                PickTalent(TalentFork, 0);
                return true;
            case Key.Key2 or Key.Kp2:
                PickTalent(TalentFork, 1);
                return true;
            case Key.Key3 or Key.Kp3:
                PickTalent(TalentFork, 2);
                return true;
            case Key.Backspace or Key.Delete:
                RespecTalents();
                return true;
            case Key.Enter or Key.KpEnter:
                Queue();
                return false;
            default:
                return true;
        }
    }

    private void StepKey(int delta)
    {
        if (_lobby is not { } lobby)
        {
            return;
        }
        _status = lobby.Locked ? "The key is locked in." : string.Empty;
        _lobby = lobby.StepKey(delta);
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
        if (result.Ok)
        {
            SaveProgression("reroll"); // after the item line: ScreenSceneTest reads the line right after each reroll
        }
    }

    private void Queue()
    {
        if (_lobby is not { } lobby || _driver?.Registry is not { } reg)
        {
            return;
        }
        // The loadout here only reports what each layer contributes. The request is QueuedKit.Queue's (through
        // Lobby.Queue, which adds the class): the talent kit and the gear stats, never the Facets (finding f-494ea8ef:
        // the host's RunStart.Of applies each Facet once from the progression; a Facet-patched kit on the request was
        // patched a second time).
        var loadout = lobby.Locked ? null : RunLoadout(withFacets: true);
        var (locked, request) = lobby.Queue(reg, _progression);
        _lobby = locked;
        if (request is null)
        {
            return;
        }
        Proof(FormattableString.Invariant($"SCREEN queue class={locked.ClassId}"));
        // BIBLE s7 "Tier is power": the five slots' summed stats ride the request onto the human's actor.
        foreach (var (actor, gear) in request.Gear ?? new Dictionary<int, GearStats>())
        {
            Proof(FormattableString.Invariant(
                $"SCREEN queue gear actor={actor} damageDoneBp={gear.DamageDoneBp} damageTakenBp={gear.DamageTakenBp}"));
        }
        LastRequest = request;
        Proof(FormattableString.Invariant($"SCREEN queue dungeon={request.DungeonId} key={request.KeyLevel} clause={request.ClauseId}"));
        ProofQueuedTalents(request, loadout?.TalentIds);
        ProofQueuedFacets(request, loadout?.Facets);
        var before = _driver?.Run;
        Queued?.Invoke(request);
        if (_driver?.Run is { } started && !ReferenceEquals(started, before))
        {
            ProofRunFacets(started, loadout?.Facets);
        }
        Close();
    }

    // The human's patched kit for the next run plus what each layer contributed: the talent ability ids and the gear
    // layer's resolution (null when the facet layer was not asked for or content is not loaded).
    private sealed record HumanLoadout(
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>>? Loadouts,
        IReadOnlySet<string> TalentIds,
        Keyrun.Content.FacetResolution? Facets);

    /// <summary>
    /// The human's talent-resolved kit for the next run (BIBLE s6), keyed by the human's actor id, or null when no fork
    /// is picked or content is not loaded. The talent count the panel shows; Queue hands the run
    /// <see cref="RunLoadout"/> with the gear layer as well.
    /// </summary>
    /// <returns>The talent-only loadouts, or null.</returns>
    private IReadOnlyDictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>>? TalentLoadouts() =>
        RunLoadout(withFacets: false)?.Loadouts;

    /// <summary>
    /// The human's kit for the next run in content/apply.ts applyLoadout order (BIBLE s6 then s7): the talent picks are
    /// resolved on the content abilities with <see cref="TalentPicks.Resolve"/>, then (with <paramref name="withFacets"/>)
    /// every equipped item's Facet is applied on top with <see cref="Keyrun.Content.GearFacets.Resolve"/> in slot order.
    /// The patched content is converted to sim AbilityDefs by the same <see cref="HostRunSetup.Build"/> conversion the
    /// run itself is built with, so a talented or Facet-patched ability is exactly what the host would build if the
    /// patch were the base definition. A line width (twinStrike, infernoBolt) converts to the sim line's half-width, so
    /// every shipped Facet applies; one the content cannot honour is still skipped and reported, never a refused Queue.
    /// </summary>
    /// <param name="withFacets">True to apply the gear layer.</param>
    /// <returns>The loadout, or null when content or the Key band is not there.</returns>
    private HumanLoadout? RunLoadout(bool withFacets)
    {
        if (_driver?.Registry is not { } reg || _lobby is not { } lobby)
        {
            return null;
        }
        var s = lobby.Key;
        var talented = lobby.Talents is { Count: > 0 } picks ? picks.Resolve(reg.Abilities) : new Keyrun.Content.ValueMap<Keyrun.Content.AbilityDef>();
        var patched = new Dictionary<string, Keyrun.Content.AbilityDef>(talented, StringComparer.Ordinal);
        Keyrun.Content.FacetResolution? facets = null;
        if (withFacets && reg.Classes.TryGetValue(lobby.ClassId, out var cls))
        {
            facets = Keyrun.Content.GearFacets.Resolve(
                reg.Facets,
                Merge(reg.Abilities, talented),
                cls,
                Rewards.GearSlots.Where(_progression.Equipped.ContainsKey).Select(slot => (slot, _progression.Equipped[slot].Facet.Id)));
            foreach (var (id, def) in facets.Abilities)
            {
                patched[id] = def;
            }
        }
        var talentIds = new HashSet<string>(talented.Keys, StringComparer.Ordinal);
        if (patched.Count == 0)
        {
            return new HumanLoadout(null, talentIds, facets);
        }
        var setup = HostRunSetup.Build(reg with { Abilities = Merge(reg.Abilities, patched) }, s.DungeonId, s.KeyLevel, lobby.ClassId, withBots: false);
        var kit = patched.Keys.ToDictionary(id => id, id => setup.Content.Abilities[id], StringComparer.Ordinal);
        return new HumanLoadout(new Dictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>> { [setup.HumanActorId] = kit }, talentIds, facets);
    }

    private static Keyrun.Content.ValueMap<Keyrun.Content.AbilityDef> Merge(
        IReadOnlyDictionary<string, Keyrun.Content.AbilityDef> abilities, IReadOnlyDictionary<string, Keyrun.Content.AbilityDef> patched) =>
        new(abilities.Select(kv => patched.TryGetValue(kv.Key, out var p) ? new KeyValuePair<string, Keyrun.Content.AbilityDef>(kv.Key, p) : kv));

    private static int AppliedCount(IReadOnlyDictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>>? loadouts) =>
        loadouts?.Values.Sum(kit => kit.Count) ?? 0;

    // One line for the talent count the request hands the run, then one per talented ability: the queued value next to
    // the base the run would otherwise play (read from the request's own RunOptions, the object SimDriver passes to
    // CreateRun). An ability a Facet also patched shows both layers.
    private void ProofQueuedTalents(QueueRequest request, IReadOnlySet<string>? talentIds)
    {
        var loadouts = request.ToRunOptions().Loadouts;
        var talents = loadouts?.Values.Sum(kit => kit.Keys.Count(id => talentIds?.Contains(id) == true)) ?? 0;
        Proof(FormattableString.Invariant($"SCREEN queue talents applied={talents}"));
        if (loadouts is null)
        {
            return;
        }
        var baseKit = _driver?.Setup?.Content.Abilities;
        foreach (var (actor, kit) in loadouts.OrderBy(kv => kv.Key))
        {
            foreach (var (id, def) in kit.Where(kv => talentIds?.Contains(kv.Key) == true))
            {
                var b = baseKit is not null && baseKit.TryGetValue(id, out var bd) ? bd : null;
                Proof($"SCREEN queue talent actor={actor.ToString(System.Globalization.CultureInfo.InvariantCulture)} ability={id} {Stats(def)} base {Stats(b)}");
            }
        }
    }

    // BIBLE s7: one line for the Facets the request hands the run (and how many equipped Facets were skipped), then one
    // per applied Facet with its ability's queued value next to the base (read from the options the host will create the
    // run with: HostRunSetup.StartFor, the call SimDriver.StartQueuedRun makes, since the Facets are layered there and
    // not on the request), and one per skipped Facet with the reason.
    private void ProofQueuedFacets(QueueRequest request, Keyrun.Content.FacetResolution? facets)
    {
        Proof(FormattableString.Invariant($"SCREEN queue facets applied={facets?.Applied.Count ?? 0} skipped={facets?.Skipped.Count ?? 0}"));
        if (facets is null)
        {
            return;
        }
        var loadouts = _driver?.Setup is { } setup && _driver.Registry is { } reg
            ? setup.StartFor(request, _progression, reg, ClassId).Options?.Loadouts
            : request.ToRunOptions().Loadouts;
        var baseKit = _driver?.Setup?.Content.Abilities;
        foreach (var f in facets.Applied)
        {
            foreach (var (actor, kit) in (loadouts ?? new Dictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>>()).OrderBy(kv => kv.Key))
            {
                var queued = kit.TryGetValue(f.AbilityId, out var q) ? q : null;
                var b = baseKit is not null && baseKit.TryGetValue(f.AbilityId, out var bd) ? bd : null;
                Proof($"SCREEN queue facet actor={actor.ToString(System.Globalization.CultureInfo.InvariantCulture)} {FacetKey(f)} {FacetStats(queued)} base {FacetStats(b)}");
            }
        }
        foreach (var k in facets.Skipped)
        {
            Proof($"SCREEN queue facet skipped slot={k.Slot} facet={k.FacetId} reason=\"{k.Reason}\"");
        }
    }

    // Only when Queued started a run (SimDriver.StartQueuedRun; plain --autorun does not): each applied Facet's ability
    // as the started run plays it (Run.Content, the merged ability map CreateRun built from the request's loadouts).
    private void ProofRunFacets(Keyrun.Sim.Runs.Run run, Keyrun.Content.FacetResolution? facets)
    {
        if (facets is null || _driver?.Setup is not { } setup)
        {
            return;
        }
        foreach (var f in facets.Applied)
        {
            var def = run.Content.Abilities.TryGetValue(f.AbilityId, out var d) ? d : null;
            Proof($"SCREEN run facet actor={setup.HumanActorId.ToString(System.Globalization.CultureInfo.InvariantCulture)} {FacetKey(f)} {FacetStats(def)}");
        }
    }

    private static string FacetKey(Keyrun.Content.AppliedFacet f) =>
        $"slot={Keyrun.Content.GearFacets.SlotId(f.Slot)} facet={f.FacetId} ability={f.AbilityId}";

    private static string FacetStats(Keyrun.Sim.Combat.AbilityDef? d) => d is null
        ? "shape=- range=- cast=- cooldown=- charges=- cost=- effects=- tags=-"
        : FormattableString.Invariant(
            $"shape={d.Shape.Kind}/{d.Shape.Radius}/{d.Shape.Angle1024}/{d.Shape.Length} range={d.Range} cast={d.CastTicks} cooldown={d.CooldownTicks} charges={d.Charges} cost={d.Cost.Amount} effects={d.Effects.Count}+{d.Variants?.Values.Sum(v => v.Count) ?? 0} tags={(d.Tags is { Count: > 0 } t ? string.Join(',', t) : "-")}");

    private static string Stats(Keyrun.Sim.Combat.AbilityDef? d) => d is null
        ? "shape=- cooldown=- cost=- damage=-"
        : FormattableString.Invariant(
            $"shape={d.Shape.Kind} cooldown={d.CooldownTicks} cost={d.Cost.Amount} damage={d.Effects.OfType<Keyrun.Sim.Combat.DamageOp>().FirstOrDefault()?.Amount ?? 0}");

    private void OnStepped(int tick, IReadOnlyList<Keyrun.Sim.Core.SimEvent> events)
    {
        if (_banked || _driver?.Run?.RunEnd.Result is not { } result)
        {
            return;
        }
        _banked = true;
        RecordRating(_driver.Run, result);
        // f-275d47c9: the bank records the key the run was played at (KeyMove.From, the run-end panel's own key read)
        // and its result in Progression.History, so Ascension marks are counted from the saved data.
        var played = KeyMove.ForRun(_driver.Run)!.From;
        _progression = _progression.BankRun(result, played);
        Proof(FormattableString.Invariant($"SCREEN bank result={result.ToJs()} ash={Ash.ForResult(result)} total={_progression.AshTotal}"));
        Proof(AscensionReadout.LogLine("SCREEN", Ascension.Of(_progression)));
        SaveProgression("bank");

        // M1 TIMER-TIERS (BIBLE s8): a timed run raises the key by its tier (+1, +2 within 80% of par, +3 within 60%);
        // a depleted or abandoned run keeps the key at the level it was played at (Matt 2026-10-20: no drop).
        // KeyMove is the one read of that move, shared with the run-end panel's key line, so the panel and the reopened
        // screen never disagree about the level. It uses the same tick fallback (ResultTick ?? RunTimerTicks) as main's
        // tier read, and clamps the raise to the key range.
        // LOBBY-TABS: the lobby comes back on its Play tab at that key, class and build kept (Lobby.ReturnFrom); before the
        // first open (an --autorun run) there is no lobby yet and the key waits for it.
        var move = KeyMove.ForRun(_driver.Run)!;
        var to = ScreenState.Open(SimDriver.DungeonId, ClauseIds(), move.To).KeyLevel;
        if (_lobby is { } lobby)
        {
            _lobby = lobby.ReturnFrom(_driver.Run);
            to = _lobby.Key.KeyLevel;
        }
        else
        {
            _pendingKey = to;
        }
        Proof(FormattableString.Invariant($"SCREEN key from={move.From} to={to} tier={move.Tier}"));
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
        if (_lobby is { } lobby)
        {
            // A new run: the Key band unlocks for the next queue, keeping the key, class and build the party chose.
            _lobby = lobby.Reopen(lobby.Key.KeyLevel);
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

    // BIBLE s7 persistence (lane PROGRESSION-SAVE): the banked progression is loaded once per launch and saved after
    // every change that keeps (the bank, a reward take, a reroll). ProgressionSavePlace picks the directory: user://
    // in the interactive game, --progression-dir when given, and nowhere for a headless run without it (scene tests
    // and CI never touch the real save). With no store this is exactly the old Progression.Start, and nothing prints.
    private void LoadProgression(long seed)
    {
        var dir = Keyrun.Content.ProgressionSavePlace.Directory(
            OS.GetCmdlineUserArgs(), DisplayServer.GetName() == "headless", ProjectSettings.GlobalizePath("user://"));
        if (dir is null)
        {
            _progression = Progression.Start(seed);
            return;
        }
        _store = new ProgressionStore(dir);
        _progression = _store.Load(seed);
        _ratingStore = new PrivateRatingStore(dir);
        _rating = _ratingStore.Load();
        Proof(FormattableString.Invariant(
            $"SCREEN progression load store=1 ash={_progression.AshTotal} runs={_progression.Runs} items={EquippedText()} path=\"{_store.FilePath}\""));
    }

    // The private M+ rating (f-a3230c59): a timed run's result is read, never changed (the key level is not touched),
    // and the record is saved beside the progression save. Nothing about it is printed but LogLine (no value).
    private void RecordRating(Keyrun.Sim.Runs.Run run, RunResult result)
    {
        var timed = Keyrun.Content.PrivateRating.FromResult(
            result == RunResult.Timed, run.RunEnd.ResultTick, run.Dungeon.Id, run.Encounter.KeyLevel, run.Encounter.ParTicks);
        if (timed is null)
        {
            return;
        }
        var next = _rating.Add(timed);
        if (ReferenceEquals(next, _rating))
        {
            return;
        }
        _rating = next;
        _ratingStore?.Save(_rating);
    }

    private void SaveProgression(string reason)
    {
        if (_store is null)
        {
            return;
        }
        var ok = _store.Save(_progression);
        Proof(FormattableString.Invariant(
            $"SCREEN progression save reason={reason} ok={(ok ? 1 : 0)} ash={_progression.AshTotal} runs={_progression.Runs} items={EquippedText()}"));
    }

    private string EquippedText()
    {
        var parts = Rewards.GearSlots
            .Where(slot => _progression.Equipped.ContainsKey(slot))
            .Select(slot =>
            {
                var item = _progression.Equipped[slot];
                return FormattableString.Invariant($"{slot}:t{item.Tier}:{item.Facet.Id}:c{item.Charges}");
            })
            .ToList();
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    // BIBLE s7 "Tier is power": one equipped item's stats from the content gear table, or null when content is not
    // loaded (the screen still lists the slot, with no numbers).
    private GearStats? SlotStats(string slot, int tier)
    {
        if (_driver?.Registry is not { } reg || reg.Gear.Count == 0)
        {
            return null;
        }
        var s = Keyrun.Content.GearItems.StatsAt(reg.Gear, Enum.Parse<Keyrun.Content.GearSlot>(slot, ignoreCase: true), tier);
        return new GearStats(s.DamageDoneBp, s.DamageTakenBp);
    }

    // The five slots' summed stats (GearPower.Sum), or null when content is not loaded.
    private GearStats? GearTotal()
    {
        if (_driver?.Registry is not { } reg || reg.Gear.Count == 0)
        {
            return null;
        }
        return GearPower.Sum(_progression.Equipped, (slot, tier) => SlotStats(slot, tier) ?? default);
    }

    // The character readout's five slots (equipped or empty) with their stats, then the summed total.
    private void ProofGear()
    {
        foreach (var slot in Rewards.GearSlots)
        {
            if (_progression.Equipped.TryGetValue(slot, out var item) && SlotStats(slot, item.Tier) is { } st)
            {
                Proof(FormattableString.Invariant(
                    $"SCREEN gear slot={slot} state=equipped tier={item.Tier} damageDoneBp={st.DamageDoneBp} damageTakenBp={st.DamageTakenBp}"));
            }
            else
            {
                Proof(FormattableString.Invariant(
                    $"SCREEN gear slot={slot} state={(_progression.Equipped.ContainsKey(slot) ? "equipped" : "empty")}"));
            }
        }
        var total = GearTotal() ?? default;
        Proof(FormattableString.Invariant(
            $"SCREEN gear total damageDoneBp={total.DamageDoneBp} damageTakenBp={total.DamageTakenBp}"));
    }

    // "+5% damage dealt, -2.5% damage taken" (basis points; empty when there is nothing).
    private static string GearTotalText(GearStats t)
    {
        static string Pct(long bp) => (Math.Abs(bp) / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
        var parts = new List<string>();
        if (t.DamageDoneBp != 0)
        {
            parts.Add((t.DamageDoneBp > 0 ? "+" : "-") + Pct(t.DamageDoneBp) + " damage dealt");
        }
        if (t.DamageTakenBp != 0)
        {
            parts.Add((t.DamageTakenBp < 0 ? "-" : "+") + Pct(t.DamageTakenBp) + " damage taken");
        }
        return string.Join(", ", parts);
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
        if (_lobby?.Key is not { } s)
        {
            return;
        }
        Proof(FormattableString.Invariant($"SCREEN key level={s.KeyLevel} locked={(s.Locked ? 1 : 0)}"));
        Proof(FormattableString.Invariant($"SCREEN affixes key={s.KeyLevel} line=\"{AffixLine(s.KeyLevel)}\""));
        // R1 (2026-10-21, BIBLE s8): the key sets the Clause; the band shows it, read-only, one line per key.
        var clause = s.KeyClause;
        var name = clause is null ? string.Empty : ClauseText(clause).Name;
        Proof(FormattableString.Invariant($"SCREEN clause key={s.KeyLevel} id={clause ?? "none"} name=\"{name}\" startKey={s.ClauseStartKey}"));
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

    private IReadOnlyList<string> ClauseIds() =>
        _driver?.Registry is { } reg ? reg.Clauses.Select(kv => kv.Key).ToList() : Array.Empty<string>();

    private string ClassName() => ClassName(ClassId);

    private string ClassName(string classId) =>
        _driver?.Registry is { } reg && reg.Classes.TryGetValue(classId, out var c) ? c.Name : classId;

    // The chosen class's keybind slots (all nine, 1-5 then Q E R F), from content: the class picked on the Character
    // tab, not the last run's setup.
    private IReadOnlyList<AbilitySlot> ClassSlots() =>
        _driver?.Registry is { } reg && reg.Classes.TryGetValue(ClassId, out var cls)
            ? HostRunSetup.ResolveSlots(cls)
            : Array.Empty<AbilitySlot>();

    private IEnumerable<(string Key, string Name)> Abilities() =>
        ClassSlots().Take(5).Select(s => (s.Key, _driver!.AbilityName(s.AbilityId)));

    // That slot's listed pool for the human's class: content Facets of the slot that patch one of the class kit's
    // abilities, ordered by id so the draw does not depend on content load order.
    private IReadOnlyList<Facet> SlotPool(string slot) =>
        _driver?.Registry is { } reg ? Lobby.SlotPool(reg, Lobby.Kit(reg, ClassId), slot) : Array.Empty<Facet>();

    /// <summary>
    /// One gear slot's facet pool for a class: the content Facets of <paramref name="slot"/> that patch an ability in
    /// <paramref name="kit"/>, ordered by id (load-order independent), as sim <see cref="Facet"/>s. The Screen's reroll
    /// pool and the run-end reward roll (RunEndView.Of, lane 3C-d) share it.
    /// </summary>
    /// <param name="reg">Content.</param>
    /// <param name="kit">The class kit's ability ids.</param>
    /// <param name="slot">A gear slot ("weapon", "armor", ...).</param>
    /// <returns>The pool, possibly empty.</returns>
    public static IReadOnlyList<Facet> SlotPool(Keyrun.Content.ContentRegistry reg, IReadOnlySet<string> kit, string slot) =>
        Lobby.SlotPool(reg, kit, slot);

    // The rects of every tab at a screen of `size`: the tab bar, then per tab its panels. Only the open tab's panels
    // are drawn; the Clause cards and the talent panel are child controls placed here and shown on their own tab only.
    private readonly record struct ScreenLayout(
        IReadOnlyList<Rect2> Tabs,
        Rect2 Classes,
        IReadOnlyList<Rect2> ClassCards,
        Rect2 Gear,
        Rect2 Kit,
        Rect2 Summary,
        Rect2 Key,
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

        // Play: summary | key band with the Clause cards | Queue.
        var summary = new Rect2(Margin, top, width * 0.28f, height);
        var key = new Rect2(summary.End.X + 16, top, width * 0.40f, height);
        var queue = new Rect2(key.End.X + 16, top, size.X - Margin - key.End.X - 16, 120);
        var keyDown = new Rect2(key.Position.X + 14, key.Position.Y + 44, 40, 40);
        var keyUp = new Rect2(key.Position.X + 150, key.Position.Y + 44, 40, 40);

        // Character: class cards | the five gear slots | the class kit.
        var classes = new Rect2(Margin, top, width * 0.28f, height);
        var cards = Enumerable.Range(0, 3)
            .Select(i => new Rect2(classes.Position.X + 12, classes.Position.Y + 40 + (i * 92), classes.Size.X - 24, 84))
            .ToList();
        var gear = new Rect2(classes.End.X + 16, top, width * 0.42f, height);
        var kit = new Rect2(gear.End.X + 16, top, size.X - Margin - gear.End.X - 16, height);

        // Build: the talent panel over the whole content area.
        var talents = new Rect2(Margin, top, width, height);
        return new ScreenLayout(tabs, classes, cards, gear, kit, summary, key, queue, keyDown, keyUp, talents);
    }

    private static Rect2 TalentsFor(Vector2 size) => LayoutFor(size).Talents;

    // f-0a21646d: one "SCREEN talent mark fork=<n> cursor=<ability> picked=<option> marker=<cards drawn PICKED>" line per
    // fork (or just `fork`), from the live build and cursor through TalentPanelMarks, the state the panel draws from.
    private void ProofTalentMarks(int? fork = null)
    {
        if (!TalentsOpen || _lobby?.Talents is not { } picks)
        {
            return;
        }
        for (var f = 0; f < picks.ForkCount; f++)
        {
            if (fork is null || fork == f)
            {
                Proof(Keyrun.Content.TalentPanelMarks.ProofLine(picks, f, TalentFork));
            }
        }
    }

    // One "SCREEN talent fork=..." line per fork and a fit line per option card, laid out at the design viewport.
    private void ProofTalents()
    {
        if (_lobby?.Talents is not { } picks)
        {
            return;
        }
        for (var f = 0; f < picks.ForkCount; f++)
        {
            var fork = picks.Forks[f];
            Proof(FormattableString.Invariant(
                $"SCREEN talent fork={f + 1} ability={fork.AbilityId} options={string.Join(',', fork.Options.Select(o => o.Id))} picked={picks.PickedOption(f) ?? "-"}"));
        }
        var panel = TalentsFor(DesignSize());
        var design = DesignSize();
        foreach (var card in TalentPanel.Cards(panel.Size, picks))
        {
            var text = picks.Forks[card.Fork].Options[card.Option].Text;
            var lines = TalentPanel.TextLines(text, card.Rect);
            var widest = lines.Count == 0 ? 0f : lines.Max(line => HudNodes.Width(line, TalentPanel.TextFont));
            var clipped = lines.Count > 0 && lines[^1].EndsWith("...", StringComparison.Ordinal) && !text.EndsWith("...", StringComparison.Ordinal) ? 1 : 0;
            var bottom = panel.Position.Y + card.Rect.Position.Y + TalentPanel.TextBottom(Math.Max(1, lines.Count));
            Proof(FormattableString.Invariant(
                $"SCREEN fit talent fork={card.Fork + 1} option={card.Option + 1} lines={lines.Count} widest={Math.Ceiling(widest)} box={Math.Floor(TalentPanel.TextWidthFor(card.Rect.Size.X))} clipped={clipped} text_bottom={Math.Ceiling(bottom)} card_bottom={Math.Floor(panel.Position.Y + card.Rect.End.Y)}"));
        }
        // f-0a21646d: the PICKED tag every card can carry stays right of the option name and above the description.
        foreach (var card in TalentPanel.Cards(panel.Size, picks))
        {
            var name = FormattableString.Invariant($"{card.Option + 1}  {picks.Forks[card.Fork].Options[card.Option].Name}");
            var nameRight = card.Rect.Position.X + Keyrun.Content.TalentCardLayout.TextInset + HudNodes.Width(name, TalentPanel.NameFont);
            var tag = TalentPanel.TagFor(card.Rect);
            Proof(FormattableString.Invariant(
                $"SCREEN fit talent mark fork={card.Fork + 1} option={card.Option + 1} name_right={Math.Ceiling(nameRight)} tag_left={Math.Floor(tag.Position.X)} tag_bottom={Math.Ceiling(tag.End.Y)} desc_top={Math.Floor(card.Rect.Position.Y + Keyrun.Content.TalentCardLayout.TextTop - TalentPanel.TextFont)}"));
        }
        ProofTalentMarks();
        Proof(FormattableString.Invariant(
            $"SCREEN fit talents right={Math.Ceiling(panel.End.X)} bottom={Math.Ceiling(panel.End.Y)} viewport={Math.Floor(design.X)}x{Math.Floor(design.Y)}"));
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

    // The title and the three tabs in the title row (the Dark and Darker lobby shape: one bar of tabs over one screen),
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
        LobbyTab.Build => "BUILD",
        _ => "PLAY",
    };

    private static string HintFor(LobbyTab tab) => tab switch
    {
        LobbyTab.Character => "Q/E tabs    Up/Down class    Tab item    R reroll Facet    Enter queue",
        LobbyTab.Build => "Q/E tabs    Up/Down fork    1-3 pick    Backspace respec    T back    Enter queue",
        _ => "Q/E tabs    Left/Right key    T talents    Enter queue",
    };

    // Character tab, left: one card per class (name, role, resource), the chosen one framed in gold.
    private void DrawClasses(ScreenLayout l)
    {
        Band(l.Classes, "Class");
        var ids = ClassIds();
        for (var i = 0; i < ids.Count && i < l.ClassCards.Count; i++)
        {
            var r = l.ClassCards[i];
            var on = ids[i] == ClassId;
            DrawRect(r, on ? TabOn : CardBg);
            DrawRect(r, on ? Gold : BronzeDim, filled: false, width: on ? 2f : 1f);
            HudNodes.Text(this, r.Position + new Vector2(12, 26), ClassName(ids[i]), 18, on ? Gold : Parchment);
            var role = HostRunSetup.Roster.FirstOrDefault(s => s.ClassId == ids[i])?.Role.ToString() ?? string.Empty;
            var loop = _driver?.Registry is { } reg && reg.Classes.TryGetValue(ids[i], out var cls) ? cls.Loop.Resource : string.Empty;
            HudNodes.Text(this, r.Position + new Vector2(12, 48), Clip($"{role}  -  {loop}", r.Size.X - 24, SmallFont), SmallFont, Dim);
            if (on)
            {
                HudNodes.Text(this, r.Position + new Vector2(12, 70), Locked() ? "Locked in for this queue" : "Playing", SmallFont, Good);
            }
        }
        var since = _progression.LastRun?.Text() ?? "First run";
        HudNodes.Text(this, new Vector2(l.Classes.Position.X + 14, l.Classes.End.Y - 14),
            Clip("Since last run: " + since, l.Classes.Size.X - 28, SmallFont), SmallFont, Calm);
        DrawAscension(l.Classes);
    }

    // Character tab, under the class cards and above "Since last run": the private Ascension line (BIBLE s7, f-275d47c9),
    // read from this screen's own progression (the one the run banks into and the save holds), never another seat's.
    private void DrawAscension(Rect2 classes)
    {
        var state = Ascension.Of(_progression);
        var lines = Wrap(AscensionReadout.CharacterLine(state), classes.Size.X - 28, SmallFont, 3);
        var y = classes.End.Y - 14 - 22 - ((lines.Count - 1) * 16);
        foreach (var line in lines)
        {
            HudNodes.Text(this, new Vector2(classes.Position.X + 14, y), line, SmallFont, state.Ascended ? Gold : Dim);
            y += 16;
        }
    }

    private bool Locked() => _lobby?.Locked == true;

    // Character tab, middle: the five gear slots (BIBLE s7), each with Tier, Facet and crafting charges, or empty. A Facet
    // on an ability outside the chosen class's kit is shown dim: it does nothing for this class.
    private void DrawGear(Rect2 r)
    {
        Band(r, "Gear");
        var x = r.Position.X + 14;
        if (GearTotal() is { } total && GearTotalText(total) is { Length: > 0 } totalText)
        {
            HudNodes.Text(this, new Vector2(x + 70, r.Position.Y + 24), Clip(totalText, r.Size.X - 90, SmallFont), SmallFont, Calm);
        }
        var kit = _driver?.Registry is { } reg ? Lobby.Kit(reg, ClassId) : new HashSet<string>();
        var slotHeight = Math.Min(96f, (r.Size.Y - 52) / Rewards.GearSlots.Count);
        for (var i = 0; i < Rewards.GearSlots.Count; i++)
        {
            var slot = Rewards.GearSlots[i];
            var box = new Rect2(x, r.Position.Y + 44 + (i * slotHeight), r.Size.X - 28, slotHeight - 8);
            var label = (char.ToUpperInvariant(slot[0]) + slot[1..]).ToUpperInvariant();
            var has = _progression.Equipped.TryGetValue(slot, out var item);
            var selected = has && slot == _selected;
            DrawRect(box, selected ? TabOn : CardBg);
            DrawRect(box, selected ? Good : BronzeDim, filled: false, width: selected ? 2f : 1f);
            HudNodes.Text(this, box.Position + new Vector2(10, 20), label, SmallFont, Dim);
            if (!has || item is null)
            {
                HudNodes.Text(this, box.Position + new Vector2(10, 42), "Empty", BodyFont, new Color(Parchment, 0.4f));
                continue;
            }
            var tier = FormattableString.Invariant($"Tier {item.Tier}");
            HudNodes.Text(this, box.Position + new Vector2(10, 42), tier, 18, TierColor(item.Tier));
            var charges = item.Charges == 1 ? "1 charge left" : FormattableString.Invariant($"{item.Charges} charges left");
            HudNodes.Text(this, box.Position + new Vector2(box.Size.X - 10 - HudNodes.Width(charges, SmallFont), 20), charges, SmallFont, Dim);
            var active = kit.Contains(item.Facet.AbilityId);
            var facet = active ? item.Facet.Text : item.Facet.Text + " (another class's Facet)";
            HudNodes.Text(this, box.Position + new Vector2(10, 62), Clip(facet, box.Size.X - 20, SmallFont), SmallFont, active ? Calm : Dim);
        }
    }

    // Tier colour: grey at 1, through green and blue, to gold at the cap of 10 (shape stays the same: the number is the text).
    private static Color TierColor(int tier) => tier switch
    {
        >= 10 => Gold,
        >= 7 => new Color(0.72f, 0.55f, 1f),
        >= 4 => new Color(0.45f, 0.7f, 1f),
        >= 2 => Good,
        _ => Parchment,
    };

    // Character tab, right: the chosen class's nine keybinds (1-5, Q interrupt, E defensive, R utility, F movement).
    private void DrawKit(Rect2 r)
    {
        Band(r, "Kit");
        var x = r.Position.X + 14;
        var y = r.Position.Y + 58;
        foreach (var slot in ClassSlots())
        {
            DrawRect(new Rect2(x, y - 15, 22, 20), CardBg);
            DrawRect(new Rect2(x, y - 15, 22, 20), BronzeDim, filled: false, width: 1f);
            HudNodes.Text(this, new Vector2(x + 6, y), slot.Key, BodyFont, Gold);
            HudNodes.Text(this, new Vector2(x + 32, y), Clip(_driver!.AbilityName(slot.AbilityId), r.Size.X - 60, BodyFont), BodyFont, Parchment);
            y += 26;
        }
    }

    // Play tab, left: who is queueing and what the last run moved.
    private void DrawSummary(Rect2 r)
    {
        Band(r, "Character");
        var x = r.Position.X + 14;
        var y = r.Position.Y + 58;
        HudNodes.Text(this, new Vector2(x, y), ClassName(), 20, Parchment);
        y += 22;
        var picks = _lobby?.Talents;
        HudNodes.Text(this, new Vector2(x, y), FormattableString.Invariant($"Talents {picks?.Count ?? 0}/{picks?.ForkCount ?? 0}    Gear {_progression.Equipped.Count}/{Rewards.GearSlots.Count}"), SmallFont, Dim);
        y += 26;
        var since = _progression.LastRun?.Text() ?? "First run";
        foreach (var line in Wrap("Since last run: " + since, r.Size.X - 28, BodyFont, 3))
        {
            HudNodes.Text(this, new Vector2(x, y), line, BodyFont, Calm);
            y += 18;
        }
        y += 12;
        foreach (var (keyLabel, name) in Abilities())
        {
            DrawRect(new Rect2(x, y - 15, 22, 20), CardBg);
            HudNodes.Text(this, new Vector2(x + 6, y), keyLabel, BodyFont, Gold);
            HudNodes.Text(this, new Vector2(x + 32, y), Clip(name, r.Size.X - 60, BodyFont), BodyFont, Parchment);
            y += 24;
        }
    }

    private void DrawKey(Rect2 r, ScreenState state)
    {
        Band(r, "Key");
        var l = Layout();
        foreach (var (rect, glyph) in new[] { (l.KeyDown, "<"), (l.KeyUp, ">") })
        {
            DrawRect(rect, new Color(CardBg, state.Locked ? 0.4f : 1f));
            DrawRect(rect, BronzeDim, filled: false, width: 1f);
            HudNodes.Text(this, rect.Position + new Vector2(14, 27), glyph, 20, Parchment);
        }
        HudNodes.Text(this, new Vector2(r.Position.X + 74, r.Position.Y + 76), FormattableString.Invariant($"Key {state.KeyLevel}"), TitleFont, Parchment);
        HudNodes.Text(this, new Vector2(r.Position.X + 14, r.Position.Y + 98), AffixLine(state.KeyLevel), SmallFont, Calm);
        // R1 (BIBLE s8): the key sets the Clause; the band shows it read-only, and says so below the start key.
        var label = state.KeyClause is { } clause
            ? "Clause: " + ClauseText(clause).Name
            : FormattableString.Invariant($"Clause: none below key {state.ClauseStartKey}");
        HudNodes.Text(this, new Vector2(r.Position.X + 14, r.Position.Y + 116), Clip(label, r.Size.X - 28, BodyFont), BodyFont, Gold);
    }

    /// <summary>
    /// Plan M5 (BIBLE s8): the key's affixes as the run will be built with them, "Affixes: Fortified" or "Affixes: none",
    /// from content run.json and the setup's rotation index (the same <see cref="Affixes.AffixesFor"/> the host scales
    /// spawns with), so the screen never shows an affix the run does not carry.
    /// </summary>
    /// <param name="keyLevel">The key on the band.</param>
    /// <returns>The line.</returns>
    public string AffixLine(int keyLevel)
    {
        if (_driver?.Registry is not { } reg)
        {
            return "Affixes: none";
        }
        var affixes = Affixes.AffixesFor(HostRunSetup.AffixTableOf(reg.Run.Affixes), keyLevel, _driver.Setup?.RotationIndex ?? 0);
        return affixes.Names.Count == 0 ? "Affixes: none" : "Affixes: " + string.Join(" + ", affixes.Names);
    }

    private void DrawQueue(Rect2 r, ScreenState state)
    {
        DrawRect(r, state.Locked ? new Color(0.16f, 0.22f, 0.15f) : new Color(0.3f, 0.2f, 0.08f));
        DrawRect(r, Gold, filled: false, width: 2f);
        DrawRect(new Rect2(r.Position + new Vector2(4, 4), r.Size - new Vector2(8, 8)), BronzeDim, filled: false, width: 1f);
        HudNodes.Text(this, r.Position + new Vector2(18, 48), state.Locked ? "QUEUED" : "QUEUE", 28, Gold);
        var clauseName = state.KeyClause is { } clause ? ClauseText(clause).Name : "no Clause";
        HudNodes.Text(this, r.Position + new Vector2(18, 80), Clip(FormattableString.Invariant($"{ClassName()}, key {state.KeyLevel} with {clauseName}"), r.Size.X - 36, BodyFont), BodyFont, Parchment);
        HudNodes.Text(this, r.Position + new Vector2(18, 102), "Enter or click", SmallFont, Dim);
        var y = r.End.Y + 24;
        foreach (var line in CaptionLines(r))
        {
            HudNodes.Text(this, new Vector2(r.Position.X, y), line, SmallFont, Calm);
            y += SmallFont + 4;
        }
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
