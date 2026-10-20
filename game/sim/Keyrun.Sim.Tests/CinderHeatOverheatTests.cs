using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// design/BIBLE.md section 5 (Cinder): "Heat rises per cast. At 100 the next cast is a free Overheat that dumps Heat
/// to 0 and applies Chill (slower casts for 3 s)." Every case drives the REAL content data through
/// <see cref="Rules.Make"/>: content/data/abilities/cinder.json (the per-cast Heat ops), content/data/classes/cinder.json
/// (loop max 100), content/data/effects/player.json (chill, hasteBp -3000) and game/content/data/run.json
/// (heatOverheat: the Chill effect id and its 60-tick, 3 s, duration).
/// </summary>
public class CinderHeatOverheatTests
{
    private const int CinderId = 1;
    private const int EnemyId = 2;

    [Fact]
    public void ACinderOpensAtZeroHeatAndOtherClassesAtFullResource()
    {
        var rules = OverheatRules();
        Assert.Equal(0, Actors.StartingResource("cinder", 100, rules));
        Assert.Equal(100, Actors.StartingResource("warden", 100, rules));
        Assert.Equal(1, Actors.StartingResource("chanter", 1, rules));
        // No rule configured (every oracle run): the TS start, full resource.
        Assert.Equal(100, Actors.StartingResource("cinder", 100, null));
    }

    [Fact]
    public void TheRunRootOpensACinderAtZeroHeatWhenTheRuleIsSet()
    {
        var party = new[]
        {
            RunTestKit.Member(1, "cinder", ActorRole.Damage, 0, 0, 250, 100),
            RunTestKit.Member(2, "warden", ActorRole.Tank, 1000, 0, 400, 100),
        };
        var dungeon = RunTestKit.OnePull("d", "p0", RunTestKit.Spawn(10, "e", 0, 5000, 100));

        var withRule = RunRoot.CreateRun(1, party, dungeon, RunTestKit.Content() with { Overheat = OverheatRules() });
        Assert.Equal(0, withRule.State.Actors[1].Resource);
        Assert.Equal(100, withRule.State.Actors[2].Resource);

        var oracle = RunRoot.CreateRun(1, party, dungeon, RunTestKit.Content());
        Assert.Equal(100, oracle.State.Actors[1].Resource);
    }

    [Fact]
    public void HeatRisesPerCastAndReachingOneHundredIsNotItselfAnOverheat()
    {
        var (state, rules) = Arena(heat: 80);
        var events = new List<SimEvent>();

        CastToCompletion(state, rules, "cinder.emberBolt", events);

        var cinder = state.Actors[CinderId];
        Assert.Equal(100, cinder.Resource);
        Assert.DoesNotContain(events, e => e.Type == "overheat");
        Assert.DoesNotContain(cinder.Effects, e => e.EffectId == "chill");
    }

    [Fact]
    public void AtOneHundredTheNextCastIsAFreeOverheatThatDumpsHeatAndAppliesChill()
    {
        var (state, rules) = Arena(heat: 100);
        var events = new List<SimEvent>();

        // Detonate costs 30 Heat; as the Overheat it costs nothing (instant, so it completes on this tick).
        Tick(state, rules, Cmds(Cast(CinderId, "cinder.detonate", EnemyId)), events.Add);

        var cinder = state.Actors[CinderId];
        Assert.Equal(0, cinder.Resource);
        var overheat = Assert.Single(events, e => e.Type == "overheat");
        Assert.Equal(CinderId, Int(overheat, "actorId"));
        Assert.Equal("cinder.detonate", Str(overheat, "abilityId"));
        Assert.Contains(events, e => e.Type == "damage" && Int(e, "targetId") == EnemyId);
        var chill = Assert.Single(cinder.Effects, e => e.EffectId == "chill");
        Assert.Equal(0 + 60, chill.EndsTick);
    }

    [Fact]
    public void TheOverheatIsFreeAtCastStartEvenForACostedAbility()
    {
        var (state, rules) = Arena(heat: 100);
        var cinder = state.Actors[CinderId];
        var err = Casting.StartCast(state, cinder, Ability("cinder.detonate"), EnemyId, null, 0, null, Ctx());
        Assert.Null(err);
        Assert.Equal(100, cinder.Resource);
    }

