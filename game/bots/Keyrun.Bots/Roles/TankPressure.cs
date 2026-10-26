using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/tank-pressure.ts: the pack-pressure signal (<see cref="PressureAttackers"/>+ living enemies
/// aimed at the tank) and the pressure-aware spends: Vigil as Brace runs out, Bulwark whenever Braced is down, and a
/// rotation that holds enough momentum for Vigil. In a lull the rotation is empty (bank GCD and momentum).
/// </summary>
public static class TankPressure
{
    /// <summary>tank-pressure.ts PRESSURE_ATTACKERS.</summary>
    public const int PressureAttackers = 3;

    /// <summary>tank-pressure.ts BRACE_EXPIRY_LEAD_TICKS: cast Vigil once Brace has this many ticks or fewer left.</summary>
    public const int BraceExpiryLeadTicks = 30;

    /// <summary>tank-pressure.ts attackersOf: living enemies aimed at <paramref name="self"/>.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <returns>Count.</returns>
    public static int AttackersOf(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        return view.State.Actors.Values.Count(a => a.Kind == ActorKind.Enemy && a.Alive && a.TargetId == self.Id);
    }

    /// <summary>tank-pressure.ts underPackPressure.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <returns>True under pack pressure.</returns>
    public static bool UnderPackPressure(BotView view, ActorState self) => AttackersOf(view, self) >= PressureAttackers;

    private static IEnumerable<string> AppliedEffectIds(BotView view, string abilityId)
    {
        if (view.Kits.Content is not { } reg || !reg.Abilities.TryGetValue(abilityId, out var def))
        {
            return Array.Empty<string>();
        }
        return def.Effects.OfType<ApplyEffectOp>().Select(e => e.EffectId);
    }

    private static long EffectTicksLeft(BotView view, ActorState self, string abilityId, int tick)
    {
        var ids = new HashSet<string>(AppliedEffectIds(view, abilityId), StringComparer.Ordinal);
        long best = 0;
        foreach (var e in self.Effects)
        {
            if (ids.Contains(e.EffectId))
            {
                best = Math.Max(best, (long)e.EndsTick - tick);
            }
        }
        return best;
    }

    /// <summary>tank-pressure.ts pressureSustainCommand: under pressure, Vigil when Brace is down or about to expire and Vigil is not up.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The cast, or null.</returns>
    public static CastCommand? PressureSustainCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var tick = view.State.Tick;
        var sustain = kit.SelfSustainAbilityId;
        if (string.IsNullOrEmpty(sustain) || !UnderPackPressure(view, self))
        {
            return null;
        }
        if (!RoleShared.ReadyAndAffordable(self, sustain, tick, kit) || EffectTicksLeft(view, self, sustain, tick) > 0)
        {
            return null;
        }
        var braceLeft = string.IsNullOrEmpty(kit.DefensiveAbilityId) ? 0 : EffectTicksLeft(view, self, kit.DefensiveAbilityId, tick);
        return braceLeft > BraceExpiryLeadTicks ? null : new CastCommand(tick, self.Id, sustain);
    }

    /// <summary>
    /// ALLY-VIGIL (ruling R-FORKPARITY-UNPLAYED): an ally is pressured when a living enemy is aimed at it or its health
    /// is under <see cref="HealerRole.HealThresholdPct"/> (an ally the healer would triage). The tank's own 60% sustain
    /// floor is not used: measured on the fork-parity hv-k2-s7 run, an ally other than the tank is under 60% on 1
    /// ally-tick of the whole run, so a Guardian keyed to it never cast.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="ally">A party member.</param>
    /// <returns>True when pressured.</returns>
    public static bool AllyPressured(BotView view, ActorState ally)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(ally);
        return AttackersOf(view, ally) > 0 || (double)ally.Health / ally.MaxHealth < HealerRole.HealThresholdPct;
    }

    /// <summary>R-AGGRO-1 (ab): ally pressure is sticky for this many ticks after the last tick an ally was pressured.</summary>
    public const int AllyPressureStickyTicks = 60;

    /// <summary>True when any living party member other than the tank is pressured on this tick (<see cref="AllyPressured"/>).</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <returns>True while an ally is pressured right now.</returns>
    public static bool AnyAllyPressuredNow(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        return view.State.Actors.Values.Any(a => a.Kind == ActorKind.Player && a.Alive && a.Id != self.Id && AllyPressured(view, a));
    }

    /// <summary>
    /// ALLY-VIGIL pool (R-AGGRO-1 ab): true when an ally is pressured now, or was within the last
    /// <see cref="AllyPressureStickyTicks"/> ticks (<see cref="BotView.LastAllyPressuredTick"/>, kept in <see cref="BotMemory"/>).
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <returns>True while ally pressure is live or sticky.</returns>
    public static bool AnyAllyPressured(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        return StickyAllyPressure(view) || AnyAllyPressuredNow(view, self);
    }

    private static bool StickyAllyPressure(BotView view) =>
        view.LastAllyPressuredTick >= 0 && view.State.Tick - view.LastAllyPressuredTick <= AllyPressureStickyTicks;

    /// <summary>
    /// ALLY-VIGIL pool (WoW M+: a tank pools resource for its ally save): true when casting <paramref name="abilityId"/>
    /// now would wait, because an ally is pressured and the cast would leave the tank below the ally-shield spender's cost.
    /// Free casts (cost 0) and the spender itself are never pooled, and with no ally-shield talent nothing is pooled.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <param name="kit">Its kit.</param>
    /// <param name="abilityId">The ability the tank would cast.</param>
    /// <returns>True when the cast must wait for the pool.</returns>
    public static bool PoolsForAllySpender(BotView view, ActorState self, EffectiveKit kit, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        if (string.IsNullOrEmpty(kit.AllyShieldAbilityId) || string.Equals(abilityId, kit.AllyShieldAbilityId, StringComparison.Ordinal))
        {
            return false;
        }
        var reserve = RoleShared.Cost(kit, kit.AllyShieldAbilityId);
        var cost = RoleShared.Cost(kit, abilityId);
        return cost > 0 && self.Resource - cost < reserve && AnyAllyPressured(view, self);
    }

    /// <summary>
    /// ALLY-VIGIL: the ally-shield spender (<see cref="EffectiveKit.AllyShieldAbilityId"/>, Vigil with the "ally"
    /// talent) cast on the most pressured ally other than the tank: alive, within the run def's range, not already
    /// carrying the shield's effect; lowest health fraction first, then lowest id. Null without the talent, when it is
    /// not ready or affordable, or when no ally qualifies.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The targeted cast, or null.</returns>
    public static CastCommand? AllyShieldCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var tick = view.State.Tick;
        var id = kit.AllyShieldAbilityId;
        if (string.IsNullOrEmpty(id) || view.Kits.RunAbility(id) is not { } def || !RoleShared.ReadyAndAffordable(self, id, tick, kit))
        {
            return null;
        }
        var shieldEffects = new HashSet<string>(def.Effects.OfType<S.ApplyEffectOp>().Select(e => e.EffectId), StringComparer.Ordinal);
        var candidates = view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != self.Id)
            .Where(a => S.Casting.RangeMm(self, a) <= def.Range)
            .Where(a => !a.Effects.Any(e => shieldEffects.Contains(e.EffectId) && e.EndsTick > tick))
            .ToList();
        // R-AGGRO-1 (ab): an ally pressured now is shielded first; inside the sticky window with nobody pressured this tick,
        // the lowest-health ally in range is.
        var pressuredNow = candidates.Where(a => AllyPressured(view, a)).ToList();
        var target = (pressuredNow.Count > 0 ? pressuredNow : StickyAllyPressure(view) ? candidates : pressuredNow)
            .OrderBy(a => (double)a.Health / a.MaxHealth)
            .ThenBy(a => a.Id)
            .FirstOrDefault();
        return target is null ? null : new CastCommand(tick, self.Id, id, target.Id);
    }

    /// <summary>The spender the pressure rotation holds back and reserves momentum for: self sustain, else the ally shield.</summary>
    private static string? ReservedSpender(EffectiveKit kit) =>
        string.IsNullOrEmpty(kit.SelfSustainAbilityId) ? kit.AllyShieldAbilityId : kit.SelfSustainAbilityId;

    /// <summary>
    /// tank-pressure.ts groupMitigationAbilityId: the first rotational circle-shaped ability applying an effect with a
    /// negative damageTakenBp (Warden's Bulwark), read from the registry; null without one.
    /// </summary>
    /// <param name="view">Bot view (its registry).</param>
    /// <param name="kit">Effective kit.</param>
    /// <returns>Ability id, or null.</returns>
    public static string? GroupMitigationAbilityId(BotView view, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(kit);
        if (view.Kits.Content is not { } reg)
        {
            return null;
        }
        foreach (var id in kit.Rotation)
        {
            if (!reg.Abilities.TryGetValue(id, out var def) || def.Shape.Kind != ShapeKind.Circle)
            {
                continue;
            }
            var mitigates = def.Effects.OfType<ApplyEffectOp>().Any(e =>
                (reg.Effects.TryGetValue(e.EffectId, out var eff) ? eff.Modifiers?.DamageTakenBp ?? 0 : 0) < 0);
            if (mitigates)
            {
                return id;
            }
        }
        return null;
    }

    /// <summary>tank-pressure.ts pressureMitigationCommand: under pressure and not mid-cast, the group mitigation when ready and its effect is down.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The cast, or null.</returns>
    public static CastCommand? PressureMitigationCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var tick = view.State.Tick;
        var id = GroupMitigationAbilityId(view, kit);
        if (id is null || !UnderPackPressure(view, self) || self.CastId is not null || PoolsForAllySpender(view, self, kit, id))
        {
            return null;
        }
        if (!RoleShared.ReadyAndAffordable(self, id, tick, kit) || EffectTicksLeft(view, self, id, tick) > 0)
        {
            return null;
        }
        return new CastCommand(tick, self.Id, id);
    }

    /// <summary>
    /// tank-pressure.ts pressureRotation: empty in a lull; otherwise the rotation without the self-sustain spender and,
    /// under pressure, without any ability whose cost would leave less than the spender's cost.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The rotation to spend from.</returns>
    public static IReadOnlyList<string> PressureRotation(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        if (RoleShared.InLull(view))
        {
            return Array.Empty<string>();
        }
        var sustain = ReservedSpender(kit);
        // R-AOE-1: Bulwark has a cooldown now, so the rotation list reaches it; it is the group mitigation, pressed under pack
        // pressure by PressureMitigationCommand (D48: a mitigation, not a filler), so it is not also a rotation press.
        var mitigation = GroupMitigationAbilityId(view, kit);
        var rotation = kit.Rotation
            .Where(id => !string.Equals(id, sustain, StringComparison.Ordinal) && !string.Equals(id, mitigation, StringComparison.Ordinal))
            .Where(id => !PoolsForAllySpender(view, self, kit, id))
            .ToList();
        if (string.IsNullOrEmpty(sustain) || !UnderPackPressure(view, self))
        {
            return rotation;
        }
        var reserve = RoleShared.Cost(kit, sustain);
        return rotation.Where(id =>
        {
            var cost = RoleShared.Cost(kit, id);
            return cost == 0 || self.Resource - cost >= reserve;
        }).ToList();
    }
}
