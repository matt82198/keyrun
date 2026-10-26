using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Keyrun.Content.Tests;

/// <summary>
/// The C# generator for the talent patch parity snapshot (<c>Fixtures/talent-patch-parity.json</c>). It reproduces the
/// output of the removed TS harness (<c>tests/content/talent-patch-parity.test.ts</c> at merge 07e8bdd2^) from the
/// loaded content: every shipped talent option and every shipped facet is patched onto its base ability with the
/// frozen TS patch semantics (<c>content/apply-patch.ts applyPatch</c>: each field the patch sets replaces the base field
/// whole, <c>tagAdd</c>/<c>tagRemove</c> edit the tags, <c>effectsAppend</c> appends ops, and a patch's variants replace
/// the base variants without being replayed), and each row records the verdict of the production loadout check
/// (<c>content/apply.ts applyLoadout</c>): <c>"ok"</c>, or the exact message the TS throws when the rotational abilities
/// do not keep exactly 2 Setup and 2 Payoff tags.
/// </summary>
/// <remarks>
/// The output is a JSON document with sorted keys at every level, two-space indentation, LF newlines and a trailing
/// newline, byte-identical to the committed fixture for the shipped content. Regenerate the fixture with the
/// <c>KEYRUN_REGEN_PARITY=1</c> switch (see <c>TalentPatchParityRegenTests</c>); a default test run never writes it.
/// </remarks>
public static class TalentPatchParitySnapshot
{
    /// <summary>The snapshot's <c>kind</c> tag.</summary>
    public const string Kind = "keyrun-talent-patch-parity";

    /// <summary>The snapshot's <c>version</c>.</summary>
    public const int Version = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Builds the snapshot document for <paramref name="reg"/>.</summary>
    /// <param name="reg">The loaded content.</param>
    /// <returns>The snapshot JSON text, LF newlines, with a trailing newline.</returns>
    /// <exception cref="InvalidOperationException">A class or facet names an ability the content does not define.</exception>
    public static string Generate(ContentRegistry reg)
    {
        ArgumentNullException.ThrowIfNull(reg);

        var talents = new JsonArray();
        foreach (var cls in reg.Classes.Values)
        {
            foreach (var fork in cls.Talents)
            {
                foreach (var option in fork.Options)
                {
                    var patched = Patch(Base(reg, fork.AbilityId), option.Patch, facet: null);
                    talents.Add(Obj(
                        ("classId", cls.Id),
                        ("abilityId", fork.AbilityId),
                        ("optionId", option.Id),
                        ("loadout", Verdict(cls, WorkingSet(reg, cls, patched))),
                        ("ability", AbilityNode(patched))));
                }
            }
        }

        var facets = new JsonArray();
        foreach (var facet in reg.Facets.Values)
        {
            var cls = OwnerOf(reg, facet.AbilityId);
            var patched = Patch(Base(reg, facet.AbilityId), facet.Patch, facet.Patch);
            facets.Add(Obj(
                ("classId", cls.Id),
                ("abilityId", facet.AbilityId),
                ("facetId", facet.Id),
                ("loadout", Verdict(cls, WorkingSet(reg, cls, patched))),
                ("ability", AbilityNode(patched))));
        }

        var root = Obj(
            ("facets", facets),
            ("kind", Kind),
            ("talents", talents),
            ("version", Version));
        return root.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n";
    }

