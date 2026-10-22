namespace Keyrun.Sim.Combat;

/// <summary>
/// Port of sim/combat/casting.ts: the cast lifecycle. startCast validates silence, GCD/charges, cost, school lockout
/// and (target shape) target and range; completeCast applies the ability's ops to its shape-resolved targets at
/// castEndsTick (cones and lines use the caster's facing at completion); stopcast cancels with no effects;
/// interruptCast cancels an interruptible cast and locks its school. Rules.cs completes due casts before a tick's
/// commands, so an interrupt landing exactly at castEndsTick is too late (casting.ts:8-13).
/// The class is split by concern: Casting.Start.cs (start checks), Casting.Haste.cs (cast time), Casting.Overheat.cs
/// (the Overheat rule), Casting.Target.cs (range and the cast target pin), Casting.Complete.cs (completion and ops),
/// Casting.Stop.cs (stopcast, interrupt and clearing); CastContext.cs holds the cast context.
/// </summary>
public static partial class Casting
{
}
