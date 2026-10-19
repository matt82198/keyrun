using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The party's model table (design/ART-PLAN.md section 4 and lane 3; <see cref="ActorBinding"/>): each class gets its
/// own KayKit Adventurers body, every scaled body stays at or under the 1.8 m head line the name plates assume
/// (LabelView PlateHeight 2.35 m), and an unknown class falls back to the capsule instead of throwing. The native
/// heights the scales are computed from are re-measured here from the shipped .glb files (bind pose, every node the
/// view keeps visible), so a swapped model or a wrong height constant fails this test, not a frame review.
/// </summary>
public sealed class ActorBindingTests
{
    private static ActorBinding Table => ActorBinding.Default;

    [Theory]
    [InlineData("warden", "Knight.glb")]
    [InlineData("cinder", "Mage.glb")]
    [InlineData("chanter", "Rogue_Hooded.glb")]
    public void Party_class_binds_its_adventurer_model(string classId, string file)
    {
        ActorBindingRow row = Table.For(classId);
        Assert.False(row.CapsuleFallback, classId + " fell back to the capsule");
        Assert.Equal(file, row.ModelFile);
        Assert.Equal(classId, row.Key);
    }

    [Fact]
    public void The_three_party_models_are_pairwise_distinct()
    {
        string[] files = new[] { "warden", "cinder", "chanter" }.Select(c => Table.For(c).ModelFile!).ToArray();
        Assert.Equal(3, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_model_row_stands_at_or_under_the_head_line()
    {
        ActorBindingRow[] models = Table.Rows.Where(r => !r.CapsuleFallback).ToArray();
        Assert.True(models.Length >= 3, "vacuity guard: only " + models.Length + " model rows");
        foreach (ActorBindingRow row in models)
        {
            Assert.True(row.StandingHeightMm <= ActorBinding.HeadLineMm,
                $"{row.Key}: {row.NativeHeightMm} mm x {row.ScalePermille} permille = {row.StandingHeightMm} mm, over the {ActorBinding.HeadLineMm} mm head line");
            Assert.True(row.StandingHeightMm >= 1600,
                $"{row.Key}: {row.StandingHeightMm} mm is too short to read as a party member (scale {row.ScalePermille} permille)");
        }
    }

    [Fact]
    public void The_knight_stands_about_one_point_seven_five_metres()
    {
        int h = Table.For("warden").StandingHeightMm;
        Assert.InRange(h, 1700, 1800);
    }

    [Theory]
    [InlineData("choir.melee")]
    [InlineData("no-such-class")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_class_falls_back_to_the_capsule_without_throwing(string? classId)
    {
        ActorBindingRow row = Table.For(classId);
        Assert.True(row.CapsuleFallback);
        Assert.Null(row.ModelFile);
    }

    [Fact]
    public void A_later_lane_extends_the_table_by_adding_a_row()
    {
        var enemy = new ActorBindingRow("choir.melee", "Skeleton_Warrior.glb", 2400, 700, null, Array.Empty<string>(),
            ActorClips.Default);
        ActorBinding extended = Table.With(enemy);
        Assert.Equal("Skeleton_Warrior.glb", extended.For("choir.melee").ModelFile);
        Assert.True(Table.For("choir.melee").CapsuleFallback, "With must not change the default table");
        Assert.Equal("Knight.glb", extended.For("warden").ModelFile);
        Assert.Throws<ArgumentException>(() => extended.With(enemy));
    }

    [Fact]
    public void Native_heights_match_the_shipped_glb_bind_pose()
    {
        string dir = AssetDir();
        foreach (ActorBindingRow row in Table.Rows.Where(r => !r.CapsuleFallback))
        {
            string path = Path.Combine(dir, row.ModelFile!);
            Assert.True(File.Exists(path), "missing model " + path);
            int measured = GlbBindPose.TopMm(path, row.HiddenNodes);
            Assert.True(Math.Abs(measured - row.NativeHeightMm) <= 5,
                $"{row.Key}: {row.ModelFile} measures {measured} mm in bind pose, the table says {row.NativeHeightMm} mm");
            foreach (string hidden in row.HiddenNodes)
            {
                Assert.True(GlbBindPose.HasNode(path, hidden), $"{row.Key}: hidden node {hidden} is not in {row.ModelFile}");
            }
        }
    }

    [Fact]
    public void Clip_names_exist_in_every_shipped_model()
    {
        string dir = AssetDir();
        foreach (ActorBindingRow row in Table.Rows.Where(r => !r.CapsuleFallback))
        {
            HashSet<string> clips = GlbBindPose.Animations(Path.Combine(dir, row.ModelFile!));
            foreach (string clip in new[] { row.Clips.Idle, row.Clips.Move, row.Clips.Cast, row.Clips.Act, row.Clips.Hit, row.Clips.Death })
            {
                Assert.True(clips.Contains(clip), $"{row.Key}: clip {clip} is not in {row.ModelFile}");
            }
        }
    }

    private static string AssetDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            string candidate = Path.Combine(d.FullName, "game", "assets", "kaykit-adventurers");
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(d.FullName, "assets", "kaykit-adventurers");
            if (Directory.Exists(candidate)) return candidate;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("game/assets/kaykit-adventurers not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Reads a .glb's JSON chunk and computes the bind-pose top (max y) of every visible mesh node.</summary>
    private static class GlbBindPose
    {
        private static JsonDocument Read(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            Assert.Equal(0x46546C67u, BitConverter.ToUInt32(data, 0)); // "glTF"
            int len = BitConverter.ToInt32(data, 12);
            return JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, len));
        }

        public static bool HasNode(string path, string name)
        {
            using JsonDocument doc = Read(path);
            return doc.RootElement.GetProperty("nodes").EnumerateArray()
                .Any(n => n.TryGetProperty("name", out JsonElement v) && v.GetString() == name);
        }

        public static HashSet<string> Animations(string path)
        {
            using JsonDocument doc = Read(path);
            return doc.RootElement.GetProperty("animations").EnumerateArray()
                .Select(a => a.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
        }

        public static int TopMm(string path, IReadOnlyList<string> hidden)
        {
            using JsonDocument doc = Read(path);
            JsonElement root = doc.RootElement;
            JsonElement[] nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
            var world = new double[nodes.Length][];
            int scene = root.TryGetProperty("scene", out JsonElement s) ? s.GetInt32() : 0;
            foreach (JsonElement r in root.GetProperty("scenes")[scene].GetProperty("nodes").EnumerateArray())
            {
                Walk(nodes, world, r.GetInt32(), Identity());
            }
            double top = double.MinValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                JsonElement n = nodes[i];
                if (!n.TryGetProperty("mesh", out JsonElement meshIdx)) continue;
                string name = n.TryGetProperty("name", out JsonElement nm) ? nm.GetString() ?? "" : "";
                if (hidden.Contains(name)) continue;
                double[] m = n.TryGetProperty("skin", out _) ? Identity() : world[i]; // KayKit skins bind in mesh space
                foreach (JsonElement prim in root.GetProperty("meshes")[meshIdx.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    JsonElement acc = root.GetProperty("accessors")[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                    double[] lo = acc.GetProperty("min").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                    double[] hi = acc.GetProperty("max").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                    foreach (double x in new[] { lo[0], hi[0] })
                        foreach (double y in new[] { lo[1], hi[1] })
                            foreach (double z in new[] { lo[2], hi[2] })
                                top = Math.Max(top, m[4] * x + m[5] * y + m[6] * z + m[7]);
                }
            }
            return (int)Math.Round(top * 1000);
        }

        private static void Walk(JsonElement[] nodes, double[][] world, int i, double[] parent)
        {
            world[i] = Mul(parent, Local(nodes[i]));
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
            {
                foreach (JsonElement k in kids.EnumerateArray()) Walk(nodes, world, k.GetInt32(), world[i]);
            }
        }

        private static double[] Identity() => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        // Row-major 4x4.
        private static double[] Mul(double[] a, double[] b)
        {
            var r = new double[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    for (int k = 0; k < 4; k++)
                        r[i * 4 + j] += a[i * 4 + k] * b[k * 4 + j];
            return r;
        }

        private static double[] Vec(JsonElement n, string prop, double[] dflt) =>
            n.TryGetProperty(prop, out JsonElement v) ? v.EnumerateArray().Select(e => e.GetDouble()).ToArray() : dflt;

        private static double[] Local(JsonElement n)
        {
            if (n.TryGetProperty("matrix", out JsonElement mm))
            {
                double[] c = mm.EnumerateArray().Select(e => e.GetDouble()).ToArray(); // column-major
                var r = new double[16];
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 4; col++)
                        r[row * 4 + col] = c[col * 4 + row];
                return r;
            }
            double[] t = Vec(n, "translation", new double[] { 0, 0, 0 });
            double[] q = Vec(n, "rotation", new double[] { 0, 0, 0, 1 });
            double[] sc = Vec(n, "scale", new double[] { 1, 1, 1 });
            double qx = q[0], qy = q[1], qz = q[2], qw = q[3];
            return new[]
            {
                (1 - 2 * (qy * qy + qz * qz)) * sc[0], 2 * (qx * qy - qz * qw) * sc[1], 2 * (qx * qz + qy * qw) * sc[2], t[0],
                2 * (qx * qy + qz * qw) * sc[0], (1 - 2 * (qx * qx + qz * qz)) * sc[1], 2 * (qy * qz - qx * qw) * sc[2], t[1],
                2 * (qx * qz - qy * qw) * sc[0], 2 * (qy * qz + qx * qw) * sc[1], (1 - 2 * (qx * qx + qy * qy)) * sc[2], t[2],
                0, 0, 0, 1,
            };
        }
    }
}
