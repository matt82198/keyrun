using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// BOT-RESPAWN-RETURN on the real host path (finding f-30d6273b, R-PATH-5 (i)): the headless autorun replica
/// <see cref="PlaytestRunner"/> at key 2 on the Hollow Vault, seed 1, the same party and route leader the host runs. The
/// route leader (seat <see cref="LeaderId"/>) is killed by a scripted forced death set in the <c>Observe</c> hook (the
/// run state is shared and mutable, so the next step sees the death) at the first tick it stands at least
/// <see cref="FarMm"/> from the Entrance with an engaged enemy alive. The sim releases and respawns it at the Entrance.
/// Facts: the respawned leader issues a non-zero move toward the party within <see cref="MoveWindowTicks"/> ticks, and gets
/// within <see cref="ReachMm"/> of a living seat within <see cref="ReturnWindowTicks"/> ticks. The per-tick trace for
/// <see cref="TraceTicks"/> ticks after the respawn is written to the test output.
/// </summary>
public sealed class BotRespawnReturnHostTests
{
    /// <summary>The route leader (the autorun Warden, the lowest seat).</summary>
    private const int LeaderId = 1;

    private const long Seed = 1;
    private const long FarMm = 30000;
    private const int MoveWindowTicks = 100;
    private const int ReturnWindowTicks = 2000;
    private const long ReachMm = 5000;
    private const int TraceTicks = 200;

    /// <summary>One post-step observation of the leader: alive, position, nearest living other seat, and its move this tick.</summary>
    private sealed record Sample(int Tick, bool Alive, Vec3 Self, Vec3? Party, long Distance, MoveCommand? Move);

    private sealed record Drive(int KillTick, int RespawnTick, Vec3 SpawnPos, List<Sample> Samples, PlaytestResult Result);

