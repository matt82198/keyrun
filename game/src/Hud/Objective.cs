using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>The objective line for the current pull (Godot-free).</summary>
/// <param name="Number">1-based position of the current pull in the dungeon (boss last).</param>
/// <param name="Total">Pulls in the dungeon, boss included.</param>
/// <param name="PullId">The sim pull id ("choir", ..., "boss").</param>
/// <param name="Name">The pull's content display name (the boss's enemy name for the boss pull).</param>
/// <param name="Remaining">Enemies of this pull still alive.</param>
/// <param name="Count">Enemies the pull spawned.</param>
/// <param name="Complete">True once the pull index is past the last pull (the run is cleared).</param>
public sealed record ObjectiveLine(int Number, int Total, string PullId, string Name, int Remaining, int Count, bool Complete)
{
    /// <summary>
    /// The line a player reads: "Pull 1/4  The Choir" (<see cref="PullHeader.Title"/>; the content <c>teaches</c> note
    /// is designer intent and is never shown).
    /// </summary>
    public string Title => Complete
        ? FormattableString.Invariant($"{Name} cleared")
        : new PullHeader(Number, Total, Name).Title;

    /// <summary>The task under it: "Defeat the pull: 3 of 3 remain".</summary>
    public string Task => Complete ? "No enemies remain" : FormattableString.Invariant($"Defeat the pull: {Remaining} of {Count} remain");

    /// <summary>"HUD objective pull=1/4 id=choir name="The Choir" remaining=3/3".</summary>
    public string LogLine() => FormattableString.Invariant(
        $"HUD objective pull={Number}/{Total} id={PullId} name=\"{Name}\" remaining={Remaining}/{Count}{(Complete ? " complete" : string.Empty)}");
}

/// <summary>Builds the objective line from the run (Godot-free).</summary>
public static class ObjectiveModel
{
    /// <summary>
    /// The current pull: the sim's <c>Encounter.PullIndex</c> into the run's dungeon, named and numbered from the
    /// content dungeon by pull id (a host that starts at a later pull still numbers it in the full dungeon).
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="registry">Content, or null (ids only, numbered within the run's dungeon).</param>
    /// <returns>The line.</returns>
    public static ObjectiveLine Build(Run run, ContentRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(run);
        var pulls = run.Dungeon.Pulls;
        var content = registry is not null && registry.Dungeons.TryGetValue(run.Dungeon.Id, out var d) ? d : null;
        var contentIds = content is null ? pulls.Select(p => p.Id).ToList() : content.Pulls.Select(p => p.Id).Append("boss").ToList();
        var total = contentIds.Count;
        var index = run.Encounter.PullIndex;
        if (index >= pulls.Count)
        {
            return new ObjectiveLine(total, total, "done", content?.Name ?? run.Dungeon.Id, 0, 0, true);
        }
        var pull = pulls[index];
        var number = contentIds.IndexOf(pull.Id) + 1;
        if (number <= 0)
        {
            number = index + 1;
        }
        var name = content is not null && PullHeader.Of(registry!, content, pull.Id) is { } header ? header.Name : pull.Id;
        var ids = pull.Spawns.Select(s => s.ActorId).ToHashSet();
        var remaining = run.State.Actors.Values.Count(a => ids.Contains(a.Id) && a.Alive && a.Kind == ActorKind.Enemy);
        // An actor not spawned yet is still owed: count the spawns the state does not hold yet.
        remaining += ids.Count(id => !run.State.Actors.ContainsKey(id));
        return new ObjectiveLine(number, total, pull.Id, name, remaining, ids.Count, false);
    }
}

/// <summary>
/// The objective line (ENGINE-PLAN 3C): the current pull's number and content name (never its designer teaches note),
/// and how many of its enemies remain, top left under the key help. Reads the sim state only.
/// </summary>
public partial class Objective : Control
{
    private const int TitleFont = 18;
    private const int TaskFont = 14;

    private SimDriver? _driver;
    private string? _logged;

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _driver = HudNodes.FindDriver(this, DriverPath);
        if (_driver is not null)
        {
            _driver.Stepped += OnStepped;
            _driver.RunStarted += OnRunStarted;
            OnRunStarted();
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
    public override void _Draw()
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        var line = ObjectiveModel.Build(run, _driver.Registry);
        // HUD polish (ART-PLAN lane 8): the panel lives in its HudLayout.TopBar slot, left of the Slack bar, and its
        // lines are clipped with "..." to the slot instead of running under the bar. This node spans the HUD root, so
        // the slot is in viewport pixels.
        var bar = HudLayout.TopBar((int)Size.X, (int)Size.Y);
        var title = HudLayout.FitTitle(line.Title, bar.TitleMaxWidth, s => Measure(s, TitleFont));
        var task = HudLayout.FitTitle(line.Task, bar.TitleMaxWidth, s => Measure(s, TaskFont));
        var slot = bar.Objective;
        var width = HudLayout.PanelWidth(Measure(title, TitleFont), Measure(task, TaskFont), bar);
        DrawRect(new Rect2(slot.X, slot.Y, width, slot.Height), HudNodes.Back);
        HudNodes.Text(this, new Vector2(slot.X + HudLayout.PanelPad, slot.Y + 19), title, TitleFont, new Color(1f, 0.9f, 0.55f));
        HudNodes.Text(this, new Vector2(slot.X + HudLayout.PanelPad, slot.Y + 38), task, TaskFont, Colors.White);
    }

    private static int Measure(string text, int size) => (int)Math.Ceiling(HudNodes.Width(text, size));

    private void OnRunStarted()
    {
        _logged = null;
        Log();
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events) => Log();

    private void Log()
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        var text = ObjectiveModel.Build(run, _driver.Registry).LogLine();
        if (text != _logged)
        {
            _logged = text;
            HudNodes.Proof(_driver, text);
        }
    }
}