    // content/apply-patch.ts applyPatch: direct fields replace whole; tagAdd/tagRemove/effectsAppend merge. The TS
    // engine does not replay a talent's Effects onto variants and does not fill a missing setupClass, so neither does this.
    private static AbilityDef Patch(AbilityDef ability, AbilityPatch patch, FacetPatch? facet)
    {
        var tags = patch.Tags ?? ability.Tags;
        var effects = patch.Effects ?? ability.Effects;
        if (facet?.TagAdd is { } add)
            tags = new ValueList<string>(tags.Concat(add).Distinct(StringComparer.Ordinal));
        if (facet?.TagRemove is { } remove)
        {
            var drop = new HashSet<string>(remove, StringComparer.Ordinal);
            tags = new ValueList<string>(tags.Where(t => !drop.Contains(t)));
        }
        if (facet?.EffectsAppend is { } append)
            effects = new ValueList<EffectOp>(effects.Concat(append));
        return ability with
        {
            Name = patch.Name ?? ability.Name,
            CastTicks = patch.CastTicks ?? ability.CastTicks,
            GcdTicks = patch.GcdTicks ?? ability.GcdTicks,
            CooldownTicks = patch.CooldownTicks ?? ability.CooldownTicks,
            Charges = patch.Charges ?? ability.Charges,
            Cost = patch.Cost ?? ability.Cost,
            Range = patch.Range ?? ability.Range,
            Shape = patch.Shape ?? ability.Shape,
            Interruptible = patch.Interruptible ?? ability.Interruptible,
            Tags = tags,
            Effects = effects,
            Variants = patch.Variants ?? ability.Variants,
        };
    }

    private static AbilityDef Base(ContentRegistry reg, string abilityId) =>
        reg.Abilities.TryGetValue(abilityId, out var def)
            ? def
            : throw new InvalidOperationException("talent parity: ability \"" + abilityId + "\" is not in the content");

    private static ClassDef OwnerOf(ContentRegistry reg, string abilityId) =>
        reg.Classes.Values.FirstOrDefault(c => SlotIds(c).Contains(abilityId))
        ?? throw new InvalidOperationException("talent parity: no class uses ability \"" + abilityId + "\"");

    private static IEnumerable<string> SlotIds(ClassDef cls)
    {
        var a = cls.Abilities;
        return new[] { a.Rot1, a.Rot2, a.Rot3, a.Rot4, a.Rot5, a.Interrupt, a.Defensive, a.Utility, a.Movement };
    }

    // The class's nine ability slots at base, with the one patched ability swapped in (applyLoadout's working set).
    private static Dictionary<string, AbilityDef> WorkingSet(ContentRegistry reg, ClassDef cls, AbilityDef patched)
    {
        var working = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        foreach (var id in SlotIds(cls))
            working[id] = id == patched.Id ? patched : Base(reg, id);
        return working;
    }

    // applyLoadout's invariant: exactly 2 Setup and 2 Payoff rotational abilities, with its exact message otherwise.
    private static string Verdict(ClassDef cls, IReadOnlyDictionary<string, AbilityDef> working)
    {
        var a = cls.Abilities;
        var rot = new[] { a.Rot1, a.Rot2, a.Rot3, a.Rot4, a.Rot5 }.Select(id => working[id]).ToList();
        var setups = rot.Count(r => r.Tags.Any(t => t.StartsWith("setup:", StringComparison.Ordinal)));
        var payoffs = rot.Count(r => r.Tags.Any(t => t == "payoff"));
        if (setups == 2 && payoffs == 2) return "ok";
        var detail = string.Join(", ", rot.Select(r => r.Id + "=[" + string.Join(",", r.Tags) + "]"));
        return "applyLoadout: class \"" + cls.Id + "\" loadout would leave " + setups + " setup / " + payoffs +
            " payoff rotational abilities (bible invariant requires exactly 2/2). Rotational tags: " + detail;
    }

    private static JsonObject AbilityNode(AbilityDef ability)
    {
        var variants = ability.Variants is null ? null : VariantsNode(ability.Variants);
        return Obj(
            ("id", ability.Id),
            ("name", ability.Name),
            ("castTicks", ability.CastTicks),
            ("gcdTicks", ability.GcdTicks),
            ("cooldownTicks", ability.CooldownTicks),
            ("charges", ability.Charges),
            ("cost", Obj(("amount", ability.Cost.Amount), ("resource", ability.Cost.Resource))),
            ("range", ability.Range),
            ("shape", ShapeNode(ability.Shape)),
            ("interruptible", ability.Interruptible),
            ("tags", Arr(ability.Tags)),
            ("effects", Arr(ability.Effects.Select(OpNode))),
            ("variants", variants));
    }

