using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// design/BIBLE.md section 5 (Chanter): "always in Mend or Ward verse; Refrain swaps and fires a burst on the swap.
/// Every other ability behaves differently per verse". The verse is the Chanter's loop resource (classes/chanter.json
/// loop "verse", max 1): 1 is Mend, 0 is Ward. Every case drives the REAL content/data/abilities/chanter.json through
/// <see cref="Rules.Make"/>, so the data and the sim rule are proven together.
/// </summary>
public class ChanterVerseTests
{
    private const int ChanterId = 1;

    [Fact]
    public void ChanterStartsInMend()
    {
        var chanter = NewChanter();
        Assert.Equal(Abilities.MendVerse, Actors.Verse(chanter));
    }

    [Fact]
    public void RefrainFromMendSwapsToWardAndFiresTheBurstShield()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(ChanterId, "chanter.refrain")), events.Add);

        var chanter = state.Actors[ChanterId];
        Assert.Equal(0, chanter.Resource);
        Assert.Equal(Abilities.WardVerse, Actors.Verse(chanter));
        Assert.Contains(chanter.Effects, e => e.EffectId == "wardShield");
        Assert.DoesNotContain(events, e => e.Type == "heal");
    }

    [Fact]
    public void RefrainFromWardSwapsBackToMendAndFiresTheBurstHeal()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(ChanterId, "chanter.refrain")), events.Add);
        ReadyAgain(state.Actors[ChanterId]);
        events.Clear();

        Tick(state, rules, Cmds(Cast(ChanterId, "chanter.refrain")), events.Add);

        var chanter = state.Actors[ChanterId];
        Assert.Equal(1, chanter.Resource);
        Assert.Equal(Abilities.MendVerse, Actors.Verse(chanter));
        var heal = Assert.Single(events, e => e.Type == "heal");
        Assert.Equal(150, Int(heal, "amount"));
    }

    [Fact]
    public void ChorusPlaysTheVerseTheChanterIsIn()
    {
        var (mendState, mendRules) = Arena();
        var mendEvents = new List<SimEvent>();
        Tick(mendState, mendRules, Cmds(Cast(ChanterId, "chanter.chorus", ChanterId)), mendEvents.Add);
        Assert.Contains(mendEvents, e => e.Type == "heal");
        Assert.DoesNotContain(mendState.Actors[ChanterId].Effects, e => e.EffectId == "wardPulse");

        var (wardState, wardRules) = Arena();
        Tick(wardState, wardRules, Cmds(Cast(ChanterId, "chanter.refrain")), _ => { });
        var chanter = wardState.Actors[ChanterId];
        ReadyAgain(chanter);
        chanter.Effects.Clear();
        chanter.Health = 500;
        var wardEvents = new List<SimEvent>();
        Tick(wardState, wardRules, Cmds(Cast(ChanterId, "chanter.chorus", ChanterId)), wardEvents.Add);
        Assert.DoesNotContain(wardEvents, e => e.Type == "heal");
        Assert.Contains(chanter.Effects, e => e.EffectId == "wardPulse");
    }

    [Fact]
    public void AnAbilityWithNoVariantsPlaysItsBaseEffects()
    {
        var def = Def("plain", new AbilityShape(ShapeKind.Self), new EffectOp[] { new ResourceOp(1) });
        Assert.Same(def, Abilities.ResolveVerse(def, NewChanter()));
    }

    [Fact]
    public void ValidatorRejectsAnUnknownOpInsideAVariant()
    {
        var def = Def("v", new AbilityShape(ShapeKind.Self), new EffectOp[] { new PayoffOp() }) with
        {
            Variants = new Dictionary<string, IReadOnlyList<EffectOp>> { [Abilities.WardVerse] = new EffectOp[] { new ComboOp("nope") } },
        };
        Assert.Contains(Abilities.ValidateAbilityDef(def), e => e.Contains("comboTable", StringComparison.Ordinal));
    }

    private static ActorState NewChanter()
    {
        var a = Make(ChanterId, ActorKind.Player, "chanter", ActorRole.Healer, maxHealth: 1000, maxResource: 1);
        a.Health = 500;
        return a;
    }

    private static void ReadyAgain(ActorState a)
    {
        a.Cooldowns = new JsRecord<int>();
        a.GcdReadyTick = 0;
    }

    private static (SimState State, StepRules Rules) Arena()
    {
        var abilities = LoadChanterAbilities();
        var effects = abilities.Values
            .SelectMany(d => d.Effects.Concat(d.Variants?.Values.SelectMany(v => v) ?? Enumerable.Empty<EffectOp>()))
            .OfType<ApplyEffectOp>()
            .Select(a => a.EffectId)
            .Distinct(StringComparer.Ordinal)
            // Beneficial polarity, as content/data/effects/player.json gives wardShield/wardPulse (damageTakenBp < 0),
            // so the ward ops target allies (Affinity.cs); only their presence is asserted.
            .ToDictionary(id => id, id => new EffectDef(id, 1, null, new EffectModifiers(null, -2000, null, null, null)), StringComparer.Ordinal);
        return (At(0, NewChanter()), Rules.Make(Config(abilities, effects)));
    }

    // The content/data JSON read the way game/src/Host/HostRunSetup.cs ToSim reads it (the ops chanter.json uses).
    private static IReadOnlyDictionary<string, AbilityDef> LoadChanterAbilities()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ChanterJsonPath()));
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
            Dictionary<string, IReadOnlyList<EffectOp>>? variants = null;
            if (a.TryGetProperty("variants", out var vs))
            {
                variants = vs.EnumerateObject().ToDictionary(p => p.Name, p => Ops(p.Value, id), StringComparer.Ordinal);
            }
            map[id] = Def(
                id,
                new AbilityShape(kind, radius),
                Ops(a.GetProperty("effects"), id),
                castTicks: a.GetProperty("castTicks").GetInt32(),
                gcdTicks: a.GetProperty("gcdTicks").GetInt32(),
                cooldownTicks: a.GetProperty("cooldownTicks").GetInt32(),
                charges: a.GetProperty("charges").GetInt64(),
                costResource: "none",
                range: a.GetProperty("range").GetInt64(),
                tags: a.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToList()) with { Variants = variants };
        }
        return map;
    }

    private static IReadOnlyList<EffectOp> Ops(JsonElement arr, string where) => arr.EnumerateArray().Select(o => (EffectOp)(o.GetProperty("op").GetString() switch
    {
        "heal" => new HealOp(o.GetProperty("amount").GetInt64()),
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

    private static string ChanterJsonPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content", "data", "abilities", "chanter.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException("content/data/abilities/chanter.json not found above " + AppContext.BaseDirectory);
    }
}
