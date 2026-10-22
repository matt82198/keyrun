using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

public partial class Screen
{
    // Tab (and Up/Down on the Gear tab): the next or previous equipped slot, wrapping.
    private void CycleSelected(int delta)
    {
        var owned = Rewards.GearSlots.Where(s => _progression.Equipped.ContainsKey(s)).ToList();
        if (owned.Count == 0)
        {
            return;
        }
        var i = _selected is null ? (delta > 0 ? -1 : 0) : owned.IndexOf(_selected);
        _selected = owned[(((i + delta) % owned.Count) + owned.Count) % owned.Count];
        ProofItem(_selected);
    }

    private void Reroll()
    {
        if (_selected is not { } slot)
        {
            return;
        }
        var (next, result) = _progression.Reroll(slot, SlotPool(slot));
        _progression = next;
        if (result is null)
        {
            return;
        }
        _status = result.Ok ? string.Empty : FormattableString.Invariant($"Reroll refused: {result.Refusal}.");
        Proof(result.Ok
            ? FormattableString.Invariant($"SCREEN reroll slot={slot} ok=1 facet={result.Item.Facet.Id} charges={result.Item.Charges}")
            : FormattableString.Invariant($"SCREEN reroll slot={slot} ok=0 charges={result.Item.Charges} refused=\"{result.Refusal}\""));
        ProofItem(slot);
        if (result.Ok)
        {
            SaveProgression("reroll"); // after the item line: ScreenSceneTest reads the line right after each reroll
        }
    }

