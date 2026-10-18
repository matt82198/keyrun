using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>sim/combat/payoff.ts:28 PayoffOutcome.</summary>
public enum PayoffOutcome
{
    /// <summary>A Window closed and its Combo fired.</summary>
    Closed,

    /// <summary>A Window was found but this class may not close it ("combo_denied").</summary>
    Denied,

    /// <summary>No closable Window (none open, unparseable, or no Combo for this pair).</summary>
    NoWindow,
}

/// <summary>
/// Port of sim/combat/payoff.ts: closing a Window with a Payoff. Denials (same-class self-combo, Lock alternation)
/// emit "combo_denied" and leave the Window open for another class. A closed Window's Combo fires inside
/// <see cref="ClauseHooks.RunAsCombo"/>, so a Pact Clause weights its damage as a combo hit.
/// </summary>
public static class Payoff
{
    /// <summary>
    /// payoff.ts:34-85 tryClosePayoff on a target/self (or Lock) Window carried by <paramref name="target"/>. The
    /// denied class is the Window's setupClass, or for a Lock the class that closed this carrier's last Lock. A
    /// closed Lock records the closer's class and resets the unpaid-Lock counter.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="target">The Window's carrier.</param>
    /// <param name="payoffActor">The Payoff caster.</param>
    /// <param name="table">The burst ComboTable.</param>
    /// <param name="effectDefs">Effect definitions.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="hooks">The step's Clause hooks.</param>
    /// <returns>The outcome.</returns>
    public static PayoffOutcome TryClosePayoff(
        SimState state,
        ActorState target,
        ActorState payoffActor,
        ComboTable table,
        IReadOnlyDictionary<string, EffectDef> effectDefs,
        Action<SimEvent> emit,
        ClauseHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        var now = state.Tick;
        var inst = WindowState.FindOpenWindow(target, now);
        if (inst is null)
        {
            return PayoffOutcome.NoWindow;
        }
        var parsed = WindowState.ParseWindowEffectId(inst.EffectId);
        if (parsed is null)
        {
            return PayoffOutcome.NoWindow;
        }
        var deniedClass = parsed.Alternate ? WindowState.LastSetupClassOf(target) : parsed.SetupClass;
        if (deniedClass is not null && payoffActor.ClassId == deniedClass)
        {
            emit(new SimEvent(
                now,
                "combo_denied",
                ("windowId", JsValue.Str(parsed.WindowId)),
                ("targetId", JsValue.Int(target.Id)),
                ("setupActorId", JsValue.Int(inst.SourceId)),
                ("payoffActorId", JsValue.Int(payoffActor.Id)),
                ("reason", JsValue.Str(parsed.Alternate ? "lock_alternate" : "self_combo"))));
            return PayoffOutcome.Denied;
        }
        var combo = Combos.LookupCombo(table, parsed.SetupClass, payoffActor.ClassId);
        if (combo is null)
        {
            return PayoffOutcome.NoWindow;
        }
        Effects.RemoveEffectInstanceWhere(target, e => ReferenceEquals(e, inst));
        if (parsed.Alternate)
        {
            WindowState.SetLastSetupClass(target, payoffActor.ClassId);
            WindowState.ResetLockFailCount(target);
        }
        hooks.RunAsCombo(() => Combos.ApplyComboEffects(state, payoffActor, target, combo.Effects, effectDefs, emit, hooks.Weighting));
        emit(new SimEvent(
            now,
            "combo",
            ("comboId", JsValue.Str(combo.Id)),
            ("windowId", JsValue.Str(parsed.WindowId)),
            ("setupActorId", JsValue.Int(inst.SourceId)),
            ("setupClass", JsValue.Str(parsed.SetupClass)),
            ("payoffActorId", JsValue.Int(payoffActor.Id)),
            ("payoffClass", JsValue.Str(payoffActor.ClassId)),
            ("targetId", JsValue.Int(target.Id))));
        return PayoffOutcome.Closed;
    }

    /// <summary>
    /// payoff.ts:90-132 tryClosePayoffOnFloor: the first open patch (JS key order) whose circle holds the Payoff actor.
    /// Its own class is denied ("self_combo", no targetId); a pair with no field Combo moves on to the next patch; a
    /// closed patch is deleted and its field Combo fires onto the Payoff actor itself.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="payoffActor">The Payoff caster.</param>
    /// <param name="floor">The patch registry.</param>
    /// <param name="fieldTable">The field ComboTable.</param>
    /// <param name="effectDefs">Effect definitions.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="hooks">The step's Clause hooks.</param>
    /// <returns>The outcome.</returns>
    public static PayoffOutcome TryClosePayoffOnFloor(
        SimState state,
        ActorState payoffActor,
        FloorWindowRegistry floor,
        ComboTable fieldTable,
        IReadOnlyDictionary<string, EffectDef> effectDefs,
        Action<SimEvent> emit,
        ClauseHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(hooks);
        var now = state.Tick;
        var standing = new[] { payoffActor };
        foreach (var key in floor.Patches.Keys)
        {
            var patch = floor.Patches[key];
            if (patch.EndsTick <= now)
            {
                continue;
            }
            if (Targeting.InCircle(patch.Pos, patch.RadiusMm, standing).Count == 0)
            {
                continue;
            }
            if (payoffActor.ClassId == patch.SetupClass)
            {
                emit(new SimEvent(
                    now,
                    "combo_denied",
                    ("windowId", JsValue.Str(patch.WindowId)),
                    ("setupActorId", JsValue.Int(patch.SetupActorId)),
                    ("payoffActorId", JsValue.Int(payoffActor.Id)),
                    ("reason", JsValue.Str("self_combo"))));
                return PayoffOutcome.Denied;
            }
            var combo = Combos.LookupCombo(fieldTable, patch.SetupClass, payoffActor.ClassId);
            if (combo is null)
            {
                continue;
            }
            floor.Patches.Remove(patch.WindowId);
            hooks.RunAsCombo(() => Combos.ApplyComboEffects(state, payoffActor, payoffActor, combo.Effects, effectDefs, emit, hooks.Weighting));
            emit(new SimEvent(
                now,
                "combo",
                ("comboId", JsValue.Str(combo.Id)),
                ("windowId", JsValue.Str(patch.WindowId)),
                ("setupActorId", JsValue.Int(patch.SetupActorId)),
                ("setupClass", JsValue.Str(patch.SetupClass)),
                ("payoffActorId", JsValue.Int(payoffActor.Id)),
                ("payoffClass", JsValue.Str(payoffActor.ClassId)),
                ("field", JsValue.Bool(true))));
            return PayoffOutcome.Closed;
        }
        return PayoffOutcome.NoWindow;
    }
}
