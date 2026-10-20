using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>A named Combo and the ops it fires (sim/combat/combos.ts:21-24 ComboDef).</summary>
/// <param name="Id">Combo id, e.g. "shatter".</param>
/// <param name="Effects">Ops fired from the Payoff actor onto the Window's carrier.</param>
public sealed record ComboDef(string Id, IReadOnlyList<EffectOp> Effects);

/// <summary>
/// <c>{op:"combo", comboTable}</c>: the facet op that adds a second Payoff to a neutral ability (BIBLE section 7;
/// content/data/facets "...charm..." and "...keystone.eternal..." facets). It is a Payoff that closes a target/self
/// Window through the named table ("combos" or "fieldCombos"); a floor fallback always uses the field table, as a
/// plain Payoff does. The frozen TS sim has no branch for this op (it was a silent no-op there) and no oracle
/// content uses it, so this is new runtime behaviour, not a port divergence (ENGINE-PLAN section 5 row 2B).
/// </summary>
/// <param name="ComboTable">Table name; <see cref="Abilities.OpKindErrors"/> rejects any other.</param>
public sealed record ComboOp(string ComboTable) : EffectOp;

/// <summary>
/// <c>{op:"partyEffect"}</c>: a combo op that applies <paramref name="EffectId"/> to every living actor of the Payoff
/// actor's faction (BIBLE section 4 Anthem: "Party gains 15% haste for 6 s."), not to the Window's carrier. C#-only
/// (game/content/data/combo-riders.json); the frozen TS sim has no such op, so no oracle run carries it.
/// </summary>
/// <param name="EffectId">Effect id, e.g. "hasted".</param>
/// <param name="DurationTicks">Duration.</param>
/// <param name="Stacks">Stacks applied.</param>
public sealed record PartyEffectOp(string EffectId, int DurationTicks, long Stacks) : EffectOp;

/// <summary>
/// <c>{op:"cloud"}</c>: a combo op that opens a <see cref="FloorCloud"/> centred on the Window's carrier (BIBLE section 4
/// Steam: "a cloud that blinds enemies inside ... for 4 s"). While it lasts, each living enemy of the Payoff actor inside
/// it carries <paramref name="EffectId"/> until the cloud ends (<see cref="Combos.TickClouds"/>). C#-only, like
/// <see cref="PartyEffectOp"/>.
/// </summary>
/// <param name="EffectId">Effect id, e.g. "blinded".</param>
/// <param name="DurationTicks">Cloud lifetime.</param>
/// <param name="RadiusMm">Cloud radius, mm.</param>
public sealed record CloudOp(string EffectId, int DurationTicks, long RadiusMm) : EffectOp;

/// <summary>
/// <c>{op:"closerBurn"}</c>: a combo op that burns the Payoff actor itself (keyrun STATE.md D34, Matt 2026-10-20 on the
/// Cinderfield closer: "as damage scales this will turn into a one shot, so abilities and damage reduction stuff needs
/// to be considered"). The raw burn is <paramref name="MaxHealthBp"/> of the closer's max health; it then takes the
/// Pact weighting and the closer's own modifiers exactly as a damage op would (so Brace, Vigil and any damageTakenBp
/// still reduce it), and the result is clamped to <paramref name="CapBp"/> of the closer's max health, so no amplifier
/// can grow it toward a one-shot. C#-only, like <see cref="PartyEffectOp"/>; the content validator keeps
/// <c>0 &lt; MaxHealthBp &lt;= CapBp &lt; 10000</c>.
/// </summary>
/// <param name="MaxHealthBp">Base burn, bp of the closer's max health.</param>
/// <param name="CapBp">Ceiling after every amplifier, bp of the closer's max health.</param>
/// <param name="School">Damage school tag.</param>
public sealed record CloserBurnOp(long MaxHealthBp, long CapBp, string School) : EffectOp;

/// <summary>
/// [setupClass][payoffClass] -&gt; ComboDef (sim/combat/combos.ts:34 ComboTable). Lookup-only: nothing iterates it,
/// so the map's own order never reaches the sim.
/// </summary>
public sealed class ComboTable
{
    private readonly Dictionary<string, Dictionary<string, ComboDef>> _rows = new(StringComparer.Ordinal);

    /// <summary>Sets the cell for (<paramref name="setupClass"/>, <paramref name="payoffClass"/>).</summary>
    /// <param name="setupClass">Row key (the Window's setupClass).</param>
    /// <param name="payoffClass">Column key (the Payoff actor's classId).</param>
    /// <param name="combo">The combo.</param>
    public void Set(string setupClass, string payoffClass, ComboDef combo)
    {
        if (!_rows.TryGetValue(setupClass, out var row))
        {
            row = new Dictionary<string, ComboDef>(StringComparer.Ordinal);
            _rows[setupClass] = row;
        }
        row[payoffClass] = combo ?? throw new ArgumentNullException(nameof(combo));
    }

