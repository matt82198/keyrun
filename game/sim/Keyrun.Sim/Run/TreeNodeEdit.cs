using Keyrun.Sim.Combat;

namespace Keyrun.Sim.Runs;

// G2.2 design/ABILITY-TREES.md s1.2, s5.2 (ruling R-TREES-7): the host turns a content node's edit into one of these; the
// sim never reads Keyrun.Content (R-TREES-1). Design deltas are flat integers.

/// <summary>
/// A rank node's flat change to one numeric field of one op of the base ability (warden.json "Strike deals 8 more damage":
/// <c>Op "damage"</c>, <c>Field "amount"</c>, <c>Delta 8</c>). Resolved against the base ability into a replacement ops list.
/// </summary>
/// <param name="Op">The effect op kind the delta targets (for example "damage").</param>
/// <param name="Field">The numeric field of that op (for example "amount").</param>
/// <param name="Delta">The signed flat change added to the field.</param>
public sealed record EffectDelta(string Op, string Field, long Delta);

/// <summary>
/// One tree node's contribution to an ability. A rank node carries <see cref="Delta"/>; an enhanced node carries
/// <see cref="Patch"/>, whose <see cref="GearPatch.EffectsAppend"/> ops are appended after the base ops.
/// </summary>
/// <param name="Delta">The rank delta, or null.</param>
/// <param name="Patch">The enhanced node's patch, or null.</param>
public sealed record TreeNodeEdit(EffectDelta? Delta = null, GearPatch? Patch = null);
