using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// ABILITY-ANIMS (Matt 2026-10-21 "theres still no ability animations"): every ability in content, player and enemy,
/// maps to a clip the CC0 model of every actor that uses it actually ships (read from the .glb's own animation list),
/// so no ability resolves with no body motion. On main only the ten <see cref="AbilityAnim"/> abilities had a clip of
/// their own; the other thirty-odd played the row's one swing or nothing readable.
/// </summary>
public sealed class AbilityClipTableTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    // Every (owner row key, ability id) pair: enemies by their ability lists, classes by the "<class>." id prefix.
    private static List<(string Owner, string Ability)> Owned()
    {
        var list = new List<(string, string)>();
        foreach (var e in Reg.Enemies.Values)
        {
            list.AddRange(e.Abilities.Select(a => (e.Id, a)));
        }
        foreach (var c in Reg.Classes.Values)
        {
            list.AddRange(Reg.Abilities.Keys.Where(k => k.StartsWith(c.Id + ".", StringComparison.Ordinal))
                .Select(a => (c.Id, a)));
        }
        return list;
    }

    [Fact]
    public void Every_content_ability_has_its_own_clip_row()
    {
        Assert.True(Reg.Abilities.Count >= 40, "vacuity guard: content loaded too few abilities");
        var missing = Reg.Abilities.Keys.Where(id => AbilityClipTable.For(id) is null)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "abilities with no clip: " + string.Join(", ", missing));
        var stale = AbilityClipTable.Rows.Select(r => r.AbilityId).Where(id => !Reg.Abilities.ContainsKey(id)).ToList();
        Assert.True(stale.Count == 0, "clip rows for no content ability: " + string.Join(", ", stale));
    }

    [Fact]
    public void Every_ability_maps_to_a_clip_the_model_that_uses_it_ships()
    {
        var owned = Owned();
        Assert.True(owned.Count >= 40, "vacuity guard: too few owned abilities");
        var bad = new List<string>();
        foreach (var (owner, ability) in owned)
        {
            var row = ActorBinding.Default.For(owner);
            Assert.False(row.CapsuleFallback, $"{owner} has no model row");
            var clips = Clips(row);
            var release = AbilityClipTable.ReleaseClip(ability, row.Clips.Act);
            if (!clips.Contains(release))
            {
                bad.Add($"{ability} release {release} not in {row.ModelFile} ({owner})");
            }
            var cast = AbilityClipTable.CastClip(ability) ?? row.Clips.Cast;
            if (Reg.Abilities[ability].CastTicks > 0 && !clips.Contains(cast))
            {
                bad.Add($"{ability} cast {cast} not in {row.ModelFile} ({owner})");
            }
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    [Fact]
    public void A_release_is_never_a_looped_row_state()
    {
        // The view loops a row's idle, move and cast clips on the shared animation resource; a one-shot release on one of
        // them would never end and the body would hold it forever.
        var loops = ActorBinding.Default.Rows.SelectMany(r => new[] { r.Clips.Idle, r.Clips.Move, r.Clips.Cast })
            .Concat(AbilityClipTable.Rows.Select(r => r.Cast).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var row in AbilityClipTable.Rows)
        {
            Assert.False(loops.Contains(row.Release), $"{row.AbilityId}: release {row.Release} is a looped state");
        }
    }

    [Fact]
    public void Strike_casts_keep_their_measured_wind_up_clip()
    {
        // KF1-WINDUP timing (AbilityAnimTests) is measured on AbilityAnim.ActClip's clip: the table must agree on every
        // cast that winds up a strike, and on every ability AbilityAnim maps.
        foreach (var (owner, ability) in Owned())
        {
            var rowAct = ActorBinding.Default.For(owner).Clips.Act;
            var timed = AbilityAnim.ActClip(ability, rowAct);
            if (AbilityAnim.MappedAbilities.Contains(ability) ||
                (Reg.Abilities[ability].CastTicks > 0 && AbilityAnim.IsStrike(timed)))
            {
                Assert.Equal(timed, AbilityClipTable.ReleaseClip(ability, rowAct));
            }
        }
    }

    [Fact]
    public void Interrupts_swing_a_short_strike()
    {
        var kicks = Reg.Abilities.Values.Where(a => a.Tags.Contains("interrupt")).Select(a => a.Id).ToList();
        Assert.Equal(3, kicks.Count);
        foreach (var id in kicks)
        {
            var row = AbilityClipTable.For(id)!;
            Assert.Equal(AbilityAnimKind.Kick, row.Kind);
            Assert.True(AbilityAnim.IsStrike(row.Release), $"{id}: {row.Release} is not a strike");
            Assert.True(AnimRule.ContactMs(row.Release) is int c && c <= 500, $"{id}: {row.Release} is not short");
        }
    }

    [Fact]
    public void Gaps_name_a_real_clip_the_model_lacks_and_a_clip_it_has()
    {
        Assert.NotEmpty(AbilityClipTable.Gaps);
        var every = ActorBinding.Default.Rows.Where(r => !r.CapsuleFallback).SelectMany(Clips).ToHashSet(StringComparer.Ordinal);
        foreach (var gap in AbilityClipTable.Gaps)
        {
            var row = ActorBinding.Default.Rows.First(r => r.ModelFile == gap.Model);
            var clips = Clips(row);
            Assert.False(clips.Contains(gap.Wanted), $"{gap.AbilityId}: {gap.Model} does ship {gap.Wanted}");
            Assert.True(every.Contains(gap.Wanted), $"{gap.AbilityId}: no pack ships {gap.Wanted}");
            Assert.True(clips.Contains(gap.Used), $"{gap.AbilityId}: {gap.Model} lacks {gap.Used}");
            Assert.Equal(gap.Used, AbilityClipTable.For(gap.AbilityId)!.Release);
        }
    }

    [Theory]
    [InlineData("warden.strike", "1H_Melee_Attack_Chop")]
    [InlineData("chanter.rebuke", "Unarmed_Melee_Attack_Kick")]
    [InlineData("warden.brace", "Blocking")]
    [InlineData(null, "1H_Melee_Attack_Chop")]
    [InlineData("no.such.ability", "1H_Melee_Attack_Chop")]
    public void Release_falls_back_to_the_row_swing(string? ability, string want)
    {
        Assert.Equal(want, AbilityClipTable.ReleaseClip(ability, "1H_Melee_Attack_Chop"));
    }

    [Fact]
    public void Anim_line_has_the_capture_shape()
    {
        Assert.Equal("ANIM actor=3 clip=Spellcast_Shoot ability=cinder.flare at=112",
            AbilityClipTable.AnimLine(3, "Spellcast_Shoot", "cinder.flare", 112));
        Assert.Equal("ANIM actor=100 clip=Death_C_Skeletons ability=- at=640",
            AbilityClipTable.AnimLine(100, "Death_C_Skeletons", null, 640));
    }

    [Fact]
    public void Every_release_plays_at_least_the_readable_minimum_on_its_model()
    {
        // ANIMS-READABLE-2: the clip's own length from where the view starts it (an instant's SwingStartMs, a wound-up
        // swing's contact), at ReleaseHold's stretched rate, lasts at least ReleaseHold.MinReleaseMs of screen time.
        var owned = Owned();
        Assert.True(owned.Count >= 40, "vacuity guard: too few owned abilities");
        var bad = new List<string>();
        foreach (var (owner, ability) in owned)
        {
            var row = ActorBinding.Default.For(owner);
            var release = AbilityClipTable.ReleaseClip(ability, row.Clips.Act);
            var lengthMs = LengthsMs(row)[release];
            foreach (var woundUp in new[] { false, true })
            {
                var start = AbilityAnim.ResolveStartMs(release, woundUp);
                var shownMs = (lengthMs - start) * 1000 / ReleaseHold.SpeedPermille(start, lengthMs);
                if (shownMs < ReleaseHold.MinReleaseMs - 1)
                {
                    bad.Add(ability + " " + release + " from " + start + " of " + lengthMs + " ms shows " + shownMs);
                }
            }
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    private static readonly Dictionary<string, HashSet<string>> Cache = new(StringComparer.Ordinal);

    private static HashSet<string> Clips(ActorBindingRow row)
    {
        var path = Path.Combine(AssetsRoot(), row.Folder, row.ModelFile!);
        lock (Cache)
        {
            if (!Cache.TryGetValue(path, out var set))
            {
                var data = File.ReadAllBytes(path);
                var len = BitConverter.ToInt32(data, 12);
                using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, len));
                set = doc.RootElement.GetProperty("animations").EnumerateArray()
                    .Select(a => a.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
                Cache[path] = set;
            }
            return set;
        }
    }

    // Each animation's length in ms: the largest keyframe time over its channels' input accessors.
    private static Dictionary<string, int> LengthsMs(ActorBindingRow row)
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsRoot(), row.Folder, row.ModelFile!));
        var len = BitConverter.ToInt32(data, 12);
        using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, len));
        var accessors = doc.RootElement.GetProperty("accessors");
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var a in doc.RootElement.GetProperty("animations").EnumerateArray())
        {
            var samplers = a.GetProperty("samplers");
            var max = 0.0;
            foreach (var ch in a.GetProperty("channels").EnumerateArray())
            {
                var input = samplers[ch.GetProperty("sampler").GetInt32()].GetProperty("input").GetInt32();
                max = Math.Max(max, accessors[input].GetProperty("max")[0].GetDouble());
            }
            map[a.GetProperty("name").GetString()!] = (int)Math.Round(max * 1000.0);
        }
        return map;
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
