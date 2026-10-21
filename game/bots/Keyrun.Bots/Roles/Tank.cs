using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/tank.ts, the tank state machine: kick the priority cast, flee or re-approach around floor
/// patches, then defensives (the hard defensive under the 35% floor or pack pressure; Vigil and Bulwark under
/// pressure; Vigil under the 60% floor) ahead of ENGAGE (<see cref="TankEngage"/>). With nothing to engage it faces
/// away from the party centroid.
/// </summary>
public sealed class TankRole : IRole
{
    /// <summary>tank.ts TANK_DEFENSIVE_THRESHOLD_PCT.</summary>
    public const double DefensiveThresholdPct = 0.35;

    /// <summary>tank.ts TANK_SUSTAIN_THRESHOLD_PCT: the earlier floor for the self-sustain spender.</summary>
    public const double SustainThresholdPct = 0.6;

    /// <summary>The shared instance.</summary>
    public static TankRole Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<Command> Decide(BotView view, ActorState self) => TankDecide(view, self);

    private static List<Command> DefensiveCommands(BotView view, ActorState self, EffectiveKit kit)
    {
        var tick = view.State.Tick;
        var healthPct = (double)self.Health / self.MaxHealth;
        var pressured = healthPct < DefensiveThresholdPct || TankPressure.UnderPackPressure(view, self);
        if (!string.IsNullOrEmpty(kit.DefensiveAbilityId) && pressured && RoleShared.AbilityReady(self, kit.DefensiveAbilityId, tick, kit))
        {
            return new List<Command> { new CastCommand(tick, self.Id, kit.DefensiveAbilityId) };
        }
        if (TankPressure.PressureSustainCommand(view, self, kit) is { } vigil)
        {
            return new List<Command> { vigil };
        }
        if (TankPressure.AllyShieldCommand(view, self, kit) is { } guardian)
        {
            return new List<Command> { guardian };
        }
        if (TankPressure.PressureMitigationCommand(view, self, kit) is { } bulwark)
        {
            return new List<Command> { bulwark };
        }
        if (!string.IsNullOrEmpty(kit.SelfSustainAbilityId)
            && healthPct < SustainThresholdPct
            && RoleShared.ReadyAndAffordable(self, kit.SelfSustainAbilityId, tick, kit))
        {
            return new List<Command> { new CastCommand(tick, self.Id, kit.SelfSustainAbilityId) };
        }
        return new List<Command>();
    }

    /// <summary>tank.ts tankDecide.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <returns>Commands, possibly empty.</returns>
    public static IReadOnlyList<Command> TankDecide(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        var kit = RoleShared.KitOf(view, self.ClassId);

        if (RoleShared.LedgerKickCommand(view, self, kit) is { } kick)
        {
            return new Command[] { kick };
        }
        if (Floor.FloorEscapeCommand(view, self) is { } escape)
        {
            return new Command[] { escape };
        }
        if (Floor.MeleeReapproachCommand(view, self) is { } reapproach)
        {
            return new Command[] { reapproach };
        }

        // Defensives first: the sim gates even a gcd-0 cast on the GCD, so it must precede a rotation cast.
        var defensive = DefensiveCommands(view, self, kit);
        if (TankEngage.TankEngageCommands(view, self, kit) is { } engage)
        {
            defensive.AddRange(engage);
            return defensive;
        }

        var output = new List<Command>();
        if (Priority.PickTankTarget(view.State.Actors, self.Id) is { } targetId && self.TargetId != targetId)
        {
            output.Add(new TargetCommand(tick, self.Id, targetId));
        }
        var party = view.State.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != self.Id).ToList();
        if (party.Count > 0)
        {
            var c = RoleShared.Centroid(party);
            if (self.Pos.X != c.X || self.Pos.Z != c.Z)
            {
                var desiredYaw = Moves.YawToward(c, PointMm.Of(self.Pos)); // faces away from the party centroid
                if (Moves.YawChanged(self.Yaw, desiredYaw))
                {
                    output.Add(new MoveCommand(tick, self.Id, new Dir2(0, 0), desiredYaw));
                }
            }
        }
        output.AddRange(defensive);
        return output;
    }
}
