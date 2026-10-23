// G2.2 design/ABILITY-TREES.md s5.5 ConditionalOpTests: each condition kind fires only when its state holds; consume removes exactly what it read; the same seed and commands give a byte-identical log.
using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The <c>conditional</c> effect op (ABILITY-TREES s5.2, s1.3): a closed list of conditions read at resolve time from
/// sim state (<c>selfHas</c>, <c>targetHas</c>, <c>selfResourceAtLeast</c>, <c>targetCasting</c>,
/// <c>hitCountAtLeast</c>). Red until S1's PR 1 adds <c>ConditionalOp</c> and <c>ConditionalResolver</c> to the sim.
/// </summary>
public sealed class ConditionalOpTests
{
    public static TheoryData<ConditionKind, bool, bool> Fires => new()
    {
        // kind, state holds, fires
        { ConditionKind.SelfHas, true, true },
        { ConditionKind.SelfHas, false, false },
        { ConditionKind.TargetHas, true, true },
        { ConditionKind.TargetHas, false, false },
        { ConditionKind.SelfResourceAtLeast, true, true },
        { ConditionKind.SelfResourceAtLeast, false, false },
        { ConditionKind.TargetCasting, true, true },
        { ConditionKind.TargetCasting, false, false },
        { ConditionKind.HitCountAtLeast, true, true },
        { ConditionKind.HitCountAtLeast, false, false },
    };

    [Theory]
    [MemberData(nameof(Fires))]
    public void Each_condition_kind_fires_only_when_its_state_holds(ConditionKind kind, bool holds, bool fires)
    {
        var caster = Actor(1, ActorKind.Player);
        var target = Actor(2, ActorKind.Enemy);
        ConditionalFixture.Arrange(kind, holds, caster, target);
        var op = ConditionalFixture.Op(kind);
        Assert.Equal(fires, ConditionalResolver.Holds(op, caster, target, hitCount: 3));
    }

    [Fact]
    public void Consume_removes_exactly_what_the_condition_read()
    {
        // A consuming selfHas removes the braced state it read and nothing else on the caster.
        var caster = Actor(1, ActorKind.Player);
        var target = Actor(2, ActorKind.Enemy);
        ConditionalFixture.Arrange(ConditionKind.SelfHas, true, caster, target);
        ConditionalFixture.AddEffect(caster, "vigil");
        var op = ConditionalFixture.Op(ConditionKind.SelfHas, consume: true);
        ConditionalResolver.Apply(op, caster, target, hitCount: 0);
        Assert.False(ConditionalResolver.Holds(op, caster, target, hitCount: 0));
        Assert.True(ConditionalFixture.HasEffect(caster, "vigil"));
    }

    [Fact]
    public void Same_seed_and_commands_give_a_byte_identical_log()
    {
        IReadOnlyList<string> first = ConditionalFixture.RunLog(seed: 42);
        IReadOnlyList<string> second = ConditionalFixture.RunLog(seed: 42);
        Assert.Equal(first, second);
    }
}
