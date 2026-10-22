using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>The between-runs lobby screen (Character, Build and Play tabs); its lane history and design
/// summary is in game/src/Hud/CLAUDE.md, and the members are split across Screen.*.cs partials by tab and concern.</summary>
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
    // T, 1, Down, 2, Backspace, 1, T, then Enter). The Key band does not pick a Clause (BIBLE s8, R1: the key sets it).
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
}
