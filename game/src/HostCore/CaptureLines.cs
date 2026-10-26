using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Host;

/// <summary>
/// The capture-log line formatter for the TICK, ACTOR and GROUP lines (R-AGGRO-1 / G7): one set of line shapes shared by the
/// Godot run log (<c>game/src/Host/RunLog.cs</c>) and the headless playtest log (<c>PlaytestRunner.Play</c>), so both emit the
/// same lines. Godot-free: no engine types, no clock, no randomness. Integers only (mm and ticks).
/// <para>
/// Shapes (each line starts with its kind):
/// <list type="bullet">
/// <item><c>TICK n [warden=(x,z) yaw=y hp=h res=r] events=E</c>: the pre-step sim tick <c>n</c>, the Warden after the step
/// when it is in the state, and the step's event count.</item>
/// <item><c>ACTOR tick=n id=i class=c x=X z=Z engaged=0|1 holder=H|none</c> per living enemy (engaged: its aggro group has
/// engaged). Any other living actor carries <c>holder=H|none</c> only.</item>
/// <item><c>GROUP tick=n id=g pull=p engaged=0|1 ring_mm=R center=(x,z) actors=N</c> per aggro group (pull, pack or patrol);
/// <c>R</c> is the group's aggro ring radius.</item>
/// </list>
/// The positions are the state after the step; the tick label is the pre-step tick the commands and events carry, so a
/// frame label (seconds x 20) maps to the tick whose state it shows.
/// </para>
/// </summary>
public static class CaptureLines
{
    /// <summary>
    /// The TICK line for one step: the pre-step tick, the Warden after the step (omitted when <paramref name="wardenId"/> is
    /// not in the state), and the event count.
    /// </summary>
    /// <param name="tick">The pre-step tick.</param>
    /// <param name="after">The state after the step.</param>
    /// <param name="wardenId">The Warden's actor id.</param>
    /// <param name="eventCount">The step's event count.</param>
    /// <returns>The line.</returns>
    public static string TickLine(int tick, SimState after, int wardenId, int eventCount)
    {
        ArgumentNullException.ThrowIfNull(after);
        return after.Actors.TryGetValue(wardenId, out var w)
            ? FormattableString.Invariant($"TICK {tick} warden=({w.Pos.X},{w.Pos.Z}) yaw={w.Yaw} hp={w.Health} res={w.Resource} events={eventCount}")
            : FormattableString.Invariant($"TICK {tick} events={eventCount}");
    }

    /// <summary>
    /// The ACTOR lines of one step: one per living actor in the state's actor order (ascending id), with its class, integer
    /// position and either <c>engaged=</c> plus <c>holder=</c> (a living enemy: engaged is 1 when the aggro group it belongs to
    /// has engaged) or <c>holder=</c> alone (any other living actor).
    /// </summary>
    /// <param name="tick">The pre-step tick the line is labelled with.</param>
    /// <param name="after">The state after the step.</param>
    /// <param name="encounter">The encounter state after the step (the engaged groups).</param>
    /// <param name="groups">The run's aggro groups for the tick (<see cref="Aggro.GroupsOf"/>).</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> ActorLines(int tick, SimState after, EncounterState encounter, IReadOnlyList<AggroGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(groups);
        var groupOf = new Dictionary<int, AggroGroup>();
        foreach (var g in groups)
        {
            foreach (var id in g.ActorIds)
            {
                groupOf.TryAdd(id, g);
            }
        }
        var lines = new List<string>();
        foreach (var a in after.Actors.Values)
        {
            if (!a.Alive)
            {
                continue;
            }
            var holder = a.HolderId is { } h ? h.ToString(CultureInfo.InvariantCulture) : "none";
            var head = FormattableString.Invariant($"ACTOR tick={tick} id={a.Id} class={a.ClassId} x={a.Pos.X} z={a.Pos.Z} ");
            if (a.Kind == ActorKind.Enemy)
            {
                var engaged = groupOf.TryGetValue(a.Id, out var g) && Pulls.IsEngaged(encounter, g.Id) ? 1 : 0;
                lines.Add(head + FormattableString.Invariant($"engaged={engaged} holder={holder}"));
            }
            else
            {
                lines.Add(head + "holder=" + holder);
            }
        }
        return lines;
    }

    /// <summary>
    /// The GROUP lines of one step: one per aggro group, in <see cref="Aggro.GroupsOf"/> order, with its pull index, engaged
    /// flag, ring radius in mm (<c>ring_mm</c>), ring centre and member count.
    /// </summary>
    /// <param name="tick">The pre-step tick the line is labelled with.</param>
    /// <param name="encounter">The encounter state after the step.</param>
    /// <param name="groups">The run's aggro groups for the tick.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> GroupLines(int tick, EncounterState encounter, IReadOnlyList<AggroGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(groups);
        var lines = new List<string>(groups.Count);
        foreach (var g in groups)
        {
            var engaged = Pulls.IsEngaged(encounter, g.Id) ? 1 : 0;
            lines.Add(FormattableString.Invariant(
                $"GROUP tick={tick} id={g.Id} pull={g.PullIndex} engaged={engaged} ring_mm={g.AggroMm} center=({g.Center.X},{g.Center.Z}) actors={g.ActorIds.Count}"));
        }
        return lines;
    }

    /// <summary>
    /// Every capture line of one step, in order: the TICK line, then the ACTOR lines, then the GROUP lines.
    /// </summary>
    /// <param name="tick">The pre-step tick.</param>
    /// <param name="wardenId">The Warden's actor id.</param>
    /// <param name="after">The state after the step.</param>
    /// <param name="encounter">The encounter state after the step.</param>
    /// <param name="groups">The run's aggro groups for the tick.</param>
    /// <param name="eventCount">The step's event count.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> StepLines(int tick, int wardenId, SimState after, EncounterState encounter, IReadOnlyList<AggroGroup> groups, int eventCount)
    {
        var lines = new List<string> { TickLine(tick, after, wardenId, eventCount) };
        lines.AddRange(ActorLines(tick, after, encounter, groups));
        lines.AddRange(GroupLines(tick, encounter, groups));
        return lines;
    }

    /// <summary>Appends each line to a builder, LF-terminated.</summary>
    /// <param name="sb">The builder.</param>
    /// <param name="lines">The lines.</param>
    public static void AppendLines(StringBuilder sb, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(lines);
        foreach (var line in lines)
        {
            sb.Append(line).Append('\n');
        }
    }
}
