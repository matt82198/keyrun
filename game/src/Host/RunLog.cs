using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Host;

/// <summary>
/// The arena's run log: one <c>TICK n</c> line per step plus one <c>EVENT {json}</c> line per sim event
/// (canonical JSON, the trace format ORACLE.md uses), the host lines <see cref="CombatNarrator"/> derives from state
/// (PULL, CHASE, CAST), and free-form lines (RUN, FRAME, AUTORUN). Lines go to the Godot console and, with
/// <c>--log FILE</c>, to that file (UTF-8, no BOM, LF). TICK lines reach the console only in autorun, so interactive
/// play is not flooded at 20 lines a second. This is the harness log (BIBLE s10: not player-facing); the private
/// player-facing views of the same events are Hud/RunEndPanel.cs PrivateRunLog (what got you) and PrivateComboLog
/// (which half of a Combo was late, from combo_denied, BIBLE s4).
/// </summary>
public sealed class RunLog : IDisposable
{
    private readonly StreamWriter? _file;
    private readonly bool _ticksToConsole;
    private readonly CombatNarrator _narrator;
    private bool _disposed;

    /// <summary>Opens the log.</summary>
    /// <param name="path">File to write, or null for console only. Its directory is created if missing.</param>
    /// <param name="ticksToConsole">Echo TICK lines to the console too.</param>
    /// <param name="registry">Content, for display names in CAST lines (null = ids only).</param>
    public RunLog(string? path, bool ticksToConsole, ContentRegistry? registry)
    {
        _ticksToConsole = ticksToConsole;
        _narrator = new CombatNarrator(registry);
        if (!string.IsNullOrEmpty(path))
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _file = new StreamWriter(full, append: false, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }
    }

    /// <summary>Writes one free-form line to the console and the file.</summary>
    /// <param name="text">The line.</param>
    public void Line(string text)
    {
        GD.Print(text);
        Write(text);
    }

    /// <summary>Logs a pull's spawn (createRun's pull 0 has no pull_start event).</summary>
    /// <param name="pullIndex">The pull index.</param>
    /// <param name="dungeon">The dungeon.</param>
    public void PullSpawn(int pullIndex, SimDungeon dungeon)
    {
        if (CombatNarrator.PullSpawnLine(pullIndex, dungeon) is { } line)
        {
            Line(line);
        }
    }

    /// <summary>Logs every group standing idle at run creation (<see cref="CombatNarrator.IdleGroupLines"/>).</summary>
    /// <param name="run">The created run.</param>
    public void IdleGroups(Keyrun.Sim.Runs.Run run)
    {
        foreach (var line in CombatNarrator.IdleGroupLines(run))
        {
            Line(line);
        }
    }

    /// <summary>Resets the per-run narration (chase and cast tracking) for a fresh run.</summary>
    /// <param name="initial">The run's initial state.</param>
    public void Begin(SimState initial) => _narrator.Reset(initial);

