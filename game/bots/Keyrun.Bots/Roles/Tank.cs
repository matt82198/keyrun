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
        // TAKE-THE-PACK-FIRST (R-BOTTANK-LEASH-1, f-2f6982e9): a pack aimed at another player is loose, and its TargetId still
        // names the tank (the sim's default), so UnderPackPressure read it as three attackers and the Warden spent the ticks
        // before the taunt on casts (Brace, then Bulwark's cast time) while the pack hit the party. A healthy tank takes the
        // loose pack first; the pressure casts resume once he holds it.
        // ALLY-VIGIL priority over take-the-pack: a pressured ally's Vigil shield (AllyShieldCommand) runs before the taunt.
        // Take-the-pack never returns an empty list: the taunt when ready and in range, else the approach move.
        if (healthPct >= DefensiveThresholdPct && TankEngage.PickLooseEnemy(view.State.Actors, self) is not null)
        {
            if (TankPressure.AllyShieldCommand(view, self, kit) is { } allyShield)
            {
                return new List<Command> { allyShield };
            }
            if (TankEngage.TauntTarget(view, self, kit) is { } tauntAt)
            {
                return new List<Command> { new CastCommand(tick, self.Id, kit.TauntAbilityId!, tauntAt.Id) };
            }
            if (TankEngage.ApproachMove(view, self, kit) is { } approach)
            {
                return new List<Command> { approach };
            }
            return new List<Command>();
        }
        var pressured =healthPct < DefensiveThresholdPct || TankPressure.UnderPackPressure(view, self);
        // ALLY-VIGIL pool: a resource-spending defensive waits while an ally is pressured and would drop the tank below Vigil's cost.
        if (!string.IsNullOrEmpty(kit.DefensiveAbilityId) && pressured && RoleShared.AbilityReady(self, kit.DefensiveAbilityId, tick, kit)
            && !TankPressure.PoolsForAllySpender(view, self, kit, kit.DefensiveAbilityId))
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
            && RoleShared.ReadyAndAffordable(self, kit.SelfSustainAbilityId, tick, kit)
            && !TankPressure.PoolsForAllySpender(view, self, kit, kit.SelfSustainAbilityId))
        {
            return new List<Command> { new CastCommand(tick, self.Id, kit.SelfSustainAbilityId) };
        }
        return new List<Command>();
    }

    /// <summary>True when <paramref name="command"/> repeats a taunt cast or a move already in <paramref name="issued"/>.</summary>
    private static bool AlreadyIssued(IReadOnlyList<Command> issued, Command command) => command switch
    {
        CastCommand cast => issued.OfType<CastCommand>().Any(c => c.AbilityId == cast.AbilityId && c.TargetId == cast.TargetId),
        MoveCommand => issued.OfType<MoveCommand>().Any(),
        _ => false,
    };

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

        // ALLY-VIGIL pool: a resource-spending kick waits while an ally is pressured and would drop the tank below Vigil's cost.
        if (RoleShared.LedgerKickCommand(view, self, kit) is { } kick && !TankPressure.PoolsForAllySpender(view, self, kit, kick.AbilityId))
        {
            return new Command[] { Reasons.Tag(kick, "tank:ledger-kick") };
        }
        if (Floor.FloorEscapeCommand(view, self) is { } escape)
        {
            return new Command[] { Reasons.Tag(escape, "tank:floor-escape") };
        }
        if (BreakBarKick.Command(view, self, kit) is { } barKick && !TankPressure.PoolsForAllySpender(view, self, kit, barKick.AbilityId))
        {
            return new Command[] { Reasons.Tag(barKick, "tank:break-bar-kick") };
        }
        if (Floor.MeleeReapproachCommand(view, self) is { } reapproach)
        {
            return new Command[] { Reasons.Tag(reapproach, "tank:melee-reapproach") };
        }

        // Defensives first: the sim gates even a gcd-0 cast on the GCD, so it must precede a rotation cast.
        var defensive = DefensiveCommands(view, self, kit);
        Reasons.TagAll(defensive, "tank:defensive");
        if (TankEngage.TankEngageCommands(view, self, kit) is { } engage)
        {
            Reasons.TagAll(engage, "tank:engage");
            // A take-the-pack taunt or approach move already issued from the defensive stage is not repeated by ENGAGE.
            defensive.AddRange(engage.Where(c => !AlreadyIssued(defensive, c)));
            return defensive;
        }

        var output = new List<Command>();
        if (Priority.PickTankTarget(view.State.Actors, self.Id) is { } targetId && self.TargetId != targetId)
        {
            output.Add(Reasons.Tag(new TargetCommand(tick, self.Id, targetId), "tank:retarget"));
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
                    output.Add(Reasons.Tag(new MoveCommand(tick, self.Id, new Dir2(0, 0), desiredYaw), "tank:face-away"));
                }
            }
        }
        output.AddRange(defensive);
        return output;
    }
}
