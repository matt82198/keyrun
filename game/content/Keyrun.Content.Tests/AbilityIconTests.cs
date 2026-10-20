using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The ability bar icon table (lane ABILITY-ICONS; <see cref="AbilityIcons"/>): every ability any playable class puts on
/// its nine-slot bar has an icon row, so a new kit ability cannot ship as a bare text slot; every row points at a real
/// 64 px PNG in the vendored CC0 pack folder, beside the pack's own licence; every row names a real player ability; and
/// an id with no row resolves to null (the bar keeps the name text) instead of throwing.
/// </summary>
public sealed class AbilityIconTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // The nine bar slots of a class kit, in ability-bar order (HostRunSetup.ResolveSlots).
    private static IEnumerable<(string Slot, string AbilityId)> KitSlots(ClassDef c)
    {
        ClassAbilitySlots a = c.Abilities;
        yield return ("1", a.Rot1);
        yield return ("2", a.Rot2);
        yield return ("3", a.Rot3);
        yield return ("4", a.Rot4);
        yield return ("5", a.Rot5);
        yield return ("Q", a.Interrupt);
        yield return ("E", a.Defensive);
        yield return ("R", a.Utility);
        yield return ("F", a.Movement);
    }

    private static List<(string Class, string Slot, string AbilityId)> AllKitSlots() =>
        Reg.Classes.Values.SelectMany(c => KitSlots(c).Select(s => (c.Id, s.Slot, s.AbilityId))).ToList();

    [Fact]
    public void Every_bar_slot_of_every_playable_class_has_an_icon()
    {
        var slots = AllKitSlots();
        Assert.True(slots.Count == 27, "vacuity guard: want 3 classes x 9 slots, got " + slots.Count);
        var missing = slots.Where(s => AbilityIcons.Default.ResPathFor(s.AbilityId) is null)
            .Select(s => s.Class + " slot " + s.Slot + " " + s.AbilityId).ToList();
        Assert.True(missing.Count == 0, "ability bar slots with no icon row: " + string.Join(", ", missing));
    }

    [Fact]
    public void A_new_kit_ability_without_a_row_is_caught()
    {
        // The guard above must redden on a kit ability the table does not know: drop one row and re-check.
        var rows = AbilityIcons.Default.Rows.Where(kv => kv.Key != "warden.strike").ToDictionary(kv => kv.Key, kv => kv.Value);
        var table = new AbilityIcons(rows);
        Assert.Null(table.ResPathFor("warden.strike"));
        Assert.Contains(AllKitSlots(), s => table.ResPathFor(s.AbilityId) is null);
    }

    [Fact]
    public void Every_icon_row_names_a_real_player_ability()
    {
        var kit = AllKitSlots().Select(s => s.AbilityId).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(AbilityIcons.Default.Rows);
        foreach (string id in AbilityIcons.Default.Rows.Keys)
        {
            Assert.True(Reg.Abilities.ContainsKey(id), "icon row for unknown ability " + id);
            Assert.True(kit.Contains(id), "icon row for " + id + ", which is on no class bar");
        }
    }

    [Fact]
    public void Every_icon_file_is_a_shipped_64px_png_beside_the_pack_licence()
    {
        string dir = Path.Combine(AssetsRoot(), AbilityIcons.Folder);
        Assert.True(File.Exists(Path.Combine(dir, "LICENSE.txt")), "no LICENSE.txt in " + dir);
        Assert.Contains("CC0", File.ReadAllText(Path.Combine(dir, "LICENSE.txt")), StringComparison.Ordinal);
        Assert.NotEmpty(AbilityIcons.Default.Rows);
        foreach (var (id, file) in AbilityIcons.Default.Rows)
        {
            string path = Path.Combine(dir, file);
            Assert.True(File.Exists(path), id + " -> missing icon file " + path);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 24 && bytes.Take(8).SequenceEqual(PngSignature), id + " -> " + file + " is not a PNG");
            // IHDR width and height, big-endian, at bytes 16..23.
            int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            Assert.True(w == AbilityIcons.SizePx && h == AbilityIcons.SizePx, id + " -> " + file + " is " + w + "x" + h);
            Assert.Equal(AbilityIcons.Dir + "/" + file, AbilityIcons.Default.ResPathFor(id));
        }
    }

    [Fact]
    public void Each_class_bar_shows_nine_distinct_icons()
    {
        foreach (ClassDef c in Reg.Classes.Values)
        {
            var files = KitSlots(c).Select(s => AbilityIcons.Default.FileFor(s.AbilityId)).ToList();
            Assert.DoesNotContain(null, files);
            Assert.True(files.Distinct(StringComparer.Ordinal).Count() == 9, c.Id + " bar repeats an icon: " + string.Join(",", files));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("test.unbound")]
    [InlineData("choir.melee.swing")]
    public void An_id_without_a_row_resolves_to_null(string? id)
    {
        Assert.Null(AbilityIcons.Default.FileFor(id));
        Assert.Null(AbilityIcons.Default.ResPathFor(id));
    }

    // game/assets (the folder AbilityIcons.Folder is relative to), found above the test binary.
    private static string AssetsRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            string candidate = Path.Combine(d.FullName, "game", "assets");
            if (File.Exists(Path.Combine(candidate, "ATTRIBUTION.md")))
            {
                return candidate;
            }
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("game/assets not found above " + AppContext.BaseDirectory);
    }
}
