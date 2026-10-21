using System;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// KF1 (Matt 2026-10-19, "combat animation reads badly"): a fight reads by feel only when the blow, the number and the
/// victim's reaction land together (BIBLE s0 test 1, s3). The sim resolves a swing and its damage on one tick, so the
/// view's swing must reach its contact frame on that tick (<see cref="AnimRule.SwingStartMs"/>), every health drop
/// must flash the victim for a few ticks (<see cref="AnimRule.FlashPermille"/>), and a kicked caster must flinch
/// (<see cref="AnimRule.Kicked"/>).
/// </summary>
public sealed class AnimTimingTests
{
    private const int TickMs = 50;

    [Fact]
    public void Every_bound_swing_clip_has_a_contact_frame()
    {
        foreach (var row in ActorBinding.DefaultRows)
        {
            Assert.True(AnimRule.ContactMs(row.Clips.Act) is not null,
                $"{row.Key}: swing clip {row.Clips.Act} has no contact frame, so its blow lands late");
        }
    }

    [Theory]
    [InlineData("Unarmed_Melee_Attack_Punch_A", 500)]
    [InlineData("1H_Melee_Attack_Chop", 700)]
    [InlineData("2H_Melee_Attack_Chop", 800)]
    [InlineData("Unarmed_Melee_Attack_Kick", 400)]
    [InlineData("Spellcast_Shoot", 300)]
    [InlineData("Spellcast_Raise", 600)]
    public void Swing_lands_its_blow_within_two_ticks_of_the_hit(string clip, int contactMs)
    {
        Assert.Equal(contactMs, AnimRule.ContactMs(clip));
        var start = AnimRule.SwingStartMs(clip);
        var lag = contactMs - start; // ms from the hit tick to the drawn contact
        Assert.InRange(lag, 0, 2 * TickMs);
        Assert.True(start > 0, $"{clip}: a swing from frame 0 lands {contactMs} ms after the damage number");
    }

    [Fact]
    public void Unknown_clip_starts_from_its_first_frame()
    {
        Assert.Null(AnimRule.ContactMs("Idle"));
        Assert.Equal(0, AnimRule.SwingStartMs("Idle"));
        Assert.Throws<ArgumentNullException>(() => AnimRule.ContactMs(null!));
    }

    [Theory]
    [InlineData(0, 0, 1000)]
    [InlineData(0, 500, 834)]
    [InlineData(1, 0, 667)]
    [InlineData(2, 500, 167)]
    [InlineData(2, 999, 1)]
    [InlineData(3, 0, 0)]
    [InlineData(40, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(1_073_741_824, 0, 0)] // never hurt: the view's sentinel hurt tick is far in the past
    [InlineData(int.MaxValue, 999, 0)]
    public void Hit_flash_fades_over_three_ticks(int ticksSince, int alpha, int expected)
    {
        Assert.Equal(expected, AnimRule.FlashPermille(ticksSince, alpha));
    }

    [Fact]
    public void Hit_flash_never_rises_while_it_fades()
    {
        var last = int.MaxValue;
        for (var t = 0; t < AnimRule.FlashTicks + 1; t++)
        {
            for (var a = 0; a < 1000; a += 125)
            {
                var v = AnimRule.FlashPermille(t, a);
                Assert.InRange(v, 0, 1000);
                Assert.True(v <= last, $"flash rose at tick {t} alpha {a}");
                last = v;
            }
        }
    }

    // ENEMY-BODY-STABLE: an enemy body never flashes white on a hit (its colour stays stable); a party body keeps the flash.
    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(40)]
    [InlineData(int.MaxValue)]
    public void An_enemy_never_flashes_at_any_tick_or_alpha(int ticksSince)
    {
        for (var a = 0; a < 1000; a += 125)
        {
            Assert.Equal(0, AnimRule.FlashPermilleFor(FadeRole.Enemy, ticksSince, a));
        }
    }

    [Fact]
    public void A_party_body_flashes_at_full_strength_on_the_hurt_tick()
    {
        Assert.Equal(1000, AnimRule.FlashPermilleFor(FadeRole.Ally, 0, 0));
        Assert.Equal(1000, AnimRule.FlashPermilleFor(FadeRole.Self, 0, 0));
    }

    [Fact]
    public void A_party_body_flash_at_two_ticks_is_the_existing_fade()
    {
        // Existing rule: 1000 - (2000 + 0) * 1000 / 3000 = 334 (the contract's "about 333" is this value, floored from 333.3).
        Assert.Equal(334, AnimRule.FlashPermilleFor(FadeRole.Ally, 2, 0));
        Assert.Equal(1, AnimRule.FlashPermilleFor(FadeRole.Self, 2, 999));
    }

    [Fact]
    public void A_party_body_flash_is_zero_at_and_after_the_flash_ticks()
    {
        Assert.Equal(0, AnimRule.FlashPermilleFor(FadeRole.Ally, AnimRule.FlashTicks, 0));
        Assert.Equal(0, AnimRule.FlashPermilleFor(FadeRole.Self, AnimRule.FlashTicks + 5, 0));
        Assert.Equal(0, AnimRule.FlashPermilleFor(FadeRole.Ally, -1, 0));
    }

    [Fact]
    public void The_party_flash_is_unchanged_by_the_role_wrapper()
    {
        for (var t = -2; t < AnimRule.FlashTicks + 2; t++)
        {
            for (var a = 0; a < 1000; a += 125)
            {
                Assert.Equal(AnimRule.FlashPermille(t, a), AnimRule.FlashPermilleFor(FadeRole.Ally, t, a));
                Assert.Equal(AnimRule.FlashPermille(t, a), AnimRule.FlashPermilleFor(FadeRole.Self, t, a));
            }
        }
    }

    [Fact]
    public void The_flash_receipt_line_names_actor_tick_and_strength()
    {
        Assert.Equal("FLASH actor=102 tick=3952 strength=1000", AnimRule.FlashLine(102, 3952, 1000));
        Assert.Equal("FLASH actor=1 tick=7 strength=334", AnimRule.FlashLine(1, 7, 334));
    }

    [Fact]
    public void Kicked_reads_the_interrupt_target_not_the_kicker()
    {
        // The sim's interrupt event: sourceId is the kicker, targetId the caster whose cast was cut.
        var events = new (string Type, int? TargetId)[] { ("cast_complete", 2), ("interrupt", 102), ("damage", 7) };
        Assert.True(AnimRule.Kicked(102, events));
        Assert.False(AnimRule.Kicked(2, events));
        Assert.False(AnimRule.Kicked(7, events)); // a damage target is not a kicked caster
        Assert.False(AnimRule.Kicked(102, Array.Empty<(string, int?)>()));
    }

    [Fact]
    public void A_kicked_caster_flinches_instead_of_dropping_to_idle()
    {
        var kicked = AnimRule.Kicked(102, new (string, int?)[] { ("interrupt", 102) });
        var d = AnimRule.Decide(new AnimFacts(Alive: true, NewTick: true, Moving: false, Casting: false, Acted: false,
            Hurt: false, Staggered: kicked, Playing: AnimClip.None));
        Assert.Equal(new AnimDecision(AnimClip.Hit, true, true), d);
    }
}