    private static readonly Lazy<Drive> Driven = new(DriveRun);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the per-tick trace is written there).</summary>
    /// <param name="output">The sink.</param>
    public BotRespawnReturnHostTests(ITestOutputHelper output) => _output = output;

    private static Drive DriveRun()
    {
        var samples = new List<Sample>();
        var killTick = -1;
        var respawnTick = -1;
        var spawnPos = new Vec3(0, 0, 0);
        var spawnKnown = false;

        var result = PlaytestRunner.Play(new PlaytestOptions(2, Seed, null)
        {
            DungeonId = "hollow-vault",
            Observe = (tick, run, commands) =>
            {
                if (!spawnKnown && run.Spawns is { } spawns && spawns.TryGetValue(LeaderId, out var seatSpawn))
                {
                    spawnPos = seatSpawn.Pos;
                    spawnKnown = true;
                }
                if (!run.State.Actors.TryGetValue(LeaderId, out var leader))
                {
                    return;
                }

                // Scripted forced death: the party is far from the Entrance and an engaged enemy lives.
                if (killTick < 0 && leader.Alive && spawnKnown && Follow.AnyLivingEnemy(run.State) && Distance(leader.Pos, spawnPos) >= FarMm)
                {
                    leader.Alive = false;
                    leader.Health = 0;
                    killTick = tick;
                }
                else if (killTick >= 0 && respawnTick < 0 && leader.Alive)
                {
                    respawnTick = tick;
                }

                var nearest = NearestLivingPlayer(run.State, LeaderId);
                var move = commands.OfType<MoveCommand>().FirstOrDefault(c => c.ActorId == LeaderId);
                samples.Add(new Sample(
                    tick,
                    leader.Alive,
                    leader.Pos,
                    nearest?.Pos,
                    nearest is null ? -1 : Distance(leader.Pos, nearest.Pos),
                    move));
            },
        });
        return new Drive(killTick, respawnTick, spawnPos, samples, result);
    }

    /// <summary>Fact 1: the respawned leader issues a non-zero move toward the party within 100 ticks of its respawn.</summary>
    [Fact(DisplayName = "Host path bot respawn return: the respawned route leader moves toward the party within 100 ticks")]
    public void Respawned_leader_moves_toward_the_party_within_100_ticks()
    {
        var d = Driven.Value;
        WriteTrace(d);
        Assert.True(d.KillTick >= 0, $"leader {LeaderId} was never killed: no tick had it {FarMm} mm from the entrance with an engaged enemy alive");
        Assert.True(d.RespawnTick >= 0, $"leader {LeaderId} was killed at tick {d.KillTick} and never respawned before the run ended");

        var window = d.Samples.Where(s => s.Tick >= d.RespawnTick && s.Tick <= d.RespawnTick + MoveWindowTicks).ToList();
        var toward = window.Any(s => s.Alive && s.Move is { Dir: var dir } && (dir.X != 0 || dir.Z != 0) && s.Party is { } p &&
            dir.X * (p.X - s.Self.X) + dir.Z * (p.Z - s.Self.Z) > 0);
        if (toward)
        {
            return;
        }

        var anyMove = window.Any(s => s.Alive && s.Move is not null);
        var atRespawn = window.FirstOrDefault();
        var reason = anyMove
            ? $"its moves by tick {d.RespawnTick + MoveWindowTicks} did not point at the party"
            : $"no move command by tick {d.RespawnTick + MoveWindowTicks}";
        Assert.Fail(
            $"leader {LeaderId} respawned at {d.RespawnTick} at ({d.SpawnPos.X},{d.SpawnPos.Z}); {reason}; " +
            $"nearest party seat {atRespawn?.Distance} mm away");
    }

    /// <summary>Fact 2: the respawned leader gets within 5000 mm of a living seat within 2000 ticks of its respawn.</summary>
    [Fact(DisplayName = "Host path bot respawn return: the respawned route leader gets back within 5000 mm of a living seat within 2000 ticks")]
    public void Respawned_leader_rejoins_the_party_within_5000_mm()
    {
        var d = Driven.Value;
        Assert.True(d.KillTick >= 0, $"leader {LeaderId} was never killed: no tick had it {FarMm} mm from the entrance with an engaged enemy alive");
        Assert.True(d.RespawnTick >= 0, $"leader {LeaderId} was killed at tick {d.KillTick} and never respawned before the run ended");

        var window = d.Samples
            .Where(s => s.Alive && s.Party is not null && s.Tick >= d.RespawnTick && s.Tick <= d.RespawnTick + ReturnWindowTicks)
            .ToList();
        Assert.NotEmpty(window);

        var closest = window.MinBy(s => s.Distance)!;
        if (closest.Distance < ReachMm)
        {
            return;
        }
        var first = window[0];
        Assert.Fail(
            $"leader {LeaderId} respawned at {d.RespawnTick} at ({d.SpawnPos.X},{d.SpawnPos.Z}); nearest living seat {first.Distance} mm away at tick {first.Tick}; " +
            $"closest {closest.Distance} mm at tick {closest.Tick} within {ReturnWindowTicks} ticks (needs under {ReachMm} mm)");
    }

    /// <summary>Writes the per-tick path of the respawned leader for <see cref="TraceTicks"/> ticks after its respawn.</summary>
    private void WriteTrace(Drive d)
    {
        _output.WriteLine($"kill tick {d.KillTick}, respawn tick {d.RespawnTick}, spawn ({d.SpawnPos.X},{d.SpawnPos.Z}), run result {d.Result}");
        if (d.RespawnTick < 0)
        {
            return;
        }
        foreach (var s in d.Samples.Where(s => s.Tick >= d.RespawnTick && s.Tick < d.RespawnTick + TraceTicks))
        {
            var target = s.Party is { } p ? $"({p.X},{p.Z})" : "none";
            var move = s.Move is { } m ? $"dir=({m.Dir.X},{m.Dir.Z}) yaw={m.Yaw}" : "dir=none";
            _output.WriteLine($"t={s.Tick} alive={s.Alive} pos=({s.Self.X},{s.Self.Z}) target={target} nearest={s.Distance}mm {move}");
        }
    }

    /// <summary>Euclidean distance on the floor plane, mm, rounded.</summary>
    private static long Distance(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (long)Math.Round(Math.Sqrt((double)(dx * dx + dz * dz)));
    }

    /// <summary>The living player seat nearest to <paramref name="self"/>, other than itself; null when none lives.</summary>
    private static ActorState? NearestLivingPlayer(SimState state, int self)
    {
        var me = state.Actors[self].Pos;
        ActorState? best = null;
        var bestDistance = long.MaxValue;
        foreach (var a in state.Actors.Values)
        {
            if (a.Kind != ActorKind.Player || !a.Alive || a.Id == self)
            {
                continue;
            }
            var d = Distance(a.Pos, me);
            if (d < bestDistance)
            {
                best = a;
                bestDistance = d;
            }
        }
        return best;
    }
}
