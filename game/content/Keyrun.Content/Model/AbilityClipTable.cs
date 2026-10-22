using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>What an ability looks like on the body that uses it (the PR table's "kind" column).</summary>
public enum AbilityAnimKind
{
    /// <summary>A weapon, fist or foot blow: the strike clip, wound up over a cast bar when it has one.</summary>
    Strike,

    /// <summary>A spell: the cast loop for the bar, then a release clip when it resolves.</summary>
    Spell,

    /// <summary>A shield or guard raised: the block clip.</summary>
    Block,

    /// <summary>An interrupt: a short strike on the caster it kicks.</summary>
    Kick,

    /// <summary>A movement ability: a short lunge.</summary>
    Dash,

    /// <summary>A threat or rally gesture.</summary>
    Taunt,
}

/// <summary>One row of <see cref="AbilityClipTable"/>.</summary>
/// <param name="AbilityId">The content ability id.</param>
/// <param name="Kind">How the ability reads.</param>
/// <param name="Release">The one-shot played when the ability resolves (the sim's <c>cast_complete</c>).</param>
/// <param name="Cast">The loop shown for the cast bar, or null for the row's own cast clip (or no bar).</param>
public sealed record AbilityClipRow(string AbilityId, AbilityAnimKind Kind, string Release, string? Cast = null);

/// <summary>A clip the brief wanted that the CC0 pack does not ship, and the nearest one played instead.</summary>
/// <param name="AbilityId">The ability.</param>
/// <param name="Model">The model file that lacks the clip.</param>
/// <param name="Wanted">The clip that would read best.</param>
/// <param name="Used">The clip <see cref="AbilityClipTable"/> plays instead (present in <paramref name="Model"/>).</param>
public sealed record AbilityClipGap(string AbilityId, string Model, string Wanted, string Used);

/// <summary>
/// ABILITY-ANIMS (Matt 2026-10-21 "theres still no ability animations"; KF1 "combat animation reads badly"): every player
/// ability and every enemy cast in content maps to a clip the CC0 KayKit model that uses it carries (D17: CC0 packs
/// only). Pure, no Godot types; src/View/AbilityClipPlayer.cs resolves it against the model's AnimationPlayer and
/// src/View/ActorAnimator.cs plays it. Strike casts keep <see cref="AbilityAnim.ActClip"/>'s clip, so their measured
/// wind-up timing (contact on the resolve frame) is unchanged. Clips here are never a row's looped state (Idle, run,
/// walk, the row cast loop): a looped clip played as a one-shot would never end.
/// </summary>
public static class AbilityClipTable
{
    /// <summary>The cast loop every spell bar shows.</summary>
    public const string SpellLoop = "Spellcasting";

    private static readonly AbilityClipRow[] AllRows =
    {
        // Warden (Knight, sword and shield).
        new("warden.strike", AbilityAnimKind.Strike, "1H_Melee_Attack_Chop"),
        new("warden.guardBreak", AbilityAnimKind.Strike, "1H_Melee_Attack_Slice_Horizontal"),
        new("warden.reckoning", AbilityAnimKind.Strike, "1H_Melee_Attack_Stab"),
        new("warden.shieldBash", AbilityAnimKind.Kick, "Block_Attack"),
        new("warden.bulwark", AbilityAnimKind.Spell, "Spellcast_Raise", "Block"),
        new("warden.brace", AbilityAnimKind.Block, "Blocking"),
        new("warden.charge", AbilityAnimKind.Dash, "Dodge_Forward"),
        new("warden.taunt", AbilityAnimKind.Taunt, "Cheer"),
        new("warden.vigil", AbilityAnimKind.Spell, "Spellcast_Raise"),

        // Cinder (Mage, staff).
        new("cinder.flare", AbilityAnimKind.Spell, "Spellcast_Shoot", SpellLoop),
        new("cinder.emberBolt", AbilityAnimKind.Spell, "Spellcast_Shoot", SpellLoop),
        new("cinder.cinderfield", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("cinder.coldSnap", AbilityAnimKind.Spell, "Spellcast_Shoot"),
        new("cinder.detonate", AbilityAnimKind.Spell, "Spellcast_Raise"),
        new("cinder.quench", AbilityAnimKind.Kick, "1H_Melee_Attack_Slice_Horizontal"),
        new("cinder.purge", AbilityAnimKind.Spell, "Spellcast_Shoot"),
        new("cinder.ashenSkin", AbilityAnimKind.Block, "Blocking"),
        new("cinder.blink", AbilityAnimKind.Dash, "Dodge_Forward"),

        // Chanter (Rogue_Hooded, unarmed).
        new("chanter.cadence", AbilityAnimKind.Spell, "Spellcast_Shoot", SpellLoop),
        new("chanter.hymn", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("chanter.chorus", AbilityAnimKind.Spell, "Spellcast_Raise"),
        new("chanter.dissonance", AbilityAnimKind.Spell, "Spellcast_Shoot"),
        new("chanter.refrain", AbilityAnimKind.Spell, "Spellcast_Raise"),
        new("chanter.rebuke", AbilityAnimKind.Kick, "Unarmed_Melee_Attack_Kick"),
        new("chanter.aegis", AbilityAnimKind.Block, "Blocking"),
        new("chanter.cleanse", AbilityAnimKind.Spell, "Spellcast_Raise"),
        new("chanter.glide", AbilityAnimKind.Dash, "Dodge_Forward"),

        // Enemies (KayKit Skeletons; the boss is the Knight with a two-handed sword).
        new("choir.meleeSwing", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
        new("choir.patrolSwing", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
        new("choir.healCast", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("pair.meleeSwing", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
        new("pair.spreadMark", AbilityAnimKind.Spell, "Spellcast_Shoot", SpellLoop),
        new("pair.stackCall", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("seed.lash", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Kick"),
        new("seed.slam", AbilityAnimKind.Strike, "1H_Melee_Attack_Jump_Chop"),
        new("seed.deathBloom", AbilityAnimKind.Spell, "Spellcast_Raise"),
        new("seed.lock", AbilityAnimKind.Taunt, "Taunt"),
        new("twins.slam", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
        new("hollowWarden.cleave", AbilityAnimKind.Strike, "2H_Melee_Attack_Slice"),
        new("hollowWarden.slam", AbilityAnimKind.Strike, "2H_Melee_Attack_Chop"),
        new("hollowWarden.lock", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("ash.searingHex", AbilityAnimKind.Spell, "Spellcast_Shoot", SpellLoop),
        new("ash.cinders", AbilityAnimKind.Spell, "Spellcast_Raise"),
        new("bellows.stoke", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("drowned.undertow", AbilityAnimKind.Spell, "Spellcast_Shoot", SpellLoop),
        new("vault.crushingSlam", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
        // D66 the Sunken Bellfoundry (skeleton rows): its melee casts strike, its casters' casts are spells.
        new("slagRam.moltenCharge", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
        new("tollingBell.toll", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("furnaceThrall.stokeCore", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("furnaceThrall.slagBurst", AbilityAnimKind.Spell, "Spellcast_Raise"),
        new("scaldHound.boilingBlood", AbilityAnimKind.Spell, "Spellcast_Raise"),
        new("brineMender.bronzeWard", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("ommet.pourSlag", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
        new("ommet.temper", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("brineMother.undertow", AbilityAnimKind.Spell, "Spellcast_Shoot", SpellLoop),
        new("brineMother.boilingSurge", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("brineMother.drowningHymn", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("greatBell.clangour", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
        new("greatBell.stillTheBell", AbilityAnimKind.Spell, "Spellcast_Raise", SpellLoop),
        new("greatBell.peal", AbilityAnimKind.Strike, "Unarmed_Melee_Attack_Punch_A"),
    };

    private static readonly Dictionary<string, AbilityClipRow> ById = Index(AllRows);

    /// <summary>Every row, in table order.</summary>
    public static IReadOnlyList<AbilityClipRow> Rows => AllRows;

    /// <summary>
    /// The clips the Adventurers pack lacks (the Skeletons pack ships them), with the nearest clip played instead (the
    /// PR's follow-up list).
    /// </summary>
    public static IReadOnlyList<AbilityClipGap> Gaps { get; } = new AbilityClipGap[]
    {
        new("warden.taunt", "Knight.glb", "Taunt", "Cheer"),
        new("cinder.cinderfield", "Mage.glb", "Spellcast_Summon", "Spellcast_Raise"),
        new("warden.charge", "Knight.glb", "1H_Melee_Attack_Jump_Chop", "Dodge_Forward"),
    };

    /// <summary>The row for <paramref name="abilityId"/>, or null for a null or unmapped id.</summary>
    public static AbilityClipRow? For(string? abilityId) =>
        abilityId is not null && ById.TryGetValue(abilityId, out var row) ? row : null;

    /// <summary>
    /// The one-shot <paramref name="abilityId"/> plays when it resolves: the table's, else
    /// <see cref="AbilityAnim.ActClip"/> (the row's swing, <paramref name="rowAct"/>, for an unmapped or null id).
    /// </summary>
    public static string ReleaseClip(string? abilityId, string rowAct)
    {
        ArgumentNullException.ThrowIfNull(rowAct);
        return For(abilityId)?.Release ?? AbilityAnim.ActClip(abilityId, rowAct);
    }

    /// <summary>The cast-bar loop for <paramref name="abilityId"/>, or null for the row's own cast clip.</summary>
    public static string? CastClip(string? abilityId) => For(abilityId)?.Cast;

    /// <summary>
    /// The run-log proof line for an ability clip starting on an actor:
    /// <c>ANIM actor=N clip=X ability=Y</c> (<c>ability=-</c> for a clip no ability started, a hit or a death).
    /// </summary>
    public static string AnimLine(int actorId, string clip, string? abilityId)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return FormattableString.Invariant($"ANIM actor={actorId} clip={clip} ability={abilityId ?? "-"}");
    }

    private static Dictionary<string, AbilityClipRow> Index(IEnumerable<AbilityClipRow> rows)
    {
        var map = new Dictionary<string, AbilityClipRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!map.TryAdd(row.AbilityId, row))
            {
                throw new InvalidOperationException("duplicate ability clip row: " + row.AbilityId);
            }
        }
        return map;
    }
}
