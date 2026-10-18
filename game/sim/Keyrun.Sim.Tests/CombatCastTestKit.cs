using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Tests;

/// <summary>Builders shared by the lane-2B casting, windows and rules tests (the TS suites' local helpers).</summary>
public static class CombatCastTestKit
{
    /// <summary>The TS tests' <c>createActor({...})</c> with an explicit class and role.</summary>
    public static ActorState Make(
        int id,
        ActorKind kind,
        string classId = "x",
        ActorRole role = ActorRole.None,
        long x = 0,
        long z = 0,
        long maxHealth = 100,
        long maxResource = 0,
        int yaw = 0) =>
        Actors.Create(id, kind, classId, role, new Vec3(x, 0, z), maxHealth, maxResource, yaw);

    /// <summary>The TS makeState at a given tick.</summary>
    public static SimState At(int tick, params ActorState[] actors)
    {
        var s = CombatTestKit.State(actors);
        s.Tick = tick;
        return s;
    }

    /// <summary>An ability map keyed by id.</summary>
    public static IReadOnlyDictionary<string, AbilityDef> AbilityMap(params AbilityDef[] defs)
    {
        var d = new Dictionary<string, AbilityDef>();
        foreach (var def in defs)
        {
            d[def.Id] = def;
        }
        return d;
    }

    /// <summary>A fully specified ability (the TS object literals).</summary>
    public static AbilityDef Def(
        string id,
        AbilityShape shape,
        IReadOnlyList<EffectOp> effects,
        int castTicks = 0,
        int gcdTicks = 0,
        int cooldownTicks = 0,
        long charges = 1,
        string costResource = "energy",
        long costAmount = 0,
        long range = 5000,
        bool interruptible = false,
        IReadOnlyList<string>? tags = null) => new()
        {
            Id = id,
            CastTicks = castTicks,
            GcdTicks = gcdTicks,
            CooldownTicks = cooldownTicks,
            Charges = charges,
            Cost = new AbilityCost(costResource, costAmount),
            Range = range,
            Shape = shape,
            Interruptible = interruptible,
            Effects = effects,
            Tags = tags,
        };

    /// <summary>A RulesConfig with the TS tests' usual movement numbers.</summary>
    public static RulesConfig Config(
        IReadOnlyDictionary<string, AbilityDef> abilities,
        IReadOnlyDictionary<string, EffectDef>? effects = null,
        long boundsHalf = 10000,
        long actorRadiusMm = 50,
        ComboTable? combos = null,
        ComboTable? fieldCombos = null,
        ICombatClause? clause = null,
        int lockoutTicks = 60) => new()
        {
            Abilities = abilities,
            Effects = effects ?? CombatTestKit.NoDefs,
            LockoutTicks = lockoutTicks,
            BaseSpeedMmPerTick = 300,
            ActorRadiusMm = actorRadiusMm,
            Bounds = new Bounds(-boundsHalf, boundsHalf, -boundsHalf, boundsHalf),
            Combos = combos,
            FieldCombos = fieldCombos,
            Clause = clause,
        };

    /// <summary>The TS suites' <c>tick</c> helper: run the rules, then advance the tick as Tick.Step would.</summary>
    public static void Tick(SimState state, StepRules rules, IReadOnlyList<Command> cmds, System.Action<SimEvent> emit)
    {
        rules(state, cmds, emit);
        state.Tick += 1;
    }

    /// <summary>A cast command addressed to tick 0 (the TS suites ignore the address when driving rules directly).</summary>
    public static Command Cast(int actorId, string abilityId, int? targetId = null) => new CastCommand(0, actorId, abilityId, targetId);

    /// <summary>One command list.</summary>
    public static IReadOnlyList<Command> Cmds(params Command[] cmds) => cmds;

    /// <summary>The integer value of an event field.</summary>
    public static long Int(SimEvent e, string key) => ((JsInt)e.Get(key)!).Value;

    /// <summary>The string value of an event field.</summary>
    public static string Str(SimEvent e, string key) => ((JsString)e.Get(key)!).Value;

    /// <summary>The boolean value of an event field, or null when absent.</summary>
    public static bool? Bool(SimEvent e, string key) => e.Get(key) is JsBool b ? b.Value : null;

    /// <summary>A ComboTable from (setupClass, payoffClass, combo) rows.</summary>
    public static ComboTable Table(params (string Setup, string Payoff, ComboDef Combo)[] rows)
    {
        var t = new ComboTable();
        foreach (var (setup, payoff, combo) in rows)
        {
            t.Set(setup, payoff, combo);
        }
        return t;
    }
}
