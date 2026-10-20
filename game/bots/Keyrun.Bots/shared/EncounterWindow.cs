using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Casting = Keyrun.Sim.Combat.Casting;

namespace Keyrun.Bots.Shared;

/// <summary>
/// SEED-BLOOM-TEACHES (finding f-add02b73; design/BIBLE.md section 9.6 item 3 and section 4). An encounter floor Window
/// is a floor patch an enemy opened for the party to close: its setup class is <see cref="EncounterSetupClass"/>, the
/// class the Lock uses too (content/data/combos.json "seedbloom-field" and "lock-*"). In the POC that is the Seed's Death
/// Bloom. The sim marks such a patch hostile for the oracle's floor read (its opener is an enemy, so floor:&lt;windowId&gt;),
/// which made bots flee it and then follow the leader out, so the Window always expired with no Combo and the Seed
/// never taught the boss's core rule. This reads the patch from the floor state the view draws (WindowRingView) instead.
/// While one is open, a bot that holds a Payoff it could land there walks into the ring and lands it, ahead of travel and
/// of its role. C#-only and layout-gated like the travel it outranks (Follow), so it is inert in the oracle runner,
/// whose view carries no layout.
/// </summary>
public static class EncounterWindow
{
    /// <summary>The setup class of a Window an encounter opens for the party (the Seed's bloom, the Lock).</summary>
    public const string EncounterSetupClass = "boss";

    /// <summary>A bot walks in until it is within this share of the ring's radius (basis points), so it lands inside.</summary>
    public const long InsideBp = 5000;

    /// <summary>
    /// The bot's encounter-Window commands, or null when none applies. On a layout run, for the first open encounter
    /// floor Window (JS key order): outside the ring, a move toward its centre; inside, the first ready, affordable
    /// Payoff that can resolve here (a non-target shape, or a target shape with a living enemy in range), or an empty
    /// list (hold in the ring) while none is ready yet. Null for a bot whose kit has no Payoff it could land here, so
    /// such a bot keeps its travel and role.
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="self">The bot.</param>
    /// <returns>The commands (possibly empty, meaning hold), or null.</returns>
    public static IReadOnlyList<Command>? Decide(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        if (view.Layout is null || view.Kits.Content is not { } reg)
        {
            return null;
        }
        var state = view.State;
        var tick = state.Tick;
        foreach (var key in state.FloorWindows.Keys)
        {
            var patch = state.FloorWindows[key];
            if (patch.EndsTick <= tick || !string.Equals(patch.SetupClass, EncounterSetupClass, StringComparison.Ordinal))
            {
                continue;
            }
            var kit = RoleShared.KitOf(view, self.ClassId);
            var usable = kit.PayoffAbilityIds
                .Where(id => reg.Abilities.ContainsKey(id))
                .Select(id => (Id: id, Target: TargetFor(state, self, reg.Abilities[id])))
                .Where(p => p.Target.Usable)
                .ToList();
            if (usable.Count == 0)
            {
                return null;
            }
            var selfMm = PointMm.Of(self.Pos);
            var dx = patch.Pos.X - self.Pos.X;
            var dz = patch.Pos.Z - self.Pos.Z;
            var inside = patch.RadiusMm * InsideBp / 10000;
            if ((dx * dx) + (dz * dz) > inside * inside)
            {
                var move = Moves.MoveToward(tick, self.Id, selfMm, PointMm.Of(patch.Pos));
                return move is null ? Array.Empty<Command>() : new Command[] { move };
            }
            foreach (var (id, target) in usable)
            {
                if (RoleShared.ReadyAndAffordable(self, id, tick, kit))
                {
                    return new Command[] { new CastCommand(tick, self.Id, id, target.TargetId) };
                }
            }
            return Array.Empty<Command>();
        }
        return null;
    }

    private readonly record struct PayoffTarget(bool Usable, int? TargetId);

    // A non-target Payoff resolves anywhere (its floor check is where its caster stands); a target one needs a living
    // enemy in range: the bot's own target when that qualifies, else the nearest (lowest id on a tie).
    private static PayoffTarget TargetFor(SimState state, ActorState self, AbilityDef def)
    {
        if (def.Shape.Kind != ShapeKind.Target)
        {
            return new PayoffTarget(true, null);
        }
        if (self.TargetId is { } own && state.Actors.TryGetValue(own, out var cur) && cur.Alive && cur.Kind == ActorKind.Enemy &&
            Casting.RangeMm(self, cur) <= def.Range)
        {
            return new PayoffTarget(true, own);
        }
        ActorState? best = null;
        long bestRange = 0;
        foreach (var a in state.Actors.Values)
        {
            if (!a.Alive || a.Kind != ActorKind.Enemy)
            {
                continue;
            }
            var range = Casting.RangeMm(self, a);
            if (range <= def.Range && (best is null || range < bestRange))
            {
                best = a;
                bestRange = range;
            }
        }
        return best is null ? new PayoffTarget(false, null) : new PayoffTarget(true, best.Id);
    }
}
