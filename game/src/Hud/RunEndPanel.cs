using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>One thing that hurt or downed a seat, aggregated per enemy kind (display name; Godot-free).</summary>
/// <param name="Seat">The player seat it hit.</param>
/// <param name="SourceId">The enemy actor id (the lowest of its kind when several actors share the name).</param>
/// <param name="SourceName">The enemy's display name.</param>
/// <param name="Damage">Health damage dealt to the seat (the damage events' <c>healthDamage</c>).</param>
/// <param name="Hits">Damage events.</param>
/// <param name="KillingBlow">True when this source's hit downed the seat (the sim's <c>death</c> event).</param>
public sealed record DeathCause(int Seat, int SourceId, string SourceName, long Damage, int Hits, bool KillingBlow)
{
    /// <summary>"HUD runend cause seat=1 source=100 name="Choir Blade" damage=280 hits=4 killing=1".</summary>
    public string LogLine() => FormattableString.Invariant(
        $"HUD runend cause seat={Seat} source={SourceId} name=\"{SourceName}\" damage={Damage} hits={Hits} killing={(KillingBlow ? 1 : 0)}");
}

/// <summary>
/// The private run log (BIBLE s10 "Death is not a scolding": each player privately sees what got them; ENGINE-PLAN
/// 3C): built from the run's SimEvents, keyed by the seat that was hit. <see cref="CausesFor"/> returns one seat's own
/// entries only, so a panel built for seat B can never show seat A's. RunLog.cs stays the harness JSONL; this is the
/// player-facing view. Godot-free.
/// </summary>
public sealed class PrivateRunLog
{
    private readonly Dictionary<(int Seat, int Source), (long Damage, int Hits, bool Killing, string Name)> _entries = new();

    /// <summary>Forgets everything (a fresh run).</summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// Records the damage and death events of one step whose target is a player seat in <paramref name="state"/>.
    /// </summary>
    /// <param name="events">The step's events.</param>
    /// <param name="state">The state after the step (actor kinds and source class ids).</param>
    /// <param name="nameOf">Display name of a class id.</param>
    public void Observe(IReadOnlyList<SimEvent> events, SimState state, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nameOf);
        foreach (var e in events)
        {
            var killing = e.Type == "death";
            var seatKey = killing ? "actorId" : e.Type == "damage" ? "targetId" : null;
            var amountKey = killing ? null : "healthDamage";
            if (seatKey is null || Int(e, seatKey) is not { } seat || Int(e, "sourceId") is not { } source)
            {
                continue;
            }
            if (!state.Actors.TryGetValue(seat, out var hit) || hit.Kind != ActorKind.Player)
            {
                continue;
            }
            var name = state.Actors.TryGetValue(source, out var src) ? nameOf(src.ClassId) : source.ToString(CultureInfo.InvariantCulture);
            var key = (seat, source);
            var prev = _entries.TryGetValue(key, out var p) ? p : (0L, 0, false, name);
            var amount = amountKey is not null && e.Get(amountKey) is JsInt n ? n.Value : 0L;
            _entries[key] = killing
                ? (prev.Item1, prev.Item2, true, prev.Item4)
                : (prev.Item1 + amount, prev.Item2 + 1, prev.Item3, prev.Item4);
        }
    }

    /// <summary>
    /// <paramref name="seat"/>'s own causes, one row per enemy kind (lane WHAT-GOT-YOU: actors sharing a display name
    /// merge, damage and hits summed, <see cref="CauseRows.MergeByName"/>): the killing blow first, then by damage
    /// descending, then by the kind's lowest source id.
    /// </summary>
    /// <param name="seat">The local player's seat.</param>
    /// <returns>Only entries whose seat is <paramref name="seat"/>.</returns>
    public IReadOnlyList<DeathCause> CausesFor(int seat) => CauseRows.MergeByName(_entries
            .Where(kv => kv.Key.Seat == seat)
            .OrderBy(kv => kv.Key.Source)
            .Select(kv => new CauseRow(kv.Key.Seat, kv.Key.Source, kv.Value.Name, kv.Value.Damage, kv.Value.Hits, kv.Value.Killing)))
        .Select(c => new DeathCause(c.Seat, c.SourceId, c.SourceName, c.Damage, c.Hits, c.KillingBlow))
        .ToList();

    private static int? Int(SimEvent e, string key) =>
        e.Get(key) is JsInt v && v.Value >= int.MinValue && v.Value <= int.MaxValue ? (int)v.Value : null;
}

