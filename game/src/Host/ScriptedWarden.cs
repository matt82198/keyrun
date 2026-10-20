using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

// Godot-free (no Godot using): split out of PlayerInput.cs so a headless test can compile the autorun Warden by link
// and step the arena's exact run (tools/Keyrun.Playtest.Tests/HostBackfillRunTests.cs, HudSceneTest runs F and G).

/// <summary>
/// player-commands.ts castTargetFor (D10 first-run fix), Godot-free: a hostile single-target ability with no live enemy
/// targeted goes at the nearest live enemy (squared XZ distance, lowest id on a tie); everything else keeps the current
/// target, else the caster.
/// </summary>
public static class CastTargeting
{
    /// <summary>The target a human cast of <paramref name="abilityId"/> is sent at.</summary>
    /// <param name="run">The run.</param>
    /// <param name="humanId">The caster.</param>
    /// <param name="abilityId">The ability.</param>
    /// <returns>The target id.</returns>
    public static int For(Run run, int humanId, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(run);
        var actors = run.State.Actors;
        if (!actors.TryGetValue(humanId, out var player))
        {
            return humanId;
        }
        var current = player.TargetId;
        if (!run.Content.Abilities.TryGetValue(abilityId, out var def) || def.Shape.Kind != ShapeKind.Target ||
            Affinities.Of(def, run.Content.Effects) != Affinity.Hostile)
        {
            return current ?? humanId;
        }
        if (current is { } c && actors.TryGetValue(c, out var cur) && cur.Alive && cur.Kind != player.Kind)
        {
            return cur.Id;
        }
        int? best = null;
        long bestD2 = 0;
        foreach (var a in actors.Values)
        {
            if (!a.Alive || a.Kind == player.Kind)
            {
                continue;
            }
            var dx = a.Pos.X - player.Pos.X;
            var dz = a.Pos.Z - player.Pos.Z;
            var d2 = (dx * dx) + (dz * dz);
            if (best is null || d2 < bestD2 || (d2 == bestD2 && a.Id < best))
            {
                best = a.Id;
                bestD2 = d2;
            }
        }
        return best ?? current ?? humanId;
    }
}

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
    /// <summary>The human seat's actor id (SimDriver.WardenId is this value).</summary>
    public const int WardenId = 1;

    /// <summary>Stop walking once the lieutenant is this close (Shield Bash range is 3000 mm).</summary>
    public const long ApproachMm = 2400;

    /// <summary>The lieutenant's class id.</summary>
    public const string LieutenantClass = "choir.lieutenant";

    private static readonly string[] Rotation = { "warden.reckoning", "warden.vigil", "warden.guardBreak", "warden.strike" };

    private const string Kick = "warden.shieldBash";
    private const string Defensive = "warden.brace";

    /// <summary>Appends the scripted commands for <paramref name="tick"/>.</summary>
    /// <param name="tick">The pre-step tick.</param>
    /// <param name="run">The run.</param>
    /// <param name="into">The command list.</param>
    public void Sample(int tick, Run run, List<Command> into)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(into);
        var state = run.State;
        if (!state.Actors.TryGetValue(WardenId, out var me) || !me.Alive)
        {
            return;
        }
        if (run.Content.Layout is not null && !Follow.AnyLivingEnemy(state))
        {
            if (Follow.LeadRoute(tick, me, run) is { } walk)
            {
                into.Add(walk);
            }
            return;
        }
        var abilities = run.Content.Abilities;
        var lieutenant = state.Actors.Values.FirstOrDefault(a => a.Alive && a.Kind == ActorKind.Enemy && a.ClassId == LieutenantClass);

        if (lieutenant is { CastId: { } castId, CastStartTick: { } start } && abilities.TryGetValue(castId, out var cast) && cast.Interruptible &&
            tick >= start + (cast.CastTicks / 2) && Ready(me, abilities[Kick], tick) && Casting.RangeMm(me, lieutenant) <= abilities[Kick].Range)
        {
            into.Add(new CastCommand(tick, me.Id, Kick, lieutenant.Id));
        }

        if (lieutenant is not null && Casting.RangeMm(me, lieutenant) > ApproachMm)
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

        if (!Cooldowns.IsGcdReady(me, tick))
        {
            return;
        }
        if (me.Health * 2 < me.MaxHealth && Ready(me, abilities[Defensive], tick))
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

    /// <summary>The living enemy nearest <paramref name="me"/> (squared XZ distance, lowest id on a tie), or null.</summary>
    private static ActorState? NearestEnemy(SimState state, ActorState me)
    {
        ActorState? best = null;
        long bestD2 = 0;
        foreach (var a in state.Actors.Values)
        {
            if (!a.Alive || a.Kind != ActorKind.Enemy)
            {
                continue;
            }
            var dx = a.Pos.X - me.Pos.X;
            var dz = a.Pos.Z - me.Pos.Z;
            var d2 = (dx * dx) + (dz * dz);
            if (best is null || d2 < bestD2 || (d2 == bestD2 && a.Id < best.Id))
            {
                best = a;
                bestD2 = d2;
            }
        }
        return best;
    }
}