    /// <summary>Logs one step: its pre-step tick, the Warden after it, every event, and the derived host lines.</summary>
    /// <param name="tick">The pre-step tick (the tick the commands and events carry).</param>
    /// <param name="before">The state before the step.</param>
    /// <param name="after">The state after the step.</param>
    /// <param name="events">The step's events, in emission order.</param>
    /// <param name="dungeon">The dungeon (names the pulls a pull_start spawns).</param>
    public void Tick(int tick, SimState before, SimState after, IReadOnlyList<SimEvent> events, SimDungeon dungeon)
    {
        var line = after.Actors.TryGetValue(SimDriver.WardenId, out var w)
            ? FormattableString.Invariant($"TICK {tick} warden=({w.Pos.X},{w.Pos.Z}) yaw={w.Yaw} hp={w.Health} res={w.Resource} events={events.Count}")
            : FormattableString.Invariant($"TICK {tick} events={events.Count}");
        if (_ticksToConsole)
        {
            GD.Print(line);
        }
        Write(line);
        foreach (var e in events)
        {
            Line("EVENT " + e.ToCanonicalJson());
        }
        foreach (var host in _narrator.Narrate(before, after, events, dungeon))
        {
            Line(host);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _file?.Dispose();
    }

    private void Write(string text)
    {
        if (!_disposed)
        {
            _file?.WriteLine(text);
        }
    }
}

/// <summary>
/// Derives the human-readable host lines the sim has no event for (Godot-free):
/// <list type="bullet">
/// <item><c>PULL spawn index=i id=p actors=id:class,...</c> when a pull spawns (pull_start, or createRun's pull 0).</item>
/// <item><c>CHASE actor=id dist=a-&gt;b</c> the first step an enemy moves and ends nearer the Warden.</item>
/// <item><c>CAST start actor=id ability=x name="N" ends=t target=id|none</c> when an actor's cast begins (the sim carries it only as
/// state: CastId and CastEndsTick; it emits no cast_start event).</item>
/// <item><c>CAST done actor=id ability=x name="N"</c> for each cast_complete event.</item>
/// <item><c>CAST stopped actor=id ability=x name="N"</c> for each cast_stopped event and for a cast cleared by an
/// interrupt.</item>
/// </list>
/// </summary>
public sealed class CombatNarrator
{
    private readonly ContentRegistry? _registry;
    private readonly HashSet<int> _chased = new();

    /// <summary>Builds a narrator.</summary>
    /// <param name="registry">Content for display names, or null.</param>
    public CombatNarrator(ContentRegistry? registry) => _registry = registry;

    /// <summary>Forgets the per-run chase record.</summary>
    /// <param name="initial">The run's initial state (unused today; kept so a reset names its run).</param>
    public void Reset(SimState initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _chased.Clear();
    }

    /// <summary>The spawn line of a pull, or null for an index outside the dungeon.</summary>
    /// <param name="pullIndex">The pull index.</param>
    /// <param name="dungeon">The dungeon.</param>
    /// <returns>The line, or null.</returns>
    public static string? PullSpawnLine(int pullIndex, SimDungeon dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        if (pullIndex < 0 || pullIndex >= dungeon.Pulls.Count)
        {
            return null;
        }
        var pull = dungeon.Pulls[pullIndex];
        var actors = string.Join(",", pull.Spawns.Select(s => FormattableString.Invariant($"{s.ActorId}:{s.ClassId}")));
        return FormattableString.Invariant($"PULL spawn index={pullIndex} id={pull.Id} actors={actors}");
    }

    /// <summary>
    /// PRESPAWN-AGGRO and PATH-PACKS (BIBLE section 9.1): on a layout run every group, each pull and each optional pack,
    /// stands idle in the state from tick 0, but a pull's <c>PULL spawn</c> line comes only on its engage and a pack
    /// has none (its <c>pack_engage</c> EVENT comes only if the party engages it). Without these lines nothing in the
    /// log names the enemies of a group the party never engaged (a skipped pack), so a reader of the log could not
    /// count the enemies the sim still holds. One <c>GROUP idle id=g pull=i actors=id:class,...</c> line per group not
    /// engaged at creation (<see cref="Keyrun.Sim.Encounter.Aggro.GroupsOf"/> order; pull -1 is a pack), naming only
    /// the actors the state holds. Empty on a non-layout run (pulls spawn on their pull_start there).
    /// </summary>
    /// <param name="run">The created run.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> IdleGroupLines(Keyrun.Sim.Runs.Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var lines = new List<string>();
        if (run.Content.Layout is null)
        {
            return lines;
        }
        foreach (var g in Keyrun.Sim.Encounter.Aggro.GroupsOf(run.Dungeon, run.State.Tick, run.Content.Packs))
        {
            if (Keyrun.Sim.Encounter.Pulls.IsEngaged(run.Encounter, g.Id))
            {
                continue;
            }
            var present = g.ActorIds
                .Where(id => run.State.Actors.ContainsKey(id))
                .Select(id => FormattableString.Invariant($"{id}:{run.State.Actors[id].ClassId}"))
                .ToList();
            if (present.Count > 0)
            {
                lines.Add(FormattableString.Invariant($"GROUP idle id={g.Id} pull={g.PullIndex} actors={string.Join(",", present)}"));
            }
        }
        return lines;
    }

