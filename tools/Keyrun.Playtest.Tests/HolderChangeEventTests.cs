using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// HOLDER-CHANGE EVENT (finding f-6dbdd121, G7 R-ARCH-4 observability gap; threat work R-THREAT-2 slice S4 measures from
/// the log). The playtest event log (<see cref="PlaytestResult.Log"/>, one "E {...}" line per sim event) records no event
/// when an enemy's holder changes, so a trace cannot show which seat held which enemy. The holder is set at engage (the
/// body pull, Combat/Holder.cs LockHolders), at taunt (Holder.ApplyTaunt) and on the holder's death. This pins the
/// engage case on the shipped autorun (key 2, seed 1, the same run as <see cref="AggroChaseAutorunTests"/>): every enemy
/// that goes from no holder to a party seat must have a <c>holder_change</c> event in the log naming the enemy id, the
/// new holder seat and the tick the change landed on. The tick is the step that set the holder (observed one tick later,
/// in the pre-step snapshot), so it is at T-1 or T for a first observed holder at T.
/// </summary>
public sealed class HolderChangeEventTests
{
    /// <summary>The event type the log must carry for a holder change (fields: enemyId, holderId; tick is the envelope tick).</summary>
    private const string HolderChange = "holder_change";

    private readonly ITestOutputHelper _out;

    public HolderChangeEventTests(ITestOutputHelper output) => _out = output;

    /// <summary>An enemy that went from no holder to <paramref name="HolderId"/> at <paramref name="Tick"/> (first observed).</summary>
    private sealed record Engage(int EnemyId, int HolderId, int Tick);

    private sealed record Trace(PlaytestResult Result, IReadOnlyList<Engage> Engages, IReadOnlyList<JsonElement> Events);

    private static readonly Lazy<Trace> Autorun = new(() =>
    {
        // Pre-step snapshot per tick: enemy id -> holder id (null when no holder).
        var holders = new SortedDictionary<int, Dictionary<int, int?>>();
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null)
        {
            PullToForces = false,
            Observe = (tick, run, _) => holders[tick] = run.State.Actors.Values
                .Where(a => a.Kind == ActorKind.Enemy)
                .ToDictionary(a => a.Id, a => a.HolderId),
        }, ContentLoader.Load());

        var ticks = holders.Keys.ToList();
        var engages = new List<Engage>();
        foreach (var enemyId in holders[ticks[0]].Where(kv => kv.Value is null).Select(kv => kv.Key))
        {
            foreach (var tick in ticks)
            {
                if (holders[tick].TryGetValue(enemyId, out var holder) && holder is { } seat)
                {
                    engages.Add(new Engage(enemyId, seat, tick));
                    break;
                }
            }
        }