/// <summary>
/// The run's <c>combo_denied</c> events for the private "which half was late" lines (BIBLE s4, lane COMBO-BLAME):
/// collected per step in emission order, reduced by <see cref="ComboBlame.For"/> to the local seat's own denials only.
/// Godot-free.
/// </summary>
public sealed class PrivateComboLog
{
    private readonly List<DeniedCombo> _denied = new();

    /// <summary>Forgets everything (a fresh run).</summary>
    public void Clear() => _denied.Clear();

    /// <summary>Records one step's <c>combo_denied</c> events (Payoff.cs TryClosePayoff and TryClosePayoffOnFloor).</summary>
    /// <param name="events">The step's events.</param>
    public void Observe(IReadOnlyList<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            if (e.Type != "combo_denied" || Int(e, "payoffActorId") is not { } payoff)
            {
                continue;
            }
            var setup = Int(e, "setupActorId") ?? 0;
            var window = e.Get("windowId") is JsString w ? w.Value : string.Empty;
            var reason = e.Get("reason") is JsString r ? r.Value : string.Empty;
            _denied.Add(new DeniedCombo(payoff, setup, window, reason));
        }
    }

    /// <summary>The seat's own lines (<see cref="ComboBlame.For"/>: deduped, capped, never another seat's).</summary>
    /// <param name="seat">The local player's seat.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<ComboBlameLine> LinesFor(int seat) => ComboBlame.For(seat, _denied);

    private static int? Int(SimEvent e, string key) =>
        e.Get(key) is JsInt v && v.Value >= int.MinValue && v.Value <= int.MaxValue ? (int)v.Value : null;
}

/// <summary>
/// The Seed's bonus loot for the run-end panel (BIBLE s9 item 3 "drops bonus loot", lanes SEED-LOOT-VIEW and
/// SEED-BONUS-REWARDS): the award the sim itself carries on <see cref="Run.Rewards"/> (<see cref="RunRoot.StepRun"/>
/// folds <see cref="SeedLoot.Observe"/> over every step, #197 SEED-LOOT-AWARD). The view never re-folds the events,
/// so the panel cannot disagree with the sim about what the run awarded. Godot-free.
/// </summary>
public sealed class SeedBonusLog
{
    /// <summary>The run's rewards as of the last step read (<see cref="RunRewards.Empty"/> until the Seed's Window opens).</summary>
    public RunRewards Rewards { get; private set; } = RunRewards.Empty;

    /// <summary>Forgets everything (a fresh run).</summary>
    public void Clear() => Rewards = RunRewards.Empty;

    /// <summary>Reads the sim's award after one step.</summary>
    /// <param name="run">The run as the step left it.</param>
    public void Observe(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Rewards = run.Rewards;
    }
}

/// <summary>What the run-end panel shows (Godot-free).</summary>
/// <param name="Result">How the run ended.</param>
/// <param name="ResultTick">The tick it ended.</param>
/// <param name="Title">The banner (client/src/hud/run-end.ts OUTCOME_LABEL, plus "Party wiped on pull N of T" for a
/// wipe, lane 3C-d).</param>
/// <param name="Cleared">True when every pull was cleared (the sim's pull index is past the last pull).</param>
/// <param name="Choices">The choice of three (BIBLE s7), each with its Facet text and tier.</param>
/// <param name="Causes">The local seat's own causes.</param>
public sealed record RunEndView(RunResult Result, int ResultTick, string Title, bool Cleared, IReadOnlyList<Item> Choices, IReadOnlyList<DeathCause> Causes)
{
    /// <summary>The run's bonus rewards (the Seed's loot, lane SEED-LOOT-VIEW); empty when none was earned.</summary>
    public RunRewards Bonus { get; init; } = RunRewards.Empty;

