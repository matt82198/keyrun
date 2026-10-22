using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// ABILITY-ANIMS (Matt 2026-10-21 "theres still no ability animations"): the ability side of
/// <see cref="ActorAnimator"/>. It names the clip each ability plays from <see cref="AbilityClipTable"/> (content, pure:
/// the release one-shot on the sim's <c>cast_complete</c>, the cast loop for the bar), reads which ability a step's
/// events resolved or kicked, and writes one <c>ANIM actor=N clip=X ability=Y</c> line per clip start to the run log
/// (through the host's log sink, so a capture's log proves every start; GD.Print never reaches it). Read-only: it never
/// writes sim state and never touches materials or alpha.
/// </summary>
public sealed class AbilityClipPlayer
{
    private readonly Action<string>? _log;

    /// <param name="log">The run-log sink (SimDriver.HostLine), or null to print to the console only.</param>
    public AbilityClipPlayer(Action<string>? log) => _log = log;

    /// <summary>The release clip name for <paramref name="abilityId"/> (the row's swing for an unmapped or null id).</summary>
    public static string ReleaseName(string? abilityId, string rowAct) => AbilityClipTable.ReleaseClip(abilityId, rowAct);

    /// <summary>The cast-loop clip name for <paramref name="abilityId"/>, or null for the row's own cast loop.</summary>
    public static string? CastName(string? abilityId) => AbilityClipTable.CastClip(abilityId);

    /// <summary>Writes the proof line for <paramref name="clip"/> starting on <paramref name="actorId"/>.</summary>
    public void Started(int actorId, string clip, string? abilityId)
    {
        var line = AbilityClipTable.AnimLine(actorId, clip, abilityId);
        if (_log is null)
        {
            GD.Print(line);
            return;
        }
        _log(line);
    }

    /// <summary>The ability id of this step's <c>cast_complete</c> for <paramref name="actorId"/>, or null.</summary>
    public static string? Resolved(int actorId, IReadOnlyList<SimEvent>? events) =>
        Find(events, AnimRule.ResolveEvent, "actorId", actorId);

    /// <summary>The ability id of the cast this step's <c>interrupt</c> cut on <paramref name="actorId"/>, or null.</summary>
    public static string? Kicked(int actorId, IReadOnlyList<SimEvent>? events) =>
        Find(events, AnimRule.InterruptEvent, "targetId", actorId);

    private static string? Find(IReadOnlyList<SimEvent>? events, string type, string idField, int actorId)
    {
        if (events is null)
        {
            return null;
        }
        foreach (var e in events)
        {
            if (e.Type == type && e.Get(idField) is JsInt id && id.Value == actorId)
            {
                return e.Get("abilityId") is JsString ability ? ability.Value : null;
            }
        }
        return null;
    }
}
