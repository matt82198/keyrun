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
/// shape-aware picker (<see cref="HealPick"/>); with nobody under it, open a Setup Window on an enemy for a ready
/// Payoff partner (<see cref="HealerSetup"/>).
/// </summary>
public sealed class HealerRole : IRole
{
    /// <summary>healer.ts DISPELLABLE_PREFIX: the local convention for a dispellable effect id.</summary>
    public const string DispellablePrefix = "dispellable:";

    /// <summary>healer.ts HEAL_RANGE_MM: 30 m.</summary>
    public const long HealRangeMm = 30 * 1000;

    /// <summary>healer.ts HEAL_THRESHOLD_PCT: an ally under this health fraction is a heal-triage target.</summary>
    public const double HealThresholdPct = 0.7;
    private const double PartyMendThresholdPct = 0.35;

    /// <summary>healer.ts MEND_VERSE_VALUE: resource 1 is Mend, 0 is Ward.</summary>
    private const long MendVerseValue = 1;

    /// <summary>The shared instance.</summary>
    public static HealerRole Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<Command> Decide(BotView view, ActorState self) => HealerDecide(view, self);

    /// <summary>
    /// HEALER-TOPS-OFF (the WoW Mythic+ dungeon loop): between pulls, with no living enemy, the healer tops the party
    /// off: the lowest living ally under full health, through the same shape-aware picker as a fight heal
    /// (<see cref="HealPick"/>). Nobody hurt = nothing to do.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The healer.</param>
    /// <returns>Commands, possibly empty.</returns>
    public static IReadOnlyList<Command> TopOffCommands(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var actors = view.State.Actors;
        return Priority.PickHealTarget(actors, 1.0) is { } id && actors.TryGetValue(id, out var target)
            ? HealPick.HealCommands(view, self, RoleShared.KitOf(view, self.ClassId), target)
            : Array.Empty<Command>();
    }

    /// <summary>True when <see cref="TopOffCommands"/> has an ally to heal: a living ally under full health.</summary>
    /// <param name="actors">The state's actors.</param>
    /// <returns>Whether the party needs topping off.</returns>
    public static bool NeedsTopOff(SortedDictionary<int, ActorState> actors) => Priority.PickHealTarget(actors, 1.0) is not null;

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

    /// <summary>
    /// healer.ts isHealButton: a heal the shape-aware picker owns (self, group, second group and targeted heal, the
    /// first on-cast heal) or the stance verse swap. These are never pressed by <see cref="PressCommands"/>.
    /// </summary>
    private static bool IsHealButton(EffectiveKit kit, string abilityId)
    {
        var h = kit.Heals;
        return abilityId == h.SelfHealAbilityId
            || abilityId == h.GroupHealAbilityId
            || abilityId == h.TargetHealAbilityId
            || abilityId == h.SecondGroupHeal?.AbilityId
            || abilityId == kit.HealAbilityId
            || abilityId == kit.StanceSwapAbilityId;
    }

    /// <summary>
    /// healer.ts partyTargetId: the party's current target, an engaged living enemy: the living tank's target, else the
    /// healer's own, else the lowest-id engaged enemy; null when no enemy is engaged (an idle prespawned group is never
    /// pulled by the healer).
    /// </summary>
    private static int? PartyTargetId(SortedDictionary<int, ActorState> actors, ActorState self)
    {
        bool Engaged(int? id) => id is { } v && actors.TryGetValue(v, out var a) && a.Kind == ActorKind.Enemy && a.Alive && !a.Idle;
        var tankTarget = actors.Values.FirstOrDefault(a => a.Kind == ActorKind.Player && a.Alive && a.Role == ActorRole.Tank && Engaged(a.TargetId))?.TargetId;
        if (tankTarget is not null)
        {
            return tankTarget;
        }
        if (Engaged(self.TargetId))
        {
            return self.TargetId;
        }
        return actors.Values.FirstOrDefault(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle)?.Id;
    }