    /// <summary>
    /// The key move drawn right under the result (BIBLE s8, Matt 2026-10-20: timed moves the key by its chest tier,
    /// untimed holds it), or null for a view built without a run.
    /// </summary>
    public KeyMove? Key { get; init; }

    /// <summary>The line drawn under the result, "Bonus loot: ...", or null when <see cref="Bonus"/> has none.</summary>
    public string? BonusLabel { get; init; }

    /// <summary>The local seat's own denied Payoffs (lane COMBO-BLAME); empty when none, never another seat's.</summary>
    public IReadOnlyList<ComboBlameLine> LateCombos { get; init; } = Array.Empty<ComboBlameLine>();

    /// <summary>
    /// "Bonus loot: Seed drop -- Death Bloom (field) Combo" for each entry (the Combo's content name, else its id),
    /// or null when <paramref name="loot"/> is empty.
    /// </summary>
    /// <param name="loot">The run's bonus entries.</param>
    /// <param name="registry">Content for the Combo names, or null.</param>
    /// <returns>The label, or null.</returns>
    public static string? BonusLabelOf(IReadOnlyList<BonusLootEntry> loot, ContentRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(loot);
        if (loot.Count == 0)
        {
            return null;
        }
        var names = loot.Select(e =>
        {
            var source = e.Source.Length > 0 ? char.ToUpperInvariant(e.Source[0]) + e.Source[1..] : e.Source;
            var combo = registry is not null && registry.Combos.TryGetValue(e.ComboId, out var entry) ? entry.Name : e.ComboId;
            return FormattableString.Invariant($"{source} drop -- {combo} Combo");
        });
        return "Bonus loot: " + string.Join(", ", names);
    }

    /// <summary>
    /// The view for an ended run, else null. The choices are <see cref="Rewards.RollRewardChoices(long, int, RunResult, IReadOnlyList{string}, IReadOnlyDictionary{string, IReadOnlyList{Facet}})"/>
    /// over all five slots with the reward seed server/src/run-end.ts uses (run seed x 1000003 + the ending tick), each
    /// slot drawing over the human class's content facets (<see cref="Screen.SlotPool(ContentRegistry, IReadOnlySet{string}, string)"/>,
    /// lane 3C-d), so a card names a real facet and a kit ability. Without content (<paramref name="registry"/> null) the
    /// roll falls back to the placeholder pools.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="localSeat">The local player's seat.</param>
    /// <param name="log">The private run log.</param>
    /// <param name="registry">Content, or null (placeholder facets; the wipe title numbers pulls within the run's dungeon).</param>
    /// <param name="classKit">The local player's class kit ability ids (the facet pool's scope).</param>
    /// <param name="bonus">The run's Seed bonus fold, or null (no bonus loot).</param>
    /// <param name="combos">The run's denied Combos, or null (no late-combo lines).</param>
    /// <returns>The view, or null while the run is live.</returns>
    public static RunEndView? Of(Run run, int localSeat, PrivateRunLog log, ContentRegistry? registry, IReadOnlySet<string> classKit, SeedBonusLog? bonus = null, PrivateComboLog? combos = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(classKit);
        if (run.RunEnd.Result is not { } result || run.RunEnd.ResultTick is not { } tick)
        {
            return null;
        }
        var seed = unchecked((run.State.Seed * 1_000_003L) + tick);
        var choices = registry is null
            ? Rewards.RollRewardChoices(seed, run.Encounter.KeyLevel, result, Rewards.GearSlots)
            : Rewards.RollRewardChoices(seed, run.Encounter.KeyLevel, result, Rewards.GearSlots, Rewards.GearSlots.ToDictionary(s => s, s => Screen.SlotPool(registry, classKit, s), StringComparer.Ordinal));
        // The sim finishes a lasting wipe as "depleted" with completed forced (run-wiring.ts checkRunEnd), so the
        // result alone cannot tell a cleared depleted key from a wipe: the pull index past the last pull can.
        var cleared = run.Encounter.PullIndex >= run.Dungeon.Pulls.Count;
        // The pull lost on, numbered as the objective line numbers it (Objective.cs ObjectiveModel.Build: 1-based in
        // the full content dungeon, so a --pull 2 start that wipes in the Pair reads "pull 2 of 4", not the sliced
        // run dungeon's "1 of 3").
        var lost = cleared ? null : ObjectiveModel.Build(run, registry);
        // One source with the sim (RunEndTitle.ForRun -> RunEndTitle.Of): a timed clear that finished past par but
        // inside its Slack reads "over par", never "within par".
        var title = RunEndTitle.ForRun(run, lost?.Number ?? 0, lost?.Total ?? 0)!;
        var rewards = bonus?.Rewards ?? RunRewards.Empty;
        return new RunEndView(result, tick, title, cleared, choices, log.CausesFor(localSeat))
        {
            Bonus = rewards,
            BonusLabel = BonusLabelOf(rewards.BonusLoot, registry),
            LateCombos = combos?.LinesFor(localSeat) ?? Array.Empty<ComboBlameLine>(),
            Key = KeyMove.ForRun(run),
        };
    }

