using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-SEED-1a (finding f-244d102b, the first half of the Seed-room slice; orchestrator ruling: a melee closes until its
/// target is inside the reach of the ability it would cast, and a stationary caster is a normal target). Hollow Vault
/// key 2, seed 1, played by <see cref="PlaytestRunner"/> with every seat a bot. The Choir's caster lieutenant
/// (choir.lieutenant) never approaches.
/// <para>
/// KICK-LEDGER-COOLDOWN-GAP (f-13b5c33a) converts this from a single-seed trajectory pin to a property over the trajectory
/// (memory: no fitted green; the assertions are not loosened, they are restated over the run). The pin started at "the tick
/// the caster becomes the only engaged enemy", which exists only when the party kills the Choir's melee before the caster.
/// Since the Warden now walks in to kick the caster's Mending (RoleShared.LedgerKickApproachCommand) he stands beside the
/// caster and the party kills the healer first, so that tick never comes. The property it guarded holds on any trajectory:
/// a leader outside the strike reach of every engaged enemy closes to reach and does not stand there. Every stranded
/// episode (the leader more than 3000 mm from every living engaged enemy) ends within 600 ticks, there is at least one
/// (non-vacuity), and the stationary caster, engaged and never moving, dies within 3000 ticks of its engagement.
/// </para>
/// </summary>
public sealed class LeaderClosesToReachTests
{
    /// <summary>The leader's strike reach (warden.strike, 3000 mm): beyond it on every engaged enemy the leader is stranded, mm.</summary>
    public const double ReachMm = 3000;

    /// <summary>A stranded episode must end (the leader inside reach of an engaged enemy) within this many ticks (30 s would be 600 ticks at 20 Hz).</summary>
    public const int ClosesWithinTicks = 600;

    /// <summary>The caster must die within this many ticks of its engagement.</summary>
    public const int DieWithinTicks = 3000;

    /// <summary>The caster's enemy id in the Choir pull.</summary>
    public const string CasterClassId = "choir.lieutenant";

    private const int MaxTicks = 12000;

    private sealed class Probe
    {
        public int CasterId = -1;
        public int CasterEngagedTick = -1;
        public int CasterDeathTick = -1;
        public bool CasterMoved;
        public Vec3 CasterStart;
        public int StrandedSince = -1;
        public readonly List<(int Start, int End)> Episodes = new();
        public readonly List<string> Log = new();
    }

    private static readonly Lazy<Probe> Driven = new(Drive);

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

    private static Probe Drive()
    {
        var probe = new Probe();
        var options = new PlaytestOptions(2, 1, null)
        {
            MaxTicks = MaxTicks,
            Observe = (tick, run, _) => Observe(probe, tick, run),
        };
        PlaytestRunner.Play(options);
        return probe;
    }

    private static void Observe(Probe probe, int tick, Keyrun.Sim.Runs.Run run)
    {
        var state = run.State;
        if (probe.CasterId < 0)
        {
            var spawn = run.Dungeon.Pulls.Where(p => p.Id == "choir").SelectMany(p => p.Spawns).FirstOrDefault(s => s.ClassId == CasterClassId);
            if (spawn is null)
            {
                return;
            }
            probe.CasterId = spawn.ActorId;
        }
        if (Follow.Leader(state) is not { } leader)
        {
            return;
        }
        var engaged = state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle).ToList();
        if (engaged.Count > 0 && engaged.All(e => DistMm(leader.Pos, e.Pos) > ReachMm))
        {
            if (probe.StrandedSince < 0)
            {
                probe.StrandedSince = tick;
            }
        }
        else if (probe.StrandedSince >= 0)
        {
            probe.Episodes.Add((probe.StrandedSince, tick));
            probe.Log.Add($"{tick} leader {leader.Id} back inside strike reach after being stranded since {probe.StrandedSince} ({tick - probe.StrandedSince} ticks)");
            probe.StrandedSince = -1;
        }
        if (!state.Actors.TryGetValue(probe.CasterId, out var caster))
        {
            return;
        }
        if (probe.CasterEngagedTick < 0 && caster.Alive && !caster.Idle)
        {
            probe.CasterEngagedTick = tick;
            probe.CasterStart = caster.Pos;
            probe.Log.Add($"{tick} the caster {caster.Id} is engaged at ({caster.Pos.X},{caster.Pos.Z}); leader {leader.Id} at ({leader.Pos.X},{leader.Pos.Z}), {DistMm(leader.Pos, caster.Pos):F0} mm");
        }
        if (probe.CasterEngagedTick >= 0 && caster.Alive && (caster.Pos.X != probe.CasterStart.X || caster.Pos.Z != probe.CasterStart.Z))
        {
            probe.CasterMoved = true;
        }
        if (probe.CasterEngagedTick >= 0 && probe.CasterDeathTick < 0 && !caster.Alive)
        {
            probe.CasterDeathTick = tick;
            probe.Log.Add($"{tick} caster {caster.Id} dead");
        }
    }

    [Fact(DisplayName = "R-SEED-1a: a leader outside the strike reach of every engaged enemy closes to reach within 600 ticks, and the stationary caster dies within 3000 of its engagement")]
    public void Leader_closes_to_reach_and_the_stationary_caster_dies()
    {
        var t = Driven.Value;
        foreach (var line in t.Log)
        {
            _output.WriteLine(line);
        }
        _output.WriteLine($"R-SEED-1a episodes={string.Join(",", t.Episodes.Select(e => $"{e.Start}..{e.End}"))} stillStranded={t.StrandedSince} casterEngaged={t.CasterEngagedTick} casterDeath={t.CasterDeathTick} moved={t.CasterMoved}");
        Assert.True(t.Episodes.Count > 0, "the leader was never outside the strike reach of every engaged enemy; the closing check would be vacuous");
        var slow = t.Episodes.Where(e => e.End - e.Start > ClosesWithinTicks).ToList();
        Assert.True(slow.Count == 0, "the leader stood outside every engaged enemy's strike reach for more than " + ClosesWithinTicks + " ticks: " + string.Join(", ", slow.Select(e => $"{e.Start}..{e.End}")));
        Assert.True(t.StrandedSince < 0, $"the leader was still outside every engaged enemy's strike reach at the end of the run (since {t.StrandedSince})");
        Assert.True(t.CasterEngagedTick >= 0, "the Choir's caster lieutenant was never engaged");
        Assert.False(t.CasterMoved, "precondition: the caster moved; it is not the stationary caster this test pins");
        Assert.True(t.CasterDeathTick >= 0 && t.CasterDeathTick <= t.CasterEngagedTick + DieWithinTicks,
            $"the caster died at tick {t.CasterDeathTick} (want within {DieWithinTicks} of its engagement {t.CasterEngagedTick})");
    }
}
