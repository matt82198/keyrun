using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The strike cue rule (<see cref="StrikeCueRule"/>, KF1): every melee ability that lands damage on a tick emits one
/// <see cref="StrikeCue"/> on that tick, carrying the attacker's actor id, facing and the cue duration. Instants and
/// casts count alike (both emit cast_complete); an ability that is not tagged melee, damage on another tick or from
/// another actor, and a melee resolve with no damage emit nothing.
/// </summary>
public sealed class StrikeCueTests
{
    private const int Warden = 1;
    private const int Yaw = 256;

    private static IReadOnlySet<string> Melee => new HashSet<string>(new[] { "warden.strike", "choir.meleeSwing", "pair.meleeSwing" });

    private static IReadOnlyList<StrikeCue> Run(params StrikeEvent[] events) =>
        StrikeCueRule.Cues(events, Melee, _ => Yaw);

    [Fact]
    public void Strike_landing_damage_emits_one_cue_on_the_damage_tick()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 42, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 42, Warden));

        var cue = Assert.Single(cues);
        Assert.Equal(Warden, cue.ActorId);
        Assert.Equal(42, cue.Tick);
        Assert.Equal(Yaw, cue.Yaw);
        Assert.Equal(StrikeCueRule.DurationMs, cue.DurationMs);
        Assert.Equal("warden.strike", cue.AbilityId);
    }

    [Theory]
    [InlineData("warden.strike")]
    [InlineData("choir.meleeSwing")]
    [InlineData("pair.meleeSwing")]
    public void Every_melee_ability_landing_damage_emits_a_cue(string abilityId)
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 7, Warden, abilityId),
            new StrikeEvent(StrikeCueRule.DamageEvent, 7, Warden));

        Assert.Equal(abilityId, Assert.Single(cues).AbilityId);
    }

    [Fact]
    public void Melee_set_is_the_abilities_tagged_melee()
    {
        var ids = StrikeCueRule.MeleeIds(new (string, IReadOnlyList<string>?)[]
        {
            ("warden.strike", new[] { "setup:sundered" }),
            ("choir.meleeSwing", new[] { StrikeCueRule.MeleeTag }),
            ("cinder.flare", null),
        });

        Assert.Equal(new[] { "choir.meleeSwing" }, ids.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Ability_not_tagged_melee_emits_nothing()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 9, Warden, "cinder.flare"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 9, Warden));

        Assert.Empty(cues);
    }

    [Fact]
    public void Damage_on_another_tick_emits_nothing()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 10, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 11, Warden));

        Assert.Empty(cues);
    }

    [Fact]
    public void Damage_from_another_actor_emits_nothing()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 12, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 12, 5));

        Assert.Empty(cues);
    }

    [Fact]
    public void Melee_resolve_with_no_damage_emits_nothing()
    {
        var cues = Run(new StrikeEvent(StrikeCueRule.ResolveEvent, 13, Warden, "warden.strike"));

        Assert.Empty(cues);
    }

    [Fact]
    public void Cleave_hitting_three_targets_on_one_tick_is_one_cue()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 14, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 14, Warden),
            new StrikeEvent(StrikeCueRule.DamageEvent, 14, Warden),
            new StrikeEvent(StrikeCueRule.DamageEvent, 14, Warden));

        Assert.Single(cues);
    }

    [Fact]
    public void Facing_is_read_from_the_attacker_on_the_step()
    {
        var cues = StrikeCueRule.Cues(
            new[]
            {
                new StrikeEvent(StrikeCueRule.ResolveEvent, 15, Warden, "warden.strike"),
                new StrikeEvent(StrikeCueRule.DamageEvent, 15, Warden),
            },
            Melee,
            id => id == Warden ? 768 : 0);

        Assert.Equal(768, Assert.Single(cues).Yaw);
    }

    [Fact]
    public void Duration_is_inside_the_readable_band()
    {
        Assert.InRange(StrikeCueRule.DurationMs, 150, 250);
    }
}
