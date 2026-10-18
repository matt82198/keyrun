using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/healer.ts, the healer state machine: kick the priority cast, flee a hostile floor patch
/// while staying within heal range of the party centroid, swap a stance loop into Mend when the party's missing
/// health crosses the threshold, dispel before healing, then heal the lowest ally under the threshold through the
/// shape-aware picker (<see cref="HealPick"/>).
/// </summary>
public sealed class HealerRole : IRole
{
    /// <summary>healer.ts DISPELLABLE_PREFIX: the local convention for a dispellable effect id.</summary>
    public const string DispellablePrefix = "dispellable:";

    /// <summary>healer.ts HEAL_RANGE_MM: 30 m.</summary>
    public const long HealRangeMm = 30 * 1000;

    private const double HealThresholdPct = 0.7;
    private const double PartyMendThresholdPct = 0.35;

    /// <summary>healer.ts MEND_VERSE_VALUE: resource 1 is Mend, 0 is Ward.</summary>
    private const long MendVerseValue = 1;

    /// <summary>The shared instance.</summary>
    public static HealerRole Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<Command> Decide(BotView view, ActorState self) => HealerDecide(view, self);

    private static double PartyMissingHealthRatio(SortedDictionary<int, ActorState> actors)
    {
        var allies = actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive).ToList();
        if (allies.Count == 0)
        {
            return 0;
        }
        double totalMax = 0;
        double totalMissing = 0;
        foreach (var a in allies)
        {
            totalMax += a.MaxHealth;
        }
        if (totalMax <= 0)
        {
            return 0;
        }
        foreach (var a in allies)
        {
            totalMissing += Math.Max(0, a.MaxHealth - a.Health);
        }
        return totalMissing / totalMax;
    }

    /// <summary>healer.ts healerDecide.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <returns>Commands, possibly empty.</returns>
    public static IReadOnlyList<Command> HealerDecide(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        var actors = view.State.Actors;
        var kit = RoleShared.KitOf(view, self.ClassId);

        if (RoleShared.LedgerKickCommand(view, self, kit) is { } kick)
        {
            return new Command[] { kick };
        }

        var party = actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != self.Id).ToList();
        var escapeOpts = party.Count > 0
            ? new FloorEscapeOptions { KeepNear = new KeepNear(RoleShared.Centroid(party), HealRangeMm) }
            : new FloorEscapeOptions();
        if (Floor.FloorEscapeCommand(view, self, escapeOpts) is { } escape)
        {
            return new Command[] { escape };
        }

        if (kit.LoopKind == LoopKind.Stance
            && !string.IsNullOrEmpty(kit.StanceSwapAbilityId)
            && self.Resource != MendVerseValue
            && PartyMissingHealthRatio(actors) >= PartyMendThresholdPct
            && RoleShared.ReadyAndAffordable(self, kit.StanceSwapAbilityId, tick, kit))
        {
            return new Command[] { new CastCommand(tick, self.Id, kit.StanceSwapAbilityId) };
        }

        var dispellable = new HashSet<string>(
            actors.Values.SelectMany(a => a.Effects).Select(e => e.EffectId).Where(id => id.StartsWith(DispellablePrefix, StringComparison.Ordinal)),
            StringComparer.Ordinal);
        var dispelId = dispellable.Count > 0 ? Priority.PickDispelTarget(actors, dispellable) : null;
        if (dispelId is null)
        {
            var healId = Priority.PickHealTarget(actors, HealThresholdPct);
            return healId is { } hid && actors.TryGetValue(hid, out var healTarget)
                ? HealPick.HealCommands(view, self, kit, healTarget)
                : Array.Empty<Command>();
        }

        var abilityId = kit.DispelAbilityId ?? "dispel";
        if (!actors.TryGetValue(dispelId.Value, out var target))
        {
            return Array.Empty<Command>();
        }
        if (RoleShared.Dist(target, self) > HealRangeMm)
        {
            return Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(target.Pos)) is { } move ? new Command[] { move } : Array.Empty<Command>();
        }
        return RoleShared.ReadyAndAffordable(self, abilityId, tick, kit)
            ? new Command[] { new CastCommand(tick, self.Id, abilityId, dispelId) }
            : Array.Empty<Command>();
    }
}
