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

/// <summary>One thing that hurt or downed a seat, aggregated per enemy (Godot-free).</summary>
/// <param name="Seat">The player seat it hit.</param>
/// <param name="SourceId">The enemy actor id.</param>
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
    /// <paramref name="seat"/>'s own causes: the killing blow first, then by damage descending, then by source id.
    /// </summary>
    /// <param name="seat">The local player's seat.</param>
    /// <returns>Only entries whose seat is <paramref name="seat"/>.</returns>
    public IReadOnlyList<DeathCause> CausesFor(int seat) => _entries
        .Where(kv => kv.Key.Seat == seat)
        .Select(kv => new DeathCause(kv.Key.Seat, kv.Key.Source, kv.Value.Name, kv.Value.Damage, kv.Value.Hits, kv.Value.Killing))
        .OrderByDescending(c => c.KillingBlow)
        .ThenByDescending(c => c.Damage)
        .ThenBy(c => c.SourceId)
        .ToList();

    private static int? Int(SimEvent e, string key) =>
        e.Get(key) is JsInt v && v.Value >= int.MinValue && v.Value <= int.MaxValue ? (int)v.Value : null;
}

/// <summary>What the run-end panel shows (Godot-free).</summary>
/// <param name="Result">How the run ended.</param>
/// <param name="ResultTick">The tick it ended.</param>
/// <param name="Title">The banner (client/src/hud/run-end.ts OUTCOME_LABEL, plus "Party wiped" for a wipe).</param>
/// <param name="Cleared">True when every pull was cleared (the sim's pull index is past the last pull).</param>
/// <param name="Choices">The choice of three (BIBLE s7), each with its Facet text and tier.</param>
/// <param name="Causes">The local seat's own causes.</param>
public sealed record RunEndView(RunResult Result, int ResultTick, string Title, bool Cleared, IReadOnlyList<Item> Choices, IReadOnlyList<DeathCause> Causes)
{
    /// <summary>
    /// The view for an ended run, else null. The choices are <see cref="Rewards.RollRewardChoices"/> over all five
    /// slots with the reward seed server/src/run-end.ts uses (run seed x 1000003 + the ending tick), so a host and the
    /// server roll the same three.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="localSeat">The local player's seat.</param>
    /// <param name="log">The private run log.</param>
    /// <returns>The view, or null while the run is live.</returns>
    public static RunEndView? Of(Run run, int localSeat, PrivateRunLog log)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(log);
        if (run.RunEnd.Result is not { } result || run.RunEnd.ResultTick is not { } tick)
        {
            return null;
        }
        var seed = unchecked((run.State.Seed * 1_000_003L) + tick);
        var choices = Rewards.RollRewardChoices(seed, run.Encounter.KeyLevel, result, Rewards.GearSlots);
        // The sim finishes a lasting wipe as "depleted" with completed forced (run-wiring.ts checkRunEnd), so the
        // result alone cannot tell a cleared depleted key from a wipe: the pull index past the last pull can.
        var cleared = run.Encounter.PullIndex >= run.Dungeon.Pulls.Count;
        var title = (result, cleared) switch
        {
            (RunResult.Abandoned, _) => "Run abandoned",
            (_, false) => "Party wiped -- key depleted",
            (RunResult.Timed, true) => "Run cleared -- within par",
            _ => "Run cleared -- key depleted",
        };
        return new RunEndView(result, tick, title, cleared, choices, log.CausesFor(localSeat));
    }

    /// <summary>The header line, the three choice lines and the cause lines, for the autorun proof.</summary>
    /// <returns>The lines.</returns>
    public IEnumerable<string> LogLines()
    {
        yield return FormattableString.Invariant($"HUD runend result={Result.ToJs()} choices={Choices.Count} cleared={(Cleared ? 1 : 0)} tick={ResultTick} title=\"{Title}\"");
        for (var i = 0; i < Choices.Count; i++)
        {
            var c = Choices[i];
            yield return FormattableString.Invariant(
                $"HUD runend choice n={i + 1} slot={c.Slot} tier={c.Tier} facet=\"{c.Facet.Text.Replace('"', '\'')}\" ability={c.Facet.AbilityId} charges={c.Charges}");
        }
        foreach (var cause in Causes)
        {
            yield return cause.LogLine();
        }
    }
}