    /// <summary>The header line, the three choice lines and the cause lines, for the autorun proof.</summary>
    /// <returns>The lines.</returns>
    public IEnumerable<string> LogLines()
    {
        yield return FormattableString.Invariant($"HUD runend result={Result.ToJs()} choices={Choices.Count} cleared={(Cleared ? 1 : 0)} tick={ResultTick} title=\"{Title}\"");
        if (Key is { } key)
        {
            // "HUD runend key from=7 to=10 tier=3": the level the panel shows and the screen reopens at.
            yield return key.LogLine();
        }
        for (var i = 0; i < Choices.Count; i++)
        {
            var c = Choices[i];
            yield return FormattableString.Invariant(
                $"HUD runend choice n={i + 1} slot={c.Slot} tier={c.Tier} facet=\"{c.Facet.Text.Replace('"', '\'')}\" ability={c.Facet.AbilityId} charges={c.Charges} id={c.Id}");
        }
        foreach (var cause in Causes)
        {
            yield return cause.LogLine();
        }
        // Lane COMBO-BLAME: "HUD runend combo seat=1 window=W reason=R half=payoff count=N", the local seat's only.
        foreach (var late in LateCombos)
        {
            yield return late.LogLine();
        }
        // Lane SEED-LOOT-VIEW: "HUD runend bonus count=1 seedWindowEnds=4571 label="Bonus loot: ..."" (no label when
        // nothing was earned; seedWindowEnds=none when the run never saw the Seed's Window open).
        var ends = Bonus.SeedWindowEndsTick is { } e ? e.ToString(CultureInfo.InvariantCulture) : "none";
        var label = BonusLabel is { } l ? $" label=\"{l.Replace('"', '\'')}\"" : string.Empty;
        yield return FormattableString.Invariant($"HUD runend bonus count={Bonus.BonusLoot.Count} seedWindowEnds={ends}{label}");
    }
}

