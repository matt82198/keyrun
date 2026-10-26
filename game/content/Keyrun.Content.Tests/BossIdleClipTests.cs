using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Ruling (a) of event seq 376: the boss plays its idle clip while it waits for the party. Lane S5 capture of main 4135f4fc
/// logged the Hollow Warden (actor 900) static at (93500,0) for 3431 ticks (171.6 s) with no clip line before its engage at
/// tick 3434. This file pins the data half of the contract, Godot-free: the boss row's idle clip resolves inside the shipped
/// Knight.glb by the view's own match rule (ActorAnimator.Match: exact, then "/name" suffix, then case-insensitive
/// substring), the clip has keyframe motion (a flat clip would read as a frozen boss), and a boss standing still before
/// engage decides the Idle clip, never a one-shot. The view half (the animator starting the loop and logging it) runs
/// only inside Godot and is proven by the S5 capture, not here.
/// </summary>
public sealed class BossIdleClipTests
{
    private const string BossKey = "hollowWarden.boss";

    private static ActorBindingRow Boss => ActorBinding.Default.For(BossKey);

    [Fact]
    public void The_boss_row_is_the_knight_with_an_idle_clip()
    {
        Assert.False(Boss.CapsuleFallback, "the boss fell back to the capsule");
        Assert.Equal("Knight.glb", Boss.ModelFile);
        Assert.False(string.IsNullOrWhiteSpace(Boss.Clips.Idle), "the boss row names no idle clip");
    }

    [Fact]
    public void The_boss_idle_clip_resolves_inside_the_shipped_knight_model()
    {
        IReadOnlyList<string> names = Animations(Path.Combine(AssetsRoot(), Boss.Folder, Boss.ModelFile!));
        string? resolved = ViewMatch(names, Boss.Clips.Idle);
        Assert.True(resolved is not null, $"idle clip '{Boss.Clips.Idle}' resolves to no animation in {Boss.ModelFile}");
    }

    [Fact]
    public void The_boss_idle_clip_has_keyframe_motion()
    {
        string path = Path.Combine(AssetsRoot(), Boss.Folder, Boss.ModelFile!);
        string? resolved = ViewMatch(Animations(path), Boss.Clips.Idle);
        Assert.NotNull(resolved);
        float spread = MaxKeyframeSpread(path, resolved!);
        // Vacuity guard: the walk clip of the same model must move, so the measure itself is not reading flat data.
        float walk = MaxKeyframeSpread(path, Boss.Clips.Move);
        Assert.True(walk > 0.5f, $"vacuity guard: the move clip {Boss.Clips.Move} spans only {walk}");
        Assert.True(spread > 0.01f, $"idle clip '{resolved}' spans {spread} units over all keyframes: a frozen boss");
    }

    [Fact]
    public void A_standing_boss_before_engage_decides_the_idle_clip_not_a_one_shot()
    {
        // Before engage the boss is alive, not moving, not casting, not hurt, with no clip playing (the Adventurer
        // Knight row has no spawn clip, so the animator starts in Idle).
        var decision = AnimRule.Decide(new AnimFacts(
            Alive: true, NewTick: true, Moving: false, Casting: false, Acted: false, Hurt: false, Staggered: false,
            Playing: AnimClip.None));
        Assert.Equal(AnimClip.Idle, decision.Clip);
        Assert.False(decision.OneShot);
    }

    [Fact]
    public void The_boss_has_no_spawn_clip_so_it_starts_in_idle()
    {
        Assert.Null(Boss.Clips.Spawn);
    }

    // The view's match rule (src/View/ActorAnimator.cs Match): exact or "/name" suffix first, then the first name that
    // contains the wanted text case-insensitively. Copied here because the view itself needs a Godot AnimationPlayer.
    private static string? ViewMatch(IReadOnlyList<string> names, string wanted)
    {
        foreach (string n in names)
        {
            if (n == wanted || n.EndsWith("/" + wanted, StringComparison.Ordinal))
            {
                return n;
            }
        }
        foreach (string n in names)
        {
            if (n.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return n;
            }
        }
        return null;
    }

    private static IReadOnlyList<string> Animations(string path)
    {
        using JsonDocument doc = ReadJson(path);
        return doc.RootElement.GetProperty("animations").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString()!).ToList();
    }

    // Largest (max - min) over every translation and rotation channel's keyframes of one named animation.
    private static float MaxKeyframeSpread(string path, string clip)
    {
        (JsonElement root, byte[] bin) = ReadGlb(path);
        JsonElement anim = root.GetProperty("animations").EnumerateArray()
            .First(a => a.GetProperty("name").GetString() == clip);
        JsonElement[] samplers = anim.GetProperty("samplers").EnumerateArray().ToArray();
        float max = 0f;
        foreach (JsonElement ch in anim.GetProperty("channels").EnumerateArray())
        {
            string target = ch.GetProperty("target").GetProperty("path").GetString()!;
            if (target is not ("translation" or "rotation"))
            {
                continue;
            }
            JsonElement sampler = samplers[ch.GetProperty("sampler").GetInt32()];
            float[] values = ReadFloats(root, bin, sampler.GetProperty("output").GetInt32(), out int comps);
            for (int c = 0; c < comps; c++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = c; i < values.Length; i += comps)
                {
                    lo = Math.Min(lo, values[i]);
                    hi = Math.Max(hi, values[i]);
                }
                if (values.Length > c)
                {
                    max = Math.Max(max, hi - lo);
                }
            }
        }
        return max;
    }

    private static float[] ReadFloats(JsonElement root, byte[] bin, int accessorIndex, out int comps)
    {
        JsonElement accessor = root.GetProperty("accessors")[accessorIndex];
        JsonElement view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        int count = accessor.GetProperty("count").GetInt32();
        comps = accessor.GetProperty("type").GetString() switch
        {
            "SCALAR" => 1,
            "VEC3" => 3,
            "VEC4" => 4,
            var other => throw new InvalidDataException("unsupported accessor type " + other),
        };
        int start = (view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0)
            + (accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
        var floats = new float[count * comps];
        Buffer.BlockCopy(bin, start, floats, 0, floats.Length * sizeof(float));
        return floats;
    }

    private static (JsonElement Root, byte[] Bin) ReadGlb(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        int jsonLen = BitConverter.ToInt32(data, 12);
        int binStart = 20 + jsonLen;
        int binLen = BitConverter.ToInt32(data, binStart);
        byte[] bin = data.AsSpan(binStart + 8, binLen).ToArray();
        JsonDocument doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, jsonLen));
        return (doc.RootElement.Clone(), bin);
    }

    private static JsonDocument ReadJson(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        int len = BitConverter.ToInt32(data, 12);
        return JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, len));
    }

    // game/assets, the folder every row's Folder is relative to (kaykit-adventurers holds the Knight).
    private static string AssetsRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            foreach (string candidate in new[] { Path.Combine(d.FullName, "game", "assets"), Path.Combine(d.FullName, "assets") })
            {
                if (Directory.Exists(Path.Combine(candidate, ActorBinding.AdventurersFolder)))
                {
                    return candidate;
                }
            }
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("game/assets with kaykit-adventurers not found above " + AppContext.BaseDirectory);
    }
}
