using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// The animation clip names a model row plays (names inside the model's own AnimationPlayer). The view matches each
/// name exactly first, then by a case-insensitive substring, and skips a state whose clip it cannot find.
/// </summary>
/// <param name="Idle">Looped while the actor stands still.</param>
/// <param name="Move">Looped while the interpolated position moves.</param>
/// <param name="Cast">Looped while the sim actor has a <c>CastId</c>.</param>
/// <param name="Act">Played once when the actor's global cooldown restarts (an instant ability went out).</param>
/// <param name="Hit">Played once when the actor's health drops.</param>
/// <param name="Death">Played once when the actor dies; its last frame is held.</param>
/// <param name="Spawn">
/// Played once when the view first binds the body (ART-PLAN section 3: enemies rise where they spawn); null = start in
/// <paramref name="Idle"/>.
/// </param>
public sealed record ActorClips(string Idle, string Move, string Cast, string Act, string Hit, string Death,
    string? Spawn = null)
{
    /// <summary>KayKit Rig_Medium clip names shared by every Adventurers character.</summary>
    public static ActorClips Default { get; } =
        new("Idle", "Running_A", "Spellcasting", "1H_Melee_Attack_Chop", "Hit_A", "Death_A");

    /// <summary>
    /// KayKit Skeletons clip names (the same Rig_Medium library plus the skeleton-only clips): they rise out of the
    /// ground on spawn and collapse into a bone pile on death. The skeleton models carry no weapons, so the instant
    /// ability plays an unarmed punch.
    /// </summary>
    public static ActorClips Skeleton { get; } =
        new("Idle", "Running_A", "Spellcasting", "Unarmed_Melee_Attack_Punch_A", "Hit_A", "Death_C_Skeletons",
            "Spawn_Ground_Skeletons");
}

/// <summary>
/// One row of <see cref="ActorBinding"/>: which model a sim class (party class id or enemy id) is drawn with.
/// Integers only (game/CLAUDE.md purity rule): the view divides <see cref="ScalePermille"/> by 1000.
/// </summary>
/// <param name="Key">The sim <c>ActorState.ClassId</c> this row binds ("warden", "choir.melee").</param>
/// <param name="ModelFile">File name inside <paramref name="Folder"/>, or null for the capsule fallback.</param>
/// <param name="NativeHeightMm">Bind-pose height of the model's visible nodes in the pack's own units, mm.</param>
/// <param name="ScalePermille">Uniform scale applied by the view, 1/1000 (target height / native height).</param>
/// <param name="TintRgb">Optional 0xRRGGBB multiplied into the model's albedo (null = the pack colours).</param>
/// <param name="HiddenNodes">Model nodes the view hides (spare weapons and shields the pack attaches by default).</param>
/// <param name="Clips">The clips this model plays.</param>
/// <param name="TargetHeightMm">
/// The height this body is sized to, mm: <see cref="ActorBinding.HeadLineMm"/> by default, more for the roles ART-PLAN
/// section 4 makes bigger (Seed 1.3x, Twins 1.15x, the boss 1.9x the Knight). The scaled model lands within 2 percent.
/// </param>
/// <param name="Folder">The pack folder under game/assets the model file lives in.</param>
/// <param name="Boss">True for the boss row: its name plate is capped at <see cref="PlateRule.BossCapMm"/>.</param>
public sealed record ActorBindingRow(
    string Key,
    string? ModelFile,
    int NativeHeightMm,
    int ScalePermille,
    int? TintRgb,
    IReadOnlyList<string> HiddenNodes,
    ActorClips Clips,
    int TargetHeightMm = ActorBinding.HeadLineMm,
    string Folder = ActorBinding.AdventurersFolder,
    bool Boss = false)
{
    /// <summary>True when the view keeps the capsule body (no model bound).</summary>
    public bool CapsuleFallback => ModelFile is null;

    /// <summary>Height of the scaled model, mm (what the name plate must clear).</summary>
    public int StandingHeightMm => (int)((long)NativeHeightMm * ScalePermille / 1000);

    /// <summary>The model's res:// path (<see cref="ActorBinding.AssetsRoot"/>/Folder/ModelFile), or null for the capsule.</summary>
    public string? ResPath => ModelFile is null ? null : ActorBinding.AssetsRoot + "/" + Folder + "/" + ModelFile;

    /// <summary>
    /// Height of this class's name plate anchor above its feet, mm (<see cref="PlateRule.PlateHeightMm"/>): the higher of
    /// its target height and its scaled model top plus <see cref="PlateRule.ClearanceMm"/> 250, the boss's capped at
    /// <see cref="PlateRule.BossCapMm"/> 5000, so a 1.8 m body's plate sits at 2.05 m just over its head (a Mage's just
    /// over the hat) and the 4.69 m boss's at 4.937 m just over its helmet.
    /// </summary>
    public int PlateHeightMm => PlateRule.PlateHeightMm(TargetHeightMm, NativeHeightMm, ScalePermille, Boss);

    /// <summary>
    /// How much of the pack's albedo brightness <see cref="TintRgb"/> keeps, permille (integer Rec. 601 luma of the tint:
    /// 299 R + 587 G + 114 B over 255), or null for an untinted row. The view multiplies the tint into the albedo, so a
    /// tint at 300 permille leaves the body at under a third of its pack brightness.
    /// </summary>
    public int? TintLumaPermille => TintRgb is int rgb
        ? (299 * ((rgb >> 16) & 0xFF) + 587 * ((rgb >> 8) & 0xFF) + 114 * (rgb & 0xFF)) / 255
        : null;
}

