using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Combat;

/// <summary>
/// Port of sim/combat/abilities.ts: the AbilityDef validator and the ability-school rule. The records themselves
/// (AbilityDef, AbilityShape, EffectOp) are lane 2A's, in Types.cs.
/// </summary>
/// <remarks>
/// C# additions for the FACET RUNTIME (ENGINE-PLAN section 5 row 2B, BIBLE section 7), none of which the oracle
/// content uses: the validator fails closed on an EffectOp subtype the sim does not run and on a
/// <see cref="ComboOp"/> naming an unknown table, and <see cref="LineShape"/> maps a facet's full line width onto
/// the sim's half-width radius.
/// </remarks>
public static class Abilities
{
    /// <summary>The <see cref="ComboOp"/> table name for the burst table.</summary>
    public const string CombosTable = "combos";

    /// <summary>The <see cref="ComboOp"/> table name for the floor-patch field table.</summary>
    public const string FieldCombosTable = "fieldCombos";

    /// <summary>
    /// sim/combat/abilities.ts:62-98 validateAbilityDef, message for message. The TS <c>Number.isInteger</c> halves
    /// cannot fail on C# integer fields, so only the range halves remain. Then the C#-only fail-closed checks: every
    /// op must be a kind the sim runs, and a combo op must name "combos" or "fieldCombos".
    /// </summary>
    /// <param name="def">The ability.</param>
    /// <returns>Error strings; empty when valid.</returns>
    public static List<string> ValidateAbilityDef(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        var errs = new List<string>();
        if (string.IsNullOrEmpty(def.Id))
        {
            errs.Add("id is required");
        }
        if (def.CastTicks < 0)
        {
            errs.Add("castTicks must be an integer >= 0");
        }
        if (def.GcdTicks < 0)
        {
            errs.Add("gcdTicks must be an integer >= 0");
        }
        if (def.CooldownTicks < 0)
        {
            errs.Add("cooldownTicks must be an integer >= 0");
        }
        if (def.Charges < 1)
        {
            errs.Add("charges must be an integer >= 1");
        }
        if (def.Cost.Amount < 0)
        {
            errs.Add("cost.amount must be an integer >= 0");
        }
        if (def.Range < 0)
        {
            errs.Add("range must be an integer mm >= 0");
        }
        var shape = def.Shape;
        if (shape.Kind == ShapeKind.Cone && (shape.Angle1024 is null || shape.Radius is null))
        {
            errs.Add("cone shape requires radius and angle1024");
        }
        if (shape.Kind == ShapeKind.Circle && shape.Radius is null)
        {
            errs.Add("circle shape requires radius");
        }
        if (shape.Kind == ShapeKind.Line && (shape.Length is null || shape.Radius is null))
        {
            errs.Add("line shape requires length and radius (half-width)");
        }
        if (def.Effects.Count == 0)
        {
            errs.Add("effects must not be empty");
        }
        foreach (var op in def.Effects)
        {
            switch (op)
            {
                case ApplyEffectOp a:
                    if (a.DurationTicks < 0)
                    {
                        errs.Add($"applyEffect {a.EffectId} durationTicks must be >= 0");
                    }
                    if (a.Stacks < 1)
                    {
                        errs.Add($"applyEffect {a.EffectId} stacks must be an integer >= 1");
                    }
                    break;
                case DamageOp d when d.Amount < 0:
                    errs.Add("damage amount must be an integer >= 0");
                    break;
                case HealOp h when h.Amount < 0:
                    errs.Add("heal amount must be an integer >= 0");
                    break;
                case WindowOp w:
                    if (string.IsNullOrEmpty(w.WindowId))
                    {
                        errs.Add("window op requires windowId");
                    }
                    if (w.DurationTicks < 0)
                    {
                        errs.Add($"window {w.WindowId} durationTicks must be an integer >= 0");
                    }
                    if (string.IsNullOrEmpty(w.SetupClass))
                    {
                        errs.Add($"window {w.WindowId} requires setupClass");
                    }
                    if (w.RadiusMm is not null && w.RadiusMm.Value <= 0)
                    {
                        errs.Add($"window {w.WindowId} radiusMm must be a positive integer");
                    }
                    break;
            }
            errs.AddRange(OpKindErrors(op));
        }
        return errs;
    }

    /// <summary>
    /// The C#-only fail-closed op check shared by <see cref="ValidateAbilityDef"/> and <see cref="Rules.Make"/>: an op
    /// the sim does not run yields "unknown effect op &lt;Type&gt;", a combo op naming another table yields its error.
    /// </summary>
    /// <param name="op">One op.</param>
    /// <returns>Zero or one error.</returns>
    public static IEnumerable<string> OpKindErrors(EffectOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        switch (op)
        {
            case DamageOp or HealOp or ApplyEffectOp or RemoveEffectOp or InterruptOp or ResourceOp or ThreatOp or WindowOp or PayoffOp:
                return Array.Empty<string>();
            case ComboOp c when c.ComboTable is CombosTable or FieldCombosTable:
                return Array.Empty<string>();
            case ComboOp c:
                return new[] { $"combo op comboTable must be \"{CombosTable}\" or \"{FieldCombosTable}\" (got \"{c.ComboTable}\")" };
            default:
                return new[] { $"unknown effect op {op.GetType().Name}" };
        }
    }

    /// <summary>sim/combat/abilities.ts:100-104 abilitySchool: the first damage op's school, else "physical".</summary>
    /// <param name="def">The ability.</param>
    /// <returns>The school.</returns>
    public static string AbilitySchool(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        return def.Effects.OfType<DamageOp>().FirstOrDefault()?.School ?? "physical";
    }

    /// <summary>
    /// A line from a facet's <c>{kind:"line", length, width}</c> (content/data/facets: cinder.keystone.infernoBolt,
    /// warden.weapon.twinStrike). The sim line's radius is its half-width (abilities.ts:77-78, targeting.ts:92), so
    /// radius = width / 2 (non-negative, so C# <c>/</c> is the TS trunc).
    /// </summary>
    /// <param name="length">Line length, mm.</param>
    /// <param name="width">Full width, mm; must be positive.</param>
    /// <returns>The sim shape.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A non-positive width or a negative length.</exception>
    public static AbilityShape LineShape(long length, long width)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "line width must be positive");
        }
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "line length must be >= 0");
        }
        return new AbilityShape(ShapeKind.Line, Radius: width / 2, Length: length);
    }
}
