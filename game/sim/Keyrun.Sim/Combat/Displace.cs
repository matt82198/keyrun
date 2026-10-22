using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// <c>{op:"displace", distanceMm, freesBinds?, towardTarget?}</c>: C#-only op (TOOLKIT-CONTRACT lane L2, ruling
/// R-TOOLKIT-2) that moves the CASTER, once per completed cast, in one tick. <see cref="PullOp"/> is the only other
/// displacement op and it moves the target. Without <paramref name="TowardTarget"/> the caster moves
/// <paramref name="DistanceMm"/> along its facing (<see cref="Facing.FaceDir"/>, a Blink); with it the caster moves
/// toward the cast's first target by up to <paramref name="DistanceMm"/>, never past it (a Charge).
/// Combat then clamps to the arena bounds and a layout run clamps to walkable space against the pre-step position
/// (Run.StepLayout), so a displace stops at a wall exactly as a pull does. Integers only.
/// </summary>
/// <param name="DistanceMm">Distance, mm.</param>
/// <param name="FreesBinds">True removes the rooted and slowed effects from the caster.</param>
/// <param name="TowardTarget">True moves toward the cast's target instead of along the facing.</param>
public sealed record DisplaceOp(long DistanceMm, bool FreesBinds = false, bool TowardTarget = false) : EffectOp;

/// <summary>Applies a <see cref="DisplaceOp"/> to the caster.</summary>
public static class Displace
{
    /// <summary>Effect ids a <see cref="DisplaceOp.FreesBinds"/> displace removes from the caster.</summary>
    private static readonly string[] BindIds = { "rooted", "slowed" };

    /// <summary>
    /// Moves <paramref name="caster"/> per <paramref name="op"/>. A dead caster, a non-positive distance, or a
    /// toward-target op with no other target does not move (binds are still freed when asked).
    /// </summary>
    /// <param name="caster">The caster.</param>
    /// <param name="target">The cast's first target, or null.</param>
    /// <param name="op">The op.</param>
    public static void Apply(ActorState caster, ActorState? target, DisplaceOp op)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(op);
        if (!caster.Alive)
        {
            return;
        }
        if (op.FreesBinds)
        {
            foreach (var id in BindIds)
            {
                Effects.RemoveEffectInstances(caster, id);
            }
        }
        if (op.DistanceMm <= 0)
        {
            return;
        }
        if (op.TowardTarget)
        {
            if (target is not null && target.Id != caster.Id)
            {
                Movement.PullToward(caster, target, op.DistanceMm);
            }
            return;
        }
        var face = Facing.FaceDir(caster.Yaw);
        caster.Pos = caster.Pos with
        {
            X = caster.Pos.X + IntMath.TruncDiv(face.X * op.DistanceMm, Facing.Q),
            Z = caster.Pos.Z + IntMath.TruncDiv(face.Z * op.DistanceMm, Facing.Q),
        };
    }
}
