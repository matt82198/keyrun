using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Ruling (b) of event seq 376 (WoW M+): any enemy that attacks or holds a target is engaged. Lane S5 capture of main
/// 4135f4fc found the boss adds 901 and 902 attacking while their ACTOR line read engaged=0 and holder=1.
/// The run is the live host path (<see cref="HostRunSetup.Build"/> for hollow-vault key 2, seed 1, three bot seats, the
/// boss armed by <see cref="BossRules.Arm"/>), because the Twins are the host's Break-bar adds and the oracle setup
/// fixture does not spawn them. At every tick an enemy has an attack (a completed cast, or a damage event it is the source
/// of) or a holder set, its aggro group must be engaged on that tick, read the way the ACTOR capture line reads it.
/// </summary>
public sealed class AddsEngagedTests
{
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const int MaxTicks = 8000;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());
    private static readonly Lazy<Driven> Result = new(Drive);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public AddsEngagedTests(ITestOutputHelper output) => _output = output;

    /// <summary>One enemy-tick row: an attack by the enemy (its cast or the damage it dealt) or a held target.</summary>
    /// <param name="Tick">The tick the row was logged on.</param>
    /// <param name="EnemyId">The enemy's actor id.</param>
    /// <param name="Attacks">True for an attack row, false for a holder row.</param>
    /// <param name="Engaged">Whether the enemy's aggro group was engaged after the step.</param>
    /// <param name="HolderId">The target the enemy holds (holder rows only).</param>
    /// <param name="GroupId">The enemy's aggro group, or "none".</param>
    private sealed record EngageRow(int Tick, int EnemyId, bool Attacks, bool Engaged, int? HolderId, string GroupId);

    /// <summary>The drive result: the rows, the living enemy-tick count and the attack count.</summary>
    /// <param name="Rows">Attack and holder rows in tick order.</param>
    /// <param name="EnemyCount">Living enemy-ticks observed.</param>
    /// <param name="AttackCount">Attack events by enemies.</param>
    private sealed record Driven(List<EngageRow> Rows, int EnemyCount, int AttackCount);

    private static Driven Drive()
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();

        var backfill = BackfillState.Empty;
        var rows = new List<EngageRow>();
        var enemyCount = 0;
        var attackCount = 0;

        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null)
        {
            var commands = new List<Command>();
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;

            // Engagement is read after the step, from the same encounter and groups the ACTOR capture line reads.
            var groups = Aggro.GroupsOf(run.Dungeon, run.State.Tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols);
            var groupOf = new Dictionary<int, AggroGroup>();
            foreach (var g in groups)
            {
                foreach (var id in g.ActorIds)
                {
                    groupOf.TryAdd(id, g);
                }
            }
            bool Engaged(int actorId) => groupOf.TryGetValue(actorId, out var g) && Pulls.IsEngaged(run.Encounter, g.Id);
            string GroupOf(int actorId) => groupOf.TryGetValue(actorId, out var g) ? g.Id : "none";
            bool IsEnemy(int id) => run.State.Actors.TryGetValue(id, out var a) && a.Kind == ActorKind.Enemy;

            // Only enemies are in scope: a party seat's casts and hits are not the ruling's subject.
            var attackers = new SortedSet<int>();
            foreach (var e in step.Step.Events)
            {
                if (e.Type == "cast_complete" && e.Get("actorId") is JsInt c && IsEnemy((int)c.Value))
                {
                    attackers.Add((int)c.Value);
                    rows.Add(new EngageRow((int)e.Tick, (int)c.Value, true, Engaged((int)c.Value), null, GroupOf((int)c.Value)));
                }
                else if (e.Type == "damage" && e.Get("sourceId") is JsInt s && IsEnemy((int)s.Value))
                {
                    attackers.Add((int)s.Value);
                    rows.Add(new EngageRow((int)e.Tick, (int)s.Value, true, Engaged((int)s.Value), null, GroupOf((int)s.Value)));
                }
            }
            attackCount += attackers.Count;

            foreach (var actor in run.State.Actors.Values.Where(a => a.Alive && a.Kind == ActorKind.Enemy))
            {
                enemyCount++;
                if (actor.HolderId is int holder)
                {
                    rows.Add(new EngageRow((int)run.State.Tick, actor.Id, false, Engaged(actor.Id), holder, GroupOf(actor.Id)));
                }
            }
        }
        return new Driven(rows, enemyCount, attackCount);
    }

    /// <summary>
    /// Every enemy that attacks or holds a target is inside an engaged aggro group on that tick. Returns the violations.
    /// </summary>
    private static List<string> UnengagedActors(IEnumerable<EngageRow> rows) =>
        rows.Where(r => !r.Engaged)
            .Select(r => r.Attacks
                ? $"enemy {r.EnemyId} attacked at tick {r.Tick} (group {r.GroupId}) while not engaged"
                : $"enemy {r.EnemyId} held target {r.HolderId} at tick {r.Tick} (group {r.GroupId}) while not engaged")
            .ToList();

    [Fact(DisplayName = "Adds that attack or hold a target are engaged on that tick (ruling b, S5 defect: adds 901/902)")]
    public void Every_enemy_that_attacks_or_holds_a_target_is_engaged()
    {
        Driven r = Result.Value;
        _output.WriteLine($"living enemy-ticks {r.EnemyCount}; attack events {r.AttackCount}; attack or holder rows {r.Rows.Count}");
        _output.WriteLine("attacking enemy ids: " + string.Join(", ", r.Rows.Where(x => x.Attacks).Select(x => x.EnemyId).Distinct().OrderBy(i => i)));
        _output.WriteLine("holding enemy ids: " + string.Join(", ", r.Rows.Where(x => !x.Attacks).Select(x => x.EnemyId).Distinct().OrderBy(i => i)));

        // Vacuity guard: the property must have been exercised on real enemy attacks, not on an empty run.
        Assert.True(r.AttackCount > 0, "no enemy attack was logged, so the property is vacuous");

        var violations = UnengagedActors(r.Rows);
        _output.WriteLine($"unengaged attack or holder rows: {violations.Count}");
        Assert.True(violations.Count == 0, string.Join("; ", violations.Take(20)));
    }
}