/// <summary>
/// The table from a sim class id to the body the view draws (design/ART-PLAN.md section 4, lanes 3 and 4). It is a
/// table, not a switch: a later lane adds rows with <see cref="With"/> or by extending <see cref="DefaultRows"/>, and any
/// class without a row gets <see cref="Capsule"/>, never an exception. Godot-free; src/View/ActorView.cs applies it.
/// </summary>
public sealed class ActorBinding
{
    /// <summary>The res:// folder every pack folder lives in (game/assets).</summary>
    public const string AssetsRoot = "res://assets";

    /// <summary>The party models' pack folder (game/assets/kaykit-adventurers; the boss reuses its Knight).</summary>
    public const string AdventurersFolder = "kaykit-adventurers";

    /// <summary>The enemy models' pack folder (game/assets/kaykit-skeletons).</summary>
    public const string SkeletonsFolder = "kaykit-skeletons";

    /// <summary>The res:// folder the party models live in (game/assets/kaykit-adventurers).</summary>
    public const string AdventurersDir = AssetsRoot + "/" + AdventurersFolder;

    /// <summary>
    /// The default target height, mm: the 1.8 m capsule the name plates were placed for. Party and ordinary enemy
    /// bodies are sized to it; bigger roles set their own <see cref="ActorBindingRow.TargetHeightMm"/>.
    /// </summary>
    public const int HeadLineMm = 1800;

    /// <summary>
    /// The least <see cref="ActorBindingRow.TintLumaPermille"/> a tinted row may have (BOSS-READABILITY, design/BIBLE.md
    /// section 3 "the player must read what enemies do"; ART-PLAN section 3 readability rule): a tint recolours a body,
    /// it never darkens it below half its pack brightness, so its silhouette and pose stay readable against the dark
    /// stone floors. The 0x50505C "hollow" boss tint (319 permille) rendered the Hollow Warden near-black.
    /// </summary>
    public const int MinTintLumaPermille = 500;

    /// <summary>
    /// The effect the sim puts on the Twins when their shared break bar empties (content hollow-warden.json
    /// <c>onEmpty applyEffect stunned</c>); the view plays the hit clip when it appears (stagger).
    /// </summary>
    public const string StaggerEffectId = "stunned";

    /// <summary>The Knight's bind-pose helmet top with the party Warden's hidden nodes, mm (the boss is 1.9x it).</summary>
    public const int KnightNativeHeightMm = 2467;

    /// <summary>The row every unbound class gets: keep the capsule.</summary>
    public static ActorBindingRow Capsule { get; } =
        new("capsule", null, 1800, 1000, null, Array.Empty<string>(), ActorClips.Default);

