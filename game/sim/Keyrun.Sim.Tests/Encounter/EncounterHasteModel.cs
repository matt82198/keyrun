using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// The encounter-only replay's model of each enemy's live haste (<see cref="EncounterTraceTests"/>, ruling R-HASTE-1:
/// haste from every live effect shortens casts, Casting.HastedCastTicks). The replay runs no combat, so it learns the
/// haste effects that land on an enemy from the oracle's own <c>combo</c> events: a target combo (no <c>field</c> flag)
/// applies its combo's <c>applyEffect</c> ops to the event's <c>targetId</c> from the payoff actor, as
/// sim/combat/combos.ts applyComboEffects and Combos.cs do (on the Hollow Vault traces, the Anthem's hasted +1500 bp on
/// the Seed). Field combos land on the payoff actor (a player) and never on an enemy, so they are not modelled.
/// Instances follow applyEffectInstance: one per (effect, source), stacks capped at maxStacks, endsTick = tick + duration,
/// live on tick t while endsTick &gt; t.
/// </summary>
internal sealed class EncounterHasteModel
{
    private readonly record struct Application(int Target, string EffectId, int Source, int Tick, int Duration, int Stacks);

    private readonly List<Application> _applications = new();
    private readonly Dictionary<string, (long HasteBp, int MaxStacks)> _hasteEffects = new(StringComparer.Ordinal);

    /// <summary>Builds the model from the setup's run content and the trace's records.</summary>
    /// <param name="content">The setup's <c>content</c> (its <c>combos</c> table and <c>effects</c>).</param>
    /// <param name="records">The trace's records, in tick order.</param>
    public EncounterHasteModel(JsObject content, IEnumerable<OracleRecord> records)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(records);
        if (content.Get("effects") is JsObject effects)
        {
            foreach (var (id, value) in effects.Members)
            {
                var def = (JsObject)value;
                var haste = def.Get("modifiers") is JsObject m && m.Get("hasteBp") is JsInt h ? h.Value : 0;
                if (haste != 0)
                {
                    var max = def.Get("maxStacks") is JsInt ms ? (int)ms.Value : 1;
                    _hasteEffects[id] = (haste, max);
                }
            }
        }
        var combos = content.Get("combos") as JsObject;
        foreach (var e in records.SelectMany(r => r.Events).Where(e => e.Type == "combo" && e.Get("field") is null))
        {
            if (e.Get("targetId") is not JsInt target || e.Get("payoffActorId") is not JsInt source ||
                e.Get("setupClass") is not JsString setup || e.Get("payoffClass") is not JsString payoff ||
                combos?.Get(setup.Value) is not JsObject row || row.Get(payoff.Value) is not JsObject combo ||
                combo.Get("effects") is not JsArray ops)
            {
                continue;
            }
            foreach (var op in ops.Items.OfType<JsObject>())
            {
                if (op.Get("op") is JsString { Value: "applyEffect" } && op.Get("effectId") is JsString effectId &&
                    _hasteEffects.ContainsKey(effectId.Value))
                {
                    var duration = op.Get("durationTicks") is JsInt d ? (int)d.Value : 0;
                    var stacks = op.Get("stacks") is JsInt s ? (int)s.Value : 1;
                    _applications.Add(new Application((int)target.Value, effectId.Value, (int)source.Value, e.Tick, duration, stacks));
                }
            }
        }
    }

    /// <summary>The number of haste applications the model learned (so a vacuous model is visible).</summary>
    public int Applications => _applications.Count;

    /// <summary>
    /// <paramref name="actor"/>'s summed live haste when a cast starts on <paramref name="tick"/>: every application on
    /// or before the tick (a cast start sees a combo that landed earlier in the same tick's combat), merged per
    /// (effect, source) as applyEffectInstance merges them, counted while its endsTick is after the tick.
    /// </summary>
    /// <param name="actor">The caster.</param>
    /// <param name="tick">The cast start tick.</param>
    /// <returns>The haste total, basis points.</returns>
    public long HasteBpAt(int actor, int tick)
    {
        var live = new Dictionary<(string, int), (int Stacks, int Ends)>();
        foreach (var a in _applications.Where(a => a.Target == actor && a.Tick <= tick))
        {
            var max = _hasteEffects[a.EffectId].MaxStacks;
            // An instance is swept at the end of its endsTick's step (expireEffects), so one applied on that tick or
            // earlier is refreshed, and one applied after it starts fresh.
            live[(a.EffectId, a.Source)] = live.TryGetValue((a.EffectId, a.Source), out var cur) && cur.Ends >= a.Tick
                ? (Math.Min(max, cur.Stacks + a.Stacks), a.Tick + a.Duration)
                : (Math.Min(max, Math.Max(1, a.Stacks)), a.Tick + a.Duration);
        }
        long haste = 0;
        foreach (var ((effectId, _), inst) in live)
        {
            if (inst.Ends > tick)
            {
                haste += _hasteEffects[effectId].HasteBp * inst.Stacks;
            }
        }
        return haste;
    }

    /// <summary>The cast time of <paramref name="castTicks"/> begun by <paramref name="actor"/> on <paramref name="tick"/>.</summary>
    /// <param name="actor">The caster.</param>
    /// <param name="castTicks">The content cast time.</param>
    /// <param name="tick">The cast start tick.</param>
    /// <returns>The hasted cast time (Casting.HastedTicks).</returns>
    public int CastTicksAt(int actor, int castTicks, int tick) => Casting.HastedTicks(castTicks, HasteBpAt(actor, tick));
}
