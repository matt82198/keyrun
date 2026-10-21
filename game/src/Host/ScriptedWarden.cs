using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// The autorun Warden (Godot-free, deterministic): walks at the Choir Cantor (the lieutenant) until his kick is in
/// range, kicks the Cantor's heal once it is half cast (so the cast bar is on screen for two seconds first), and spends
/// the GCD on his rotation against whatever is in reach: Reckoning at full Momentum, Vigil at 50, Guard Break, Strike,
/// and Brace below half health. Every command goes through the same <see cref="CastTargeting"/> rule as a key press.
/// On a layout run (lane NAV-CORE, STATE.md D18) with no living enemy he walks the route instead
/// (<see cref="Follow.LeadRoute"/>: each room's far doorway in turn, but in the active pull's room he holds at the
/// standoff point until the party is inside, LEADER-WAITS), so the pull spawns once all are in; then the combat script above. With no living lieutenant but a living enemy (lane
/// WARDEN-ENGAGES) he targets the nearest one (lowest id on a tie), walks into melee range and faces it, so the
/// rotation keeps landing until the pull is clear.
/// </summary>
public sealed class ScriptedWarden
{
    /// <summary>Stop walking once the lieutenant is this close (Shield Bash range is 3000 mm).</summary>
    public const long ApproachMm = 2400;

    /// <summary>The lieutenant's class id.</summary>
    public const string LieutenantClass = "choir.lieutenant";

    private static readonly string[] Rotation = { "warden.reckoning", "warden.vigil", "warden.guardBreak", "warden.strike" };

    private const string Kick = "warden.shieldBash";
    private const string Defensive = "warden.brace";
    private const string Taunt = "warden.taunt";

    /// <summary>Appends the scripted commands for <paramref name="tick"/>.</summary>
    /// <param name="tick">The pre-step tick.</param>
    /// <param name="run">The run.</param>
    /// <param name="into">The command list.</param>
    /// <param name="openSeats">Abandoned seats not yet refilled (the leader does not wait for them), or null.</param>
    public void Sample(int tick, Run run, List<Command> into, IReadOnlyList<int>? openSeats = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(into);
        var state = run.State;
        if (!state.Actors.TryGetValue(HostRunSetup.HumanId, out var me) || !me.Alive)
        {
            return;
        }
        if (run.Content.Layout is not null && !Follow.AnyLivingEnemy(state))
        {
            if (Follow.LeadRoute(tick, me, run, openSeats) is { } walk)
            {
                into.Add(walk);
            }
            return;
        }
        var abilities = run.Content.Abilities;
        var lieutenant = state.Actors.Values.FirstOrDefault(a => a.ClassId == LieutenantClass && EngagedTargets.IsEngagedHostile(me, a));

        if (lieutenant is { CastId: { } castId, CastStartTick: { } start } && abilities.TryGetValue(castId, out var cast) && cast.Interruptible &&
            tick >= start + (cast.CastTicks / 2) && Ready(me, abilities[Kick], tick) && Casting.RangeMm(me, lieutenant) <= abilities[Kick].Range)
        {
            into.Add(new CastCommand(tick, me.Id, Kick, lieutenant.Id));
        }

        // LOOSE-PICKUP (BIBLE section 3, the bot tank's TankEngage rule): an engaged enemy aimed at another seat that the
        // Warden does not hold is taunted back (Taunt is off the GCD); with Taunt ready but the enemy out of its range he
        // walks at it first. Run C at key 2: the Choir melee chased the careless Cinder and killed it, and he never taunted.
        var loose = TankEngage.PickLooseEnemy(state.Actors, me);
        var tauntReady = loose is not null && abilities.TryGetValue(Taunt, out var taunt) && Ready(me, taunt, tick);
        var tauntInRange = tauntReady && Casting.RangeMm(me, loose!) <= abilities[Taunt].Range;
        if (tauntInRange)
        {
            into.Add(new CastCommand(tick, me.Id, Taunt, loose!.Id));
        }

        if (tauntReady && !tauntInRange)
        {
            into.Add(Toward(tick, me, loose!));
        }
        else if (lieutenant is not null && Casting.RangeMm(me, lieutenant) > ApproachMm)
        {
            into.Add(Toward(tick, me, lieutenant));
        }
        else if (lieutenant is null && NearestEnemy(state, me) is { } foe)
        {
            // Lane WARDEN-ENGAGES (KF2b): no lieutenant left to kick, so fight the pull until it is clear. Target the
            // nearest living enemy (lowest id on a tie) so Strike and Reckoning go at it, walk into melee range, and
            // once there turn to face it so Guard Break's cone lands (a zero-length move only sets the yaw).
            if (me.TargetId != foe.Id)
            {
                into.Add(new TargetCommand(tick, me.Id, foe.Id));
            }
            into.Add(Casting.RangeMm(me, foe) > ApproachMm
                ? Toward(tick, me, foe)
                : new MoveCommand(tick, me.Id, new Dir2(0, 0), Fixed.YawBetween(me.Pos, foe.Pos)));
        }

        // R-307-C item 2 (HOLDER-LOCK D30): an enemy another living player holds is taunted back. PILE-INT merge: #326's
        // LOOSE-PICKUP above taunts an enemy aimed at another seat; this holder check runs only when that one did not
        // already queue the Taunt this tick, so the two rules never press it twice.
        if (!tauntInRange && LooseEnemy(state, me, abilities[Taunt], tick) is { } held)
        {
            into.Add(new CastCommand(tick, me.Id, Taunt, held.Id));
        }

        if (!Cooldowns.IsGcdReady(me, tick))
        {
            return;
        }
        // R-PATROL-3: Brace is held for the moment two living enemies are actually hitting the Warden (two trash mobs, the
        // WoW tank's defensive window), not spent on a lone mob during the Choir and left on cooldown for the patrol.
        if (me.Health * 2 < me.MaxHealth && Ready(me, abilities[Defensive], tick) && AttackersOn(state, me) >= 2)
        {
            into.Add(new CastCommand(tick, me.Id, Defensive, me.Id));
            return;
        }
        foreach (var id in Rotation)
        {
            var def = abilities[id];
            if (!Ready(me, def, tick))
            {
                continue;
            }
            var target = CastTargeting.For(run, me.Id, id);
            if (def.Shape.Kind == ShapeKind.Self || (state.Actors.TryGetValue(target, out var t) && t.Id != me.Id && Casting.RangeMm(me, t) <= def.Range))
            {
                into.Add(new CastCommand(tick, me.Id, id, target));
                return;
            }
        }
    }

