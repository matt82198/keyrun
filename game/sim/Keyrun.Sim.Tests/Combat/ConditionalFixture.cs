using System;
using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>Builders for <see cref="ConditionalOpTests"/>: arrange the state a condition kind reads, and build its op.</summary>
public static class ConditionalFixture
{
    private const string StateId = "braced";
    private const long Threshold = 50;

    // The hit count is a resolve argument, not actor state, so Arrange remembers whether the HitCountAtLeast threshold
    // must be met for the next Op call. Per thread: tests in one class run one after another.
    [ThreadStatic]
    private static bool _holds;

    /// <summary>Puts the state <paramref name="kind"/> reads on the actors, or leaves it absent.</summary>
    /// <param name="kind">The condition kind.</param>
    /// <param name="holds">Whether the state should hold.</param>
    /// <param name="caster">The caster.</param>
    /// <param name="target">The target.</param>
    public static void Arrange(ConditionKind kind, bool holds, ActorState caster, ActorState target)
    {
        _holds = holds;
        switch (kind)
        {
            case ConditionKind.SelfHas:
                if (holds)
                {
                    AddEffect(caster, StateId);
                }
                break;
            case ConditionKind.TargetHas:
                if (holds)
                {
                    AddEffect(target, StateId);
                }
                break;
            case ConditionKind.SelfResourceAtLeast:
                caster.MaxResource = 100;
                caster.Resource = holds ? 80 : 0;
                break;
            case ConditionKind.TargetCasting:
                target.Cast = holds ? new CastInFlight("flare", 0, 10, false, null, false) : null;
                break;
            case ConditionKind.HitCountAtLeast:
                break;
        }
    }

    /// <summary>The conditional op for <paramref name="kind"/>, thresholds set against a hit count of 3.</summary>
    /// <param name="kind">The condition kind.</param>
    /// <param name="consume">Whether the op consumes what it read.</param>
    /// <returns>The op.</returns>
    public static ConditionalOp Op(ConditionKind kind, bool consume = false) => new()
    {
        If = kind switch
        {
            ConditionKind.SelfHas or ConditionKind.TargetHas => new OpCondition(kind, StateId),
            ConditionKind.SelfResourceAtLeast => new OpCondition(kind, N: Threshold),
            ConditionKind.HitCountAtLeast => new OpCondition(kind, N: _holds ? 3 : 4),
            _ => new OpCondition(kind),
        },
        Consume = consume,
        Then = new EffectOp[] { new ResourceOp(15) },
    };

    /// <summary>Gives <paramref name="actor"/> an effect instance.</summary>
    /// <param name="actor">The holder.</param>
    /// <param name="id">The effect id.</param>
    public static void AddEffect(ActorState actor, string id) =>
        actor.Effects.Add(new EffectInstance { EffectId = id, SourceId = actor.Id, Stacks = 1, EndsTick = 1000 });

    /// <summary>True when <paramref name="actor"/> holds the effect.</summary>
    /// <param name="actor">The holder.</param>
    /// <param name="id">The effect id.</param>
    /// <returns>Whether it is present.</returns>
    public static bool HasEffect(ActorState actor, string id) => actor.Effects.Exists(e => e.EffectId == id);

    /// <summary>A resolve log for a seed-derived scenario; identical for identical seeds.</summary>
    /// <param name="seed">The seed.</param>
    /// <returns>Log lines.</returns>
    public static IReadOnlyList<string> RunLog(int seed)
    {
        var log = new List<string>();
        long roll = seed;
        foreach (ConditionKind kind in Enum.GetValues<ConditionKind>())
        {
            roll = (roll * 1103515245L + 12345L) & 0x7fffffffL;
            bool holds = (roll & 1) == 0;
            var caster = Actor(1, ActorKind.Player);
            var target = Actor(2, ActorKind.Enemy);
            Arrange(kind, holds, caster, target);
            ConditionalOp op = Op(kind, consume: true);
            IReadOnlyList<EffectOp> ran = ConditionalResolver.Apply(op, caster, target, hitCount: 3);
            log.Add($"{kind}:{holds}:{ran.Count}:{caster.Effects.Count}:{target.Effects.Count}");
        }
        return log;
    }
}
