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
/// <param name="Teaches">What the pull teaches (content), or empty.</param>
/// <param name="Remaining">Enemies of this pull still alive.</param>
/// <param name="Count">Enemies the pull spawned.</param>
/// <param name="Complete">True once the pull index is past the last pull (the run is cleared).</param>
public sealed record ObjectiveLine(int Number, int Total, string PullId, string Name, string Teaches, int Remaining, int Count, bool Complete)
{
    /// <summary>The line a player reads: "Pull 1/4  The Choir - the kick ledger".</summary>
    public string Title => Complete
        ? FormattableString.Invariant($"{Name} cleared")
        : Teaches.Length > 0
            ? FormattableString.Invariant($"Pull {Number}/{Total}  {Name} - {Teaches}")
            : FormattableString.Invariant($"Pull {Number}/{Total}  {Name}");

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
            return new ObjectiveLine(total, total, "done", content?.Name ?? run.Dungeon.Id, string.Empty, 0, 0, true);
        }
        var pull = pulls[index];
        var number = contentIds.IndexOf(pull.Id) + 1;
        if (number <= 0)
        {
            number = index + 1;
        }
        var name = pull.Id;
        var teaches = string.Empty;
        if (content is not null)
        {
            var authored = content.Pulls.FirstOrDefault(p => p.Id == pull.Id);
            if (authored is not null)
            {
                name = authored.Name;
                teaches = authored.Teaches;
            }
            else if (pull.Id == "boss" && registry!.Enemies.TryGetValue(content.Boss.EnemyId, out var boss))
            {
                name = boss.Name;
                teaches = string.Join(", ", content.Boss.Mechanics);
            }
        }
        var ids = pull.Spawns.Select(s => s.ActorId).ToHashSet();
        var remaining = run.State.Actors.Values.Count(a => ids.Contains(a.Id) && a.Alive && a.Kind == ActorKind.Enemy);
        // An actor not spawned yet is still owed: count the spawns the state does not hold yet.
        remaining += ids.Count(id => !run.State.Actors.ContainsKey(id));
        return new ObjectiveLine(number, total, pull.Id, name, teaches, remaining, ids.Count, false);
    }
}

/// <summary>
/// The objective line (ENGINE-PLAN 3C): the current pull's number, content name and what it teaches, and how many of
/// its enemies remain, top left under the key help. Reads the sim state only.
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
        var width = Math.Max(HudNodes.Width(line.Title, TitleFont), HudNodes.Width(line.Task, TaskFont)) + 20;
        DrawRect(new Rect2(0, 0, width, 50), HudNodes.Back);
        HudNodes.Text(this, new Vector2(10, 21), line.Title, TitleFont, new Color(1f, 0.9f, 0.55f));
        HudNodes.Text(this, new Vector2(10, 42), line.Task, TaskFont, Colors.White);
    }

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
