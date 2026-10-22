using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// One gear slot as the Character and Gear tabs show it (BIBLE s7): empty, or the equipped item's tier (1-10), Facet,
/// crafting charges left and its name from the content gear table.
/// </summary>
/// <param name="Slot">The slot id ("weapon", "armor", ...).</param>
/// <param name="Equipped">True when the slot holds an item.</param>
/// <param name="Tier">The item's tier, 0 when empty.</param>
/// <param name="Name">The gear table's name for the slot at that tier ("Plain Blade"), empty when the slot is empty.</param>
/// <param name="FacetId">The Facet id, empty when the slot is empty.</param>
/// <param name="FacetText">The Facet text, empty when the slot is empty.</param>
/// <param name="Charges">Crafting charges left, 0 when empty.</param>
/// <param name="FacetActive">True when the Facet patches an ability in the picked class's kit.</param>
public sealed record GearRow(string Slot, bool Equipped, int Tier, string Name, string FacetId, string FacetText, int Charges, bool FacetActive);

/// <summary>One party seat the Queue fills (the human's seat or a bot seat), in actor id order.</summary>
/// <param name="ActorId">The seat's actor id (the human is <see cref="HostRunSetup.HumanId"/>).</param>
/// <param name="ClassId">The seat's class.</param>
/// <param name="Role">The seat's role.</param>
/// <param name="Human">True for the human's seat.</param>
public sealed record SeatRow(int ActorId, string ClassId, ActorRole Role, bool Human);

/// <summary>
/// The Character tab of the one pre-run screen (f-08279d82, D10 MVP line; BIBLE s7, s10): the picked class, the key it
/// queues at, the character's Ascension state and the five gear slots. Pure and Godot-free: built from the content, the
/// character's save (<see cref="Progression"/>, which the end-of-run choice of three writes through
/// <see cref="Progression.Take"/>) and the screen's <see cref="Lobby"/>; the Godot Screen draws it.
/// </summary>
/// <param name="ClassId">The picked class id.</param>
/// <param name="ClassName">The class's display name.</param>
/// <param name="KeyLevel">The key on the Key band.</param>
/// <param name="Ascension">The character's Ascension state (<see cref="Keyrun.Sim.Runs.Ascension.Of"/>).</param>
/// <param name="Gear">The five slots in <see cref="Rewards.GearSlots"/> order.</param>
public sealed record CharacterSheet(string ClassId, string ClassName, int KeyLevel, AscensionState Ascension, IReadOnlyList<GearRow> Gear)
{
    /// <summary>The sheet for <paramref name="lobby"/>'s class and key over <paramref name="progression"/>.</summary>
    /// <param name="registry">The content.</param>
    /// <param name="progression">The character's save.</param>
    /// <param name="lobby">The screen's selection.</param>
    /// <returns>The sheet.</returns>
    public static CharacterSheet Of(C.ContentRegistry registry, Progression progression, Lobby lobby)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(lobby);
        var kit = Lobby.Kit(registry, lobby.ClassId);
        var rows = Rewards.GearSlots.Select(slot => progression.Equipped.TryGetValue(slot, out var item)
                ? new GearRow(slot, true, item.Tier, ItemName(registry, slot, item.Tier), item.Facet.Id, item.Facet.Text, item.Charges, kit.Contains(item.Facet.AbilityId))
                : new GearRow(slot, false, 0, string.Empty, string.Empty, string.Empty, 0, false))
            .ToList();
        var name = registry.Classes.TryGetValue(lobby.ClassId, out var cls) ? cls.Name : lobby.ClassId;
        return new CharacterSheet(lobby.ClassId, name, lobby.Key.KeyLevel, Keyrun.Sim.Runs.Ascension.Of(progression), rows);
    }

    /// <summary>
    /// The seats a Queue as <paramref name="classId"/> fills, in actor id order: the human's roster seat and, with
    /// <paramref name="withBots"/>, a bot in every other roster seat (<see cref="HostRunSetup.BuildParty"/>, the party
    /// SimDriver builds the queued run with).
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="classId">The human's class.</param>
    /// <param name="withBots">False for the solo seat.</param>
    /// <returns>The seats.</returns>
    public static IReadOnlyList<SeatRow> Seats(C.ContentRegistry registry, string classId, bool withBots) =>
        HostRunSetup.BuildParty(registry, classId, withBots)
            .Select(p => new SeatRow(p.Id, p.ClassId, p.Role, p.Id == HostRunSetup.HumanId))
            .ToList();

    /// <summary>
    /// The gear table's name for <paramref name="slot"/> at <paramref name="tier"/>: the slot's item with the highest
    /// defined tier not above it (the same pick <see cref="C.GearItems.StatsAt"/> makes), or the slot id when the table
    /// has none.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="slot">The slot id.</param>
    /// <param name="tier">The tier.</param>
    /// <returns>The name.</returns>
    public static string ItemName(C.ContentRegistry registry, string slot, int tier)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var best = registry.Gear.Values
            .Where(g => C.GearFacets.SlotId(g.Slot) == slot && g.Tier <= tier)
            .OrderByDescending(g => g.Tier)
            .FirstOrDefault();
        return best?.Name ?? slot;
    }
}
