using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// KF1-SPAWN-HOLD (f-fa28e36e, Matt's demo verdict 2026-10-19 KF1 "combat animation reads badly"): the hold state, an
/// enemy that exists and stands at its position before it engages, is the most-seen enemy state once packs prespawn
/// (PRESPAWN-AGGRO). It must read as a living pack waiting, not a row of clones breathing in lockstep, and leaving it
/// must not slide a half-risen body across the floor: the KayKit skeleton rise (Spawn_Ground_Skeletons) is 3.567 s long
/// and a chaser moves 3.6 m/s, so a rise held to its end slid a buried body up to 12.8 m.
/// </summary>
public sealed class HoldAnimTests
{
    // A held, living enemy on a fresh tick with nothing to show: standing still, no cast, no swing, no hit.
    private static readonly AnimFacts Holding = new(
        Alive: true, NewTick: true, Moving: false, Casting: false, Acted: false, Hurt: false, Staggered: false,
        Playing: AnimClip.None);

    // KayKit Rig_Medium "Idle" (Skeleton_Minion.glb, Knight.glb): 1.067 s.
    private const int IdleLoopMs = 1067;

    [Fact]
    public void A_holding_enemy_plays_its_idle_loop_and_no_locomotion_or_attack()
    {
        var d = AnimRule.Decide(Holding);
        Assert.Equal(AnimClip.Idle, d.Clip);
        Assert.False(d.OneShot);
        Assert.False(d.Restart);
    }

    [Fact]
    public void A_still_quiet_enemy_keeps_rising()
    {
        Assert.True(HoldAnim.KeepsRise(Holding));
        Assert.True(HoldAnim.KeepsRise(Holding with { NewTick = false }));
        Assert.True(HoldAnim.KeepsRise(Holding with { Hurt = true })); // the hit flash shows it; the rise is not cut
    }

    [Fact]
    public void Moving_cuts_the_rise_so_the_body_never_slides_half_buried()
    {
        Assert.False(HoldAnim.KeepsRise(Holding with { Moving = true }));
        Assert.False(HoldAnim.KeepsRise(Holding with { Moving = true, NewTick = false }));
        // ... and the clip that replaces it is the run cycle.
        Assert.Equal(AnimClip.Move, AnimRule.Decide(Holding with { Moving = true }).Clip);
    }

    [Fact]
    public void A_swing_a_cast_or_a_stagger_cuts_the_rise()
    {
        Assert.False(HoldAnim.KeepsRise(Holding with { Acted = true }));
        Assert.False(HoldAnim.KeepsRise(Holding with { Casting = true }));
        Assert.False(HoldAnim.KeepsRise(Holding with { Staggered = true }));
        Assert.Equal(AnimClip.Act, AnimRule.Decide(Holding with { Acted = true }).Clip);
    }

    [Fact]
    public void Death_cuts_the_rise()
    {
        Assert.False(HoldAnim.KeepsRise(Holding with { Alive = false }));
    }

    [Fact]
    public void The_rise_cut_blends_rather_than_snaps()
    {
        // Longer than the animator's 150 ms clip blend: the body comes up out of the floor instead of popping.
        Assert.InRange(HoldAnim.RiseCutBlendMs, 200, 500);
    }

    [Fact]
    public void A_held_pack_does_not_breathe_in_lockstep()
    {
        // Pack actor ids are consecutive (layout.json spawns); prespawn starts every pack's idle on the same frame.
        int[] pack = { 101, 102, 103, 104 };
        var phases = pack.Select(id => HoldAnim.IdlePhaseMs(id, IdleLoopMs)).ToArray();
        Assert.All(phases, p => Assert.InRange(p, 0, IdleLoopMs - 1));
        Assert.Equal(pack.Length, phases.Distinct().Count());
        // Spread, not merely distinct: no two members within 100 ms of each other on the loop circle.
        for (var i = 0; i < phases.Length; i++)
        {
            for (var j = i + 1; j < phases.Length; j++)
            {
                var gap = System.Math.Abs(phases[i] - phases[j]);
                gap = System.Math.Min(gap, IdleLoopMs - gap);
                Assert.True(gap >= 100, $"actors {pack[i]} and {pack[j]} idle {gap} ms apart");
            }
        }
    }

    [Fact]
    public void Idle_phase_is_deterministic_and_safe_on_a_degenerate_loop()
    {
        Assert.Equal(HoldAnim.IdlePhaseMs(7, IdleLoopMs), HoldAnim.IdlePhaseMs(7, IdleLoopMs));
        Assert.Equal(0, HoldAnim.IdlePhaseMs(7, 0));
        Assert.Equal(0, HoldAnim.IdlePhaseMs(7, -5));
        Assert.InRange(HoldAnim.IdlePhaseMs(-3, IdleLoopMs), 0, IdleLoopMs - 1);
        Assert.InRange(HoldAnim.IdlePhaseMs(int.MaxValue, IdleLoopMs), 0, IdleLoopMs - 1);
    }
}