    /// <summary>
    /// HEALER-KEEPS-PRESSING (orchestrator ruling for PR #362, the WoW Mythic+ dungeon loop: a healer with nobody to heal
    /// keeps contributing): in a fight with no ally under the heal threshold, the first ready, affordable rotation
    /// ability that is not a heal button (<see cref="IsHealButton"/>; the Chanter's Dissonance) goes at the party's
    /// current target (<see cref="PartyTargetId"/>). Nothing engaged or nothing ready = nothing to do.
    /// </summary>
    private static IReadOnlyList<Command> PressCommands(BotView view, ActorState self, EffectiveKit kit)
    {
        var tick = view.State.Tick;
        if (PartyTargetId(view.State.Actors, self) is not { } targetId)
        {
            return Array.Empty<Command>();
        }
        var abilityId = kit.Rotation.FirstOrDefault(id => !IsHealButton(kit, id) && RoleShared.ReadyAndAffordable(self, id, tick, kit));
        if (abilityId is null)
        {
            return Array.Empty<Command>();
        }
        return self.TargetId == targetId
            ? new Command[] { new CastCommand(tick, self.Id, abilityId, targetId) }
            : new Command[] { new TargetCommand(tick, self.Id, targetId), new CastCommand(tick, self.Id, abilityId, targetId) };
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
        if (BreakBarKick.Command(view, self, kit) is { } barKick)
        {
            return new Command[] { barKick };
        }

        var party =actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != self.Id).ToList();
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
            if (healId is not { } hid || !actors.TryGetValue(hid, out var healTarget))
            {
                // CADENCE-SETUP (f-b31e8f34): nobody under the threshold, so open a Setup for a ready Payoff partner;
                // BOTS-FIVE-BUTTONS (f-45e27a2d): no Setup to open, so close a Window, Refrain into Ward, or cast the
                // damaging Payoff (HealerIdle); D-HEALER-DOWNTIME: none of those, so press a non-heal rotation button.
                // BOSS-TEACHES (R-BOSS-1): with nobody to heal, a Setup into the boss's Lock comes first.
                var lockSetup = LockAnswer.SetupCommand(view, self, kit);
                if (lockSetup.Count > 0)
                {
                    return lockSetup;
                }
                var setup = HealerSetup.SetupCommands(view, self, kit);
                if (setup.Count > 0 && !LockAnswer.Holds(view, self, setup))
                {
                    return setup;
                }
                var idle = HealerIdle.IdleCommands(view, self, kit, MendVerseValue);
                if (idle.Count > 0 && !LockAnswer.Holds(view, self, idle))
                {
                    return idle;
                }
                var press = PressCommands(view, self, kit);
                return LockAnswer.Holds(view, self, press) ? Array.Empty<Command>() : press;
            }
            // BOTS-FIVE-BUTTONS: someone needs healing while the stance sits in Ward, so Refrain back to Mend first
            // (its Mend burst heals on the swap) before the shape-aware heal.
            if (HealerIdle.StanceSwapReady(self, kit, tick) && self.Resource != MendVerseValue)
            {
                return new Command[] { new CastCommand(tick, self.Id, kit.StanceSwapAbilityId!) };
            }
            return HealPick.HealCommands(view, self, kit, healTarget);
        }

        var abilityId = kit.DispelAbilityId ?? "dispel";
        if (!actors.TryGetValue(dispelId.Value, out var target))
        {
            return Array.Empty<Command>();
        }
        if (RoleShared.Dist(target, self) > HealRangeMm)
        {
            return PathMoves.Toward(view, tick, self, target.Pos) is { } move ? new Command[] { move } : Array.Empty<Command>();
        }
        return RoleShared.ReadyAndAffordable(self, abilityId, tick, kit)
            ? new Command[] { new CastCommand(tick, self.Id, abilityId, dispelId) }
            : Array.Empty<Command>();
    }
}
