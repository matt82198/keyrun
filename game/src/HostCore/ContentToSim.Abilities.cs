using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

public sealed partial record HostRunSetup
{
    // run-setup.ts:61 resolveVariant: a stance ability's static Effects are its "mend" variant (the opening verse).
    // BIBLE section 5: the variants also ride along, and the sim (Abilities.ResolveVerse) plays the caster's verse.
    private static AbilityDef ToSim(C.AbilityDef a)
    {
        var ops = a.Variants is { } v && v.TryGetValue("mend", out var mend) ? mend : a.Effects;
        Dictionary<string, IReadOnlyList<EffectOp>>? variants = null;
        if (a.Variants is { } all)
        {
            variants = new Dictionary<string, IReadOnlyList<EffectOp>>(StringComparer.Ordinal);
            foreach (var kv in all)
            {
                variants[kv.Key] = kv.Value.Select(o => ToSim(o, a.Id + " variant " + kv.Key)).ToList();
            }
        }
        return new AbilityDef
        {
            Id = a.Id,
            CastTicks = a.CastTicks,
            GcdTicks = a.GcdTicks,
            CooldownTicks = a.CooldownTicks,
            Charges = a.Charges,
            Cost = new AbilityCost(a.Cost.Resource, a.Cost.Amount),
            Range = a.Range,
            Shape = ToSim(a.Shape, a.Id),
            Interruptible = a.Interruptible,
            Effects = ops.Select(o => ToSim(o, a.Id)).ToList(),
            Tags = a.Tags.ToList(),
            Variants = variants,
        };
    }

    // A content shape in sim terms. A width (a Facet's line: twinStrike, infernoBolt) is the line's full width, and the
    // sim line's radius is its half-width (Abilities.LineShape); a width on any other kind fails closed.
    private static AbilityShape ToSim(C.AbilityShape s, string where)
    {
        if (s.Width is not { } width)
        {
            return new AbilityShape(ToSim(s.Kind), s.Radius, s.Angle1024, s.Length);
        }
        if (s.Kind != C.ShapeKind.Line)
        {
            throw new InvalidOperationException($"run setup: {where} has a width on a {s.Kind} shape (only a line has one)");
        }
        return Abilities.LineShape(s.Length ?? 0, width);
    }

    private static ShapeKind ToSim(C.ShapeKind k) => k switch
    {
        C.ShapeKind.Target => ShapeKind.Target,
        C.ShapeKind.Self => ShapeKind.Self,
        C.ShapeKind.Cone => ShapeKind.Cone,
        C.ShapeKind.Circle => ShapeKind.Circle,
        C.ShapeKind.Line => ShapeKind.Line,
        _ => throw new InvalidOperationException($"run setup: unknown shape kind {k}"),
    };

    // The content-to-sim op mapping lives in Keyrun.Bots.Shared.ContentCombos so the headless tests run it (f-89b0825e).
    private static EffectOp ToSim(C.EffectOp op, string where) => ContentCombos.ToSimOp(op, where);

    private static EffectDef ToSim(C.EffectDef e)
    {
        var m = e.Modifiers;
        return new EffectDef(
            e.Id,
            e.MaxStacks,
            e.DispellableTag,
            new EffectModifiers(m.DamageDoneBp, m.DamageTakenBp, m.HasteBp, m.MovementBp, m.Absorb),
            e.Periodic is { } p ? new EffectPeriodic(p.EveryTicks, ToSim(p.Op, e.Id + ".periodic")) : null);
    }

    private static Keyrun.Sim.Encounter.ClauseDef ToSim(C.ClauseDef c) => new(
        c.Id,
        c.Name,
        c.Kind switch
        {
            C.ClauseKind.Echo => Keyrun.Sim.Encounter.ClauseKind.Echo,
            C.ClauseKind.Tide => Keyrun.Sim.Encounter.ClauseKind.Tide,
            C.ClauseKind.Hush => Keyrun.Sim.Encounter.ClauseKind.Hush,
            C.ClauseKind.Pact => Keyrun.Sim.Encounter.ClauseKind.Pact,
            _ => throw new InvalidOperationException($"run setup: clause \"{c.Id}\" has unknown kind {c.Kind}"),
        },
        c.Params.ToDictionary(kv => kv.Key, kv => (long)kv.Value, StringComparer.Ordinal),
        c.Description);
}
