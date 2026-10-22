using System;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// BOSS-TEACHES (ruling R-BOSS-1, BIBLE s9 "The Sundered Twins"): the bots' answer to a shared Break bar. The Twins'
/// bar is drained by interrupt- and control-tagged casts on either twin, and a party empties it by pairing kicks
/// (two kicks from two seats, the content's <c>drainPerInterrupt</c>), as a WoW group spends its kicks on a stagger
/// bar. When no priority cast needs the kick (the ledger runs first), a seat whose own interrupt is ready spends it on
/// the nearest living enemy that shares a Break bar (its content <c>breakBarId</c>) and is not already stunned. Layout
/// runs only (the dungeon), so the oracle runner, which has no layout and no armed boss, is unchanged.
/// </summary>
public static class BreakBarKick
{
    /// <summary>The effect a staggered bar applies (hollow-warden.json twinsBreak onEmpty).</summary>
    public const string StunEffectId = "stunned";

    /// <summary>
    /// The kick <paramref name="self"/> spends on a Break-bar enemy this tick, or null: a layout run, its interrupt ready,
    /// a living Break-bar enemy in the interrupt's range that is not stunned (nearest, then the lower id).
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The kick, or null.</returns>
    public static CastCommand? Command(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var tick = view.State.Tick;
        var interruptId = kit.InterruptAbilityId;
        if (view.Layout is null || view.Kits.Content is not { } content || string.IsNullOrEmpty(interruptId) || !self.Alive ||
            !RoleShared.AbilityReady(self, interruptId, tick, kit))
        {
            return null;
        }
        var range = content.Abilities.TryGetValue(interruptId, out var def) ? def.Range : long.MaxValue;
        var target = view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle)
            .Where(a => a.ClassId is { } cid && content.Enemies.TryGetValue(cid, out var e) && e.BreakBarId is not null)
            .Where(a => !a.Effects.Exists(x => x.EffectId == StunEffectId && x.EndsTick > tick))
            .Select(a => (Actor: a, Dist: RoleShared.Dist(self, a)))
            .Where(c => c.Dist <= range)
            .OrderBy(c => c.Dist)
            .ThenBy(c => c.Actor.Id)
            .Select(c => c.Actor)
            .FirstOrDefault();
        return target is null ? null : new CastCommand(tick, self.Id, interruptId, target.Id);
    }
}
