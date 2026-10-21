using System;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// Warden's Riposte talent (content/data/classes/warden.json "Strike costs no GCD when it follows Brace.",
/// design/BIBLE.md:128 "Riposte (Strike after a Brace is free)"). The talent patch only tags Strike; the free GCD is
/// decided here at cast time: a <see cref="FreeAfterBraceTag"/> ability cast while the caster holds an unexpired
/// <see cref="BraceEffectId"/> costs no GCD, once per Brace. The spend is recorded as an internal
/// <see cref="SpentEffectId"/> instance (no content def, so no modifiers and no HUD row; same pattern as
/// WindowState's lock markers) that expires with the Brace. No TS counterpart: the frozen TS sim zeroed the GCD
/// unconditionally (f-d37651d1).
/// </summary>
public static class Riposte
{
    /// <summary>The tag the Riposte patch puts on warden.strike.</summary>
    public const string FreeAfterBraceTag = "riposte:free-after-brace";

    /// <summary>The self effect warden.brace applies (content/data/abilities/warden.json).</summary>
    public const string BraceEffectId = "braceSelf";

    /// <summary>Internal marker: this Brace's free Strike has been used.</summary>
    public const string SpentEffectId = "riposte:spent";

    /// <summary>
    /// The GCD <paramref name="def"/> starts when <paramref name="actor"/> casts it at <paramref name="now"/>. Returns
    /// 0 and spends the Riposte when the free-after-Brace rule applies; otherwise <c>def.GcdTicks</c>, unchanged.
    /// </summary>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The ability being cast.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>GCD ticks to start.</returns>
    public static int GcdTicksFor(ActorState actor, AbilityDef def, int now)
    {
        if (def.Tags is null || !def.Tags.Contains(FreeAfterBraceTag, StringComparer.Ordinal))
        {
            return def.GcdTicks;
        }
        var brace = actor.Effects.Find(e => e.EffectId == BraceEffectId && e.EndsTick > now);
        if (brace is null || actor.Effects.Exists(e => e.EffectId == SpentEffectId && e.EndsTick > now))
        {
            return def.GcdTicks;
        }
        Effects.RemoveEffectInstanceWhere(actor, e => e.EffectId == SpentEffectId);
        Effects.PushRawEffectInstance(actor, new EffectInstance
        {
            EffectId = SpentEffectId,
            SourceId = actor.Id,
            Stacks = 1,
            EndsTick = brace.EndsTick,
            NextPeriodicTick = null,
        });
        return 0;
    }
}
