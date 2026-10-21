using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// KF1-WINDUP (finding f-fa28e36e, Matt's demo verdict KF1 "combat animation reads badly"; BIBLE s3: what enemies do is
/// the player's language). Pure event-to-clip mapping and timing, no rendering:
/// <list type="number">
/// <item>An enemy's melee cast (the Warden's Slam and Cleave, the Seed's Slam and Lash, the Twins' Slam) looped the
/// row's Spellcasting pose for the whole cast bar and then snapped to its blow 100 ms before contact: a skeleton "cast a
/// spell" before every slam and the strike had no wind-up. Now the cast plays the strike clip's own wind-up, stretched
/// over the bar, and reaches the contact frame on the frame the sim resolves the hit (<see cref="AbilityAnim.Windup"/>).</item>
/// <item>A spell cast keeps its cast loop, then plays its release wind-up at full speed so the release lands on the
/// resolve tick instead of starting there.</item>
/// <item>Every ability of a row played the same swing clip (Cleave and Slam the same chop, the Cantor's heal the same
/// bolt as its attack). <see cref="AbilityAnim.ActClip"/> gives the abilities that read differently their own CC0 clip
/// from the packs already in the repo.</item>
/// </list>
/// Contact frames for the new clips are measured from the glTF tracks by forward kinematics (the striking hand reaching
/// furthest forward), rounded to 100 ms like <see cref="AnimRule.ContactMs"/>'s existing rows.
/// </summary>
public sealed class AbilityAnimTests
{
    private const int Tick = AbilityAnim.TickMs;
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    // Every (actor row, ability) pair, enemies and playable classes, whose cast resolves a strike: ability, the clip it
    // plays, cast ticks.
    private static List<(string Owner, string Ability, string Clip, int CastTicks)> StrikeCasts()
    {
        var owned = Reg.Enemies.Values.Select(e => (e.Id, (IEnumerable<string>)e.Abilities))
            .Concat(Reg.Classes.Values.Select(c => (c.Id,
                Reg.Abilities.Keys.Where(k => k.StartsWith(c.Id + ".", StringComparison.Ordinal)))));
        var list = new List<(string, string, string, int)>();
        foreach (var (owner, abilities) in owned)
        {
            var row = ActorBinding.Default.For(owner);
            foreach (var id in abilities)
            {
                var def = Reg.Abilities[id];
                var clip = AbilityAnim.ActClip(id, row.Clips.Act);
                if (def.CastTicks > 0 && AbilityAnim.IsStrike(clip))
                {
                    list.Add((owner, id, clip, def.CastTicks));
                }
            }
        }
        return list;
    }

    [Fact]
    public void Every_enemy_melee_cast_is_a_strike_wind_up_never_the_spell_pose()
    {
        // Exactly the enemy melee casts wind up a strike: no spell (a Pair mark, the Warden's Bulwark) winds up a punch.
        var casts = StrikeCasts();
        var names = casts.Select(c => c.Ability).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        // vault.crushingSlam joined with VAULT-KITS (S1): the Vault Brute's BIBLE 9.3 melee cast, a strike, not a spell.
        Assert.Equal(
            new[] { "hollowWarden.cleave", "hollowWarden.slam", "seed.lash", "seed.slam", "twins.slam", "vault.crushingSlam" },
            names);
        foreach (var (enemy, ability, clip, castTicks) in casts)
        {
            const int start = 100;
            var ends = start + castTicks;
            var last = -1;
            // Every render frame the cast shows (sim state ticks start+1 .. ends, alpha 0..999) poses the strike.
            for (var t = start + 1; t <= ends; t++)
            {
                foreach (var alpha in new[] { 0, 500, 999 })
                {
                    var pose = AbilityAnim.Windup(clip, start, ends, t, alpha);
                    Assert.True(pose is not null, $"{enemy} {ability}: tick {t} alpha {alpha} shows the cast loop, not {clip}");
                    Assert.True(pose!.Value.ClipMs >= last, $"{enemy} {ability}: the wind-up ran backwards at tick {t}");
                    var contact = AnimRule.ContactMs(clip)!.Value;
                    Assert.True(alpha == 0 ? pose.Value.ClipMs < contact : pose.Value.ClipMs <= contact,
                        $"{enemy} {ability}: the blow lands before the cast resolves (tick {t} alpha {alpha})");
                    last = pose.Value.ClipMs;
                }
            }
        }
    }

