using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// The table from a player ability id to the icon the ability bar draws in its slot (lane ABILITY-ICONS). Icons are
/// CC0 PNGs from Kenney's Board Game Icons 1.1 (64 px, white glyphs on transparent), vendored in
/// <c>game/assets/kenney-board-game-icons/</c> with the pack's LICENSE.txt (provenance in game/assets/ATTRIBUTION.md).
/// It is a table, not content data: like <see cref="ActorBinding"/> it is Godot-free so xUnit can prove every class kit
/// ability has a row, and src/Hud/AbilityBar.cs applies it. An id without a row gets null (the slot keeps its name text),
/// never an exception.
/// </summary>
public sealed class AbilityIcons
{
    /// <summary>The pack folder under game/assets the icon files live in.</summary>
    public const string Folder = "kenney-board-game-icons";

    /// <summary>The icon pack folder's res:// path.</summary>
    public const string Dir = ActorBinding.AssetsRoot + "/" + Folder;

    /// <summary>Every icon file is a square of this many pixels (the pack's "Default (64px)" set).</summary>
    public const int SizePx = 64;

    /// <summary>
    /// Ability id to pack file name. Each pick reads as what the ability does: a sword for a strike, a shield for a
    /// defensive, a hand for an interrupt, an arrow for a movement.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultRows { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Warden (tank): blades for the strikes, walls and shields for the mitigation.
        ["warden.strike"] = "sword.png",
        ["warden.guardBreak"] = "exploding.png",
        ["warden.bulwark"] = "structure_wall.png",
        ["warden.reckoning"] = "skull.png",
        ["warden.vigil"] = "structure_watchtower.png",
        ["warden.shieldBash"] = "dice_shield.png",
        ["warden.brace"] = "shield.png",
        ["warden.taunt"] = "flag_triangle.png",
        ["warden.charge"] = "chess_knight.png",

        // Cinder (fire and frost caster): flames for the fire school, an ice shard for Cold Snap.
        ["cinder.emberBolt"] = "fire.png",
        ["cinder.flare"] = "campfire.png",
        ["cinder.coldSnap"] = "rhombus.png",
        ["cinder.detonate"] = "exploding.png",
        ["cinder.cinderfield"] = "hexagon_tile.png",
        ["cinder.quench"] = "hand_cross.png",
        ["cinder.ashenSkin"] = "tag_shield.png",
        ["cinder.purge"] = "flask_empty.png",
        ["cinder.blink"] = "arrow_diagonal.png",

        // Chanter (healer): hearts for the heals, a hymnal and a beat for the songs.
        ["chanter.cadence"] = "hourglass.png",
        ["chanter.refrain"] = "suit_hearts.png",
        ["chanter.chorus"] = "pawns.png",
        ["chanter.dissonance"] = "suit_hearts_broken.png",
        ["chanter.hymn"] = "book_open.png",
        ["chanter.rebuke"] = "hand.png",
        ["chanter.aegis"] = "shield.png",
        ["chanter.cleanse"] = "flask_full.png",
        ["chanter.glide"] = "arrow_right.png",
    };

    /// <summary>The shipped table.</summary>
    public static AbilityIcons Default { get; } = new(DefaultRows);

    /// <summary>Builds a table from id to file rows.</summary>
    /// <param name="rows">Ability id to icon file name inside <see cref="Folder"/>.</param>
    public AbilityIcons(IReadOnlyDictionary<string, string> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Rows = rows.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>Ability id to icon file name.</summary>
    public IReadOnlyDictionary<string, string> Rows { get; }

    /// <summary>The icon file name for an ability, or null when it has no row.</summary>
    /// <param name="abilityId">A content ability id ("warden.strike").</param>
    /// <returns>The file name inside <see cref="Folder"/>, or null.</returns>
    public string? FileFor(string? abilityId) =>
        abilityId is not null && Rows.TryGetValue(abilityId, out var file) ? file : null;

    /// <summary>The icon's res:// path for an ability, or null when it has no row.</summary>
    /// <param name="abilityId">A content ability id.</param>
    /// <returns>res://assets/kenney-board-game-icons/&lt;file&gt;, or null.</returns>
    public string? ResPathFor(string? abilityId) => FileFor(abilityId) is { } file ? Dir + "/" + file : null;
}
