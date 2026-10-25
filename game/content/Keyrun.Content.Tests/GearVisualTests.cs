using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Gear marks ride the character's bones (Matt item 11, R-VIEW-HEAD-1): a mark parented to the ActorView root is left
/// behind by the run animation's head and torso bob, so a box floats at the Warden's head. Every mark definition must
/// name a skeleton bone it attaches to. GearVisual.cs lives in game/src/View (the engine assembly, not headless-testable),
/// so S2 moves the definitions into the pure <c>GearMarkDefs</c> / <c>GearMarkDef</c> (game/content/Keyrun.Content/Model/,
/// namespace Keyrun.Content) and GearVisual reads them. Bone names are from the shipped KayKit Adventurers .glb (Knight,
/// Mage, Rogue_Hooded all carry: head, chest, spine, hips, handslot.r, hand.r).
/// </summary>
public sealed class GearVisualTests
{
    private static readonly string[] Models = { "Knight.glb", "Mage.glb", "Rogue_Hooded.glb" };

    [Fact]
    public void GearVisual_AttachesToBones_NotRoot()
    {
        var defs = GearMarkDefs.All;
        Assert.Equal(new[] { "weapon", "armor", "charm", "ring", "keystone" }, defs.Select(d => d.Slot).ToArray());
        foreach (GearMarkDef def in defs)
        {
            Assert.False(string.IsNullOrWhiteSpace(def.Bone), def.Slot + " names no bone (would ride the root)");
            foreach (string model in Models)
            {
                Assert.True(HasNode(model, def.Bone), $"{def.Slot}: bone '{def.Bone}' is not in {model}");
            }
        }
        Assert.Equal("head", defs.Single(d => d.Slot == "charm").Bone);
        Assert.Contains(defs.Single(d => d.Slot == "armor").Bone, new[] { "chest", "spine" });
    }

    private static bool HasNode(string model, string name)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AssetsRoot(), "kaykit-adventurers", model));
        int len = BitConverter.ToInt32(data, 12);
        using JsonDocument doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, len));
        return doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Any(n => n.TryGetProperty("name", out JsonElement v) && v.GetString() == name);
    }

    private static string AssetsRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            string c = Path.Combine(d.FullName, "game", "assets");
            if (Directory.Exists(Path.Combine(c, "kaykit-adventurers"))) return c;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("game/assets/kaykit-adventurers not found above " + AppContext.BaseDirectory);
    }
}
