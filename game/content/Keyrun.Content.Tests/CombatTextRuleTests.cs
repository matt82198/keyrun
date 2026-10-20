using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The MISS combat text rule (<see cref="CombatTextRule"/>, lane MISS-VIEW, finding f-8a1e4403). PR #223 made a blinded
/// source's hit emit a "miss" event (sourceId, targetId, rawAmount) in place of "damage"; HUD-DECLUTTER (Matt
/// 2026-10-19: "only show the played characters damage numbers in fight") limits floating text to the human's seat.
/// So a "miss" event whose source is the human spawns exactly one "MISS" text over its target, and a bot's or an
/// enemy's miss spawns none. A "damage" event is never claimed by the miss rule (damage numbers stay on their own path).
/// </summary>
public sealed class CombatTextRuleTests
{
    private const int Human = 1;

    [Fact]
    public void The_human_s_miss_yields_one_MISS_text_over_its_target()
    {
        Assert.True(CombatTextRule.HumanMiss("miss", sourceId: Human, targetId: 7, humanActorId: Human, out var target));
        Assert.Equal(7, target);
        Assert.Equal("MISS", CombatTextRule.MissText);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(100)]
    public void A_bot_s_or_an_enemy_s_miss_yields_no_text(long sourceId)
    {
        Assert.False(CombatTextRule.HumanMiss("miss", sourceId, targetId: 7, humanActorId: Human, out var target));
        Assert.Equal(0, target);
    }

    [Fact]
    public void The_played_seat_decides_not_the_actor_id_1()
    {
        // The human may sit in any seat (HostRunSetup.HumanActorId): a miss by actor 1 is then a bot's miss.
        Assert.True(CombatTextRule.HumanMiss("miss", sourceId: 3, targetId: 9, humanActorId: 3, out var target));
        Assert.Equal(9, target);
        Assert.False(CombatTextRule.HumanMiss("miss", sourceId: 1, targetId: 9, humanActorId: 3, out _));
    }

    [Theory]
    [InlineData("damage")]
    [InlineData("heal")]
    [InlineData("death")]
    [InlineData("MISS")]
    public void Other_events_by_the_human_are_not_misses(string type)
    {
        Assert.False(CombatTextRule.HumanMiss(type, sourceId: Human, targetId: 7, humanActorId: Human, out _));
    }

    [Fact]
    public void A_miss_event_without_ids_yields_no_text()
    {
        Assert.False(CombatTextRule.HumanMiss("miss", sourceId: null, targetId: 7, humanActorId: Human, out _));
        Assert.False(CombatTextRule.HumanMiss("miss", sourceId: Human, targetId: null, humanActorId: Human, out _));
    }
}
