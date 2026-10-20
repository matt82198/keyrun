using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;
using PixelRect = Keyrun.Content.PixelRect;

namespace Keyrun.Hud;

/// <summary>One name plate this frame (Godot-free): its base name, its box where its body projects, and the target mark.</summary>
/// <param name="Name">The display name ("Paired Warrior").</param>
/// <param name="Rect">The plate box at its body's projected spot, viewport pixels.</param>
/// <param name="Targeted">True for the Warden's current target.</param>
public sealed record PlateCandidate(string Name, PixelRect Rect, bool Targeted);

/// <summary>One drawn plate and the same-name plates folded into it (input indices, leader first).</summary>
/// <param name="Leader">The input index of the plate that draws.</param>
/// <param name="Members">Every input index in the crowd, the leader first.</param>
public sealed record PlateCrowd(int Leader, IReadOnlyList<int> Members);

/// <summary>
/// Lane HUD-DECLUTTER (Matt 2026-10-19: "move friendly casts into party frames btw, only show the played characters
/// damage numbers in fight, reduce screen clutter essentially... enemy health does not need to be overhead, they can
/// have unitframes but not just a random number"). The Godot-free placement rules every HUD and view node reads, so
/// what a node draws and the proof line it prints come from the same decision. Display only; never a rule.
/// </summary>
public static class HudDeclutter
{
    /// <summary>
    /// True when <paramref name="actor"/>'s cast bar is drawn over its head: an enemy that no unit frame shows. A party
    /// member's cast (the human's and the bots') is drawn inside its party frame. NAMEPLATE-DECLUTTER (Matt 2026-10-19,
    /// "reduce screen clutter"): an enemy on a unit frame (the boss, the Warden's target) has its cast drawn on that
    /// frame only, never a second time over its head; every other enemy keeps its over-head bar (BIBLE s3, "Cast bars
    /// are the enemy's language").
    /// </summary>
    /// <param name="actor">The casting actor.</param>
    /// <param name="frames">This frame's enemy unit frames (<see cref="UnitFrameModel"/>.Build).</param>
    /// <returns>True for an overhead bar.</returns>
    public static bool OverheadCast(ActorState actor, IReadOnlyList<UnitFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(frames);
        if (actor.Kind == ActorKind.Player)
        {
            return false;
        }
        foreach (var f in frames)
        {
            if (f.ActorId == actor.Id)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>"overhead" or "frame": where <paramref name="actor"/>'s cast bar is drawn.</summary>
    /// <param name="actor">The casting actor.</param>
    /// <param name="frames">This frame's enemy unit frames.</param>
    /// <returns>The placement word the proof lines print.</returns>
    public static string CastPlacement(ActorState actor, IReadOnlyList<UnitFrame> frames) =>
        OverheadCast(actor, frames) ? "overhead" : "frame";

    /// <summary>
    /// True when <paramref name="e"/> spawns a floating damage number in the world: a "damage" event whose source is
    /// the human's seat and whose modified amount (health damage plus absorbed) is positive. A bot's hit, an enemy's
    /// hit and a zero hit spawn nothing.
    /// </summary>
    /// <param name="e">One sim event of the step.</param>
    /// <param name="humanActorId">The played character's actor id.</param>
    /// <param name="sourceId">The hitting actor (the event's sourceId).</param>
    /// <param name="targetId">The hit actor.</param>
    /// <param name="amount">The number to show.</param>
    /// <returns>True when a number is shown.</returns>
    public static bool WorldDamageNumber(SimEvent e, int humanActorId, out int sourceId, out int targetId, out long amount)
    {
        ArgumentNullException.ThrowIfNull(e);
        sourceId = e.Get("sourceId") is JsInt s ? (int)s.Value : 0;
        targetId = 0;
        amount = 0;
        if (e.Type != "damage" || e.Get("sourceId") is not JsInt source || source.Value != humanActorId ||
            e.Get("targetId") is not JsInt target || e.Get("modifiedAmount") is not JsInt modified || modified.Value <= 0)
        {
            return false;
        }
        targetId = (int)target.Value;
        amount = modified.Value;
        return true;
    }

    /// <summary>
    /// NAMEPLATE-DECLUTTER: the plate crowds of one frame. Plates arrive nearest the camera first; the target's plate
    /// leads first, then the rest in order. A plate whose box shares a pixel with an earlier leader's box of the SAME
    /// name joins that leader's crowd (five Paired Warriors on one spot are one crowd); a plate with a different name
    /// never joins (a Twin beside the boss keeps its own plate and is spread by HudLayout.Separate).
    /// </summary>
    /// <param name="plates">This frame's plates where their bodies project, nearest the camera first.</param>
    /// <returns>One crowd per drawn plate, leaders in input order; Members holds input indices, leader first.</returns>
    public static IReadOnlyList<PlateCrowd> PlateCrowds(IReadOnlyList<PlateCandidate> plates)
    {
        ArgumentNullException.ThrowIfNull(plates);
        var order = new List<int>(plates.Count);
        for (var i = 0; i < plates.Count; i++)
        {
            if (plates[i].Targeted)
            {
                order.Add(i);
            }
        }
        for (var i = 0; i < plates.Count; i++)
        {
            if (!plates[i].Targeted)
            {
                order.Add(i);
            }
        }
        var crowds = new List<List<int>>();
        foreach (var i in order)
        {
            var joined = false;
            foreach (var crowd in crowds)
            {
                var lead = plates[crowd[0]];
                if (string.Equals(lead.Name, plates[i].Name, StringComparison.Ordinal) && lead.Rect.Intersects(plates[i].Rect))
                {
                    crowd.Add(i);
                    joined = true;
                    break;
                }
            }
            if (!joined)
            {
                crowds.Add(new List<int> { i });
            }
        }
        crowds.Sort((a, b) => a[0].CompareTo(b[0]));
        return crowds.ConvertAll(c => new PlateCrowd(c[0], c));
    }

    /// <summary>
    /// The text a crowd's one plate draws: the name, "x N" when N same-name plates share the spot, wrapped in
    /// "&gt; ... &lt;" when the crowd holds the Warden's target.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="count">How many plates the crowd holds.</param>
    /// <param name="targeted">True when the target is in the crowd.</param>
    /// <returns>The plate text.</returns>
    public static string PlateText(string name, int count, bool targeted)
    {
        ArgumentNullException.ThrowIfNull(name);
        var text = count > 1 ? FormattableString.Invariant($"{name} x{count}") : name;
        return targeted ? "> " + text + " <" : text;
    }

    /// <summary>
    /// NAMEPLATE-DECLUTTER: true when a SPREAD/STACK call also draws a label in the world. SPREAD does not: its one
    /// text is the callout banner (the marked players' party frames carry the SPREAD chip), so the screen never shows
    /// "SPREAD: move apart" twice. STACK keeps its world label: it marks the spot to group up on.
    /// </summary>
    /// <param name="label">"SPREAD" or "STACK".</param>
    /// <returns>True when the call draws a world label.</returns>
    public static bool WorldPingLabel(string label) => !string.Equals(label, "SPREAD", StringComparison.Ordinal);

    /// <summary>"player" or "enemy", as the proof lines print an actor's side.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The side word.</returns>
    public static string Side(ActorState actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.Kind == ActorKind.Player ? "player" : "enemy";
    }
}
