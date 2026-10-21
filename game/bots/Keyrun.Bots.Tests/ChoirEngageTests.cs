using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// BOTS-ENGAGE-CHOIR (demo verdict KF2, finding f-210c3939: "in the first room the bots stand idle instead of pulling
/// the Choir"; design/BIBLE.md section 9: "The Choir teaches the kick ledger (must-kick 4-second heal cast)").
/// Drives the default host run at key 2 and seed 1 the way SimDriver builds it headlessly (as
/// <see cref="HostPathThreatTelegraphTests"/> does): seats 2 and 3 are <see cref="BotCommandSource"/> bots, and seat 1 is
/// the human. Two cases: the autorun human (<see cref="ScriptedWarden"/> walks the route), and a human who stands at the
/// Entrance and issues nothing (<paramref name="humanLeads"/> false), the condition under which the demo's bots stood idle.
/// Both must start the first pull within <see cref="PullStartWithinTicks"/>, hit EVERY Choir enemy before
/// <see cref="EveryChoirHitByTick"/>, and have the Cantor start at least one Choral Mending so the kick ledger has
/// something to teach. Heal kick timing is owned by PR #295 (f-70d36828) and is not asserted here.
/// </summary>
public sealed class ChoirEngageTests
{
    /// <summary>The Choir pull must start within this many ticks of the run start (30 s at 20 Hz).</summary>
    public const int PullStartWithinTicks = 600;

    /// <summary>Every Choir enemy must have taken party damage before this tick.</summary>
    public const int EveryChoirHitByTick = 1500;

    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const int MaxTicks = 6000;
    private const string HealCast = "choir.healCast";
    private static readonly int[] PartyIds = { 1, 2, 3 };

    private sealed record Tally(
        int PullStartTick,
        IReadOnlyDictionary<int, int> FirstHitByChoirEnemy,
        IReadOnlyList<int> ChoirEnemyIds,
        int HealsStarted,
        IReadOnlyList<string> Log);

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());
    private static readonly Lazy<Tally> AutorunHuman = new(() => DriveParty(humanLeads: true));
    private static readonly Lazy<Tally> IdleHuman = new(() => DriveParty(humanLeads: false));

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the trace is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public ChoirEngageTests(ITestOutputHelper output) => _output = output;

    /// <summary>Drives the default host run; the human seat runs <see cref="ScriptedWarden"/> only when <paramref name="humanLeads"/>.</summary>
    /// <param name="humanLeads">True for the autorun human; false for a human who stands still at the Entrance.</param>
    /// <returns>The tally of the first pull.</returns>
    private static Tally DriveParty(bool humanLeads)
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();

        var backfill = BackfillState.Empty;
        var choirIds = setup.Dungeon.Pulls[0].Spawns.Select(s => (int)s.ActorId).Distinct().OrderBy(id => id).ToList();
        var pullStart = -1;
        var firstHit = new Dictionary<int, int>();
        var started = 0;
        var casting = new HashSet<int>();
        var log = new List<string>();

        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null)
        {
            var commands = new List<Command>();
            if (humanLeads)
            {
                warden.Sample(run.State.Tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            }
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;

            foreach (var e in step.Step.Events)
            {
                switch (e.Type)
                {
                    case "pull_start" when ((JsString)e.Get("pullId")!).Value == "choir":
                        pullStart = e.Tick;
                        log.Add($"{e.Tick} pull_start choir");
                        break;
                    case "damage" when PartyIds.Contains((int)((JsInt)e.Get("sourceId")!).Value)
                        && choirIds.Contains((int)((JsInt)e.Get("targetId")!).Value):
                        var target = (int)((JsInt)e.Get("targetId")!).Value;
                        if (!firstHit.ContainsKey(target))
                        {
                            firstHit[target] = e.Tick;
                            log.Add($"{e.Tick} first party damage on Choir enemy {target} by {((JsInt)e.Get("sourceId")!).Value}");
                        }
                        break;
                }
            }
            foreach (var a in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy))
            {
                var now = a.Alive && a.CastId == HealCast;
                if (now && casting.Add(a.Id))
                {
                    started++;
                    log.Add($"{run.State.Tick} heal started by {a.Id}");
                }
                else if (!now)
                {
                    casting.Remove(a.Id);
                }
            }
        }
        return new Tally(pullStart, firstHit, choirIds, started, log);
    }

    /// <summary>The two human conditions the proof covers: the autorun human, and a human who stands still at the Entrance.</summary>
    /// <returns>One row per condition.</returns>
    public static IEnumerable<object[]> HumanConditions()
    {
        yield return new object[] { true };
        yield return new object[] { false };
    }

    [Theory(DisplayName = "Choir engage: default key 2 seed 1 host run pulls the Choir, hits every Choir enemy and casts a heal")]
    [MemberData(nameof(HumanConditions))]
    public void Bots_pull_the_choir_hit_every_enemy_and_cast_a_heal(bool humanLeads)
    {
        var t = humanLeads ? AutorunHuman.Value : IdleHuman.Value;
        _output.WriteLine($"HUMAN {(humanLeads ? "autorun" : "idle-at-entrance")}");
        foreach (var line in t.Log)
        {
            _output.WriteLine(line);
        }
        _output.WriteLine($"CHOIR pullStart={t.PullStartTick} enemies=[{string.Join(",", t.ChoirEnemyIds)}] firstHit=[{string.Join(",", t.FirstHitByChoirEnemy.Select(kv => $"{kv.Key}@{kv.Value}"))}] healsStarted={t.HealsStarted}");

        Assert.True(t.PullStartTick >= 0 && t.PullStartTick <= PullStartWithinTicks,
            $"the Choir pull started at tick {t.PullStartTick} (want 0..{PullStartWithinTicks}): the bots did not leave the entry and engage in time");

        var unhit = t.ChoirEnemyIds.Where(id => !t.FirstHitByChoirEnemy.ContainsKey(id)).ToList();
        Assert.True(unhit.Count == 0,
            $"Choir enemies never damaged: [{string.Join(",", unhit)}] (pull_start {t.PullStartTick}, want every enemy hit before tick {EveryChoirHitByTick})");

        var late = t.FirstHitByChoirEnemy.Where(kv => kv.Value >= EveryChoirHitByTick).ToList();
        Assert.True(late.Count == 0,
            $"Choir enemies first damaged at or after tick {EveryChoirHitByTick}: [{string.Join(",", late.Select(kv => $"{kv.Key}@{kv.Value}"))}]");

        // Non-vacuity: the kick ledger needs a heal cast to teach.
        Assert.True(t.HealsStarted >= 1, $"the Cantor started {t.HealsStarted} Choral Mendings; the kick ledger has nothing to teach");
    }
}