    /// <summary>
    /// HOLDER-LOCK (STATE.md D30): the nearest living engaged enemy in Taunt range whose locked holder is another living
    /// player (lowest id on a tie), when Taunt is ready; else null. Only a taunt moves a holder, so this is the one way
    /// the Warden takes back an enemy a party member body-pulled (the bot tank's rule, TankEngage.PickLooseEnemy).
    /// </summary>
    internal static ActorState? LooseEnemy(SimState state, ActorState me, AbilityDef taunt, int tick)
    {
        if (!Ready(me, taunt, tick))
        {
            return null;
        }
        return state.Actors.Values
            .Where(e => e.Kind == ActorKind.Enemy && e.Alive && !e.Idle
                && e.HolderId is { } h && h != me.Id
                && state.Actors.TryGetValue(h, out var holder) && holder.Alive && holder.Kind == ActorKind.Player
                && Casting.RangeMm(me, e) <= taunt.Range)
            .OrderBy(e => Casting.RangeMm(me, e))
            .ThenBy(e => e.Id)
            .FirstOrDefault();
    }

    private static bool Ready(ActorState me, AbilityDef def, int tick) =>
        Cooldowns.IsAbilityReady(me, def, tick) && (def.Cost.Resource == "none" || me.Resource >= def.Cost.Amount);

    /// <summary>A full-speed move from <paramref name="me"/> straight at <paramref name="to"/>, facing it.</summary>
    private static MoveCommand Toward(int tick, ActorState me, ActorState to)
    {
        var dx = to.Pos.X - me.Pos.X;
        var dz = to.Pos.Z - me.Pos.Z;
        var len = Movement.Isqrt((dx * dx) + (dz * dz));
        var dir = new Dir2(IntMath.TruncDiv(dx * 1000, len), IntMath.TruncDiv(dz * 1000, len));
        return new MoveCommand(tick, me.Id, dir, Atan.YawFromComponents(dir.X, dir.Z));
    }

    /// <summary>
    /// The living ENGAGED enemy nearest <paramref name="me"/> (squared XZ distance, lowest id on a tie), or null. An idle
    /// prespawned enemy is never picked (PRESPAWN-AGGRO; <see cref="EngagedTargets.Nearest"/>).
    /// </summary>
    private static ActorState? NearestEnemy(SimState state, ActorState me) => EngagedTargets.Nearest(state, me);

    /// <summary>R-PATROL-3: the living engaged enemies whose target is <paramref name="me"/> (an idle prespawned enemy never counts).</summary>
    private static int AttackersOn(SimState state, ActorState me) =>
        state.Actors.Values.Count(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle && a.TargetId == me.Id);
}