/// <summary>
/// The run-end panel (BIBLE s7 "End of every run: choose one of three"; ENGINE-PLAN 3C): the result, three reward
/// cards with slot, tier and Facet text (keys 1-3 take one), and the local player's private "what got you" list.
/// Enter opens the between-runs screen (SimDriver, lane 3D-c; under <c>--solo</c> it starts a fresh run). The choice is shown, not banked:
/// progression is the screen lane's (3D). Reads the sim state only.
/// </summary>
public partial class RunEndPanel : Control
{
    private const float PanelWidth = 780;
    private const float PanelHeight = 300;
    private const float CardGap = 16;
    private const int TitleFont = 24;
    private const int CardFont = 14;
    private const float BonusLineHeight = 22;
    private const float LineStep = CardFont + 6;

    private readonly PrivateRunLog _log = new();
    private readonly SeedBonusLog _bonus = new();
    private readonly PrivateComboLog _combos = new();
    private SimDriver? _driver;
    private RunEndView? _view;
    private int? _taken;

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>The party frames, a HUD block the panel keeps clear of (COMBATTEXT-OVERLAP).</summary>
    [Export]
    public NodePath PartyFramesPath { get; set; } = new("../PartyFrames");

    /// <summary>The choice taken this run (0-based), or null.</summary>
    public int? Taken => _taken;

