using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Tests;

/// <summary>Builders shared by the lane-2A combat tests (the TS tests' local actor/makeState/def helpers).</summary>
public static class CombatTestKit
{
    /// <summary>No effect definitions (the TS <c>{}</c>).</summary>
    public static readonly IReadOnlyDictionary<string, EffectDef> NoDefs = new Dictionary<string, EffectDef>();

    /// <summary>createActor with the TS test defaults.</summary>
    public static ActorState Actor(
        int id,
        ActorKind kind = ActorKind.Enemy,
        long x = 0,
        long z = 0,
        int yaw = 0,
        long maxHealth = 100,
        long maxResource = 0,
        ActorRole role = ActorRole.None,
        string classId = "x") =>
        Actors.Create(id, kind, classId, role, new Vec3(x, 0, z), maxHealth, maxResource, yaw);

    /// <summary>The TS makeState: tick 0, the given actors, phase "combat".</summary>
    public static SimState State(params ActorState[] actors)
    {
        var s = SimState.Create(1);
        s.Phase = "combat";
        foreach (var a in actors)
        {
            s.Actors[a.Id] = a;
        }
        return s;
    }

    /// <summary>An effect-def map from the given defs, keyed by id.</summary>
    public static IReadOnlyDictionary<string, EffectDef> Defs(params EffectDef[] defs)
    {
        var d = new Dictionary<string, EffectDef>();
        foreach (var def in defs)
        {
            d[def.Id] = def;
        }
        return d;
    }

    /// <summary>An ability with the given shape and ops (the TS defWithShape / def helpers).</summary>
    public static AbilityDef Ability(
        AbilityShape shape,
        IReadOnlyList<EffectOp> effects,
        string id = "test_ability",
        int castTicks = 0,
        int gcdTicks = 0,
        int cooldownTicks = 0,
        long charges = 1) => new()
    {
        Id = id,
        CastTicks = castTicks,
        GcdTicks = gcdTicks,
        CooldownTicks = cooldownTicks,
        Charges = charges,
        Cost = new AbilityCost("energy", 0),
        Range = 5000,
        Shape = shape,
        Interruptible = false,
        Effects = effects,
    };

    /// <summary>An event sink that records into a list.</summary>
    public static (List<SimEvent> Events, System.Action<SimEvent> Emit) Collector()
    {
        var events = new List<SimEvent>();
        return (events, events.Add);
    }
}
