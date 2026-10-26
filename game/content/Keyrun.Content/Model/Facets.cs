using System.Runtime.Serialization;

namespace Keyrun.Content;

/// <summary>Gear slot (content/schema/facet.ts GearSlotId).</summary>
public enum GearSlot
{
    /// <summary>Weapon.</summary>
    [EnumMember(Value = "weapon")] Weapon,

    /// <summary>Armor.</summary>
    [EnumMember(Value = "armor")] Armor,

    /// <summary>Charm.</summary>
    [EnumMember(Value = "charm")] Charm,

    /// <summary>Ring.</summary>
    [EnumMember(Value = "ring")] Ring,

    /// <summary>Keystone.</summary>
    [EnumMember(Value = "keystone")] Keystone,
}

/// <summary>
/// A facet patch: the union of a raw <see cref="AbilityPatch"/> and the structured
/// append/retag form (content/schema/facet.ts FacetPatch). <c>id</c> is not a member, so a patch
/// that tries to rename an ability fails the load.
/// </summary>
public sealed record FacetPatch : AbilityPatch
{
    /// <summary>Tags merged into the base ability's tags.</summary>
    public ValueList<string>? TagAdd { get; init; }

    /// <summary>Tags removed from the base ability's tags.</summary>
    public ValueList<string>? TagRemove { get; init; }

    /// <summary>Ops appended to the base ability's effects.</summary>
    public ValueList<EffectOp>? EffectsAppend { get; init; }
}

/// <summary>A Facet a gear slot can roll (content/schema/facet.ts FacetDef).</summary>
public sealed record FacetDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Slot.</summary>
    public required GearSlot Slot { get; init; }

    /// <summary>The ability this Facet patches.</summary>
    public required string AbilityId { get; init; }

    /// <summary>Tooltip text.</summary>
    public required string Text { get; init; }

    /// <summary>The patch.</summary>
    public required FacetPatch Patch { get; init; }
}

/// <summary>
/// Every loaded content kind as id-keyed maps in TS loader file order
/// (content/schema/registry.ts ContentRegistry). Structural equality is deep.
/// </summary>
public sealed record ContentRegistry
{
    /// <summary>Abilities (warden, cinder, chanter, enemies files).</summary>
    public required ValueMap<AbilityDef> Abilities { get; init; }

    /// <summary>Effects (player, enemy files).</summary>
    public required ValueMap<EffectDef> Effects { get; init; }

    /// <summary>Classes (warden, cinder, chanter).</summary>
    public required ValueMap<ClassDef> Classes { get; init; }

    /// <summary>Combos.</summary>
    public required ValueMap<ComboEntry> Combos { get; init; }

    /// <summary>Enemies (choir, pair, seed, hollow-warden files).</summary>
    public required ValueMap<EnemyDef> Enemies { get; init; }

    /// <summary>Dungeons.</summary>
    public required ValueMap<DungeonDef> Dungeons { get; init; }

    /// <summary>Clauses.</summary>
    public required ValueMap<ClauseDef> Clauses { get; init; }

    /// <summary>Facets (warden, cinder, chanter files).</summary>
    public required ValueMap<FacetDef> Facets { get; init; }

    /// <summary>Run-level movement and arena config (run.json; C#-only, no TS counterpart).</summary>
    public required RunConfig Run { get; init; }

    /// <summary>The dungeon's rooms, corridors, triggers, gates and route (layout.json; C#-only, STATE.md D18).</summary>
    public required LayoutDef Layout { get; init; }

    /// <summary>
    /// Replacement effects by combo id (combo-riders.json; C#-only, BIBLE section 4 Anthem and Steam). Empty when a
    /// registry is built by hand.
    /// </summary>
    public ValueMap<ComboRider> ComboRiders { get; init; } = new();

    /// <summary>The Forces bar size, per-enemy points and the route (forces.json; C#-only, BIBLE section 9.2).</summary>
    public required ForcesTable Forces { get; init; }

    /// <summary>
    /// Every dungeon's forces table by dungeon id (forces/&lt;dungeon&gt;.json; the Hollow Vault's entry is <see cref="Forces"/>).
    /// Empty when a registry is built by hand; <see cref="ForcesFor"/> then answers only for the Hollow Vault.
    /// </summary>
    public ValueMap<ForcesTable> ForcesByDungeon { get; init; } = new();

    /// <summary>
    /// The forces table of <paramref name="dungeonId"/>: the bar size and route that dungeon fills. The Sunken Bellfoundry's bar
    /// is its own file, so editing the Hollow Vault's Required never moves it.
    /// </summary>
    /// <param name="dungeonId">The dungeon, e.g. <c>hollow-vault</c> or <c>sunken-bellfoundry</c>.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">No forces table for that dungeon.</exception>
    public ForcesTable ForcesFor(string dungeonId)
    {
        System.ArgumentNullException.ThrowIfNull(dungeonId);
        if (ForcesByDungeon.TryGetValue(dungeonId, out ForcesTable? table)) return table;
        if (Forces.Route.DungeonId == dungeonId) return Forces;
        throw new System.Collections.Generic.KeyNotFoundException("no forces table for dungeon \"" + dungeonId + "\"");
    }

    /// <summary>The end-of-run completion roll's drop rates and slots (completion.json; C#-only, R-ROLL-1, BIBLE s1).</summary>
    public required CompletionTable Completion { get; init; }

    /// <summary>Room hazards by id (hazards.json; C#-only, BIBLE section 9.4).</summary>
    public required ValueMap<HazardDef> Hazards { get; init; }

    /// <summary>
    /// Base gear items by id (gear.json; C#-only, BIBLE section 7 "Five slots"). Empty when a registry is built by hand.
    /// </summary>
    public ValueMap<GearItemDef> Gear { get; init; } = new();

    /// <summary>
    /// Ability ids that are melee-range contact (game/content/data/melee-abilities.json; C#-only, KF1 strike cue, ruling R9).
    /// Every id names a loaded ability. Empty when a registry is built by hand.
    /// </summary>
    public ValueList<string> MeleeAbilities { get; init; } = new();

    /// <summary>
    /// The seeded dungeons' pack rosters by pack id (game/content/data/roster/*.json, one pack per file; D46). Empty when
    /// a registry is built by hand.
    /// </summary>
    public ValueMap<RosterPack> Roster { get; init; } = new();

    /// <summary>
    /// Improvement-tree nodes by node id (game/content/data/trees/&lt;classId&gt;.json, one file per class; ABILITY-TREES s5.1).
    /// Empty when a registry is built by hand.
    /// </summary>
    public ValueMap<TreeNodeDef> Trees { get; init; } = new();
}