        var events = result.Log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement)
            .ToList();
        return new Trace(result, engages, events);
    });

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static int Tick(JsonElement e) => e.GetProperty("tick").GetInt32();

    [Fact(DisplayName = "Holder change is logged: every enemy that engages gets a holder_change event naming the enemy, the seat and the tick")]
    public void Engage_is_logged_as_a_holder_change_naming_enemy_seat_and_tick()
    {
        var t = Autorun.Value;
        var seats = t.Result.Setup.Party.Select(p => p.Id).ToHashSet();
        _out.WriteLine($"engages (enemy -> seat @tick): {string.Join(", ", t.Engages.Select(e => $"{e.EnemyId} -> {e.HolderId} @{e.Tick}"))}");

        // Precondition: the autorun does engage enemies, and every holder is a party seat (the body pull locks a player).
        Assert.NotEmpty(t.Engages);
        Assert.All(t.Engages, e => Assert.Contains(e.HolderId, seats));

        var changes = t.Events.Where(e => Str(e, "type") == HolderChange).ToList();
        _out.WriteLine($"holder_change events in the log: {changes.Count}");

        foreach (var engage in t.Engages)
        {
            Assert.True(
                changes.Any(e => Int(e, "enemyId") == engage.EnemyId
                    && Int(e, "holderId") == engage.HolderId
                    && Tick(e) >= engage.Tick - 1 && Tick(e) <= engage.Tick),
                $"no {HolderChange} event for enemy {engage.EnemyId} naming seat {engage.HolderId} at tick {engage.Tick - 1} or {engage.Tick}");
        }
    }

    // ---- S1 coverage (f-6dbdd121, R-THREAT-2 (c)/(d), G2.5): taunt, leash clear and the pull-start creation-time holder ----

    private const string TauntAbility = "warden.taunt";
    private const int WardenSeat = 1;
    private const int CarelessSeat = 3;

    private static IEnumerable<SimEvent> HolderChanges(IEnumerable<SimEvent> events, int enemyId) =>
        events.Where(e => e.Type == HolderChange && e.Get("enemyId") is JsInt id && id.Value == enemyId);

    private static string Describe(IEnumerable<SimEvent> events) => string.Join(" | ", events.Select(e => e.ToCanonicalJson()));

    /// <summary>The autorun Warden's hollow-vault run (key 2, seed 1, bots on), the build ScriptedWardenTauntTests uses.</summary>
    private static (Run Run, List<int> PairIds) BuildHollowVault()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var pairIds = setup.Dungeon.Pulls.Single(p => p.Id == "pair").Spawns.Select(s => s.ActorId).ToList();
        return (run, pairIds);
    }

    /// <summary>Clones the run with one Pair enemy engaged beside the Warden and held by <paramref name="holderId"/>.</summary>
    private static Run EngageHeldBy(Run run, int enemyId, int holderId, long offsetXMm)
    {
        var state = run.State.Clone();
        var warden = state.Actors[WardenSeat];
        var e = state.Actors[enemyId];
        e.Idle = false;
        e.Alive = true;
        e.HolderId = holderId;
        e.Pos = new Vec3(warden.Pos.X + offsetXMm, warden.Pos.Y, warden.Pos.Z);
        e.Home = e.Pos;
        return run with { State = state };
    }

    [Fact(DisplayName = "Holder change is logged: a taunt that moves an enemy from seat A to seat B logs exactly one holder_change for it, naming B, at the taunt's completion tick")]
    public void Taunt_is_logged_as_one_holder_change_naming_the_new_holder_at_the_completion_tick()
    {
        var (run, pair) = BuildHollowVault();
        var enemyId = pair[0];
        run = EngageHeldBy(run, enemyId, CarelessSeat, 1500);
        // Taunt comes ready a few ticks in, so the flip lands inside the window and not on the first step.
        var state = run.State.Clone();
        state.Actors[WardenSeat].Cooldowns[TauntAbility] = state.Tick + 10;
        run = run with { State = state };
        Assert.Equal(CarelessSeat, run.State.Actors[enemyId].HolderId);

        var warden = new ScriptedWarden();
        var seen = new List<SimEvent>();
        int? flipStepTick = null;
        for (var i = 0; i < 10 + Keyrun.Sim.Core.Tick.TickHz && flipStepTick is null; i++)
        {
            var commands = new List<Command>();
            warden.Sample(run.State.Tick, run, commands);
            var preStepTick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, commands);
            seen.AddRange(stepped.Events);
            run = stepped.Run;
            if (run.State.Actors[enemyId].HolderId == WardenSeat)
            {
                flipStepTick = preStepTick;
            }
        }

        Assert.True(flipStepTick is not null, $"precondition: the Warden's taunt never moved enemy {enemyId} from seat {CarelessSeat} to seat {WardenSeat}");
        var changes = HolderChanges(seen, enemyId).ToList();
        _out.WriteLine($"taunt step began at tick {flipStepTick}; holder_change events for enemy {enemyId}: {Describe(changes)}");
        Assert.True(changes.Count == 1, $"expected exactly one {HolderChange} for enemy {enemyId} across the taunt window, got {changes.Count}: {Describe(changes)}");
        var change = changes[0];
        Assert.True(change.Get("holderId") is JsInt { Value: WardenSeat }, $"{HolderChange} for the taunt named {change.Get("holderId")}, expected seat {WardenSeat}");
        var tauntComplete = seen.Single(e => e.Type == "cast_complete" && e.Get("abilityId") is JsString { Value: TauntAbility } && e.Get("actorId") is JsInt { Value: WardenSeat });
        Assert.True(change.Tick == tauntComplete.Tick, $"{HolderChange} landed on tick {change.Tick}, the taunt completed on tick {tauntComplete.Tick} (step began at {flipStepTick})");
    }

    [Fact(DisplayName = "Holder change is logged: a leash evade that clears a holder logs holder_change with holderId null for that enemy")]
    public void Leash_clear_is_logged_as_a_holder_change_with_a_null_holder()
    {
        var (run, pair) = BuildHollowVault();
        var enemyId = pair[0];
        run = EngageHeldBy(run, enemyId, WardenSeat, 1500);
        // Drag the held enemy past its leash: home far from where it stands, an engaged group, a short leash.
        var state = run.State.Clone();
        var enemy = state.Actors[enemyId];
        enemy.LeashMm = 1000;
        enemy.Home = new Vec3(enemy.Pos.X + 60000, enemy.Pos.Y, enemy.Pos.Z);
        run = run with { State = state, Encounter = run.Encounter with { Engaged = run.Encounter.Engaged.Append("pair").Distinct().ToList() } };
        Assert.Equal(WardenSeat, run.State.Actors[enemyId].HolderId);

        var stepped = RunRoot.StepRun(run, new List<Command>());
        _out.WriteLine($"events: {Describe(stepped.Events)}");
        Assert.Contains(stepped.Events, e => e.Type == "pack_evade");
        Assert.Null(stepped.Run.State.Actors[enemyId].HolderId);
        var changes = HolderChanges(stepped.Events, enemyId).ToList();
        Assert.True(changes.Count == 1, $"expected exactly one {HolderChange} for enemy {enemyId} on the leash clear, got {changes.Count}: {Describe(changes)}");
        Assert.True(changes[0].Get("holderId") is JsNull, $"{HolderChange} on the leash clear named {changes[0].Get("holderId")}, expected null");
    }

    [Fact(DisplayName = "Holder change is logged: a pull-start run (EngageStartPull, the --pull N entry) logs a holder_change for every enemy that has a holder, on the run's first tick")]
    public void Pull_start_holders_are_logged_on_the_first_tick()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        var run = HostRunFactory.Create(setup, new RunRequest(1, registry, "warden") { StartPull = 2 }).Run;
        var held = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.HolderId is not null).ToList();
        _out.WriteLine($"created at tick {run.State.Tick}; held at creation: {string.Join(", ", held.Select(a => $"{a.Id} -> {a.HolderId}"))}");
        Assert.NotEmpty(held); // precondition: the pull start does give enemies a holder before any tick

        var first = RunRoot.StepRun(run, new List<Command>());
        // The run's first tick is tick 1: the first step starts on the created run's tick 0 and its FixupTargeting events carry
        // the stepped state's tick (the same tick-1 stamp the first step's other holder_change events show).
        var firstTick = first.Run.State.Tick;
        Assert.Equal(1, firstTick);
        _out.WriteLine($"first tick = {firstTick}; first tick events: {Describe(first.Events.Where(e => e.Type == HolderChange))}");
        foreach (var enemy in held)
        {
            var changes = HolderChanges(first.Events, enemy.Id).ToList();
            Assert.True(
                changes.Count == 1 && changes[0].Get("holderId") is JsInt id && id.Value == enemy.HolderId && changes[0].Tick == firstTick,
                $"no single {HolderChange} for enemy {enemy.Id} naming seat {enemy.HolderId} on the first tick ({firstTick}); first-tick {HolderChange} events for it: {changes.Count} [{Describe(changes)}]");
        }
    }
}
