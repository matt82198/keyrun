using System;

namespace Keyrun.Content;

/// <summary>
/// Where a name plate anchor sits above its body's feet (PLATE-HEIGHT). Pure and integer-only (game/CLAUDE.md purity
/// rule): <see cref="ActorBindingRow.PlateHeightMm"/> calls it for every row and src/View/LabelView.cs multiplies the
/// result by 0.001 to get metres.
/// </summary>
public static class PlateRule
{
    /// <summary>How far a plate anchor sits above the higher of its row's target height and its scaled model top, mm.</summary>
    public const int ClearanceMm = 250;

    /// <summary>The highest a boss plate anchor may sit above the feet, mm, so it stays inside the frame from the door.</summary>
    public const int BossCapMm = 5000;

    /// <summary>The enemy nameplate max distance from the viewing player, mm, inclusive (f-77a68e6b).</summary>
    // WoW hides enemy plates past 60 yd; from each room's party start the next doorway is <= 25500 mm (pair 36500 -> door 62000), the pull after >= 27000 (seed 66500 -> boss 93500).
    public const int PlateDistanceMm = 26000;

    /// <summary>
    /// The scaled model's top above its feet, mm: native height x scale permille / 1000, truncated exactly like
    /// <see cref="ActorBindingRow.StandingHeightMm"/>.
    /// </summary>
    /// <param name="nativeHeightMm">Bind-pose top of the model's visible nodes in the pack's own units, mm.</param>
    /// <param name="scalePermille">Uniform scale the view applies to the model, 1/1000.</param>
    public static int ScaledTopMm(int nativeHeightMm, int scalePermille) =>
        (int)((long)nativeHeightMm * scalePermille / 1000);

    /// <summary>
    /// The plate anchor height above the feet, mm: max(target, scaled top) + <see cref="ClearanceMm"/>; a boss plate is
    /// then capped at <see cref="BossCapMm"/>.
    /// </summary>
    /// <param name="targetHeightMm">The height the body is sized to, mm.</param>
    /// <param name="nativeHeightMm">Bind-pose top of the model's visible nodes, mm (hat and helmet included).</param>
    /// <param name="scalePermille">Uniform scale the view applies to the model, 1/1000.</param>
    /// <param name="boss">True for the boss row, whose plate is capped at <see cref="BossCapMm"/>.</param>
    /// <remarks>
    /// Before PLATE-HEIGHT the anchor was max(target, 1800) + 550 for every row (2.35 m for a 1.8 m body, 5.237 m for
    /// the boss), which floated the Mage plates about 0.55 m over the hat and pushed the boss plate to the top edge.
    /// </remarks>
    public static int PlateHeightMm(int targetHeightMm, int nativeHeightMm, int scalePermille, bool boss)
    {
        int plate = Math.Max(targetHeightMm, ScaledTopMm(nativeHeightMm, scalePermille)) + ClearanceMm;
        return boss ? Math.Min(plate, BossCapMm) : plate;
    }

    /// <summary>
    /// True when src/View/LabelView.cs draws an over-head name plate for this actor: alive, not the played character,
    /// and not the enemy the boss unit frame names (f-07f1b477: the boss plate and the Sundered Twin plates beside it
    /// collided and HudLayout.Separate stacked all three over the boss model; the boss frame already names the boss,
    /// the plate counterpart of HudDeclutter.OverheadCast). The target frame does not suppress: the target's plate
    /// carries the "&gt; name &lt;" mark in the world.
    /// </summary>
    /// <param name="actorId">The actor.</param>
    /// <param name="alive">Whether it is alive.</param>
    /// <param name="human">True for the played character, whose name and health live on the ability bar's meters.</param>
    /// <param name="bossFrameActorId">The actor the boss unit frame shows this frame (UnitFrameModel), or null.</param>
    public static bool DrawsOverhead(int actorId, bool alive, bool human, int? bossFrameActorId) =>
        alive && !human && actorId != bossFrameActorId;

    /// <summary>
    /// <see cref="DrawsOverhead(int, bool, bool, int?)"/> plus the nameplate distance (f-77a68e6b): an enemy plate also
    /// needs its actor within <see cref="PlateDistanceMm"/> of the viewing player (<see cref="InPlateRange"/>), so the
    /// plates of pulls far down the dungeon do not draw over the current fight. Party plates do not cull by distance.
    /// </summary>
    /// <param name="actorId">The actor.</param>
    /// <param name="alive">Whether it is alive.</param>
    /// <param name="human">True for the played character.</param>
    /// <param name="bossFrameActorId">The actor the boss unit frame shows this frame, or null.</param>
    /// <param name="enemy">True for an enemy actor, whose plate culls by distance.</param>
    /// <param name="distanceMm">The actor's distance from the viewing player, mm.</param>
    public static bool DrawsOverhead(int actorId, bool alive, bool human, int? bossFrameActorId, bool enemy, long distanceMm) =>
        DrawsOverhead(actorId, alive, human, bossFrameActorId) && (!enemy || InPlateRange(distanceMm));

    /// <summary>True when <paramref name="distanceMm"/> is within <see cref="PlateDistanceMm"/> (the boundary draws).</summary>
    /// <param name="distanceMm">Distance from the viewing player, mm.</param>
    public static bool InPlateRange(long distanceMm) => distanceMm <= PlateDistanceMm;
}
