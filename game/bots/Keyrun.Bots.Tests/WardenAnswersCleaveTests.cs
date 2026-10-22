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

namespace Keyrun.Bots.Tests;

/// <summary>
/// CLEAVE-ANSWER (f-57c04b4a ruling, event store seq 93; BIBLE s3 "Boss hits are telegraphed: that is Brace's job", s9
/// "Cleave: a slow frontal cone, readable from the boss's facing"). Once Tempo stopped dropping Cleaves that came due
/// inside their cooldown, every Cleave landed on schedule, and the Warden, who never answered one, died before the
/// first Lock opened (LockWinnabilityTests). Here the default host run (SimDriver.StartRun headlessly: the autorun
/// <see cref="ScriptedWarden"/> in seat 1, <see cref="BotCommandSource"/> seats, <see cref="Backfill.Step"/> per tick)
/// is played through the Warden fight's Lock scenario, from the boss pull's start until the first Lock resolves, and
/// every Cleave that lands is answered by the Warden: he is Braced on its landing tick or it does not hit him.
/// </summary>
public sealed class WardenAnswersCleaveTests
{
    private const string Cleave = "hollowWarden.cleave";
    private const string BraceEffect = "braceSelf";
    private const int BossId = 900;
    private const int WardenId = HostRunSetup.HumanId;
    private const int MaxTicks = 12000;

    private readonly ITestOutputHelper _out;

    public WardenAnswersCleaveTests(ITestOutputHelper output) => _out = output;

    private static long? Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : null;

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    [Theory(DisplayName = "CLEAVE-ANSWER: the Warden answers every Cleave in the Lock scenario (Braced on its landing tick, or out of the cone)")]
    [InlineData(2, 1L)]
    [InlineData(10, 7L)]
    public void The_warden_answers_every_cleave_in_the_lock_scenario(int key, long seed)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", key, Lobby.DefaultClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, Lobby.DefaultClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var backfill = BackfillState.Empty;

        int? bossStart = null, lockResolved = null;
        var landed = new List<string>();
        var unanswered = new List<string>();
        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null && lockResolved is null)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            var before = run;
            run = step.Step.Run;
            var events = step.Step.Events;
            foreach (var e in events)
            {
                if (e.Type == "pull_start" && Str(e, "pullId") == "boss")
                {
                    bossStart = e.Tick;
                }
                if (e.Type is "lock_broken" or "lock_failed")
                {
                    lockResolved = e.Tick;
                }
                if (bossStart is null || e.Type != "cast_complete" || Int(e, "actorId") != BossId || Str(e, "abilityId") != Cleave)
                {
                    continue;
                }
                var braced = before.State.Actors.TryGetValue(WardenId, out var w) && w.Alive
                    && w.Effects.Exists(x => x.EffectId == BraceEffect && x.EndsTick > e.Tick);
                // The slam never lands with a Cleave (one caster, one bar), so a boss hit on the Warden on a Cleave's
                // landing tick is the Cleave.
                var hit = events.Any(d => d.Type == "damage" && d.Tick == e.Tick && Int(d, "sourceId") == BossId && Int(d, "targetId") == WardenId);
                landed.Add($"{e.Tick}:{(braced ? "braced" : hit ? "HIT" : "dodged")}");
                if (hit && !braced)
                {
                    unanswered.Add($"Cleave at {e.Tick} hit the unbraced Warden");
                }
            }
        }
        _out.WriteLine($"key {key} seed {seed}: boss pull {bossStart}, first Lock resolved {lockResolved}, cleaves [{string.Join(", ", landed)}]");
        Assert.True(bossStart is not null, $"the boss pull never started by tick {run.State.Tick}");
        Assert.True(lockResolved is not null, $"the first Lock never resolved (run ended {run.RunEnd.Result} at {run.State.Tick}): cleaves [{string.Join(", ", landed)}]");
        Assert.True(landed.Count >= 3, $"only {landed.Count} Cleaves landed before the first Lock resolved: the test is vacuous");
        Assert.True(unanswered.Count == 0, string.Join("; ", unanswered) + $" (cleaves [{string.Join(", ", landed)}])");
    }
}
