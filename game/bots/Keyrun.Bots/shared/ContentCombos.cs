using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using C = Keyrun.Content;

namespace Keyrun.Bots.Shared;

/// <summary>
/// The content combos (content/data/combos.json plus game/content/data/combo-riders.json) as the sim's two
/// <see cref="ComboTable"/>s, and the content-to-sim effect op mapping they share with the host's abilities and
/// effects. It lives here for the reason <see cref="NavLayouts"/> does: Keyrun.Bots is the one Godot-free assembly that
/// sees both Keyrun.Content and Keyrun.Sim, so the host (HostRunSetup) builds its run tables with it and the headless
/// tests (Keyrun.Bots.Tests ContentCombosTests) check the very same code against the content (finding f-89b0825e).
/// </summary>
public static class ContentCombos
{
    /// <summary>
    /// content/sim-adapt.ts toComboTables: exact (setupClass, payoffClass) rows go to the burst table; a
    /// <c>payoffClass "*"</c> row goes to the field table once per other class. A combo with a C#-only rider
    /// (combo-riders.json: Anthem's party haste, Steam's cloud; BIBLE section 4) fires the rider's effects instead.
    /// </summary>
    /// <param name="entries">The content combo rows (registry.Combos.Values).</param>
    /// <param name="classIds">The playable class ids (registry.Classes.Keys) a <c>"*"</c> row fans out over.</param>
    /// <param name="riders">The C#-only riders by combo id (registry.ComboRiders).</param>
    /// <returns>The burst table (exact rows) and the field table (<c>"*"</c> rows).</returns>
    public static (ComboTable Combos, ComboTable FieldCombos) ToComboTables(
        IEnumerable<C.ComboEntry> entries, IReadOnlyList<string> classIds, IReadOnlyDictionary<string, C.ComboRider> riders)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(classIds);
        ArgumentNullException.ThrowIfNull(riders);
        var combos = new ComboTable();
        var field = new ComboTable();
        foreach (var entry in entries)
        {
            var ops = riders.TryGetValue(entry.Id, out var rider) ? rider.Effects : entry.Effects;
            var def = new ComboDef(entry.Id, ops.Select(o => ToSimOp(o, "combo " + entry.Id)).ToList());
            if (entry.PayoffClass == "*")
            {
                foreach (var payoff in classIds.Where(c => c != entry.SetupClass))
                {
                    field.Set(entry.SetupClass, payoff, def);
                }
            }
            else
            {
                combos.Set(entry.SetupClass, entry.PayoffClass, def);
            }
        }
        return (combos, field);
    }

    /// <summary>One content effect op as the sim op (content/sim-adapt.ts toSimOp plus the C#-only party/cloud ops).</summary>
    /// <param name="op">The content op.</param>
    /// <param name="where">Where the op sits, for the error message.</param>
    /// <returns>The sim op.</returns>
    /// <exception cref="InvalidOperationException">A window op without setupClass, or an op the sim does not run.</exception>
    public static EffectOp ToSimOp(C.EffectOp op, string where) => op switch
    {
        C.DamageOp d => new DamageOp(d.Amount, d.School),
        C.HealOp h => new HealOp(h.Amount),
        C.ApplyEffectOp e => new ApplyEffectOp(e.EffectId, e.DurationTicks, e.Stacks),
        C.RemoveEffectOp r => new RemoveEffectOp(r.EffectId),
        C.InterruptOp => new InterruptOp(),
        C.ResourceOp r => new ResourceOp(r.Delta),
        C.ThreatOp t => new ThreatOp(t.Amount, t.MultiplierBp),
        C.WindowOp w => new WindowOp(
            w.WindowId,
            w.DurationTicks,
            w.OnTarget,
            w.SetupClass ?? throw new InvalidOperationException($"run setup: {where} has a window op without setupClass"),
            w.Self,
            w.Alternate,
            w.RadiusMm),
        C.PayoffOp => new PayoffOp(),
        C.ComboOp c => new ComboOp(c.ComboTable),
        C.PartyEffectOp p => new PartyEffectOp(p.EffectId, p.DurationTicks, p.Stacks),
        C.CloudOp c => new CloudOp(c.EffectId, c.DurationTicks, c.RadiusMm),
        _ => throw new InvalidOperationException($"run setup: {where} has an op the sim does not run ({op.GetType().Name})"),
    };
}