/// <summary>
/// The run-end panel (BIBLE s7 "End of every run: choose one of three"; ENGINE-PLAN 3C): the result, three reward
/// cards with slot, tier and Facet text (keys 1-3 take one), and the local player's private "what got you" list.
/// Enter starts a fresh run (SimDriver already restarts an ended run on Enter). The choice is shown, not banked:
/// progression is the screen lane's (3D). Reads the sim state only.
/// </summary>
public partial class RunEndPanel : Control
{
    private const float PanelWidth = 780;
    private const float PanelHeight = 300;
    private const float CardGap = 16;
    private const int TitleFont = 24;
    private const int CardFont = 14;

    private readonly PrivateRunLog _log = new();
    private SimDriver? _driver;
    private RunEndView? _view;
    private int? _taken;

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>The choice taken this run (0-based), or null.</summary>
    public int? Taken => _taken;

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
        var panel = new Rect2((Size.X - PanelWidth) / 2, (Size.Y * 0.37f), PanelWidth, PanelHeight);
        DrawRect(panel, new Color(0.06f, 0.07f, 0.09f, 0.94f));
        DrawRect(panel, new Color(1f, 0.85f, 0.5f), filled: false, width: 2f);
        HudNodes.Text(this, panel.Position + new Vector2(20, 34), view.Title, TitleFont, new Color(1f, 0.85f, 0.5f));
        var hint = _taken is null ? "1-3 take a reward    Enter starts a fresh run" : "Enter starts a fresh run";
        HudNodes.Text(this, new Vector2(panel.End.X - HudNodes.Width(hint, CardFont) - 20, panel.Position.Y + 30), hint, CardFont, Colors.White);

        var cardWidth = (PanelWidth - 40 - (2 * CardGap)) / 3;
        for (var i = 0; i < view.Choices.Count && i < 3; i++)
        {
            var c = view.Choices[i];
            var card = new Rect2(panel.Position.X + 20 + (i * (cardWidth + CardGap)), panel.Position.Y + 52, cardWidth, 136);
            var picked = _taken == i;
            var dim = _taken is not null && !picked;
            DrawRect(card, new Color(0.12f, 0.13f, 0.17f, dim ? 0.5f : 1f));
            DrawRect(card, picked ? new Color(0.5f, 1f, 0.6f) : HudNodes.Edge, filled: false, width: picked ? 3f : 1f);
            var alpha = dim ? 0.45f : 1f;
            var slot = c.Slot.Length > 0 ? char.ToUpperInvariant(c.Slot[0]) + c.Slot[1..] : c.Slot;
            HudNodes.Text(this, card.Position + new Vector2(10, 22), FormattableString.Invariant($"{i + 1}  {slot}  Tier {c.Tier}"), 16, new Color(1f, 0.9f, 0.55f, alpha));
            // A Facet names its ability only when content knows it (the reward pool's placeholder ids are not shown).
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
        var cy = panel.Position.Y + 210;
        HudNodes.Text(this, new Vector2(panel.Position.X + 20, cy), view.Causes.Count == 0 ? "Nothing got you this run." : "What got you (only you see this):", CardFont, new Color(0.85f, 0.88f, 0.95f));
        foreach (var cause in view.Causes.Take(3))
        {
            cy += CardFont + 6;
            var text = FormattableString.Invariant(
                $"{cause.SourceName}: {cause.Damage} damage in {cause.Hits} hit{(cause.Hits == 1 ? string.Empty : "s")}{(cause.KillingBlow ? "  (downed you)" : string.Empty)}");
            HudNodes.Text(this, new Vector2(panel.Position.X + 32, cy), text, CardFont, cause.KillingBlow ? new Color(1f, 0.55f, 0.45f) : Colors.White);
        }
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
        if (_view is null && RunEndView.Of(run, setup.HumanActorId, _log) is { } view)
        {
            _view = view;
            foreach (var line in view.LogLines())
            {
                HudNodes.Proof(_driver, line);
            }
        }
    }
}
