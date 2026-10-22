using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// R-BOSS-4 Cleaved (ruling on #573): an unanswered boss Cleave lays a stacking debuff on every seat it hits other than its
/// target (more damage taken per stack, refreshed on each hit), so a seat that keeps standing in the cone dies even when it
/// is healed to full before every Cleave, and the run loses a key tier. The fixture: a boss cleaving its tank every 160
/// ticks for 220 raw damage (the shipped Cleave), a 250-health careless seat behind the tank inside the cone, and an
/// impossible healer: the careless seat is put back to full health on every tick it lives (more than any Consecrate can
/// give). The values are the shipped run.json cleaved block and the shipped cleaved effect (+25% per stack, 5 stacks, 400 ticks).
/// </summary>
public sealed class CleaveMarkTests
{
    private const int Tank = 1;
    private const int Careless = 3;
    private const int Boss = 900;
    private const int FightTicks = 1000;

    private static readonly CleaveMarkRule Rule = new("cleave", "cleaved", 400);

    private readonly ITestOutputHelper _out;

    public CleaveMarkTests(ITestOutputHelper output) => _out = output;

    private static Run Create(bool withRule)
    {
        var cleave = RunTestKit.Ability("cleave", new EffectOp[] { new DamageOp(220, "physical") }, new AbilityShape(ShapeKind.Cone, Radius: 4000, Angle1024: 200), cooldownTicks: 160, range: 4000);
        var effects = new Dictionary<string, EffectDef>(StringComparer.Ordinal)
        {
            ["cleaved"] = new EffectDef("cleaved", 5, null, new EffectModifiers(DamageTakenBp: 2500)),
        };
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["cleave"] = cleave }, effects, bounds: RunTestKit.Arena20k) with
        {
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Boss] = new[] { new ScriptStep(StepTrigger.Loop, "cleave") { Every = 160 } } },
            CleaveMark = withRule ? Rule : null,
            ParTicks = 24000,
        };
        var party = new[]
        {
            RunTestKit.Member(Tank, "warden", ActorRole.Tank, 2000, 0, 100000, 100),
            RunTestKit.Member(Careless, "cinder", ActorRole.Damage, 2800, 0, 250, 100),
        };
        return RunRoot.CreateRun(1, party, RunTestKit.OnePull("cleave", "boss", RunTestKit.Spawn(Boss, "boss", 0, 0, 1000000)), content);
    }

    private sealed record Fight(Run Run, List<SimEvent> Events, List<long> CarelessHits, int Deaths);

    private static Fight Drive(bool withRule)
    {
        var run = Create(withRule);
        var events = new List<SimEvent>();
        for (var i = 0; i < FightTicks; i++)
        {
            var c = run.State.Actors[Careless];
            if (c.Alive)
            {
                c.Health = c.MaxHealth;
            }
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
        }
        var hits = events.Where(e => e.Type == "damage" && ((JsInt)e.Get("sourceId")!).Value == Boss && ((JsInt)e.Get("targetId")!).Value == Careless)
            .Select(e => ((JsInt)e.Get("modifiedAmount")!).Value).ToList();
        var deaths = events.Count(e => e.Type == "death" && ((JsInt)e.Get("actorId")!).Value == Careless);
        return new Fight(run, events, hits, deaths);
    }

    // The tier a timed clear earns at the tightest par that still gives the answered (deathless) fight +3.
    private static (int Answered, int Careless) Tiers(Fight answered, Fight careless)
    {
        var elapsed = answered.Run.State.RunTimerTicks;
        var answeredClock = Timer.ClockTicks(answered.Run.Encounter, elapsed);
        var par = (int)((answeredClock * 10000 + Timer.UpgradeThreeParBp - 1) / Timer.UpgradeThreeParBp);
        var carelessClock = Timer.ClockTicks(careless.Run.Encounter, elapsed);
        return (KeyMove.Of(RunResult.Timed, 2, answeredClock, par).Tier, KeyMove.Of(RunResult.Timed, 2, carelessClock, par).Tier);
    }

    private static Run CreateTough()
    {
        var run = Create(withRule: true);
        run.State.Actors[Careless].MaxHealth = 10_000_000;
        run.State.Actors[Careless].Health = 10_000_000;
        return run;
    }

    private static int CleavedStacks(Run run) =>
        run.State.Actors[Careless].Effects.Where(e => e.EffectId == "cleaved").Select(e => (int)e.Stacks).DefaultIfEmpty(0).Max();

    [Fact(DisplayName = "R-BOSS-4: Cleaved stacks one per unanswered hit up to 5 and never past the cap")]
    public void Cleaved_stacks_grow_one_per_hit_and_cap_at_five()
    {
        var run = CreateTough();
        var reported = new List<int>();
        for (var i = 0; i < FightTicks * 2; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            reported.AddRange(stepped.Events.Where(e => e.Type == "cleaved" && ((JsInt)e.Get("actorId")!).Value == Careless).Select(e => (int)((JsInt)e.Get("stacks")!).Value));
            run = stepped.Run;
        }
        _out.WriteLine($"reported stacks [{string.Join(",", reported)}]");
        Assert.True(reported.Count >= 7, $"only {reported.Count} unanswered hits landed");
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, reported.Take(5));
        Assert.All(reported.Skip(5), s => Assert.Equal(5, s));
        Assert.Equal(5, CleavedStacks(run));
    }

    [Fact(DisplayName = "R-BOSS-4: Cleaved expires 400 ticks after the latest hit and the stack count resets")]
    public void Cleaved_expires_after_its_duration_without_a_refresh()
    {
        var run = CreateTough();
        int? lastHit = null;
        var hits = 0;
        for (var guard = 0; hits < 2 && guard < FightTicks; guard++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            if (stepped.Events.Any(e => e.Type == "cleaved" && ((JsInt)e.Get("actorId")!).Value == Careless))
            {
                hits++;
                lastHit = run.State.Tick;
            }
        }
        Assert.Equal(2, CleavedStacks(run));
        // The seat leaves the cone (and the boss's reach) now: no later Cleave can refresh the mark.
        var seat = run.State.Actors[Careless];
        seat.Pos = new Vec3(seat.Pos.X + 15000, seat.Pos.Y, seat.Pos.Z + 15000);
        var stillMarkedAt = -1;
        for (var i = 0; i < 399; i++)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
            if (CleavedStacks(run) > 0)
            {
                stillMarkedAt = run.State.Tick;
            }
        }
        _out.WriteLine($"last hit at {lastHit}, still marked through {stillMarkedAt}, now {run.State.Tick}");
        Assert.Equal(2, CleavedStacks(run));
        for (var i = 0; i < 4; i++)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
        }
        Assert.Equal(0, CleavedStacks(run));
    }

    [Fact(DisplayName = "R-BOSS-4: unanswered cleaves stack and the careless seat loses a tier even with maximal Consecrate healing")]
    public void Unanswered_cleaves_stack_and_cost_a_tier_through_full_healing()
    {
        var plain = Drive(withRule: false);
        var marked = Drive(withRule: true);
        _out.WriteLine($"no mark: hits [{string.Join(",", plain.CarelessHits)}] deaths {plain.Deaths}; Cleaved: hits [{string.Join(",", marked.CarelessHits)}] deaths {marked.Deaths}");

        // Without the mark full healing outlasts every Cleave (220 into 250 health): standing in it costs nothing.
        Assert.True(plain.CarelessHits.Count >= 2, $"the Cleave hit the careless seat {plain.CarelessHits.Count} times");
        Assert.All(plain.CarelessHits, h => Assert.Equal(220, h));
        Assert.Equal(0, plain.Deaths);

        // With it each unanswered hit stacks, the next lands harder than a full health bar, and the seat dies.
        var stacks = marked.Events.Where(e => e.Type == "cleaved" && ((JsInt)e.Get("actorId")!).Value == Careless).Select(e => ((JsInt)e.Get("stacks")!).Value).ToList();
        Assert.Equal(1, stacks[0]);
        Assert.Equal(220, marked.CarelessHits[0]);
        Assert.True(marked.CarelessHits[1] > 250, $"the second unanswered Cleave rolled {marked.CarelessHits[1]} after modifiers, not more than a full 250 health bar");
        Assert.True(marked.Deaths >= 1, "the careless seat survived every unanswered Cleave at full health");
        Assert.DoesNotContain(marked.Events, e => e.Type == "cleaved" && ((JsInt)e.Get("actorId")!).Value == Tank);

        var (answeredTier, carelessTier) = Tiers(plain, marked);
        _out.WriteLine($"tiers: answered +{answeredTier}, careless +{carelessTier}");
        Assert.Equal(3, answeredTier);
        Assert.True(carelessTier < answeredTier, $"the careless run kept tier +{carelessTier}");
    }
}
