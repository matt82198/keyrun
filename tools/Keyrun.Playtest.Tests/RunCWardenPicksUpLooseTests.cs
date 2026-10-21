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
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// LOOSE-PICKUP (PR #326 merge-forward; BIBLE section 3, the bot tank's own rule
/// <see cref="Keyrun.Bots.Roles.TankEngage.PickLooseEnemy"/>): an engaged enemy aimed at a seat the autorun Warden does not
/// hold is taunted back by <see cref="ScriptedWarden"/>. History: #326 pinned this on run C at key 2 (the careless Cinder,
/// <c>--careless-seat 3@300:pair</c>, walked through the Choir, a Choir melee nobody had threat on chased it and killed it
/// at 583, the Warden never taunted, the run ended unfinished at 7000). Under #307's HOLDER-LOCK (STATE.md D30: damage,
/// healing and threat never move a holder) that run has no loose enemy at all (integrated tree: every Choir enemy holds
/// the Warden from its engage at 76 to its death; no seat dies; the Choir clears at 2179), so the run C pin was vacuous.
/// <para>
/// R-PILE-9: the scenario is built instead. Run C's setup (key 2, seed 1, bots, careless seat 3) with the careless Cinder
/// sent at the Choir itself from tick 0 and the Warden late: <see cref="ScriptedWarden"/> acts only from the tick the
/// Choir engages. The Cinder body-pulls the Choir, so every Choir enemy locks the Cinder as its holder (WoW: a DPS pulled
/// before the tank; the bible's "the classic butt pull"). Measured on the integrated tree: engage 122 with all three held
/// by seat 3; Taunts complete at 154, 314, 474 and each moves one enemy back to the Warden; no seat dies; the Choir clears
/// at 1751. With the Warden acting from tick 0 he engages first (75) and nothing is ever loose: no real autorun run
/// produces a loose enemy under the holder lock; the trigger is content (adds, patrols, aggro resets), the rule is right.
/// </para>
/// </summary>
public sealed class RunCWardenPicksUpLooseTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 7000; // run C's --ticks (HudSceneTest $PartyTicks)
    private const int CarelessSeat = 3;
    private const int CarelessFromTick = 0; // R-PILE-9: the Cinder pulls before the Warden engages
    private const int WardenId = 1;
    private const string Taunt = "warden.taunt";

    private sealed record Drive(
        int? EngageTick, int? LooseTick, IReadOnlyDictionary<int, int> LooseHolders, List<(int Tick, int Enemy)> TauntsBack,
        int? ChoirClear, List<string> SeatDeaths, int EndTick);

    private static readonly Lazy<Drive> Driven = new(DriveLateTank);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public RunCWardenPicksUpLooseTests(ITestOutputHelper output) => _output = output;

    private static Drive DriveLateTank()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        Assert.Equal("choir", setup.Dungeon.Pulls[0].Id);
        var choirIds = setup.Dungeon.Pulls[0].Spawns.Select(s => s.ActorId).ToList();
        var choir = choirIds.ToHashSet();
        var careless = new Dictionary<int, CarelessPlan> { [CarelessSeat] = new CarelessPlan(CarelessFromTick, choirIds) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, careless);
        var seats = setup.Party.Select(p => p.Id).ToHashSet();

        int? engageTick = null;
        int? looseTick = null;
        var looseHolders = new Dictionary<int, int>();
        var tauntsBack = new List<(int, int)>();
        int? choirClear = null;
        var deaths = new List<string>();
        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && choirClear is null)
        {
            var tick = run.State.Tick;
            var engaged = run.State.Actors.Values.Where(a => choir.Contains(a.Id) && a.Alive && !a.Idle).ToList();
            foreach (var e in engaged)
            {
                // Loose: held by (so its casts and chase go at) a living seat other than the Warden.
                if (e.HolderId is { } h && h != WardenId && seats.Contains(h) && !looseHolders.ContainsKey(e.Id))
                {
                    looseHolders[e.Id] = h;
                    looseTick ??= tick;
                }
            }
            var commands = new List<Command>();
            if (engaged.Count > 0)
            {
                engageTick ??= tick;
                warden.Sample(tick, run, commands); // the late tank: he acts once the Choir has engaged
            }
            bots.Sample(tick, run, commands);
            var heldBefore = run.State.Actors.Values.Where(a => choir.Contains(a.Id)).ToDictionary(a => a.Id, a => a.HolderId);
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "cast_complete" && ((JsInt)e.Get("actorId")!).Value == WardenId && ((JsString)e.Get("abilityId")!).Value == Taunt)
                {
                    foreach (var id in choir.Where(id => heldBefore[id] != WardenId && run.State.Actors[id].HolderId == WardenId))
                    {
                        tauntsBack.Add((e.Tick, id));
                    }
                }
                else if (e.Type == "death" && seats.Contains((int)((JsInt)e.Get("actorId")!).Value))
                {
                    deaths.Add($"seat {((JsInt)e.Get("actorId")!).Value} at {e.Tick} to {e.Get("sourceId")}");
                }
                else if (e.Type == "pull_clear" && ((JsString)e.Get("pullId")!).Value == "choir")
                {
                    choirClear = e.Tick;
                }
            }
        }
        return new Drive(engageTick, looseTick, looseHolders, tauntsBack, choirClear, deaths, run.State.Tick);
    }

    [Fact(DisplayName = "LOOSE-PICKUP (R-PILE-9, late tank): every Choir enemy the Cinder body-pulled is taunted back, and no seat dies to the Choir")]
    public void Warden_taunts_a_loose_choir_enemy_and_the_party_clears_the_choir()
    {
        var d = Driven.Value;
        _output.WriteLine($"engage={d.EngageTick?.ToString() ?? "none"} loose={string.Join(",", d.LooseHolders.Select(kv => $"{kv.Key}:held-by-{kv.Value}"))}@{d.LooseTick?.ToString() ?? "-"} " +
            $"tauntsBack=[{string.Join(", ", d.TauntsBack.Select(t => $"{t.Enemy}@{t.Tick}"))}] choirClear={d.ChoirClear?.ToString() ?? "none"} " +
            $"deaths=[{string.Join("; ", d.SeatDeaths)}] endTick={d.EndTick}");
        // Non-vacuity: the scenario must actually put a Choir enemy on a seat the Warden does not hold.
        Assert.True(d.LooseTick is not null, "no Choir enemy was ever held by a seat other than the Warden: the pickup check would be vacuous");
        Assert.True(d.LooseHolders.Values.All(h => h == CarelessSeat), "a loose Choir enemy was held by a seat other than the careless Cinder");
        // Before: the Warden had not acted when the Choir locked its holders (the late tank).
        Assert.Equal(d.EngageTick, d.LooseTick);
        // After: the Warden taunts each loose enemy back, nobody dies to the Choir, and the Choir clears.
        Assert.True(d.TauntsBack.Count > 0, $"the Choir was loose from tick {d.LooseTick} and the Warden never taunted one back");
        Assert.Equal(d.LooseHolders.Keys.OrderBy(i => i), d.TauntsBack.Select(t => t.Enemy).OrderBy(i => i));
        Assert.Empty(d.SeatDeaths);
        Assert.True(d.ChoirClear is not null, $"the Choir never cleared (ended at tick {d.EndTick})");
    }
}