    /// <summary>
    /// The party and enemy rows. Native heights are the bind-pose top of the nodes left visible, measured from each
    /// .glb's node tree and mesh POSITION bounds (ActorBindingTests re-measures them from the shipped files):
    /// Knight 2467 mm (helmet top), Mage 3003 mm (hat tip), Rogue_Hooded 2251 mm (hood top); Skeleton_Warrior 2590 mm
    /// (helmet), Skeleton_Mage 2630 mm (hat), Skeleton_Minion 2166 mm (skull), Skeleton_Rogue 2308 mm (hood). Scale
    /// permille = target / native, rounded.
    /// </summary>
    public static IReadOnlyList<ActorBindingRow> DefaultRows { get; } = new[]
    {
        // Warden: armoured Knight with the one-handed sword and the round shield (ART-PLAN: "shield and sword").
        new ActorBindingRow("warden", "Knight.glb", KnightNativeHeightMm, 709, null,
            new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Spike_Shield", "2H_Sword" },
            ActorClips.Default with { Cast = "Block", Act = "1H_Melee_Attack_Chop" }, TargetHeightMm: 1750),

        // Cinder: robed Mage with the staff; the tall hat sets the scale so the hat tip stays under the head line.
        new ActorBindingRow("cinder", "Mage.glb", 3003, 599, null,
            new[] { "Spellbook", "Spellbook_open", "1H_Wand" },
            ActorClips.Default with { Cast = "Spellcasting", Act = "Spellcast_Shoot" }),

        // Chanter: hooded Rogue, no weapons, tinted green-gold (ART-PLAN section 4).
        new ActorBindingRow("chanter", "Rogue_Hooded.glb", 2251, 777, 0xB8D890,
            new[] { "Knife_Offhand", "1H_Crossbow", "2H_Crossbow", "Knife", "Throwable" },
            ActorClips.Default with { Cast = "Spellcasting", Act = "Spellcast_Raise" }, TargetHeightMm: 1750),

        // Choir Acolyte: Skeleton Warrior (helmet and cloak), the pack colours.
        new ActorBindingRow("choir.melee", "Skeleton_Warrior.glb", 2590, 695, null, Array.Empty<string>(),
            ActorClips.Skeleton, Folder: SkeletonsFolder),

        // Choir Cantor: robed Skeleton Mage, tinted the purple of the capsule it replaces (the kick target).
        new ActorBindingRow("choir.lieutenant", "Skeleton_Mage.glb", 2630, 684, 0xB070E0, Array.Empty<string>(),
            ActorClips.Skeleton with { Act = "Spellcast_Shoot" }, Folder: SkeletonsFolder),

        // Hall Sentry (Mythic+ trash only the Hall Sentries pack fields, game/content/data/enemies/hall.json): the same
        // CC0 Skeleton Warrior as the Choir Acolyte, washed bronze so the pack reads as its own enemy.
        new ActorBindingRow("hall.sentry", "Skeleton_Warrior.glb", 2590, 695, 0xD0A060, Array.Empty<string>(),
            ActorClips.Skeleton, Folder: SkeletonsFolder),

        // Paired Warrior x5: the bare Skeleton Minion.
        new ActorBindingRow("pair.melee", "Skeleton_Minion.glb", 2166, 831, null, Array.Empty<string>(),
            ActorClips.Skeleton, Folder: SkeletonsFolder),

        // The Seed: hooded Skeleton Rogue at 1.3x, green.
        new ActorBindingRow("seed.elite", "Skeleton_Rogue.glb", 2308, 1014, 0x88F088, Array.Empty<string>(),
            ActorClips.Skeleton with { Act = "Unarmed_Melee_Attack_Kick" }, TargetHeightMm: HeadLineMm * 13 / 10,
            Folder: SkeletonsFolder),

        // Sundered Twins: Skeleton Warriors at 1.15x, one blue and one pink.
        new ActorBindingRow("hollowWarden.twinA", "Skeleton_Warrior.glb", 2590, 799, 0x7898FF, Array.Empty<string>(),
            ActorClips.Skeleton, TargetHeightMm: HeadLineMm * 115 / 100, Folder: SkeletonsFolder),
        new ActorBindingRow("hollowWarden.twinB", "Skeleton_Warrior.glb", 2590, 799, 0xFF88C8, Array.Empty<string>(),
            ActorClips.Skeleton, TargetHeightMm: HeadLineMm * 115 / 100, Folder: SkeletonsFolder),

        // The Hollow Warden: the player's own Knight at 1.9x its native height, washed a pale cold steel (hollow; BOSS-
        // READABILITY: the 0x50505C dark tint rendered it near-black on the stone floor), with the two-handed sword
        // it cleaves and slams with; no spawn clip (the Adventurers have none), it starts in Idle.
        new ActorBindingRow("hollowWarden.boss", "Knight.glb", KnightNativeHeightMm, 1900, 0xB4C0DC,
            new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "Spike_Shield", "1H_Sword" },
            ActorClips.Default with { Move = "Walking_A", Act = "2H_Melee_Attack_Chop" },
            TargetHeightMm: KnightNativeHeightMm * 19 / 10, Boss: true),
    };

    /// <summary>The table the game draws with today.</summary>
    public static ActorBinding Default { get; } = new(DefaultRows);

    private readonly Dictionary<string, ActorBindingRow> _rows;

    /// <summary>Builds a table; a duplicate key throws <see cref="ArgumentException"/>.</summary>
    public ActorBinding(IEnumerable<ActorBindingRow> rows)
    {
        _rows = new Dictionary<string, ActorBindingRow>(StringComparer.Ordinal);
        foreach (ActorBindingRow row in rows)
        {
            if (!_rows.TryAdd(row.Key, row))
            {
                throw new ArgumentException("duplicate actor binding row: " + row.Key, nameof(rows));
            }
        }
        Rows = _rows.Values.ToArray();
    }

    /// <summary>Every row, in insertion order.</summary>
    public IReadOnlyList<ActorBindingRow> Rows { get; }

    /// <summary>The row for a sim class id; <see cref="Capsule"/> for null, empty or unknown ids.</summary>
    public ActorBindingRow For(string? classId) =>
        classId is not null && _rows.TryGetValue(classId, out ActorBindingRow? row) ? row : Capsule;

    /// <summary>A new table with one more row (this one is unchanged); a key already present throws.</summary>
    public ActorBinding With(ActorBindingRow row) => new(Rows.Append(row));
}
