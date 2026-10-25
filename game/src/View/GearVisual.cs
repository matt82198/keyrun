using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;

namespace Keyrun.View;

/// <summary>The drawn shape of one gear mark on the party model.</summary>
public enum GearShape
{
    /// <summary>A box; the size is its full extent on each axis, metres.</summary>
    Box,

    /// <summary>A sphere; <see cref="GearMark.SizeX"/> is its diameter in metres (SizeY and SizeZ are 0).</summary>
    Sphere,
}

/// <summary>
/// One drawn mark for an equipped item. Positions and sizes are metres in the named bone's frame (the model's frame: X
/// lateral, Y up, Z forward), so the mark rides the skeleton and is independent of the model's own scale. <see cref="Rgb"/> is the tier band colour, 0xRRGGBB.
/// </summary>
public readonly record struct GearMark(
    string Slot,
    string Bone,
    int Tier,
    GearShape Shape,
    float PosX,
    float PosY,
    float PosZ,
    float SizeX,
    float SizeY,
    float SizeZ,
    int Rgb);

/// <summary>
/// The pure rule that maps equipped gear to what the party model shows (keyrun MVP U6, Dark and Darker way: the gear
/// you equip is the gear you see). Each of the five slots has one fixed mark drawn from primitive meshes, so no binary
/// asset is needed (D17), and every mark is tinted by its item's tier, 1 (grey) to 10 (gold). Godot-free: the View builds
/// the nodes from these values (<see cref="ActorView"/>), and the GearVisualSceneTest reads the marks back from the run.
/// </summary>
public static class GearVisual
{
    /// <summary>The lowest tier a mark can show.</summary>
    public const int MinTier = 1;

    /// <summary>The highest tier a mark can show (Ash.MaxTier).</summary>
    public const int MaxTier = 10;

    /// <summary>The five gear slots, in the order the plan draws them (Rewards.GearSlots).</summary>
    public static readonly IReadOnlyList<string> Slots = new[] { "weapon", "armor", "charm", "ring", "keystone" };

    /// <summary>The tier clamped to <see cref="MinTier"/>..<see cref="MaxTier"/>.</summary>
    public static int ClampTier(int tier) => Math.Clamp(tier, MinTier, MaxTier);

    /// <summary>The tier band colour, 0xRRGGBB: a grey at tier 1 easing to a gold at tier 10.</summary>
    public static int TierRgb(int tier)
    {
        var t = (ClampTier(tier) - MinTier) / (float)(MaxTier - MinTier);
        var r = Band(0x9A, 0xFF, t);
        var g = Band(0x9A, 0xCC, t);
        var b = Band(0x9A, 0x40, t);
        return (r << 16) | (g << 8) | b;
    }

    /// <summary>The one mark for an item in <paramref name="slot"/> at <paramref name="tier"/>, or null for an unknown slot.</summary>
    public static GearMark? Mark(string slot, int tier)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var rgb = TierRgb(tier);
        var clamped = ClampTier(tier);
        var def = GearMarkDefs.All.FirstOrDefault(d => d.Slot == slot);
        if (def is null)
        {
            return null;
        }
        return new GearMark(
            slot, def.Bone, clamped, def.Sphere ? GearShape.Sphere : GearShape.Box,
            def.OffXMm / 1000f, def.OffYMm / 1000f, def.OffZMm / 1000f,
            def.SizeXMm / 1000f, def.SizeYMm / 1000f, def.SizeZMm / 1000f, rgb);
    }

    /// <summary>
    /// The plan for the equipped gear: one mark per equipped slot, in <see cref="Slots"/> order. An empty slot draws
    /// nothing, and a slot name outside the five is ignored.
    /// </summary>
    /// <param name="tierBySlot">The equipped item's tier by slot name.</param>
    public static IReadOnlyList<GearMark> Plan(IReadOnlyDictionary<string, int> tierBySlot)
    {
        ArgumentNullException.ThrowIfNull(tierBySlot);
        var marks = new List<GearMark>();
        foreach (var slot in Slots)
        {
            if (tierBySlot.TryGetValue(slot, out var tier) && Mark(slot, tier) is { } mark)
            {
                marks.Add(mark);
            }
        }
        return marks;
    }

    private static int Band(int from, int to, float t) => (int)Math.Round(from + ((to - from) * t));
}