    /// <summary>
    /// Lane COMBATTEXT-OVERLAP: the panel's viewport rectangle for a panel <paramref name="height"/> tall, laid out by
    /// <see cref="HudClearance.RunEndPanel"/> clear of the top bar band, the party frames and the ability bar
    /// footprint (the player's health and resource meters and the slot row), so it never covers them.
    /// </summary>
    /// <param name="height">The panel height (taller by one line when the run earned bonus loot).</param>
    /// <param name="ability">The ability bar footprint it avoided.</param>
    /// <param name="party">The party frames block it avoided (empty when there is none).</param>
    /// <returns>The panel rectangle in viewport pixels.</returns>
    private PixelRect LayoutPanel(int height, out PixelRect ability, out PixelRect party)
    {
        var size = GetViewport()?.GetVisibleRect().Size ?? Size;
        var w = (int)size.X;
        var h = (int)size.Y;
        var bar = AbilityBar.Footprint(size, (_driver?.Setup?.Slots.Count ?? 0) + 1);
        ability = new PixelRect((int)bar.Position.X, (int)bar.Position.Y, (int)Math.Ceiling(bar.Size.X), (int)Math.Ceiling(bar.Size.Y));
        party = _driver is null ? default : HudObstacles.PartyFrames(GetNodeOrNull<Control>(PartyFramesPath), _driver.State) ?? default;
        var avoid = new List<PixelRect> { HudLayout.TopBar(w, h).Band(w), ability, party };
        return HudClearance.RunEndPanel(w, h, (int)PanelWidth, height, avoid);
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _driver = HudNodes.FindDriver(this, DriverPath);
        if (_driver is not null)
        {
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
    public override void _Process(double delta) => QueueRedraw();

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_view is null || _taken is not null || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        var n = key.Keycode switch
        {
            Key.Key1 or Key.Kp1 => 0,
            Key.Key2 or Key.Kp2 => 1,
            Key.Key3 or Key.Kp3 => 2,
            _ => -1,
        };
        if (n >= 0 && n < _view.Choices.Count)
        {
            _taken = n;
            Screen.OpenFrom(this, _view.Choices[n]);
            if (_driver is not null)
            {
                HudNodes.Proof(_driver, FormattableString.Invariant($"HUD runend take n={n + 1} id={_view.Choices[n].Id}"));
            }
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (_view is not { } view)
        {
            return;
        }
        // Lane SEED-LOOT-VIEW: a run that earned bonus loot gets one more line under the result; the rest moves down.
        // The key line (the key's new level, BIBLE s8) sits directly under the result, above any bonus line.
        var keyShift = view.Key is null ? 0f : BonusLineHeight;
        var bonusShift = keyShift + (view.BonusLabel is null ? 0f : BonusLineHeight);
        var placed = LayoutPanel(HeightOf(view), out _, out _);
        var panel = new Rect2(new Vector2(placed.X, placed.Y) - GetGlobalRect().Position, placed.Width, placed.Height);
        DrawRect(panel, new Color(0.06f, 0.07f, 0.09f, 0.94f));
        DrawRect(panel, new Color(1f, 0.85f, 0.5f), filled: false, width: 2f);
        HudNodes.Text(this, panel.Position + new Vector2(20, 34), view.Title, TitleFont, new Color(1f, 0.85f, 0.5f));
        if (view.Key is { } keyMove)
        {
            // An upgrade reads green, a held key reads the calm body color.
            var keyColor = keyMove.To > keyMove.From ? new Color(0.5f, 1f, 0.6f) : new Color(0.85f, 0.88f, 0.95f);
            HudNodes.Text(this, panel.Position + new Vector2(20, 34 + BonusLineHeight), keyMove.Line, CardFont + 2, keyColor);
        }
        if (view.BonusLabel is { } bonusLabel)
        {
            HudNodes.Text(this, panel.Position + new Vector2(20, 34 + keyShift + BonusLineHeight), bonusLabel, CardFont, new Color(1f, 0.85f, 0.5f));
        }
        // Lane 3D-c: in the interactive game Enter opens the between-runs screen; only --solo restarts at once.
        var enter = _driver?.StartsOnScreen == true ? "Enter opens the between-runs screen" : "Enter starts a fresh run";
        var hint = _taken is null ? "1-3 take a reward    " + enter : enter;
        HudNodes.Text(this, new Vector2(panel.End.X - HudNodes.Width(hint, CardFont) - 20, panel.Position.Y + 30), hint, CardFont, Colors.White);

        var cardWidth = (PanelWidth - 40 - (2 * CardGap)) / 3;
        for (var i = 0; i < view.Choices.Count && i < 3; i++)
        {
            var c = view.Choices[i];
            var card = new Rect2(panel.Position.X + 20 + (i * (cardWidth + CardGap)), panel.Position.Y + 52 + bonusShift, cardWidth, 136);
            var picked = _taken == i;
            var dim = _taken is not null && !picked;
            DrawRect(card, new Color(0.12f, 0.13f, 0.17f, dim ? 0.5f : 1f));
            DrawRect(card, picked ? new Color(0.5f, 1f, 0.6f) : HudNodes.Edge, filled: false, width: picked ? 3f : 1f);
            var alpha = dim ? 0.45f : 1f;
            var slot = c.Slot.Length > 0 ? char.ToUpperInvariant(c.Slot[0]) + c.Slot[1..] : c.Slot;
            HudNodes.Text(this, card.Position + new Vector2(10, 22), FormattableString.Invariant($"{i + 1}  {slot}  Tier {c.Tier}"), 16, new Color(1f, 0.9f, 0.55f, alpha));
            // A Facet names its ability only when content knows it (the content pool's kit ids resolve, lane 3C-d; the
            // placeholder pool's ids, used only without content, are not shown).
            if (_driver?.Registry is { } reg && reg.Abilities.TryGetValue(c.Facet.AbilityId, out var abilityDef))
            {
                HudNodes.Text(this, card.Position + new Vector2(10, 42), abilityDef.Name, CardFont, new Color(0.7f, 0.85f, 1f, alpha));
            }
            var y = card.Position.Y + 62;
            foreach (var line in Wrap(c.Facet.Text, card.Size.X - 20, CardFont).Take(4))
            {
                HudNodes.Text(this, new Vector2(card.Position.X + 10, y), line, CardFont, new Color(1, 1, 1, alpha));
                y += CardFont + 4;
            }
            if (picked)
            {
                HudNodes.Text(this, new Vector2(card.Position.X + 10, card.End.Y - 8), "Taken", CardFont, new Color(0.5f, 1f, 0.6f));
            }
        }

        // The private run log: only this seat's own causes.
        var cy = panel.Position.Y + 210 + bonusShift;
        HudNodes.Text(this, new Vector2(panel.Position.X + 20, cy), view.Causes.Count == 0 ? "Nothing got you this run." : "What got you (only you see this):", CardFont, new Color(0.85f, 0.88f, 0.95f));
        foreach (var cause in view.Causes.Take(3))
        {
            cy += CardFont + 6;
            var text = FormattableString.Invariant(
                $"{cause.SourceName}: {cause.Damage} damage in {cause.Hits} hit{(cause.Hits == 1 ? string.Empty : "s")}{(cause.KillingBlow ? "  (downed you)" : string.Empty)}");
            HudNodes.Text(this, new Vector2(panel.Position.X + 32, cy), text, CardFont, cause.KillingBlow ? new Color(1f, 0.55f, 0.45f) : Colors.White);
        }

        // Lane COMBO-BLAME (BIBLE s4): which half of the pair was late, this seat's own denials only, under the causes'
        // three-line slot so the block sits at the same place whatever the cause count.
        if (view.LateCombos.Count > 0)
        {
            var ly = panel.Position.Y + 210 + bonusShift + (ComboBlame.MaxLines * LineStep) + LineStep + 6;
            HudNodes.Text(this, new Vector2(panel.Position.X + 20, ly), "Combos you were late on (only you see this):", CardFont, new Color(0.85f, 0.88f, 0.95f));
            foreach (var late in view.LateCombos)
            {
                ly += LineStep;
                HudNodes.Text(this, new Vector2(panel.Position.X + 32, ly), late.Text(), CardFont, new Color(1f, 0.75f, 0.45f));
            }
        }
    }

    /// <summary>The panel height: the base, one bonus line when loot was earned, and the late-combo block when any.</summary>
    /// <param name="view">The view.</param>
    /// <returns>Pixels.</returns>
    private static int HeightOf(RunEndView view)
    {
        var bonus = (view.BonusLabel is null ? 0f : BonusLineHeight) + (view.Key is null ? 0f : BonusLineHeight);
        var combos = view.LateCombos.Count == 0 ? 0f : ((view.LateCombos.Count + 1) * LineStep) + 12;
        return (int)(PanelHeight + bonus + combos);
    }

    private static IEnumerable<string> Wrap(string text, float width, int size)
    {
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && HudNodes.Width(next, size) > width)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = next;
            }
        }
        if (line.Length > 0)
        {
            yield return line;
        }
    }

    private void OnRunStarted()
    {
        _log.Clear();
        _bonus.Clear();
        _combos.Clear();
        _view = null;
        _taken = null;
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_driver?.Run is not { } run || _driver.Setup is not { } setup)
        {
            return;
        }
        var registry = _driver.Registry;
        _log.Observe(events, run.State, classId => Names.Actor(registry, classId));
        _bonus.Observe(run);
        _combos.Observe(events);
        if (_view is not null || run.RunEnd.Result is null)
        {
            return;
        }
        // The human's class kit scopes the reward facet pool (the Screen's reroll pool, Screen.SlotPool).
        var kit = new HashSet<string>(setup.Slots.Select(s => s.AbilityId), StringComparer.Ordinal);
        if (RunEndView.Of(run, setup.HumanActorId, _log, registry, kit, _bonus, _combos) is { } view)
        {
            _view = view;
            foreach (var line in view.LogLines())
            {
                HudNodes.Proof(_driver, line);
            }
            var panel = LayoutPanel(HeightOf(view), out var ability, out var party);
            var size = GetViewport()?.GetVisibleRect().Size ?? Size;
            HudNodes.Proof(_driver, FormattableString.Invariant(
                $"HUD runend layout panel={HudClearance.Format(panel)} abilitybar={HudClearance.Format(ability)} party={HudClearance.Format(party)} viewport={(int)size.X}x{(int)size.Y}"));
        }
    }
}
