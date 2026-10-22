using System;
using System.Collections.Generic;
using System.Linq;
using PixelRect = Keyrun.Content.PixelRect;

namespace Keyrun.Hud;

/// <summary>
/// One living or dead enemy as the idle-pack label rule reads it (Godot-free): its id, display name, class (the
/// lieutenant is the pack leader), its projected anchor in viewport pixels, and the pack it belongs to (null for a
/// roster actor outside any pack).
/// </summary>
/// <param name="Id">The actor id.</param>
/// <param name="Name">The display name ("Choir Cantor").</param>
/// <param name="ClassId">The actor's class id (".lieutenant" marks the pack leader).</param>
/// <param name="Alive">Whether the actor is alive this frame.</param>
/// <param name="X">The projected anchor, viewport pixels.</param>
/// <param name="Y">The projected anchor, viewport pixels.</param>
/// <param name="PackId">The pack the actor belongs to, or null.</param>
public readonly record struct IdlePackMember(int Id, string Name, string ClassId, bool Alive, int X, int Y, string? PackId);

/// <summary>
/// One name label the rule draws (Godot-free). An idle pack draws one label (<see cref="Chevron"/> set, the pack's text,
/// anchored at the centre of its members, <see cref="LeaderId"/> the lieutenant whose threat colour it keeps); every other
/// label names one actor (<see cref="ActorIds"/> holds that one id).
/// </summary>
/// <param name="Text">The label text.</param>
/// <param name="X">The anchor, viewport pixels.</param>
/// <param name="Y">The anchor, viewport pixels.</param>
/// <param name="Chevron">True for an idle pack's label (the chevron glyph beside the colour, BIBLE s3).</param>
/// <param name="LeaderId">The actor whose threat colour the label keeps (the lieutenant, or the sole actor).</param>
/// <param name="ActorIds">The living actors the label stands for, roster order.</param>
/// <param name="PackId">The pack the label belongs to, or null.</param>
public sealed record IdlePackLabel(string Text, int X, int Y, bool Chevron, int LeaderId, IReadOnlyList<int> ActorIds, string? PackId);

/// <summary>
/// The idle-pack name label rule (finding f-1bf2dace, Matt 2026-10-21 vertical capture: five Choir labels piled at the
/// bottom edge while the pack stood idle). WoW draws no separate nameplate per far, non-engaged enemy; here a pack the party
/// has not engaged draws ONE label at its centre, with the pack's display name or, when packs carry none, the leader's
/// name and "+N" for the other living members. An engaged pack (its group in <c>EncounterState.Engaged</c>) keeps one label
/// per living member. A label whose box would leave the viewport is hidden, never clamped into a pile.
/// </summary>
public static class IdlePackLabels
{
    /// <summary>The padding around a name, pixels (the same plate padding LabelView draws with).</summary>
    public const int PlatePad = 4;

    private const string LieutenantSuffix = ".lieutenant";

    /// <summary>
    /// The labels this frame. Dead members draw nothing and count for nothing; a member outside any pack, or in an engaged
    /// pack, draws its own label; an idle pack draws one centroid label; any label whose box leaves the viewport is dropped.
    /// </summary>
    /// <param name="members">The enemies and party members the frame projects, in roster order.</param>
    /// <param name="engagedPacks">The pack ids that have engaged (their members draw one label each).</param>
    /// <param name="packNames">The pack display names (an empty map on main: packs carry no display name yet).</param>
    /// <param name="width">The viewport width, pixels.</param>
    /// <param name="height">The viewport height, pixels.</param>
    /// <param name="textWidth">The rendered width of a label text, pixels.</param>
    /// <param name="fontSize">The label font size, pixels (the box height is this plus the padding).</param>
    /// <returns>The labels to draw, in roster order of their first member, each inside the viewport.</returns>
    public static IReadOnlyList<IdlePackLabel> Plan(
        IReadOnlyList<IdlePackMember> members,
        IReadOnlySet<string> engagedPacks,
        IReadOnlyDictionary<string, string> packNames,
        int width,
        int height,
        Func<string, int> textWidth,
        int fontSize)
    {
        var byPack = new Dictionary<string, List<IdlePackMember>>(StringComparer.Ordinal);
        foreach (var m in members)
        {
            if (m.Alive && m.PackId is { } pack)
            {
                if (!byPack.TryGetValue(pack, out var list))
                {
                    list = new List<IdlePackMember>();
                    byPack[pack] = list;
                }
                list.Add(m);
            }
        }

        var result = new List<IdlePackLabel>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in members)
        {
            if (!m.Alive)
            {
                continue;
            }
            if (m.PackId is not { } packId || engagedPacks.Contains(packId))
            {
                AddIfInside(result, new IdlePackLabel(m.Name, m.X, m.Y, false, m.Id, new[] { m.Id }, m.PackId),
                    width, height, textWidth, fontSize);
                continue;
            }
            if (!emitted.Add(packId))
            {
                continue;
            }
            var group = byPack[packId];
            var leader = group[0];
            foreach (var g in group)
            {
                if (g.ClassId.EndsWith(LieutenantSuffix, StringComparison.Ordinal))
                {
                    leader = g;
                    break;
                }
            }
            var text = packNames.TryGetValue(packId, out var named) && !string.IsNullOrEmpty(named)
                ? named
                : group.Count > 1 ? $"{leader.Name} +{group.Count - 1}" : leader.Name;
            var x = (int)Math.Round(group.Average(g => g.X));
            var y = (int)Math.Round(group.Average(g => g.Y));
            AddIfInside(result, new IdlePackLabel(text, x, y, true, leader.Id, group.Select(g => g.Id).ToArray(), packId),
                width, height, textWidth, fontSize);
        }
        return result;
    }

    /// <summary>
    /// The box a label takes above its anchor: centred on the anchor, its text width plus the padding, the font size plus
    /// the padding, its bottom edge at the anchor. LabelView's plate box uses this same formula.
    /// </summary>
    /// <param name="x">The anchor x, pixels.</param>
    /// <param name="y">The anchor y, pixels.</param>
    /// <param name="text">The label text.</param>
    /// <param name="textWidth">The rendered width of a text, pixels.</param>
    /// <param name="fontSize">The font size, pixels.</param>
    /// <returns>The label box.</returns>
    public static PixelRect PlateBox(int x, int y, string text, Func<string, int> textWidth, int fontSize)
    {
        var w = textWidth(text) + (2 * PlatePad);
        var h = fontSize + (2 * PlatePad);
        return new PixelRect(x - (w / 2), y - h, w, h);
    }

    private static void AddIfInside(List<IdlePackLabel> result, IdlePackLabel label, int width, int height,
        Func<string, int> textWidth, int fontSize)
    {
        var box = PlateBox(label.X, label.Y, label.Text, textWidth, fontSize);
        if (box.X >= 0 && box.Y >= 0 && box.X + box.Width <= width && box.Y + box.Height <= height)
        {
            result.Add(label);
        }
    }
}