    private string EquippedText()
    {
        var parts = Rewards.GearSlots
            .Where(slot => _progression.Equipped.ContainsKey(slot))
            .Select(slot =>
            {
                var item = _progression.Equipped[slot];
                return FormattableString.Invariant($"{slot}:t{item.Tier}:{item.Facet.Id}:c{item.Charges}");
            })
            .ToList();
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    // BIBLE s7 "Tier is power": one equipped item's stats from the content gear table, or null when content is not
    // loaded (the screen still lists the slot, with no numbers).
    private GearStats? SlotStats(string slot, int tier)
    {
        if (_driver?.Registry is not { } reg || reg.Gear.Count == 0)
        {
            return null;
        }
        var s = Keyrun.Content.GearItems.StatsAt(reg.Gear, Enum.Parse<Keyrun.Content.GearSlot>(slot, ignoreCase: true), tier);
        return new GearStats(s.DamageDoneBp, s.DamageTakenBp);
    }

    // The five slots' summed stats (GearPower.Sum), or null when content is not loaded.
    private GearStats? GearTotal()
    {
        if (_driver?.Registry is not { } reg || reg.Gear.Count == 0)
        {
            return null;
        }
        return GearPower.Sum(_progression.Equipped, (slot, tier) => SlotStats(slot, tier) ?? default);
    }

    // The character readout's five slots (equipped or empty) with their stats, then the summed total.
    private void ProofGear()
    {
        foreach (var slot in Rewards.GearSlots)
        {
            if (_progression.Equipped.TryGetValue(slot, out var item) && SlotStats(slot, item.Tier) is { } st)
            {
                Proof(FormattableString.Invariant(
                    $"SCREEN gear slot={slot} state=equipped tier={item.Tier} damageDoneBp={st.DamageDoneBp} damageTakenBp={st.DamageTakenBp}"));
            }
            else
            {
                Proof(FormattableString.Invariant(
                    $"SCREEN gear slot={slot} state={(_progression.Equipped.ContainsKey(slot) ? "equipped" : "empty")}"));
            }
        }
        var total = GearTotal() ?? default;
        Proof(FormattableString.Invariant(
            $"SCREEN gear total damageDoneBp={total.DamageDoneBp} damageTakenBp={total.DamageTakenBp}"));
    }

    // "+5% damage dealt, -2.5% damage taken" (basis points; empty when there is nothing).
    private static string GearTotalText(GearStats t)
    {
        static string Pct(long bp) => (Math.Abs(bp) / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
        var parts = new List<string>();
        if (t.DamageDoneBp != 0)
        {
            parts.Add((t.DamageDoneBp > 0 ? "+" : "-") + Pct(t.DamageDoneBp) + " damage dealt");
        }
        if (t.DamageTakenBp != 0)
        {
            parts.Add((t.DamageTakenBp < 0 ? "-" : "+") + Pct(t.DamageTakenBp) + " damage taken");
        }
        return string.Join(", ", parts);
    }

    private void ProofItem(string slot)
    {
        if (!_progression.Equipped.TryGetValue(slot, out var item))
        {
            return;
        }
        Proof(FormattableString.Invariant(
            $"SCREEN item slot={slot} tier={item.Tier} facet={item.Facet.Id} charges={item.Charges} selected={(slot == _selected ? 1 : 0)}"));
    }

    // That slot's listed pool for the human's class: content Facets of the slot that patch one of the class kit's
    // abilities, ordered by id so the draw does not depend on content load order.
    private IReadOnlyList<Facet> SlotPool(string slot) =>
        _driver?.Registry is { } reg ? Lobby.SlotPool(reg, Lobby.Kit(reg, ClassId), slot) : Array.Empty<Facet>();

    /// <summary>
    /// One gear slot's facet pool for a class: the content Facets of <paramref name="slot"/> that patch an ability in
    /// <paramref name="kit"/>, ordered by id (load-order independent), as sim <see cref="Facet"/>s. The Screen's reroll
    /// pool and the run-end reward roll (RunEndView.Of, lane 3C-d) share it.
    /// </summary>
    /// <param name="reg">Content.</param>
    /// <param name="kit">The class kit's ability ids.</param>
    /// <param name="slot">A gear slot ("weapon", "armor", ...).</param>
    /// <returns>The pool, possibly empty.</returns>
    public static IReadOnlyList<Facet> SlotPool(Keyrun.Content.ContentRegistry reg, IReadOnlySet<string> kit, string slot) =>
        Lobby.SlotPool(reg, kit, slot);

    // Gear tab, left: the five gear slots (BIBLE s7), each with Tier, Facet and crafting charges, or empty. A Facet
    // on an ability outside the chosen class's kit is shown dim: it does nothing for this class.
    private void DrawGear(Rect2 r)
    {
        Band(r, "Gear");
        var x = r.Position.X + 14;
        if (GearTotal() is { } total && GearTotalText(total) is { Length: > 0 } totalText)
        {
            HudNodes.Text(this, new Vector2(x + 70, r.Position.Y + 24), Clip(totalText, r.Size.X - 90, SmallFont), SmallFont, Calm);
        }
        var kit = _driver?.Registry is { } reg ? Lobby.Kit(reg, ClassId) : new HashSet<string>();
        var slotHeight = Math.Min(96f, (r.Size.Y - 52) / Rewards.GearSlots.Count);
        for (var i = 0; i < Rewards.GearSlots.Count; i++)
        {
            var slot = Rewards.GearSlots[i];
            var box = new Rect2(x, r.Position.Y + 44 + (i * slotHeight), r.Size.X - 28, slotHeight - 8);
            var label = (char.ToUpperInvariant(slot[0]) + slot[1..]).ToUpperInvariant();
            var has = _progression.Equipped.TryGetValue(slot, out var item);
            var selected = has && slot == _selected;
            DrawRect(box, selected ? TabOn : CardBg);
            DrawRect(box, selected ? Good : BronzeDim, filled: false, width: selected ? 2f : 1f);
            HudNodes.Text(this, box.Position + new Vector2(10, 20), label, SmallFont, Dim);
            if (!has || item is null)
            {
                HudNodes.Text(this, box.Position + new Vector2(10, 42), "Empty", BodyFont, new Color(Parchment, 0.4f));
                continue;
            }
            var tier = FormattableString.Invariant($"Tier {item.Tier}");
            HudNodes.Text(this, box.Position + new Vector2(10, 42), tier, 18, TierColor(item.Tier));
            var charges = item.Charges == 1 ? "1 charge left" : FormattableString.Invariant($"{item.Charges} charges left");
            HudNodes.Text(this, box.Position + new Vector2(box.Size.X - 10 - HudNodes.Width(charges, SmallFont), 20), charges, SmallFont, Dim);
            var active = kit.Contains(item.Facet.AbilityId);
            var facet = active ? item.Facet.Text : item.Facet.Text + " (another class's Facet)";
            HudNodes.Text(this, box.Position + new Vector2(10, 62), Clip(facet, box.Size.X - 20, SmallFont), SmallFont, active ? Calm : Dim);
        }
    }

    // Gear tab, right: the selected item (Tab or Up/Down) by name, its tier against the cap of 10, its Facet, the
    // charges left and that slot's reroll pool, or how to select one.
    private void DrawGearDetail(Rect2 r)
    {
        Band(r, "Item");
        var x = r.Position.X + 14;
        var y = r.Position.Y + 58;
        var inner = r.Size.X - 28;
        var row = _selected is { } slot && Sheet() is { } sheet ? sheet.Gear.FirstOrDefault(g => g.Slot == slot && g.Equipped) : null;
        if (row is null)
        {
            var empty = _progression.Equipped.Count == 0
                ? "No gear yet. Every clear offers a choice of three; the pick is equipped here."
                : "Tab or Up/Down selects an item.";
            foreach (var line in Wrap(empty, inner, BodyFont, 4))
            {
                HudNodes.Text(this, new Vector2(x, y), line, BodyFont, Dim);
                y += 18;
            }
            return;
        }
        HudNodes.Text(this, new Vector2(x, y), Clip(row.Name, inner, 20), 20, Parchment);
        y += 26;
        var tier = row.Tier >= Progression.MaxTier
            ? FormattableString.Invariant($"Tier {row.Tier} of {Progression.MaxTier}: the cap")
            : FormattableString.Invariant($"Tier {row.Tier} of {Progression.MaxTier}");
        HudNodes.Text(this, new Vector2(x, y), tier, 18, TierColor(row.Tier));
        y += 22;
        if (SlotStats(row.Slot, row.Tier) is { } st && GearTotalText(st) is { Length: > 0 } stats)
        {
            HudNodes.Text(this, new Vector2(x, y), Clip(stats, inner, SmallFont), SmallFont, Calm);
            y += 20;
        }
        foreach (var line in Wrap((row.FacetActive ? "Facet: " : "Facet (another class's): ") + row.FacetText, inner, SmallFont, 4))
        {
            HudNodes.Text(this, new Vector2(x, y), line, SmallFont, row.FacetActive ? Calm : Dim);
            y += 16;
        }
        y += 8;
        var charges = row.Charges == 1 ? "1 reroll charge left" : FormattableString.Invariant($"{row.Charges} reroll charges left");
        HudNodes.Text(this, new Vector2(x, y), charges, BodyFont, row.Charges > 0 ? Good : Dim);
        y += 18;
        HudNodes.Text(this, new Vector2(x, y), FormattableString.Invariant($"Reroll pool: {SlotPool(row.Slot).Count} Facets"), SmallFont, Dim);
    }

    // Tier colour: grey at 1, through green and blue, to gold at the cap of 10 (shape stays the same: the number is the text).
    private static Color TierColor(int tier) => tier switch
    {
        >= 10 => Gold,
        >= 7 => new Color(0.72f, 0.55f, 1f),
        >= 4 => new Color(0.45f, 0.7f, 1f),
        >= 2 => Good,
        _ => Parchment,
    };
}
