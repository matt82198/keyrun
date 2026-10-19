using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The actor model table (design/ART-PLAN.md section 4, lanes 3 and 4; <see cref="ActorBinding"/>): each party class
/// gets its own KayKit Adventurers body, each enemy class a KayKit Skeletons body (the boss the Knight at 1.9x), every
/// scaled body lands within 2 percent of its row's target height, every name plate sits above its body, and an unknown
/// class falls back to the capsule instead of throwing. The native heights the scales are computed from are re-measured
/// here from the shipped .glb files (bind pose, every node the view keeps visible), so a swapped model or a wrong height
/// constant fails this test, not a frame review.
/// </summary>
public sealed class ActorBindingTests
{
    private const string Adventurers = "kaykit-adventurers";
    private const string Skeletons = "kaykit-skeletons";

    // A class id no lane binds, for the fallback and extension cases.
    private const string Unbound = "test.unbound";

    private static readonly string[] Party = { "warden", "cinder", "chanter" };

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
        Assert.Equal(Adventurers, row.Folder);
        Assert.Equal(classId, row.Key);
    }

    [Fact]
    public void The_three_party_models_are_pairwise_distinct()
    {
        string[] files = Party.Select(c => Table.For(c).ModelFile!).ToArray();
        Assert.Equal(3, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("choir.melee", Skeletons, "Skeleton_Warrior.glb", 1800)]
    [InlineData("choir.lieutenant", Skeletons, "Skeleton_Mage.glb", 1800)]
    [InlineData("pair.melee", Skeletons, "Skeleton_Minion.glb", 1800)]
    [InlineData("seed.elite", Skeletons, "Skeleton_Rogue.glb", 2340)]
    [InlineData("hollowWarden.twinA", Skeletons, "Skeleton_Warrior.glb", 2070)]
    [InlineData("hollowWarden.twinB", Skeletons, "Skeleton_Warrior.glb", 2070)]
    [InlineData("hollowWarden.boss", Adventurers, "Knight.glb", 4687)]
    public void Enemy_class_binds_its_model_at_its_target_height(string classId, string folder, string file, int targetMm)
    {
        ActorBindingRow row = Table.For(classId);
        Assert.False(row.CapsuleFallback, classId + " fell back to the capsule");
        Assert.Equal(folder, row.Folder);
        Assert.Equal(file, row.ModelFile);
        Assert.Equal(targetMm, row.TargetHeightMm);
        Assert.Equal(ActorBinding.AssetsRoot + "/" + folder + "/" + file, row.ResPath);
    }

    [Fact]
    public void Every_enemy_in_content_data_has_a_model_row()
    {
        List<string> ids = EnemyIds();
        Assert.True(ids.Count >= 7, "vacuity guard: only " + ids.Count + " enemy ids in content/data/enemies");
        foreach (string id in ids)
        {
            Assert.False(Table.For(id).CapsuleFallback, $"enemy {id} has no model row and would draw as a capsule");
        }
    }

    [Fact]
    public void Every_model_row_file_exists_under_game_assets()
    {
        string root = AssetsRoot();
        foreach (ActorBindingRow row in Table.Rows.Where(r => !r.CapsuleFallback))
        {
            string path = Path.Combine(root, row.Folder, row.ModelFile!);
            Assert.True(File.Exists(path), $"{row.Key}: missing model {path}");
        }
    }

    [Fact]
    public void Every_model_row_stands_within_two_percent_of_its_target()
    {
        ActorBindingRow[] models = Table.Rows.Where(r => !r.CapsuleFallback).ToArray();
        Assert.True(models.Length >= 10, "vacuity guard: only " + models.Length + " model rows");
        foreach (ActorBindingRow row in models)
        {
            long off = Math.Abs((long)row.StandingHeightMm - row.TargetHeightMm);
            Assert.True(off * 50 <= row.TargetHeightMm,
                $"{row.Key}: {row.NativeHeightMm} mm x {row.ScalePermille} permille = {row.StandingHeightMm} mm, more than 2 percent from its {row.TargetHeightMm} mm target");
        }
    }

    [Fact]
    public void Party_rows_stand_at_or_under_the_head_line()
    {
        foreach (string key in Party)
        {
            ActorBindingRow row = Table.For(key);
            Assert.True(row.StandingHeightMm <= ActorBinding.HeadLineMm,
                $"{row.Key}: {row.StandingHeightMm} mm, over the {ActorBinding.HeadLineMm} mm head line");
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

    [Fact]
    public void The_boss_is_the_knight_at_one_point_nine_times_its_native_height()
    {
        ActorBindingRow knight = Table.For("warden");
        ActorBindingRow boss = Table.For("hollowWarden.boss");
        Assert.Equal(knight.Folder, boss.Folder);
        Assert.Equal(knight.ModelFile, boss.ModelFile);
        Assert.Equal(knight.NativeHeightMm * 19 / 10, boss.TargetHeightMm);
        Assert.NotNull(boss.TintRgb); // the hollow, darkened Knight (ART-PLAN section 4)
    }

    [Fact]
    public void Role_sizes_follow_art_plan_section_4()
    {
        Assert.Equal(ActorBinding.HeadLineMm * 13 / 10, Table.For("seed.elite").TargetHeightMm);
        Assert.Equal(ActorBinding.HeadLineMm * 115 / 100, Table.For("hollowWarden.twinA").TargetHeightMm);
        Assert.Equal(ActorBinding.HeadLineMm * 115 / 100, Table.For("hollowWarden.twinB").TargetHeightMm);
        Assert.Equal(ActorBinding.HeadLineMm, ActorBinding.Capsule.TargetHeightMm);
    }

    [Fact]
    public void The_twins_are_told_apart_by_tint()
    {
        int? a = Table.For("hollowWarden.twinA").TintRgb;
        int? b = Table.For("hollowWarden.twinB").TintRgb;
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Every_name_plate_sits_above_its_body()
    {
        foreach (ActorBindingRow row in Table.Rows.Append(ActorBinding.Capsule))
        {
            int body = row.CapsuleFallback ? row.TargetHeightMm : Math.Max(row.TargetHeightMm, row.StandingHeightMm);
            Assert.True(row.PlateHeightMm >= body + PlateRule.ClearanceMm,
                $"{row.Key}: plate at {row.PlateHeightMm} mm, body top {body} mm, clearance under {PlateRule.ClearanceMm} mm");
        }
        // PLATE-HEIGHT: a 1.8 m body's plate sits 250 mm over its head (2.05 m, was 2.35 m), the boss's under the 5 m cap.
        Assert.Equal(2050, ActorBinding.Capsule.PlateHeightMm);
        Assert.Equal(2050, Table.For("choir.melee").PlateHeightMm);
        Assert.Equal(4937, Table.For("hollowWarden.boss").PlateHeightMm);
    }

    [Fact]
    public void Enemy_rows_rise_with_a_spawn_clip_and_party_rows_have_none()
    {
        foreach (string id in EnemyIds().Where(i => i != "hollowWarden.boss"))
        {
            Assert.False(string.IsNullOrEmpty(Table.For(id).Clips.Spawn), id + " has no spawn clip");
        }
        foreach (string key in Party)
        {
            Assert.Null(Table.For(key).Clips.Spawn);
        }
    }

    [Theory]
    [InlineData(Unbound)]
    [InlineData("no-such-class")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_class_falls_back_to_the_capsule_without_throwing(string? classId)
    {
        ActorBindingRow row = Table.For(classId);
        Assert.True(row.CapsuleFallback);
        Assert.Null(row.ModelFile);
        Assert.Null(row.ResPath);
    }

    [Fact]
    public void A_later_lane_extends_the_table_by_adding_a_row()
    {
        var extra = new ActorBindingRow(Unbound, "Skeleton_Warrior.glb", 2400, 700, null, Array.Empty<string>(),
            ActorClips.Default, Folder: Skeletons);
        ActorBinding extended = Table.With(extra);
        Assert.Equal("Skeleton_Warrior.glb", extended.For(Unbound).ModelFile);
        Assert.True(Table.For(Unbound).CapsuleFallback, "With must not change the default table");
        Assert.Equal("Knight.glb", extended.For("warden").ModelFile);
        Assert.Throws<ArgumentException>(() => extended.With(extra));
    }

    [Fact]
    public void Native_heights_match_the_shipped_glb_bind_pose()
    {
        string root = AssetsRoot();
        foreach (ActorBindingRow row in Table.Rows.Where(r => !r.CapsuleFallback))
        {
            string path = Path.Combine(root, row.Folder, row.ModelFile!);
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
        string root = AssetsRoot();
        foreach (ActorBindingRow row in Table.Rows.Where(r => !r.CapsuleFallback))
        {
            HashSet<string> clips = GlbBindPose.Animations(Path.Combine(root, row.Folder, row.ModelFile!));
            var wanted = new List<string> { row.Clips.Idle, row.Clips.Move, row.Clips.Cast, row.Clips.Act, row.Clips.Hit, row.Clips.Death };
            if (row.Clips.Spawn is { } spawn) wanted.Add(spawn);
            foreach (string clip in wanted)
            {
                Assert.True(clips.Contains(clip), $"{row.Key}: clip {clip} is not in {row.ModelFile}");
            }
        }
    }

    // game/assets, the folder every row's Folder is relative to (kaykit-adventurers and kaykit-skeletons both).
    private static string AssetsRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            foreach (string candidate in new[] { Path.Combine(d.FullName, "game", "assets"), Path.Combine(d.FullName, "assets") })
            {
                if (Directory.Exists(Path.Combine(candidate, Adventurers)) && Directory.Exists(Path.Combine(candidate, Skeletons)))
                {
                    return candidate;
                }
            }
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("game/assets with kaykit-adventurers and kaykit-skeletons not found above " + AppContext.BaseDirectory);
    }

    // Every enemy id in the repo-root content/data/enemies/*.json (each file is a JSON array of enemy objects).
    private static List<string> EnemyIds()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            string dir = Path.Combine(d.FullName, "content", "data", "enemies");
            if (Directory.Exists(dir))
            {
                var ids = new List<string>();
                foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                    foreach (JsonElement e in doc.RootElement.EnumerateArray())
                    {
                        ids.Add(e.GetProperty("id").GetString()!);
                    }
                }
                return ids;
            }
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("content/data/enemies not found above " + AppContext.BaseDirectory);
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