    /// <summary>The host lines for one step.</summary>
    /// <param name="before">The state before the step.</param>
    /// <param name="after">The state after the step.</param>
    /// <param name="events">The step's events.</param>
    /// <param name="dungeon">The dungeon.</param>
    /// <returns>The lines, in a fixed order: pulls, chases, cast starts, cast completions and stops.</returns>
    public IReadOnlyList<string> Narrate(SimState before, SimState after, IReadOnlyList<SimEvent> events, SimDungeon dungeon)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(events);
        var lines = new List<string>();
        foreach (var e in events)
        {
            if (e.Type == "pull_start" && e.Get("pullIndex") is JsInt idx && PullSpawnLine((int)idx.Value, dungeon) is { } spawn)
            {
                lines.Add(spawn);
            }
        }

        if (before.Actors.TryGetValue(SimDriver.WardenId, out var w0) && after.Actors.TryGetValue(SimDriver.WardenId, out var w1))
        {
            foreach (var (id, now) in after.Actors)
            {
                if (now.Kind != ActorKind.Enemy || !now.Alive || _chased.Contains(id) || !before.Actors.TryGetValue(id, out var prev))
                {
                    continue;
                }
                var d0 = Casting.RangeMm(prev, w0);
                var d1 = Casting.RangeMm(now, w1);
                // The enemy itself moved (not only the Warden walking closer) and the gap shrank. One rule with the
                // --write-movie chase frame (MovieDirector.IsChase).
                if (MovieDirector.IsChase(prev, now, w0, w1))
                {
                    _chased.Add(id);
                    lines.Add(FormattableString.Invariant($"CHASE actor={id} class={now.ClassId} dist={d0}->{d1}"));
                }
            }
        }

        foreach (var (id, now) in after.Actors)
        {
            if (now.CastId is not { } castId)
            {
                continue;
            }
            var prev = before.Actors.TryGetValue(id, out var p) ? p : null;
            if (prev?.CastId != castId || prev.CastStartTick != now.CastStartTick)
            {
                // R-PILE-10 (HudSceneTest check 8): name the target the cast started on, so a log reader can prove no
                // cast starts on a dead player (Casting.StartCast sets TargetId; a mid-cast retarget pins CastTargetId).
                var target = now.CastTargetPinned ? now.CastTargetId : now.TargetId;
                lines.Add(FormattableString.Invariant(
                    $"CAST start actor={id} ability={castId} name=\"{Names.Ability(_registry, castId)}\" ends={now.CastEndsTick} target={(target is { } t ? t.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}"));
            }
        }

        foreach (var e in events)
        {
            if (e.Type is "cast_complete" or "cast_stopped" && e.Get("actorId") is JsInt actor && e.Get("abilityId") is JsString ability)
            {
                var verb = e.Type == "cast_complete" ? "done" : "stopped";
                lines.Add(FormattableString.Invariant(
                    $"CAST {verb} actor={actor.Value} ability={ability.Value} name=\"{Names.Ability(_registry, ability.Value)}\""));
            }
            else if (e.Type == "interrupt" && e.Get("targetId") is JsInt target && e.Get("abilityId") is JsString kicked)
            {
                lines.Add(FormattableString.Invariant(
                    $"CAST stopped actor={target.Value} ability={kicked.Value} name=\"{Names.Ability(_registry, kicked.Value)}\" by={(e.Get("sourceId") is JsInt s ? s.Value : 0)}"));
            }
        }
        return lines;
    }
}
