using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The engine-free twin of HudSceneTest runs F and G (game/src/Host/HudSceneTest.ps1, lane 3J backfill in the real
/// loop). It steps the arena's own run the way SimDriver.StepOnce does under <c>--autorun --abandon-seat A@T
/// --joiner-key K</c>: the run built by HostRunSetup.Build and armed with BossRules.Arm, the autorun Warden
/// (game/src/Host/ScriptedWarden.cs) on seat 1 and the host's BotCommandSource on seats 2 and 3 (both compiled in by
/// link, unedited), the scripted abandon at tick T with one QueueRequest at key K, and Backfill.Step every tick. So a
/// change that moves the Choir clear (a bot that casts more, a retuned enemy) shows here, in dotnet test, before the
/// Godot run reports "the abandon came after pull 2 spawned".
/// <para><see cref="AbandonSeat"/> and <see cref="BackfillTicks"/> are HudSceneTest's <c>-AbandonSeat</c> and
/// <c>-BackfillTicks</c> defaults; change them together.</para>
/// </summary>
public sealed class HostBackfillRunTests
{
    /// <summary>HudSceneTest <c>-AbandonSeat</c>: the Chanter (actor 2) leaves at this run tick.</summary>
    public const int AbandonActor = 2;

    /// <summary>HudSceneTest <c>-AbandonSeat</c>'s tick.</summary>
    public const int AbandonTick = 1500;

    /// <summary>HudSceneTest <c>-BackfillTicks</c>: the autorun's tick count for runs F and G.</summary>
    public const int BackfillTicks = 3500;

    private const string DungeonId = "hollow-vault";
    private const int KeyLevel = 2;
    private const long Seed = 1;

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    /// <summary>What one arena autorun did.</summary>
    internal sealed record ArenaRun(
        int? AbandonedAt,
        int? Pull2StartTick,
        IReadOnlyList<SeatFill> Fills,
        IReadOnlyList<QueueRequest> QueueLeft,
        IReadOnlyList<(int Step, SimEvent Event)> Events,
        RunEndState RunEnd,
        string BotsProof);

    /// <summary>SimDriver.StartRun then <paramref name="ticks"/> SimDriver.StepOnce calls, as the arena autorun runs them.</summary>
    internal static ArenaRun Arena(int ticks, int? abandonActor, int? abandonTick, int? joinerKey)
    {
        var setup = HostRunSetup.Build(Registry, DungeonId, KeyLevel, "warden", withBots: true);
        var created = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, null);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(Registry), Seed);
        var backfill = BackfillState.Empty;
        var queue = new List<QueueRequest>();
        var fills = new List<SeatFill>();
        var events = new List<(int Step, SimEvent Event)>();
        int? abandonedAt = null;
        int? pull2 = null;
        for (var n = 0; n < ticks; n++)
        {
            var preTick = run.State.Tick;
            // SimDriver.MaybeScriptedAbandon.
            if (abandonActor is { } actor && abandonTick == preTick && run.RunEnd.Result is null)
            {
                backfill = Backfill.Abandon(backfill, run, actor);
                abandonedAt = preTick;
                if (joinerKey is { } key)
                {
                    var clause = run.Encounter.Rules.ActiveClauseId
                        ?? Registry.Clauses.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault()
                        ?? "none";
                    queue.Add(new QueueRequest(DungeonId, key, clause));
                }
            }
            var commands = new List<Command>();
            warden.Sample(preTick, run, commands);
            bots.Sample(preTick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, queue);
            backfill = step.Backfill;
            run = step.Step.Run;
            events.AddRange(step.Step.Events.Select(e => (preTick, e)));
            if (pull2 is null && step.Step.Events.Any(e => e.Type == "pull_start" && e.Get("pullIndex") is JsInt { Value: 1 }))
            {
                pull2 = preTick;
            }
            fills.AddRange(step.Fills);
            foreach (var i in step.ConsumedQueueIndices.OrderByDescending(i => i))
            {
                queue.RemoveAt(i);
            }
        }
        return new ArenaRun(abandonedAt, pull2, fills, queue, events, run.RunEnd, bots.ProofLine());
    }

    private static int? ActorOf(SimEvent e) => e.Get("actorId") is JsInt { } a ? (int)a.Value : null;

    [Fact(DisplayName = "Host backfill (HudSceneTest run F): the Chanter leaves in pull 1, the party lives to the Pair, and a key-2 joiner takes her seat at its start")]
    public void Run_F_abandons_in_pull_1_and_refills_at_pull_2()
    {
        var f = Arena(BackfillTicks, AbandonActor, AbandonTick, joinerKey: 2);
        Assert.True(f.AbandonedAt == AbandonTick, $"the run ended before tick {AbandonTick}, so nobody left");
        Assert.True(f.Pull2StartTick is not null, $"the party never started pull 2 (the Pair) in {BackfillTicks} ticks");
        Assert.True(AbandonTick <= f.Pull2StartTick,
            $"the abandon at tick {AbandonTick} came after pull 2 spawned (step {f.Pull2StartTick}); it must be in pull 1");
        // Mid-pull: the seat empties while the Choir still stands, not in the walk between the clear and the Pair.
        var clear1 = f.Events.First(x => x.Event.Type == "pull_clear" && x.Event.Get("pullIndex") is JsInt { Value: 0 }).Step;
        Assert.True(AbandonTick < clear1, $"the abandon at tick {AbandonTick} came after the Choir cleared (step {clear1}); it must be mid-fight");
        var deadEarly = f.Events.FirstOrDefault(x => x.Step <= f.Pull2StartTick && x.Event.Type == "death" && ActorOf(x.Event) is 1 or 2 or 3);
        Assert.True(deadEarly.Event is null, $"a party seat died before pull 2: step {deadEarly.Step} {deadEarly.Event}");
        var fill = Assert.Single(f.Fills);
        Assert.Equal(AbandonActor, fill.ActorId);
        Assert.Equal(f.Pull2StartTick + 1, fill.FillTick);
        Assert.Equal(2, fill.Joiner.KeyLevel);
        Assert.Empty(f.QueueLeft);
        // The open seat stands idle from the abandon step through the refill step; the joiner casts in a later step.
        var idle = f.Events.FirstOrDefault(x => x.Step >= AbandonTick && x.Step < fill.FillTick && x.Event.Type == "cast_complete" && ActorOf(x.Event) == AbandonActor);
        Assert.True(idle.Event is null, $"the open seat acted before the refill: step {idle.Step} {idle.Event}");
        Assert.Contains(f.Events, x => x.Step >= fill.FillTick && x.Event.Type == "cast_complete" && ActorOf(x.Event) == AbandonActor);
    }

    [Fact(DisplayName = "Host backfill (HudSceneTest run G): a key-3 joiner never takes a key-2 run's seat")]
    public void Run_G_key_mismatch_never_refills()
    {
        var g = Arena(BackfillTicks, AbandonActor, AbandonTick, joinerKey: 3);
        Assert.True(g.AbandonedAt == AbandonTick, $"the run ended before tick {AbandonTick}, so nobody left");
        Assert.True(g.Pull2StartTick is not null, $"the party never started pull 2 (the Pair) in {BackfillTicks} ticks");
        Assert.True(AbandonTick <= g.Pull2StartTick,
            $"the abandon at tick {AbandonTick} came after pull 2 spawned (step {g.Pull2StartTick}); it must be in pull 1");
        Assert.Empty(g.Fills);
        var left = Assert.Single(g.QueueLeft);
        Assert.Equal(3, left.KeyLevel);
    }
}
