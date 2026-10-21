using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-SEED-1a (finding f-244d102b, the first half of the Seed-room slice; orchestrator ruling: a melee closes until its
/// target is inside the reach of the ability it would cast, and a stationary caster is a normal target). Hollow Vault
/// key 2, seed 1, played by <see cref="PlaytestRunner"/> with every seat a bot. The Choir's caster lieutenant
/// (choir.lieutenant) never approaches. The scenario starts at the tick the caster becomes the only engaged enemy (the
/// Choir's melee are dead or idle), which is the engagement this test pins. There the caster must be more than 3000 mm
/// (outside the Warden's 3000 mm strike reach) and less than 6000 mm from the leader; the leader must then close to reach
/// and land at least one strike cast on it within 600 ticks; and the caster must die within 3000 ticks.
/// The preconditions (the caster is stationary and starts at 3000 to 6000 mm) are asserted first, so a red run names the
/// broken part of the scenario, not the leader.
/// </summary>
public sealed class LeaderClosesToReachTests
{
    /// <summary>The caster must start farther than the leader's strike reach (warden.strike, 3000 mm), mm.</summary>
    public const double MinStartMm = 3000;

    /// <summary>The caster must start nearer than this (engaged from outside reach, not across the room), mm.</summary>
    public const double MaxStartMm = 6000;

    /// <summary>The leader must cast on the caster within this many ticks of the engagement (30 s would be 600 ticks at 20 Hz).</summary>
    public const int CastWithinTicks = 600;

    /// <summary>The caster must die within this many ticks of the engagement.</summary>
    public const int DieWithinTicks = 3000;

    /// <summary>The caster's enemy id in the Choir pull.</summary>
    public const string CasterClassId = "choir.lieutenant";

    /// <summary>The Warden's strike (the basic attack, range 3000 mm): the cast the test requires on the caster.</summary>
    public const string StrikeAbilityId = "warden.strike";

    private const int MaxTicks = 12000;

    private sealed class Probe
    {
        public int CasterId = -1;
        public int LeaderId = -1;
        public int EngageTick = -1;
        public double StartMm = -1;
        public Vec3 StartPos;
        public bool Moved;
        public int FirstCastTick = -1;
        public int DeathTick = -1;
        public readonly List<string> Log = new();
    }

    private sealed record Tally(int EngageTick, double StartMm, bool Moved, int FirstCastTick, int DeathTick, List<string> Log);

    private static readonly Lazy<Tally> Driven = new(Drive);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the trace is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public LeaderClosesToReachTests(ITestOutputHelper output) => _output = output;

    private static double DistMm(Vec3 a, Vec3 b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    private static Tally Drive()
    {
        var probe = new Probe();
        var options = new PlaytestOptions(2, 1, null)
        {
            MaxTicks = MaxTicks,
            Observe = (tick, run, commands) => Observe(probe, tick, run, commands),
        };
        PlaytestRunner.Play(options);
        return new Tally(probe.EngageTick, probe.StartMm, probe.Moved, probe.FirstCastTick, probe.DeathTick, probe.Log);
    }

    private static void Observe(Probe probe, int tick, Keyrun.Sim.Runs.Run run, IReadOnlyList<Command> commands)
    {
        if (probe.CasterId < 0)
        {
            var spawn = run.Dungeon.Pulls.Where(p => p.Id == "choir").SelectMany(p => p.Spawns).FirstOrDefault(s => s.ClassId == CasterClassId);
            if (spawn is null)
            {
                return;
            }
            probe.CasterId = spawn.ActorId;
        }
        var state = run.State;
        if (!state.Actors.TryGetValue(probe.CasterId, out var caster) || Follow.Leader(state) is not { } leader)
        {
            return;
        }
        probe.LeaderId = leader.Id;
        if (probe.EngageTick < 0)
        {
            var engaged = state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle).Select(a => a.Id).ToList();
            if (engaged.Count == 1 && engaged[0] == probe.CasterId)
            {
                probe.EngageTick = tick;
                probe.StartMm = DistMm(leader.Pos, caster.Pos);
                probe.StartPos = caster.Pos;
                probe.Log.Add($"{tick} engaged: the caster {caster.Id} is the only engaged enemy, at ({caster.Pos.X},{caster.Pos.Z}); leader {leader.Id} at ({leader.Pos.X},{leader.Pos.Z}), {probe.StartMm:F0} mm");
            }
            return;
        }
        if (caster.Alive && (caster.Pos.X != probe.StartPos.X || caster.Pos.Z != probe.StartPos.Z))
        {
            probe.Moved = true;
        }
        if (probe.DeathTick < 0 && !caster.Alive)
        {
            probe.DeathTick = tick;
            probe.Log.Add($"{tick} caster {caster.Id} dead");
        }
        var cast = commands.OfType<CastCommand>().FirstOrDefault(c => c.ActorId == leader.Id && c.TargetId == probe.CasterId && c.AbilityId == StrikeAbilityId);
        if (cast is not null && probe.FirstCastTick < 0)
        {
            probe.FirstCastTick = tick;
            probe.Log.Add($"{tick} leader {leader.Id} casts {cast.AbilityId} on caster {probe.CasterId}, {DistMm(leader.Pos, caster.Pos):F0} mm");
        }
        if ((tick - probe.EngageTick) % 100 == 0)
        {
            probe.Log.Add($"{tick} sample leader ({leader.Pos.X},{leader.Pos.Z}) caster ({caster.Pos.X},{caster.Pos.Z}) {DistMm(leader.Pos, caster.Pos):F0} mm");
        }
    }

    [Fact(DisplayName = "R-SEED-1a: a leader engaged with a stationary caster outside its strike reach closes to reach, casts on it within 600 ticks, and the caster dies within 3000")]
    public void Leader_closes_to_reach_and_strikes_a_stationary_caster()
    {
        var t = Driven.Value;
        foreach (var line in t.Log)
        {
            _output.WriteLine(line);
        }
        _output.WriteLine($"R-SEED-1a engageTick={t.EngageTick} startMm={t.StartMm:F0} moved={t.Moved} firstCastTick={t.FirstCastTick} deathTick={t.DeathTick}");
        Assert.True(t.EngageTick >= 0, "the Choir's caster lieutenant was never the only engaged enemy");
        Assert.True(t.StartMm > MinStartMm && t.StartMm < MaxStartMm,
            $"precondition: the caster starts {t.StartMm:F0} mm from the leader at engagement (want {MinStartMm}..{MaxStartMm}, outside the 3000 mm strike reach)");
        Assert.False(t.Moved, "precondition: the caster moved; it is not the stationary caster this test pins");
        Assert.True(t.FirstCastTick >= t.EngageTick && t.FirstCastTick <= t.EngageTick + CastWithinTicks,
            $"the leader's first {StrikeAbilityId} on the caster was at tick {t.FirstCastTick} (want {t.EngageTick}..{t.EngageTick + CastWithinTicks}); it never closed to reach and struck");
        Assert.True(t.DeathTick >= 0 && t.DeathTick <= t.EngageTick + DieWithinTicks,
            $"the caster died at tick {t.DeathTick} (want within {DieWithinTicks} of engagement {t.EngageTick})");
    }
}
