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
    /// True when src/View/LabelView.cs draws an over-head name plate for this actor.
    /// </summary>
    /// <param name="actorId">The actor.</param>
    /// <param name="alive">Whether it is alive.</param>
    /// <param name="human">True for the played character, whose name and health live on the ability bar's meters.</param>
    /// <param name="bossFrameActorId">The actor the boss unit frame shows this frame (UnitFrameModel), or null.</param>
    public static bool DrawsOverhead(int actorId, bool alive, bool human, int? bossFrameActorId) => alive && !human;
}
