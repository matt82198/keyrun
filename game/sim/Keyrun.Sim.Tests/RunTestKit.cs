using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Shared builders for the run-root tests (ports of tests/sim/integration/*.test.ts that drive createRun/stepRun).
/// Everything here goes through the public path only: <see cref="RunRoot.CreateRun"/> and <see cref="RunRoot.StepRun"/>.
/// </summary>
internal static class RunTestKit
{
    public static readonly Bounds Arena10k = new(-10000, 10000, -10000, 10000);

    public static readonly Bounds Arena20k = new(-20000, 20000, -20000, 20000);

    /// <summary>An instant, no-cost, target-shaped ability (the TS test helpers' <c>ability(id, effects)</c>).</summary>
    public static AbilityDef Ability(string id, IReadOnlyList<EffectOp> effects, AbilityShape? shape = null, int cooldownTicks = 0, int gcdTicks = 0, long range = 100000) => new()
    {
        Id = id,
        CastTicks = 0,
        GcdTicks = gcdTicks,
        CooldownTicks = cooldownTicks,
        Charges = 1,
        Cost = new AbilityCost("energy", 0),
        Range = range,
        Shape = shape ?? new AbilityShape(ShapeKind.Target),
        Interruptible = false,
        Effects = effects,
    };

    /// <summary>A RunContent with the given abilities and effects and nothing else (no scripts, no combos).</summary>
    public static RunContent Content(
        IReadOnlyDictionary<string, AbilityDef>? abilities = null,
        IReadOnlyDictionary<string, EffectDef>? effects = null,
        int lockoutTicks = 40,
        Bounds? bounds = null) => new()
    {
        Abilities = abilities ?? new Dictionary<string, AbilityDef>(StringComparer.Ordinal),
        Effects = effects ?? new Dictionary<string, EffectDef>(StringComparer.Ordinal),
        LockoutTicks = lockoutTicks,
        BaseSpeedMmPerTick = 180,
        ActorRadiusMm = 400,
        Bounds = bounds ?? Arena10k,
        EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>(),
    };

    public static PartyMemberSpec Member(int id, string classId, ActorRole role, long x, long z, long maxHealth, long maxResource) =>
        new(id, classId, role, new Vec3(x, 0, z), maxHealth, maxResource);

    public static DungeonDef OnePull(string dungeonId, string pullId, params PullSpawn[] spawns) =>
        new(dungeonId, new[] { new PullDef(pullId, spawns) });

    public static PullSpawn Spawn(int actorId, string classId, long x, long z, long health) =>
        new(actorId, classId, new Vec3(x, 0, z), 0, health);

    /// <summary>Steps <paramref name="run"/> until its tick reaches <paramref name="untilTick"/>, feeding each tick its commands.</summary>
    public static (Run Run, List<SimEvent> Events) StepUntil(Run run, int untilTick, IReadOnlyList<Command>? commands = null)
    {
        var events = new List<SimEvent>();
        var current = run;
        while (current.State.Tick < untilTick)
        {
            var tick = current.State.Tick;
            var due = (commands ?? Array.Empty<Command>()).Where(c => c.Tick == tick).ToList();
            var stepped = RunRoot.StepRun(current, due);
            events.AddRange(stepped.Events);
            current = stepped.Run;
        }
        return (current, events);
    }

    public static IReadOnlyList<long> Damages(IEnumerable<SimEvent> events) =>
        events.Where(e => e.Type == "damage").Select(e => ((JsInt)e.Get("healthDamage")!).Value).ToList();
}
