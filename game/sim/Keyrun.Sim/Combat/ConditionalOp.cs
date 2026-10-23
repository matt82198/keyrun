using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

// G2.2 design/ABILITY-TREES.md s1.3, s5.2 (ruling R-TREES-1): the closed list of conditions a conditional op reads.
// C#-only: no TypeScript twin, no oracle content uses it.

/// <summary>The closed list of conditions a <see cref="ConditionalOp"/> may read (ABILITY-TREES s1.3).</summary>
public enum ConditionKind
{
    /// <summary>The caster has the effect or Window <see cref="OpCondition.Id"/>.</summary>
    SelfHas,

    /// <summary>The primary target has the effect or Window <see cref="OpCondition.Id"/>.</summary>
    TargetHas,

    /// <summary>The caster's resource is at least <see cref="OpCondition.N"/>.</summary>
    SelfResourceAtLeast,

    /// <summary>The primary target has a cast bar up.</summary>
    TargetCasting,

    /// <summary>The shape hit at least <see cref="OpCondition.N"/> enemies on this cast.</summary>
    HitCountAtLeast,
}

/// <summary>One condition of a <see cref="ConditionalOp"/>.</summary>
/// <param name="Kind">What is read.</param>
/// <param name="Id">Effect or Window id (SelfHas, TargetHas); empty otherwise.</param>
/// <param name="N">Threshold (SelfResourceAtLeast, HitCountAtLeast); 0 otherwise.</param>
public sealed record OpCondition(ConditionKind Kind, string Id = "", long N = 0);

/// <summary>
/// <c>{op:"conditional", if, consume, then}</c> (ABILITY-TREES s5.2): when <see cref="If"/> holds at resolve time, the
/// state it read is optionally consumed and the <see cref="Then"/> body runs; otherwise nothing happens.
/// </summary>
public sealed record ConditionalOp : EffectOp
{
    /// <summary>The condition.</summary>
    public required OpCondition If { get; init; }

    /// <summary>True: remove exactly the state the condition read (SelfHas, TargetHas).</summary>
    public bool Consume { get; init; }

    /// <summary>The ops that run when the condition holds.</summary>
    public required IReadOnlyList<EffectOp> Then { get; init; }
}

/// <summary>Resolves a <see cref="ConditionalOp"/> from sim state, with no randomness and no clock.</summary>
public static class ConditionalResolver
{
    /// <summary>True when the op's condition holds.</summary>
    /// <param name="op">The conditional.</param>
    /// <param name="caster">The caster.</param>
    /// <param name="target">The primary target, or null.</param>
    /// <param name="hitCount">Enemies the shape hit on this cast.</param>
    /// <returns>Whether the condition holds.</returns>
    public static bool Holds(ConditionalOp op, ActorState caster, ActorState? target, int hitCount)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(caster);
        OpCondition c = op.If;
        return c.Kind switch
        {
            ConditionKind.SelfHas => HasEffect(caster, c.Id),
            ConditionKind.TargetHas => target is not null && HasEffect(target, c.Id),
            ConditionKind.SelfResourceAtLeast => caster.Resource >= c.N,
            ConditionKind.TargetCasting => target is not null && target.CastId is not null,
            ConditionKind.HitCountAtLeast => hitCount >= c.N,
            _ => false,
        };
    }

    /// <summary>
    /// Resolves the conditional: when it holds, consumes what it read (if <see cref="ConditionalOp.Consume"/>) and
    /// returns the <see cref="ConditionalOp.Then"/> ops for the pipeline to run; otherwise returns no ops.
    /// </summary>
    /// <param name="op">The conditional.</param>
    /// <param name="caster">The caster.</param>
    /// <param name="target">The primary target, or null.</param>
    /// <param name="hitCount">Enemies the shape hit on this cast.</param>
    /// <returns>The ops to run.</returns>
    public static IReadOnlyList<EffectOp> Apply(ConditionalOp op, ActorState caster, ActorState? target, int hitCount)
    {
        if (!Holds(op, caster, target, hitCount))
        {
            return Array.Empty<EffectOp>();
        }
        if (op.Consume)
        {
            if (op.If.Kind == ConditionKind.SelfHas)
            {
                Effects.RemoveEffectInstances(caster, op.If.Id);
            }
            else if (op.If.Kind == ConditionKind.TargetHas && target is not null)
            {
                Effects.RemoveEffectInstances(target, op.If.Id);
            }
        }
        return op.Then;
    }

    private static bool HasEffect(ActorState actor, string id) => actor.Effects.Any(e => e.EffectId == id);
}
