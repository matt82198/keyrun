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
public sealed record ActorClips(string Idle, string Move, string Cast, string Act, string Hit, string Death)
{
    /// <summary>KayKit Rig_Medium clip names shared by every Adventurers character.</summary>
    public static ActorClips Default { get; } =
        new("Idle", "Running_A", "Spellcasting", "1H_Melee_Attack_Chop", "Hit_A", "Death_A");
}

/// <summary>
/// One row of <see cref="ActorBinding"/>: which model a sim class (party class id, later enemy id) is drawn with.
/// Integers only (game/CLAUDE.md purity rule): the view divides <see cref="ScalePermille"/> by 1000.
/// </summary>
/// <param name="Key">The sim <c>ActorState.ClassId</c> this row binds ("warden", "choir.melee").</param>
/// <param name="ModelFile">File name inside the pack folder, or null for the capsule fallback.</param>
/// <param name="NativeHeightMm">Bind-pose height of the model's visible nodes in the pack's own units, mm.</param>
/// <param name="ScalePermille">Uniform scale applied by the view, 1/1000.</param>
/// <param name="TintRgb">Optional 0xRRGGBB multiplied into the model's albedo (null = the pack colours).</param>
/// <param name="HiddenNodes">Model nodes the view hides (spare weapons and shields the pack attaches by default).</param>
/// <param name="Clips">The clips this model plays.</param>
public sealed record ActorBindingRow(
    string Key,
    string? ModelFile,
    int NativeHeightMm,
    int ScalePermille,
    int? TintRgb,
    IReadOnlyList<string> HiddenNodes,
    ActorClips Clips)
{
    /// <summary>True when the view keeps the capsule body (no model bound).</summary>
    public bool CapsuleFallback => ModelFile is null;

    /// <summary>Height of the scaled model, mm (what the name plate must clear).</summary>
    public int StandingHeightMm => (int)((long)NativeHeightMm * ScalePermille / 1000);
}

/// <summary>
/// The table from a sim class id to the body the view draws (design/ART-PLAN.md section 4, lane 3). It is a table, not
/// a switch: lane 4 (enemies) adds its rows with <see cref="With"/> or by extending <see cref="DefaultRows"/>, and any
/// class without a row gets <see cref="Capsule"/>, never an exception. Godot-free; src/View/ActorView.cs applies it.
/// </summary>
public sealed class ActorBinding
{
    /// <summary>The res:// folder the party models live in (game/assets/kaykit-adventurers).</summary>
    public const string AdventurersDir = "res://assets/kaykit-adventurers";

    /// <summary>
    /// The highest a scaled model may reach above its feet, mm. LabelView draws name plates at 2.35 m; the capsule they
    /// were placed for is 1.8 m tall, so every model stays at or under it.
    /// </summary>
    public const int HeadLineMm = 1800;

    /// <summary>The row every unbound class gets: keep the capsule.</summary>
    public static ActorBindingRow Capsule { get; } =
        new("capsule", null, 1800, 1000, null, Array.Empty<string>(), ActorClips.Default);

    /// <summary>
    /// The party rows. Native heights are the bind-pose top of the nodes left visible, measured from each .glb's node
    /// tree and mesh POSITION bounds (ActorBindingTests re-measures them from the shipped files):
    /// Knight 2467 mm (helmet top), Mage 3003 mm (hat tip), Rogue_Hooded 2251 mm (hood top).
    /// </summary>
    public static IReadOnlyList<ActorBindingRow> DefaultRows { get; } = new[]
    {
        // Warden: armoured Knight with the one-handed sword and the round shield (ART-PLAN: "shield and sword").
        new ActorBindingRow("warden", "Knight.glb", 2467, 709, null,
            new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Spike_Shield", "2H_Sword" },
            ActorClips.Default with { Cast = "Block", Act = "1H_Melee_Attack_Chop" }),

        // Cinder: robed Mage with the staff; the tall hat sets the scale so the hat tip stays under the head line.
        new ActorBindingRow("cinder", "Mage.glb", 3003, 599, null,
            new[] { "Spellbook", "Spellbook_open", "1H_Wand" },
            ActorClips.Default with { Cast = "Spellcasting", Act = "Spellcast_Shoot" }),

        // Chanter: hooded Rogue, no weapons, tinted green-gold (ART-PLAN section 4).
        new ActorBindingRow("chanter", "Rogue_Hooded.glb", 2251, 777, 0xB8D890,
            new[] { "Knife_Offhand", "1H_Crossbow", "2H_Crossbow", "Knife", "Throwable" },
            ActorClips.Default with { Cast = "Spellcasting", Act = "Spellcast_Raise" }),
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
