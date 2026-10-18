using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;

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
        if (id is null || !UnderPackPressure(view, self) || self.CastId is not null)
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
        var sustain = kit.SelfSustainAbilityId;
        var rotation = kit.Rotation.Where(id => !string.Equals(id, sustain, StringComparison.Ordinal)).ToList();
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