    [Theory]
    [InlineData("hollowWarden.boss", "hollowWarden.slam")]
    [InlineData("hollowWarden.boss", "hollowWarden.cleave")]
    [InlineData("seed.elite", "seed.slam")]
    [InlineData("seed.elite", "seed.lash")]
    [InlineData("hollowWarden.twinA", "twins.slam")]
    public void A_strike_reaches_contact_on_the_resolve_frame(string rowKey, string ability)
    {
        var clip = AbilityAnim.ActClip(ability, ActorBinding.Default.For(rowKey).Clips.Act);
        var contact = AnimRule.ContactMs(clip)!.Value;
        var castTicks = Reg.Abilities[ability].CastTicks;
        const int start = 40;
        var ends = start + castTicks;
        // The resolve frame: the first state after the resolving step (state tick ends + 1), alpha 0.
        Assert.Equal(contact, AbilityAnim.Windup(clip, start, ends, ends + 1, 0)!.Value.ClipMs);
        // The last cast frame is under one tick of wind-up short of contact.
        var lastFrame = AbilityAnim.Windup(clip, start, ends, ends, 999)!.Value;
        Assert.InRange(contact - lastFrame.ClipMs, 0, Tick);
        // The wind-up plays at its own rate or slower, never faster, and never slower than the floor.
        Assert.InRange(lastFrame.SpeedPermille, AbilityAnim.MinWindupSpeedPermille, 1000);
        // On the resolve tick the swing continues from contact, not 100 ms back (no hitch at the blow).
        Assert.Equal(contact, AbilityAnim.ResolveStartMs(clip, woundUp: true));
        Assert.Equal(AnimRule.SwingStartMs(clip), AbilityAnim.ResolveStartMs(clip, woundUp: false));
    }

    [Fact]
    public void A_long_strike_cast_stretches_its_wind_up_over_the_bar()
    {
        // Cleave: 40 ticks (2000 ms). The wind-up runs at the floor speed and holds the clip's first frame before it.
        var clip = AbilityAnim.ActClip("hollowWarden.cleave", "2H_Melee_Attack_Chop");
        var contact = AnimRule.ContactMs(clip)!.Value;
        Assert.Equal(300, contact);
        Assert.Equal(new WindupPose(0, AbilityAnim.MinWindupSpeedPermille), AbilityAnim.Windup(clip, 0, 40, 1, 0));
        // The wind-up window is 300 / 0.4 = 750 ms; 375 ms before the resolve frame (tick 33, alpha 500) it is halfway.
        Assert.Equal(150, AbilityAnim.Windup(clip, 0, 40, 33, 500)!.Value.ClipMs);
        // Before the window opens the clip's first frame is held (the readied stance, not a spell pose).
        Assert.Equal(0, AbilityAnim.Windup(clip, 0, 40, 20, 0)!.Value.ClipMs);
        // Seed Slam: 20 ticks (1000 ms) against an 800 ms contact plays at 800 permille from frame 0.
        var slam = AbilityAnim.ActClip("seed.slam", "Unarmed_Melee_Attack_Kick");
        Assert.Equal(new WindupPose(0, 800), AbilityAnim.Windup(slam, 0, 20, 1, 0));
        // Boss Slam: 10 ticks (500 ms) against an 800 ms contact starts 300 ms in at full speed.
        Assert.Equal(new WindupPose(300, 1000), AbilityAnim.Windup("2H_Melee_Attack_Chop", 0, 10, 1, 0));
    }

    [Fact]
    public void A_spell_cast_holds_its_cast_loop_then_releases_on_the_resolve_tick()
    {
        // Flare: 30 ticks with Spellcast_Shoot (contact 300 ms).
        Assert.Null(AbilityAnim.Windup("Spellcast_Shoot", 0, 30, 1, 0));
        Assert.Null(AbilityAnim.Windup("Spellcast_Shoot", 0, 30, 25, 0)); // 300 ms left: still the cast loop
        Assert.Equal(new WindupPose(50, 1000), AbilityAnim.Windup("Spellcast_Shoot", 0, 30, 26, 0));
        Assert.Equal(new WindupPose(300, 1000), AbilityAnim.Windup("Spellcast_Shoot", 0, 30, 31, 0));
        Assert.False(AbilityAnim.IsStrike("Spellcast_Shoot"));
        Assert.False(AbilityAnim.IsStrike("Spellcast_Raise"));
        // A clip with no measured contact frame keeps the cast loop (no guessed timing).
        Assert.Null(AbilityAnim.Windup("Spellcasting", 0, 30, 30, 0));
        // A cast with no length (instant) has no wind-up.
        Assert.Null(AbilityAnim.Windup("2H_Melee_Attack_Chop", 5, 5, 6, 0));
    }

