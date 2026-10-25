using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// BOT-RESPAWN-RETURN (finding f-30d6273b, R-PATH-5 (i); WoW Mythic+: a dead player releases and runs back to the group).
/// The host shape: every seat is a bot driven by <see cref="Bot.RunBots"/> (the route leader included, no scripted Warden),
/// on the real hollow-vault k2 layout. The route leader (the lowest living seat, <see cref="LeaderId"/>) is killed by a
/// scripted forced death (its state is set dead) at the first tick it stands at least <see cref="FarMm"/> from the Entrance
/// with an engaged enemy alive. The sim releases it (<see cref="RunWiring.ReleaseDelayTicks"/>) and respawns it at the
/// Entrance, where the run placed it. Both facts pin the walk back: the respawned leader must issue a non-zero move within
/// <see cref="MoveWindowTicks"/> ticks, and must reach within <see cref="ReachMm"/> of a living seat within
/// <see cref="ReturnWindowTicks"/> ticks of its respawn. The per-tick path trace for <see cref="TraceTicks"/> ticks after the
/// respawn is written to the test output (rule id of the command, Dir, Yaw, the nearest living seat as target).
/// </summary>
public sealed class BotRespawnReturnTests
{
    /// <summary>The route leader: the lowest seat, killed and respawned.</summary>
    private const int LeaderId = 1;

    private const long Seed = 1;
    private const int MaxTicks = 12000;
    private const int HealthCap = 1500;

    /// <summary>The kill waits until the leader is this far (mm) from the Entrance, so the party is far from the respawn point.</summary>
    private const long FarMm = 30000;

    /// <summary>Fact 1: a non-zero move toward the party must be issued within this many ticks of the respawn tick.</summary>
    private const int MoveWindowTicks = 100;

    /// <summary>Fact 2: the respawned leader must reach a living seat within this many ticks of its respawn.</summary>
    private const int ReturnWindowTicks = 2000;

    /// <summary>Fact 2: the respawned leader is back with the party once it is this close (mm) to a living seat.</summary>
    private const long ReachMm = 5000;

    /// <summary>The per-tick trace length after the respawn, printed to the test output.</summary>
    private const int TraceTicks = 200;

    /// <summary>One pre-step observation of the killed seat: alive, position, nearest living other seat, and its move and rule this tick.</summary>
    private sealed record Sample(int Tick, bool Alive, Vec3 Self, Vec3? Party, long Distance, MoveCommand? Move, string Rule);

    private sealed record Drive(int KillTick, int RespawnTick, Vec3 SpawnPos, List<Sample> Samples);

    private static readonly Lazy<Drive> Driven = new(DriveRun);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the per-tick path trace is written there).</summary>
    /// <param name="output">The sink.</param>
    public BotRespawnReturnTests(ITestOutputHelper output) => _output = output;

    private static Drive DriveRun()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        var capped = setup.Dungeon with
        {
            Pulls = setup.Dungeon.Pulls.Select(p => p with { Spawns = p.Spawns.Select(s => s with { Health = Math.Min(s.Health, HealthCap) }).ToList() }).ToList(),
        };
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, capped);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);

        var kits = KitRegistry.From(registry);
        var bots = new SortedDictionary<int, IBot>();
        var memories = new SortedDictionary<int, BotMemory>();
        foreach (var p in party)
        {
            bots[p.Id] = Bot.MakeBot(RoleSet.Default);
            memories[p.Id] = BotMemory.Default(80);
        }
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(Seed);

        var spawnPos = Release.SpawnsOf(run.State)[LeaderId].Pos;
        var killTick = -1;
        var respawnTick = -1;
        var samples = new List<Sample>();

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;

            // Scripted forced death of the route leader: the party is far from the Entrance and an engaged enemy lives.
            if (killTick < 0 &&
                run.State.Actors.TryGetValue(LeaderId, out var victim) && victim.Alive &&
                Follow.AnyLivingEnemy(run.State) &&
                Distance(victim.Pos, spawnPos) >= FarMm)
            {
                victim.Alive = false;
                victim.Health = 0;
                killTick = tick;
            }

            // Pre-step observation of the killed seat (a dead seat carries no move).
            var seat = run.State.Actors[LeaderId];
            var nearest = NearestLivingPlayer(run.State, LeaderId);

            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();

            // The move the leader issues this tick, and the rule id that issued it (Reasons is index-aligned with Commands).
            var move = commands.OfType<MoveCommand>().FirstOrDefault(c => c.ActorId == LeaderId);
            var rule = string.Empty;
            if (move is not null && batch.Reasons.Count == commands.Count)
            {
                rule = batch.Reasons[commands.FindIndex(c => ReferenceEquals(c, move))].RuleId;
            }

            samples.Add(new Sample(
                tick,
                seat.Alive,
                seat.Pos,
                nearest?.Pos,
                nearest is null ? -1 : Distance(seat.Pos, nearest.Pos),
                move,
                rule));

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events.Where(e => e.Type == "respawn" && ((JsInt)e.Get("actorId")!).Value == LeaderId))
            {
                respawnTick = respawnTick < 0 ? e.Tick : respawnTick;
            }
        }
        return new Drive(killTick, respawnTick, spawnPos, samples);
    }

    /// <summary>
    /// Fact 1: a leader that respawns at the entrance while the party is far away issues a non-zero move toward the party
    /// within 100 ticks of the respawn tick.
    /// </summary>
    [Fact(DisplayName = "Bot respawn return: the respawned route leader moves toward the party within 100 ticks")]
    public void Respawned_leader_moves_toward_the_party_within_100_ticks()
    {
        var d = Driven.Value;
        WriteTrace(d);
        Assert.True(d.KillTick >= 0, $"leader {LeaderId} was never killed: no tick in {MaxTicks} had it {FarMm} mm from the entrance with an engaged enemy alive");
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

    /// <summary>
    /// Fact 2: the respawned leader's distance to the nearest living seat falls below 5000 mm within 2000 ticks of its respawn.
    /// </summary>
    [Fact(DisplayName = "Bot respawn return: the respawned route leader gets back within 5000 mm of a living seat within 2000 ticks")]
    public void Respawned_leader_rejoins_the_party_within_5000_mm()
    {
        var d = Driven.Value;
        Assert.True(d.KillTick >= 0, $"leader {LeaderId} was never killed: no tick in {MaxTicks} had it {FarMm} mm from the entrance with an engaged enemy alive");
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
        _output.WriteLine($"kill tick {d.KillTick}, respawn tick {d.RespawnTick}, spawn ({d.SpawnPos.X},{d.SpawnPos.Z})");
        if (d.RespawnTick < 0)
        {
            return;
        }
        foreach (var s in d.Samples.Where(s => s.Tick >= d.RespawnTick && s.Tick < d.RespawnTick + TraceTicks))
        {
            var target = s.Party is { } p ? $"({p.X},{p.Z})" : "none";
            var move = s.Move is { } m ? $"rule={s.Rule} dir=({m.Dir.X},{m.Dir.Z}) yaw={m.Yaw}" : "rule=none dir=none";
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
