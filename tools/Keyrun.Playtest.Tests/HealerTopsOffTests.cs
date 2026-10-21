using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// HEALER-TOPS-OFF (the WoW Mythic+ dungeon loop: the healer tops the party off between pulls). The run SimDriver
/// builds at key 2, seed 1 (<see cref="HostRunSetup.Build"/>, then <see cref="BossRules.Arm"/>), seat 1 the autorun
/// <see cref="ScriptedWarden"/> and seats 2 and 3 the host's own <see cref="BotCommandSource"/>, the full party, no
/// abandon. Since D54 no enemy displaces a player, so the whole Pair reaches the Warden at once: walking into the Pair
/// with the Warden still at 184/400 from the Choir, the party wiped at 2111. With the healer topping off on the walk
/// between pulls, the full party clears the Pair with nobody dead. (A party of bots only is not this case: its tank
/// bot left the Choir at 340/400 and cleared the Pair before and after the fix.)
/// </summary>
public sealed class HealerTopsOffTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 7000;

    private sealed record Drive(List<SimEvent> Events, Run Final, List<string> Log);

    private static readonly Lazy<Drive> Driven = new(DriveRun);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the pull and death ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public HealerTopsOffTests(ITestOutputHelper output) => _output = output;

    private static Drive DriveRun()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var created = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed);
        var events = new List<SimEvent>();
        var log = new List<string>();
        var pairCleared = false;
        var enteredPair = false;

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && !pairCleared)
        {
            // SimDriver.StepOnce's order: the autorun Warden's commands first, then the bot seats'.
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands);
            bots.Sample(tick, run, commands);
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
            foreach (var e in stepped.Events)
            {
                if (e.Type == "pull_start" && PullOf(e) == "pair")
                {
                    enteredPair = true;
                    log.Add(FormattableString.Invariant($"{e.Tick} pull_start pair hp {string.Join(" ", setup.Party.Select(p => run.State.Actors[p.Id]).Select(a => $"{a.Id}={a.Health}/{a.MaxHealth}"))}"));
                }
                pairCleared |= enteredPair && e.Type == "pull_clear" && PullOf(e) == "pair";
            }
        }
        return new Drive(events, run, log);
    }

    private static string PullOf(SimEvent e) => e.Get("pullId") is JsString id ? id.Value : string.Empty;

    private static long ActorOf(SimEvent e) => e.Get("actorId") is JsInt id ? id.Value : -1;

    [Fact(DisplayName = "Healer tops off: a full key-2 party walks into the Pair healed and clears it with nobody dead (D54)")]
    public void A_full_party_survives_the_pair()
    {
        var r = Driven.Value;
        foreach (var line in r.Log)
        {
            _output.WriteLine(line);
        }
        foreach (var e in r.Events.Where(e => e.Type is "pull_start" or "pull_clear" or "run_end" || (e.Type == "death" && ActorOf(e) is >= 1 and <= 3)))
        {
            _output.WriteLine(FormattableString.Invariant($"{e.Tick} {e.Type} {PullOf(e)} {(e.Type == "death" ? ActorOf(e).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty)}"));
        }

        var pairStart = r.Events.FirstOrDefault(e => e.Type == "pull_start" && PullOf(e) == "pair");
        Assert.True(pairStart is not null, "the party never reached the Pair");
        var pairClear = r.Events.FirstOrDefault(e => e.Type == "pull_clear" && PullOf(e) == "pair");
        var deaths = r.Events.Where(e => e.Type == "death" && ActorOf(e) is >= 1 and <= 3).Select(e => FormattableString.Invariant($"{ActorOf(e)}@{e.Tick}")).ToList();
        Assert.True(deaths.Count == 0, $"party deaths before the Pair cleared: {string.Join(", ", deaths)} (run {r.Final.RunEnd.Result?.ToJs() ?? "running"} at {r.Final.State.Tick})");
        Assert.True(pairClear is not null, $"the Pair never cleared (run {r.Final.RunEnd.Result?.ToJs() ?? "running"} at {r.Final.State.Tick})");
    }
}