    [Theory]
    [InlineData("hollowWarden.cleave", "2H_Melee_Attack_Chop", "2H_Melee_Attack_Slice")]
    [InlineData("hollowWarden.slam", "2H_Melee_Attack_Chop", "2H_Melee_Attack_Chop")]
    [InlineData("choir.healCast", "Spellcast_Shoot", "Spellcast_Raise")]
    [InlineData("seed.slam", "Unarmed_Melee_Attack_Kick", "1H_Melee_Attack_Jump_Chop")]
    [InlineData("seed.lash", "Unarmed_Melee_Attack_Kick", "Unarmed_Melee_Attack_Kick")]
    [InlineData("warden.strike", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Chop")]
    [InlineData("warden.guardBreak", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Horizontal")]
    [InlineData("warden.reckoning", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Stab")]
    [InlineData("warden.shieldBash", "1H_Melee_Attack_Chop", "Block_Attack")]
    [InlineData("warden.bulwark", "1H_Melee_Attack_Chop", "Spellcast_Raise")]
    [InlineData("pair.spreadMark", "Unarmed_Melee_Attack_Punch_A", "Spellcast_Shoot")]
    [InlineData("pair.stackCall", "Unarmed_Melee_Attack_Punch_A", "Spellcast_Raise")]
    [InlineData(null, "1H_Melee_Attack_Chop", "1H_Melee_Attack_Chop")]
    [InlineData("no.such.ability", "Spellcast_Shoot", "Spellcast_Shoot")]
    public void Abilities_that_read_differently_play_their_own_clip(string? ability, string rowAct, string want)
    {
        Assert.Equal(want, AbilityAnim.ActClip(ability, rowAct));
    }

    [Theory]
    [InlineData("1H_Melee_Attack_Slice_Horizontal", 200)]
    [InlineData("1H_Melee_Attack_Stab", 700)]
    [InlineData("Block_Attack", 500)]
    [InlineData("2H_Melee_Attack_Slice", 300)]
    [InlineData("1H_Melee_Attack_Jump_Chop", 800)]
    public void New_clips_have_their_measured_contact_frame(string clip, int contactMs)
    {
        Assert.Equal(contactMs, AnimRule.ContactMs(clip));
        Assert.True(AbilityAnim.IsStrike(clip));
    }

    [Fact]
    public void Every_mapped_ability_is_real_and_its_clip_ships_in_the_model_that_casts_it()
    {
        Assert.True(AbilityAnim.MappedAbilities.Count >= 6, "vacuity guard: the per-ability table is empty");
        var root = AssetsRoot();
        foreach (var ability in AbilityAnim.MappedAbilities)
        {
            Assert.True(Reg.Abilities.ContainsKey(ability), $"{ability} is not a content ability");
            var owners = Reg.Enemies.Values.Where(e => e.Abilities.Contains(ability)).Select(e => e.Id)
                .Concat(Reg.Classes.Values.Where(c => ability.StartsWith(c.Id + ".", StringComparison.Ordinal)).Select(c => c.Id))
                .ToList();
            Assert.NotEmpty(owners);
            foreach (var owner in owners)
            {
                var row = ActorBinding.Default.For(owner);
                var clip = AbilityAnim.ActClip(ability, row.Clips.Act);
                Assert.True(AnimRule.ContactMs(clip) is not null, $"{ability}: {clip} has no contact frame");
                var clips = Animations(Path.Combine(root, row.Folder, row.ModelFile!));
                Assert.True(clips.Contains(clip), $"{ability}: {clip} is not in {row.ModelFile} ({owner})");
            }
        }
    }

    private static HashSet<string> Animations(string path)
    {
        var data = File.ReadAllBytes(path);
        var len = BitConverter.ToInt32(data, 12);
        using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, len));
        return doc.RootElement.GetProperty("animations").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
    }

    private static string AssetsRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            var candidate = Path.Combine(d.FullName, "game", "assets");
            if (Directory.Exists(Path.Combine(candidate, "kaykit-skeletons")))
            {
                return candidate;
            }
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("game/assets not found above " + AppContext.BaseDirectory);
    }
}
