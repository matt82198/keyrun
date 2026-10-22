using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// The run's own ability as tooltip facts (Godot-free): the HUD reads the sim ability the run plays (talents and gear
/// Facets applied by the run setup), never the bare content row, so a tooltip's cast time, cooldown, range and numbers
/// are the ones this run uses. The ops go back to their content records, the vocabulary
/// <see cref="C.AbilityTooltip"/> reads; an op the tooltip does not describe is dropped.
/// </summary>
public static class RunAbilityFacts
{
    /// <summary>The facts of a run's sim ability.</summary>
    /// <param name="def">The sim ability (run.Content.Abilities).</param>
    /// <returns>The facts.</returns>
    public static C.AbilityTooltipFacts FromSim(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        IReadOnlyList<C.EffectOp>? ward = def.Variants is { } v && v.TryGetValue("ward", out var w) ? Ops(w) : null;
        return new C.AbilityTooltipFacts
        {
            Id = def.Id,
            CastTicks = def.CastTicks,
            CooldownTicks = def.CooldownTicks,
            Charges = (int)def.Charges,
            Range = (int)def.Range,
            Shape = Shape(def.Shape.Kind),
            CostResource = def.Cost.Resource,
            CostAmount = (int)def.Cost.Amount,
            Tags = def.Tags?.ToList() ?? new List<string>(),
            Effects = Ops(def.Effects),
            WardEffects = ward,
        };
    }

    private static C.ShapeKind Shape(ShapeKind k) => k switch
    {
        ShapeKind.Target => C.ShapeKind.Target,
        ShapeKind.Self => C.ShapeKind.Self,
        ShapeKind.Cone => C.ShapeKind.Cone,
        ShapeKind.Circle => C.ShapeKind.Circle,
        ShapeKind.Line => C.ShapeKind.Line,
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "tooltip: unknown shape kind"),
    };

    private static List<C.EffectOp> Ops(IReadOnlyList<EffectOp> ops)
    {
        var list = new List<C.EffectOp>();
        foreach (var op in ops)
        {
            C.EffectOp? mapped = op switch
            {
                DamageOp d => new C.DamageOp { Amount = (int)d.Amount, School = d.School },
                HealOp h => new C.HealOp { Amount = (int)h.Amount },
                ApplyEffectOp a => new C.ApplyEffectOp { EffectId = a.EffectId, DurationTicks = a.DurationTicks, Stacks = (int)a.Stacks },
                RemoveEffectOp r => new C.RemoveEffectOp { EffectId = r.EffectId },
                InterruptOp => new C.InterruptOp(),
                ResourceOp r => new C.ResourceOp { Delta = (int)r.Delta },
                ThreatOp t => new C.ThreatOp { Amount = (int?)t.Amount, MultiplierBp = (int?)t.MultiplierBp },
                WindowOp w => new C.WindowOp { WindowId = w.WindowId, DurationTicks = w.DurationTicks, OnTarget = w.OnTarget, SetupClass = w.SetupClass },
                PayoffOp => new C.PayoffOp(),
                _ => null,
            };
            if (mapped is not null)
            {
                list.Add(mapped);
            }
        }
        return list;
    }
}