    private static JsonObject VariantsNode(ValueMap<ValueList<EffectOp>> variants)
    {
        var node = new JsonObject();
        foreach (var entry in variants.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            node.Add(entry.Key, Arr(entry.Value.Select(OpNode)));
        return node;
    }

    private static JsonObject ShapeNode(AbilityShape shape) => Obj(
        ("kind", KindName(shape.Kind)),
        ("radius", shape.Radius),
        ("angle1024", shape.Angle1024),
        ("length", shape.Length),
        ("width", shape.Width));

    private static string KindName(ShapeKind kind) => kind switch
    {
        ShapeKind.Target => "target",
        ShapeKind.Self => "self",
        ShapeKind.Cone => "cone",
        ShapeKind.Circle => "circle",
        ShapeKind.Line => "line",
        _ => throw new InvalidOperationException("talent parity: no snapshot name for shape kind " + kind),
    };

    // One effect op in its content JSON shape: the "op" discriminator, then each field the content sets (null omitted).
    private static JsonObject OpNode(EffectOp op) => op switch
    {
        DamageOp d => Obj(("op", "damage"), ("amount", d.Amount), ("school", d.School)),
        DamagePctOp d => Obj(("op", "damagePct"), ("maxHealthBp", d.MaxHealthBp), ("school", d.School)),
        PullOp p => Obj(("op", "pull"), ("distanceMm", p.DistanceMm)),
        DisplaceOp d => Obj(("op", "displace"), ("distanceMm", d.DistanceMm), ("freesBinds", Flag(d.FreesBinds)), ("towardTarget", Flag(d.TowardTarget))),
        HealOp h => Obj(("op", "heal"), ("amount", h.Amount)),
        ApplyEffectOp a => Obj(("op", "applyEffect"), ("effectId", a.EffectId), ("durationTicks", a.DurationTicks), ("stacks", a.Stacks)),
        RemoveEffectOp r => Obj(("op", "removeEffect"), ("effectId", r.EffectId)),
        InterruptOp => Obj(("op", "interrupt")),
        ResourceOp r => Obj(("op", "resource"), ("delta", r.Delta), ("resource", r.Resource)),
        ThreatOp t => Obj(("op", "threat"), ("amount", t.Amount), ("multiplierBp", t.MultiplierBp)),
        WindowOp w => Obj(
            ("op", "window"),
            ("windowId", w.WindowId),
            ("durationTicks", w.DurationTicks),
            ("onTarget", w.OnTarget),
            ("setupClass", w.SetupClass),
            ("self", w.Self),
            ("alternate", w.Alternate),
            ("radiusMm", w.RadiusMm)),
        PayoffOp => Obj(("op", "payoff")),
        ComboOp c => Obj(("op", "combo"), ("comboTable", c.ComboTable)),
        PartyEffectOp p => Obj(("op", "partyEffect"), ("effectId", p.EffectId), ("durationTicks", p.DurationTicks), ("stacks", p.Stacks)),
        CloudOp c => Obj(("op", "cloud"), ("effectId", c.EffectId), ("durationTicks", c.DurationTicks), ("radiusMm", c.RadiusMm)),
        CloserBurnOp c => Obj(("op", "closerBurn"), ("maxHealthBp", c.MaxHealthBp), ("capBp", c.CapBp), ("school", c.School)),
        _ => throw new InvalidOperationException("talent parity: no snapshot shape for effect op " + op.GetType().Name),
    };

    // A boolean the content writes only when set: false and unset both omit the key.
    private static object? Flag(bool value) => value ? true : null;

    // An object with its non-null members written in key order (the snapshot sorts every object's keys).
    private static JsonObject Obj(params (string Key, object? Value)[] members)
    {
        var node = new JsonObject();
        foreach (var (key, value) in members.Where(m => m.Value is not null).OrderBy(m => m.Key, StringComparer.Ordinal))
            node.Add(key, Node(value));
        return node;
    }

    private static JsonArray Arr(IEnumerable<object?> items)
    {
        var node = new JsonArray();
        foreach (var item in items) node.Add(Node(item));
        return node;
    }

    private static JsonNode Node(object? value) => value switch
    {
        JsonNode n => n,
        string s => JsonValue.Create(s)!,
        int i => JsonValue.Create(i)!,
        bool b => JsonValue.Create(b)!,
        _ => throw new InvalidOperationException("talent parity: cannot write a " + value?.GetType().Name + " into the snapshot"),
    };
}