    [Fact]
    public void BelowOneHundredACostedCastSpendsHeatAndIsNoOverheat()
    {
        var (state, rules) = Arena(heat: 99);
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CinderId, "cinder.detonate", EnemyId)), events.Add);

        var cinder = state.Actors[CinderId];
        Assert.Equal(69, cinder.Resource);
        Assert.DoesNotContain(events, e => e.Type == "overheat");
        Assert.DoesNotContain(cinder.Effects, e => e.EffectId == "chill");
    }

    [Fact]
    public void ACastTimeOverheatKeepsItsOwnHeatGainOutOfTheDump()
    {
        var (state, rules) = Arena(heat: 100);
        var events = new List<SimEvent>();

        CastToCompletion(state, rules, "cinder.emberBolt", events);

        var cinder = state.Actors[CinderId];
        Assert.Equal(0, cinder.Resource);
        Assert.Single(events, e => e.Type == "overheat");
        Assert.Contains(cinder.Effects, e => e.EffectId == "chill");
    }

    [Fact]
    public void ChillMakesCastsSlowerForItsThreeSeconds()
    {
        var (state, rules) = Arena(heat: 100);
        Tick(state, rules, Cmds(Cast(CinderId, "cinder.detonate", EnemyId)), _ => { });
        var cinder = state.Actors[CinderId];
        ReadyAgain(cinder);

        var startTick = state.Tick;
        Tick(state, rules, Cmds(Cast(CinderId, "cinder.emberBolt", EnemyId)), _ => { });

        // Ember Bolt is 20 ticks; Chill's hasteBp -3000 stretches it to 20 * 10000 / 7000 = 28 (floor).
        Assert.Equal("cinder.emberBolt", cinder.CastId);
        Assert.Equal(startTick + 28, cinder.CastEndsTick);
    }

    [Fact]
    public void AnUnchilledCinderCastsAtTheContentCastTime()
    {
        var (state, rules) = Arena(heat: 0);
        Tick(state, rules, Cmds(Cast(CinderId, "cinder.emberBolt", EnemyId)), _ => { });
        Assert.Equal(20, state.Actors[CinderId].CastEndsTick);
    }

    [Fact]
    public void AfterTheOverheatHeatBuildsAgainFromZeroWithNoSecondOverheat()
    {
        var (state, rules) = Arena(heat: 100);
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(CinderId, "cinder.detonate", EnemyId)), events.Add);
        ReadyAgain(state.Actors[CinderId]);
        events.Clear();

        CastToCompletion(state, rules, "cinder.flare", events);

        Assert.Equal(25, state.Actors[CinderId].Resource);
        Assert.DoesNotContain(events, e => e.Type == "overheat");
    }

    [Fact]
    public void ANonCinderAtFullResourceIsUnaffected()
    {
        var abilities = AbilityMap(Def("w.slam", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(10, "physical") }, costResource: "momentum", costAmount: 40, range: 25000));
        var warden = Make(CinderId, ActorKind.Player, "warden", ActorRole.Tank, maxHealth: 1000, maxResource: 100);
        var enemy = Make(EnemyId, ActorKind.Enemy, "e", z: 1000, maxHealth: 100000);
        var state = At(0, warden, enemy);
        var rules = Rules.Make(Config(abilities, LoadEffects()) with { Overheat = OverheatRules() });
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CinderId, "w.slam", EnemyId)), events.Add);

        Assert.Equal(60, state.Actors[CinderId].Resource);
        Assert.DoesNotContain(events, e => e.Type == "overheat");
        Assert.DoesNotContain(state.Actors[CinderId].Effects, e => e.EffectId == "chill");
    }

    [Fact]
    public void WithNoOverheatRuleACinderAtOneHundredSpendsAsBefore()
    {
        var abilities = LoadCinderAbilities();
        var state = At(0, NewCinder(100), NewEnemy());
        var rules = Rules.Make(Config(abilities, LoadEffects()));
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CinderId, "cinder.detonate", EnemyId)), events.Add);

        Assert.Equal(70, state.Actors[CinderId].Resource);
        Assert.DoesNotContain(events, e => e.Type == "overheat");
        Assert.DoesNotContain(state.Actors[CinderId].Effects, e => e.EffectId == "chill");
    }

    [Fact]
    public void AnInterruptedOverheatIsNotSpentAndTheNextCastStillOverheats()
    {
        var (state, rules) = Arena(heat: 100);
        Tick(state, rules, Cmds(Cast(CinderId, "cinder.emberBolt", EnemyId)), _ => { });
        var cinder = state.Actors[CinderId];
        Casting.Stopcast(cinder, _ => { }, state.Tick);
        Assert.Equal(100, cinder.Resource);
        ReadyAgain(cinder);
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CinderId, "cinder.detonate", EnemyId)), events.Add);

        Assert.Single(events, e => e.Type == "overheat");
        Assert.Equal(0, cinder.Resource);
    }

    private static void CastToCompletion(SimState state, StepRules rules, string abilityId, List<SimEvent> events)
    {
        Tick(state, rules, Cmds(Cast(CinderId, abilityId, EnemyId)), events.Add);
        for (var guard = 0; guard < 200 && state.Actors[CinderId].CastId is not null; guard++)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        Assert.Null(state.Actors[CinderId].CastId);
    }

    private static void ReadyAgain(ActorState a)
    {
        a.Cooldowns = new JsRecord<int>();
        a.GcdReadyTick = 0;
    }

    private static ActorState NewCinder(long heat)
    {
        var a = Make(CinderId, ActorKind.Player, "cinder", ActorRole.Damage, maxHealth: 1000, maxResource: LoopMax());
        a.Resource = heat;
        return a;
    }

    private static ActorState NewEnemy() => Make(EnemyId, ActorKind.Enemy, "e", z: 1000, maxHealth: 100000);

    private static (SimState State, StepRules Rules) Arena(long heat)
    {
        var cfg = Config(LoadCinderAbilities(), LoadEffects()) with { Overheat = OverheatRules() };
        return (At(0, NewCinder(heat), NewEnemy()), Rules.Make(cfg));
    }

    private static CastContext Ctx() => new(LoadCinderAbilities(), LoadEffects(), 60) { Overheat = OverheatRules() };

    private static AbilityDef Ability(string id) => LoadCinderAbilities()[id];

    // The Overheat rule a host builds from content: the heat-overheat class's loop max is the threshold, run.json
    // heatOverheat names the Chill effect and its duration.
    private static IReadOnlyDictionary<string, OverheatRule> OverheatRules()
    {
        using var run = JsonDocument.Parse(File.ReadAllText(DataPath("game", "content", "data", "run.json")));
        var block = run.RootElement.GetProperty("heatOverheat");
        return new Dictionary<string, OverheatRule>(StringComparer.Ordinal)
        {
            ["cinder"] = new OverheatRule(LoopMax(), block.GetProperty("chillEffectId").GetString()!, block.GetProperty("chillTicks").GetInt32()),
        };
    }

    private static long LoopMax()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(DataPath("content", "data", "classes", "cinder.json")));
        var loop = doc.RootElement.GetProperty("loop");
        Assert.Equal("heat-overheat", loop.GetProperty("kind").GetString());
        return loop.GetProperty("max").GetInt64();
    }

    private static IReadOnlyDictionary<string, EffectDef> LoadEffects()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(DataPath("content", "data", "effects", "player.json")));
        var map = new Dictionary<string, EffectDef>(StringComparer.Ordinal);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var id = e.GetProperty("id").GetString()!;
            var m = e.GetProperty("modifiers");
            long? Field(string name) => m.TryGetProperty(name, out var v) ? v.GetInt64() : null;
            var tag = e.GetProperty("dispellableTag");
            map[id] = new EffectDef(
                id,
                e.GetProperty("maxStacks").GetInt64(),
                tag.ValueKind == JsonValueKind.Null ? null : tag.GetString(),
                new EffectModifiers(Field("damageDoneBp"), Field("damageTakenBp"), Field("hasteBp"), Field("movementBp"), Field("absorb")));
        }
        return map;
    }

    // content/data/abilities/cinder.json read the way game/src/Host/HostRunSetup.cs ToSim reads it (the ops it uses).
    private static IReadOnlyDictionary<string, AbilityDef> LoadCinderAbilities()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(DataPath("content", "data", "abilities", "cinder.json")));
        var map = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        foreach (var a in doc.RootElement.EnumerateArray())
        {
            var id = a.GetProperty("id").GetString()!;
            var shape = a.GetProperty("shape");
            var kind = shape.GetProperty("kind").GetString() switch
            {
                "self" => ShapeKind.Self,
                "target" => ShapeKind.Target,
                "circle" => ShapeKind.Circle,
                var k => throw new InvalidOperationException($"{id}: shape {k} not mapped by this test"),
            };
            long? radius = shape.TryGetProperty("radius", out var r) ? r.GetInt64() : null;
            var cost = a.GetProperty("cost");
            map[id] = Def(
                id,
                new AbilityShape(kind, radius),
                Ops(a.GetProperty("effects"), id),
                castTicks: a.GetProperty("castTicks").GetInt32(),
                gcdTicks: a.GetProperty("gcdTicks").GetInt32(),
                cooldownTicks: a.GetProperty("cooldownTicks").GetInt32(),
                charges: a.GetProperty("charges").GetInt64(),
                costResource: cost.GetProperty("resource").GetString()!,
                costAmount: cost.GetProperty("amount").GetInt64(),
                range: a.GetProperty("range").GetInt64(),
                interruptible: a.GetProperty("interruptible").GetBoolean(),
                tags: a.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToList());
        }
        return map;
    }

    private static IReadOnlyList<EffectOp> Ops(JsonElement arr, string where) => arr.EnumerateArray().Select(o => (EffectOp)(o.GetProperty("op").GetString() switch
    {
        "damage" => new DamageOp(o.GetProperty("amount").GetInt64(), o.GetProperty("school").GetString()!),
        "applyEffect" => new ApplyEffectOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("stacks").GetInt64()),
        "removeEffect" => new RemoveEffectOp(o.GetProperty("effectId").GetString()!),
        "resource" => new ResourceOp(o.GetProperty("delta").GetInt64()),
        "payoff" => new PayoffOp(),
        "interrupt" => new InterruptOp(),
        "window" => new WindowOp(
            o.GetProperty("windowId").GetString()!,
            o.GetProperty("durationTicks").GetInt32(),
            o.GetProperty("onTarget").GetBoolean(),
            o.GetProperty("setupClass").GetString()!,
            RadiusMm: o.TryGetProperty("radiusMm", out var rm) ? rm.GetInt64() : null),
        var op => throw new InvalidOperationException($"{where}: op {op} not mapped by this test"),
    })).ToList();

    private static string DataPath(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException(string.Join("/", parts) + " not found above " + AppContext.BaseDirectory);
    }
}