    /// <summary>combos.ts:36-38 lookupCombo: <c>table[setupClass]?.[payoffClass]</c>.</summary>
    /// <param name="setupClass">Row key.</param>
    /// <param name="payoffClass">Column key.</param>
    /// <returns>The combo, or null.</returns>
    public ComboDef? Lookup(string setupClass, string payoffClass) =>
        _rows.TryGetValue(setupClass, out var row) && row.TryGetValue(payoffClass, out var c) ? c : null;
}

/// <summary>Port of sim/combat/combos.ts: table lookup and the combo effect pipeline.</summary>
public static class Combos
{
    /// <summary>
    /// Radius, mm, around the Window's carrier inside which a combo's heal pulse reaches the Payoff actor's allies
    /// (BIBLE section 4 Consecrate: "a 6-second heal pulse for allies near the target"). The BIBLE gives no number, so
    /// this is the smallest ally-area radius in content: the Chanter's Hymn heal patch, <c>radiusMm 2200</c>
    /// (content/data/abilities/chanter.json, chanter.hymn window op).
    /// </summary>
    public const long HealPulseRadiusMm = 2200;

    /// <summary>combos.ts:36-38 lookupCombo.</summary>
    /// <param name="table">The table.</param>
    /// <param name="setupClass">Row key.</param>
    /// <param name="payoffClass">Column key.</param>
    /// <returns>The combo, or null.</returns>
    public static ComboDef? LookupCombo(ComboTable table, string setupClass, string payoffClass)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Lookup(setupClass, payoffClass);
    }

    /// <summary>
    /// combos.ts:47-74 applyComboEffects, from <paramref name="source"/> (the Payoff actor) onto
    /// <paramref name="target"/> (the Window's carrier): damage through mitigation (with the Pact weighting the
    /// caller passes; Payoff.cs passes the hooks' weighting inside RunAsCombo), heal, applyEffect, removeEffect
    /// ("*" = dispel), resource (credited to the Payoff actor), threat. interrupt, window, payoff and combo ops are
    /// no-ops: combos do not chain. C#-only: partyEffect applies to every living actor of the Payoff actor's faction,
    /// and cloud opens a <see cref="FloorCloud"/> on the carrier (BIBLE section 4 Anthem and Steam). An applyEffect
    /// whose effect is a heal-over-time, closed on an enemy carrier, goes to the Payoff actor's living allies near the
    /// carrier instead (BIBLE section 4 Consecrate; <see cref="HealPulseRadiusMm"/>).
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="source">The Payoff actor.</param>
    /// <param name="target">The carrier.</param>
    /// <param name="ops">The combo's ops.</param>
    /// <param name="effectDefs">Effect definitions.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="weighting">The active Pact weighting, or null.</param>
    public static void ApplyComboEffects(
        SimState state,
        ActorState source,
        ActorState target,
        IReadOnlyList<EffectOp> ops,
        IReadOnlyDictionary<string, EffectDef> effectDefs,
        Action<SimEvent> emit,
        DamageWeighting? weighting = null)
    {
        foreach (var op in ops)
        {
            switch (op)
            {
                case DamageOp d:
                    Mitigation.DealDamage(state.Tick, source, target, d.Amount, effectDefs, emit, source.Id, weighting);
                    break;
                case HealOp h:
                    Damage.ApplyHeal(state.Tick, target, h.Amount, source.Id, emit);
                    break;
                case ApplyEffectOp a:
                    if (effectDefs.TryGetValue(a.EffectId, out var def))
                    {
                        if (IsHealPulse(def) && target.Kind != source.Kind)
                        {
                            ApplyHealPulseToAllies(state, source, target, def, a);
                        }
                        else
                        {
                            Effects.ApplyEffectInstance(target, def, source.Id, a.DurationTicks, a.Stacks, state.Tick);
                        }
                    }
                    break;
                case RemoveEffectOp r:
                    if (r.EffectId == "*")
                    {
                        EffectsRemove.RemoveDispellableEffects(source.Kind, target, effectDefs);
                    }
                    else
                    {
                        Effects.RemoveEffectInstances(target, r.EffectId);
                    }
                    break;
                case ResourceOp res:
                    Mitigation.CreditResource(source, res.Delta);
                    break;
                case ThreatOp t:
                    Threat.AddThreat(target, source.Id, t.Amount ?? 0, t.MultiplierBp);
                    break;
                case PartyEffectOp p:
                    if (effectDefs.TryGetValue(p.EffectId, out var partyDef))
                    {
                        foreach (var member in state.Actors.Values)
                        {
                            if (member.Alive && member.Kind == source.Kind)
                            {
                                Effects.ApplyEffectInstance(member, partyDef, source.Id, p.DurationTicks, p.Stacks, state.Tick);
                            }
                        }
                    }
                    break;
                case CloudOp c:
                    OpenCloud(state, source, target, c, effectDefs);
                    break;
                case CloserBurnOp b:
                    BurnCloser(state.Tick, source, b, effectDefs, emit, weighting);
                    break;
            }
        }
    }

    /// <summary>
    /// Advances every open <see cref="FloorCloud"/> one step: a cloud whose end tick has come is removed; each other
    /// cloud gives its effect, ending at the cloud's end, to every living actor of another faction standing inside it.
    /// Rules.Make calls it once per step after floor expiries; on an oracle run there are no clouds, so it does nothing.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="effectDefs">Effect definitions.</param>
    public static void TickClouds(SimState state, IReadOnlyDictionary<string, EffectDef> effectDefs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(effectDefs);
        state.FloorClouds.RemoveAll(c => c.EndsTick <= state.Tick);
        foreach (var cloud in state.FloorClouds)
        {
            ApplyCloud(state, cloud, effectDefs);
        }
    }

    /// <summary>True when <paramref name="def"/> is a heal-over-time: its periodic op is a heal.</summary>
    private static bool IsHealPulse(EffectDef def) => def.Periodic is { Op: HealOp };

    /// <summary>
    /// BIBLE section 4 Consecrate: a heal pulse closed on an enemy carrier goes to each living ally of the Payoff actor
    /// within <see cref="HealPulseRadiusMm"/> of the carrier (the Payoff actor too, when in range), never to the enemy.
    /// Ids come from <see cref="Targeting.InCircle"/> over <c>state.Actors</c> in its own order, so the result is
    /// deterministic. No oracle run fires Consecrate (Fixtures/oracle traces), so the oracle replays are unchanged.
    /// </summary>
    private static void ApplyHealPulseToAllies(SimState state, ActorState source, ActorState target, EffectDef def, ApplyEffectOp op)
    {
        var allies = state.Actors.Values.Where(a => a.Alive && a.Kind == source.Kind).ToList();
        foreach (var id in Targeting.InCircle(target.Pos, HealPulseRadiusMm, allies))
        {
            Effects.ApplyEffectInstance(state.Actors[id], def, source.Id, op.DurationTicks, op.Stacks, state.Tick);
        }
    }

    /// <summary>
    /// <see cref="CloserBurnOp"/> on <paramref name="closer"/> (the Payoff actor): raw = MaxHealthBp of its max health,
    /// then the Pact <paramref name="weighting"/> and its damageDone/damageTaken modifiers as
    /// <see cref="Mitigation.DealDamage"/> applies them, clamped to CapBp of its max health, then absorb and health
    /// through <see cref="Damage.ResolveModified"/> (one "damage" event, source and target the closer). The blinded miss
    /// rule does not apply: the closer is not attacking. A dead closer takes nothing.
    /// </summary>
    private static void BurnCloser(
        int tick,
        ActorState closer,
        CloserBurnOp op,
        IReadOnlyDictionary<string, EffectDef> effectDefs,
        Action<SimEvent> emit,
        DamageWeighting? weighting)
    {
        if (!closer.Alive)
        {
            return;
        }
        var raw = IntMath.TruncDiv(closer.MaxHealth * op.MaxHealthBp, Effects.BpDenom);
        var cap = IntMath.TruncDiv(closer.MaxHealth * op.CapBp, Effects.BpDenom);
        var weighted = weighting is null ? raw : weighting(raw);
        var mods = Effects.AggregateModifiers(closer, effectDefs);
        var modified = Math.Min(cap, Damage.ApplyModifiers(weighted, mods.DamageDoneBp, mods.DamageTakenBp));
        Damage.ResolveModified(tick, closer, raw, modified, effectDefs, emit, closer.Id);
    }

    private static void OpenCloud(SimState state, ActorState source, ActorState target, CloudOp op, IReadOnlyDictionary<string, EffectDef> effectDefs)
    {
        var cloud = new FloorCloud
        {
            Id = op.EffectId + ":" + source.Id + ":" + state.Tick,
            EffectId = op.EffectId,
            SourceId = source.Id,
            SourceKind = source.Kind,
            Pos = target.Pos,
            RadiusMm = op.RadiusMm,
            EndsTick = state.Tick + op.DurationTicks,
        };
        state.FloorClouds.Add(cloud);
        ApplyCloud(state, cloud, effectDefs);
    }

    private static void ApplyCloud(SimState state, FloorCloud cloud, IReadOnlyDictionary<string, EffectDef> effectDefs)
    {
        if (cloud.EndsTick <= state.Tick || !effectDefs.TryGetValue(cloud.EffectId, out var def))
        {
            return;
        }
        var enemies = state.Actors.Values.Where(a => a.Alive && a.Kind != cloud.SourceKind).ToList();
        foreach (var id in Targeting.InCircle(cloud.Pos, cloud.RadiusMm, enemies))
        {
            Effects.ApplyEffectInstance(state.Actors[id], def, cloud.SourceId, cloud.EndsTick - state.Tick, 1, state.Tick);
        }
    }
}
