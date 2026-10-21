using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// D54 ENEMIES-NEVER-DISPLACE (defer to WoW, the core dungeon loop rule; finding f-e17638f7, Matt 2026-10-20: enemies
/// stack on the player they attack). In Mythic+ a mob chasing or meleeing you never pushes you: it closes to melee range
/// and stacks on its target. Before D54 the rules' push-out was symmetric for every pair (the TS port), so a pack
/// chasing a stationary Warden shoved him across the room: HudSceneTest run B on PR #396 drove a Warden whose commands
/// were all <c>Dir=(0,0)</c> 18 m west with the Pair in single file behind him, only the lead warrior in reach.
/// <para>
/// The rule: enemy-versus-player separation moves the enemy only, by the whole overlap; a player standing still never
/// moves; enemy-versus-enemy and player-versus-player separation stay symmetric, so a pack fans around its target.
/// The chase here is the encounter chase's own rule (<see cref="Keyrun.Sim.Encounter.Chase"/>): a melee enemy moves
/// straight at its target until it is within <c>range - margin</c> (3000 - 500 mm, pair.meleeSwing), at the shipped
/// actor radius (400 mm).
/// </para>
/// </summary>
public sealed class EnemiesNeverDisplacePlayerTests
{
    private const int Warden = 1;
    private const long RadiusMm = 400;
    private const long SwingRangeMm = 3000;
    private const long StopMm = SwingRangeMm - 500;
    private const int Ticks = 400;

    private static readonly long[][] PackFromTheEast =
    {
        new long[] { 6000, 0 },
        new long[] { 6800, 0 },
        new long[] { 7600, 300 },
        new long[] { 6400, -500 },
        new long[] { 7200, 700 },
    };

    // HudSceneTest run B's Pair as PRESPAWN-AGGRO places it (Pulls.SpawnAllIdle: one row, 800 mm apart, on the Warden's line).
    private static readonly long[][] PairRow =
    {
        new long[] { 5000, 0 },
        new long[] { 5800, 0 },
        new long[] { 6600, 0 },
        new long[] { 7400, 0 },
        new long[] { 8200, 0 },
    };

    [Fact]
    public void The_Pair_row_chasing_a_standing_player_never_moves_him_and_all_five_reach_melee()
    {
        AssertStandsAndAllReach(PairRow);
    }

    [Fact]
    public void Five_melee_enemies_converging_on_a_standing_player_never_move_him_and_all_reach_melee()
    {
        AssertStandsAndAllReach(PackFromTheEast);
    }

    private static void AssertStandsAndAllReach(long[][] pack)
    {
        var state = Converge(pack, out var rules);
        for (var i = 0; i < Ticks; i++)
        {
            Tick(state, rules, ChaseAndStand(state), _ => { });
            var w = state.Actors[Warden];
            Assert.True(w.Pos.X == 0 && w.Pos.Z == 0, FormattableString.Invariant($"tick {state.Tick}: the standing Warden was moved to ({w.Pos.X},{w.Pos.Z})"));
        }
        foreach (var enemy in state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy))
        {
            var d = Casting.RangeMm(enemy, state.Actors[Warden]);
            Assert.True(d <= SwingRangeMm, FormattableString.Invariant($"enemy {enemy.Id} at ({enemy.Pos.X},{enemy.Pos.Z}) is {d} mm off, past melee range {SwingRangeMm}"));
        }
    }

    [Fact]
    public void An_enemy_overlapping_a_player_is_moved_out_by_the_whole_overlap_and_the_player_stays()
    {
        var state = At(0, Make(Warden, ActorKind.Player, "warden", ActorRole.Tank, 0, 0, 400), Make(10, ActorKind.Enemy, "pair.melee", ActorRole.None, 500, 0, 1200));
        var rules = Rules.Make(Config(AbilityMap(), actorRadiusMm: RadiusMm));
        Tick(state, rules, Cmds(), _ => { });

        Assert.Equal(new Vec3(0, 0, 0), state.Actors[Warden].Pos);
        Assert.Equal(new Vec3(2 * RadiusMm, 0, 0), state.Actors[10].Pos);
    }

    [Fact]
    public void Two_enemies_overlapping_each_other_still_part_symmetrically()
    {
        var state = At(0, Make(10, ActorKind.Enemy, "pair.melee", ActorRole.None, 0, 0, 1200), Make(11, ActorKind.Enemy, "pair.melee", ActorRole.None, 400, 0, 1200));
        var rules = Rules.Make(Config(AbilityMap(), actorRadiusMm: RadiusMm));
        Tick(state, rules, Cmds(), _ => { });

        Assert.Equal(new Vec3(-200, 0, 0), state.Actors[10].Pos);
        Assert.Equal(new Vec3(600, 0, 0), state.Actors[11].Pos);
    }

    private static SimState Converge(long[][] pack, out StepRules rules)
    {
        var actors = new List<ActorState> { Make(Warden, ActorKind.Player, "warden", ActorRole.Tank, 0, 0, 400) };
        for (var i = 0; i < pack.Length; i++)
        {
            // Each chaser is on the Warden, as the encounter's targeting (no threat yet: the nearest player) sets it.
            var enemy = Make(10 + i, ActorKind.Enemy, "pair.melee", ActorRole.None, pack[i][0], pack[i][1], 1200);
            enemy.TargetId = Warden;
            actors.Add(enemy);
        }
        rules = Rules.Make(Config(AbilityMap(), actorRadiusMm: RadiusMm) with { BaseSpeedMmPerTick = 60 });
        return At(0, actors.ToArray());
    }

    // The Warden stands (a zero move only sets his yaw); each enemy closes straight at him until within StopMm.
    private static IReadOnlyList<Command> ChaseAndStand(SimState state)
    {
        var cmds = new List<Command> { new MoveCommand(state.Tick, Warden, new Dir2(0, 0), 0) };
        var w = state.Actors[Warden];
        foreach (var e in state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy).OrderBy(a => a.Id))
        {
            if (Casting.RangeMm(e, w) <= StopMm)
            {
                continue;
            }
            var dx = w.Pos.X - e.Pos.X;
            var dz = w.Pos.Z - e.Pos.Z;
            var len = Movement.Isqrt((dx * dx) + (dz * dz));
            cmds.Add(new MoveCommand(state.Tick, e.Id, new Dir2(IntMath.TruncDiv(dx * 1000, len), IntMath.TruncDiv(dz * 1000, len)), 0));
        }
        return cmds;
    }
}
