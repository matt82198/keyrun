using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

public static partial class Casting
{
    /// <summary>
    /// Cast time under haste: a cast takes castTicks * 10000 / (10000 + hasteBp) ticks (floor), hasteBp summed over the
    /// caster's live effects. BIBLE section 5 Chill, "slower casts" (hasteBp -3000 makes 20 ticks 28), and BIBLE 9.3
    /// Stoked, "20% cast speed" (hasteBp 2500 makes Stoke's 70 ticks 56, 2000 bp shorter). An instant cast stays
    /// instant; no context, no haste, or a total at or below -10000 leaves the content cast time unchanged.
    /// </summary>
    private static int HastedCastTicks(ActorState actor, int castTicks, OverheatRule? rule, CastContext? ctx, int now)
    {
        _ = rule;
        if (ctx is null || castTicks <= 0)
        {
            return castTicks;
        }
        long haste = 0;
        foreach (var inst in actor.Effects)
        {
            if (inst.EndsTick > now && ctx.Effects.TryGetValue(inst.EffectId, out var def))
            {
                haste += (def.Modifiers.HasteBp ?? 0) * inst.Stacks;
            }
        }
        return HastedTicks(castTicks, haste);
    }

    /// <summary>
    /// The cast-time formula of <see cref="HastedCastTicks"/> for a known haste total (R-HASTE-1; mirrored by
    /// sim/combat/haste.ts): castTicks * 10000 / (10000 + hasteBp), floored. An instant cast, zero haste, or a total at or
    /// below -10000 leaves the cast time unchanged.
    /// </summary>
    /// <param name="castTicks">The content cast time, ticks.</param>
    /// <param name="hasteBp">The caster's summed live haste, basis points.</param>
    /// <returns>The hasted cast time, ticks.</returns>
    public static int HastedTicks(int castTicks, long hasteBp)
    {
        if (castTicks <= 0 || hasteBp == 0 || Effects.BpDenom + hasteBp <= 0)
        {
            return castTicks;
        }
        return (int)IntMath.FloorDiv(castTicks * Effects.BpDenom, Effects.BpDenom + hasteBp);
    }
}
