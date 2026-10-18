using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Reads an oracle setup file's <c>content</c> (ORACLE.md section 4: the RunContent exactly as the TS run held it,
/// so already in sim shape) into the lane-2A/2B combat records, and its party and pull spawns into actors. This is
/// a test-side reader for the parity checks only; the run root's content adapter is lane 2D. An op the reader does
/// not know throws, so a setup that grows a new op fails loudly instead of being skipped.
/// </summary>
public sealed class OracleSetupContent
{
    private OracleSetupContent(
        IReadOnlyDictionary<string, AbilityDef> abilities,
        IReadOnlyDictionary<string, EffectDef> effects,
        ComboTable combos,
        ComboTable fieldCombos,
        int lockoutTicks,
        IReadOnlyList<ActorState> actors)
    {
        Abilities = abilities;
        Effects = effects;
        Combos = combos;
        FieldCombos = fieldCombos;
        LockoutTicks = lockoutTicks;
        Actors = actors;
    }

    /// <summary>Abilities by id.</summary>
    public IReadOnlyDictionary<string, AbilityDef> Abilities { get; }

    /// <summary>Effect definitions by id.</summary>
    public IReadOnlyDictionary<string, EffectDef> Effects { get; }

    /// <summary>Burst combo table.</summary>
    public ComboTable Combos { get; }

    /// <summary>Field combo table.</summary>
    public ComboTable FieldCombos { get; }

    /// <summary>Interrupt lockout length.</summary>
    public int LockoutTicks { get; }

    /// <summary>The party then every pull spawn, as fresh actors (players then enemies).</summary>
    public IReadOnlyList<ActorState> Actors { get; }

    /// <summary>Loads a setup file from the oracle fixture directory.</summary>
    public static OracleSetupContent Load(string setupFileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(OracleFixtures.PathOf(setupFileName)));
        var root = doc.RootElement;
        var content = root.GetProperty("content");
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        foreach (var a in content.GetProperty("abilities").EnumerateObject())
        {
            abilities[a.Name] = Ability(a.Value);
        }
        var effects = new Dictionary<string, EffectDef>(StringComparer.Ordinal);
        foreach (var e in content.GetProperty("effects").EnumerateObject())
        {
            effects[e.Name] = Effect(e.Value);
        }
        var actors = new List<ActorState>();
        foreach (var p in root.GetProperty("party").EnumerateArray())
        {
            actors.Add(Actors.Create(
                p.GetProperty("id").GetInt32(),
                ActorKind.Player,
                p.GetProperty("classId").GetString()!,
                ActorTags.ParseRole(p.GetProperty("role").GetString()!),
                Pos(p.GetProperty("pos")),
                p.GetProperty("maxHealth").GetInt64(),
                p.GetProperty("maxResource").GetInt64(),
                p.TryGetProperty("yaw", out var y) ? y.GetInt32() : 0));
        }
        foreach (var pull in root.GetProperty("dungeon").GetProperty("pulls").EnumerateArray())
        {
            foreach (var s in pull.GetProperty("spawns").EnumerateArray())
            {
                actors.Add(Actors.Create(
                    s.GetProperty("actorId").GetInt32(),
                    ActorKind.Enemy,
                    s.GetProperty("classId").GetString()!,
                    ActorRole.None,
                    Pos(s.GetProperty("pos")),
                    s.GetProperty("health").GetInt64(),
                    0,
                    s.TryGetProperty("yaw", out var y) ? y.GetInt32() : 0));
            }
        }
        return new OracleSetupContent(
            abilities,
            effects,
            Table(content, "combos"),
            Table(content, "fieldCombos"),
            content.GetProperty("lockoutTicks").GetInt32(),
            actors);
    }

    private static Vec3 Pos(JsonElement p) => new(p.GetProperty("x").GetInt64(), p.GetProperty("y").GetInt64(), p.GetProperty("z").GetInt64());

    private static long? OptLong(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    private static bool? OptBool(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static ComboTable Table(JsonElement content, string key)
    {
        var t = new ComboTable();
        if (!content.TryGetProperty(key, out var rows))
        {
            return t;
        }
        foreach (var setup in rows.EnumerateObject())
        {
            foreach (var payoff in setup.Value.EnumerateObject())
            {
                t.Set(setup.Name, payoff.Name, new ComboDef(payoff.Value.GetProperty("id").GetString()!, Ops(payoff.Value.GetProperty("effects"))));
            }
        }
        return t;
    }

    private static AbilityDef Ability(JsonElement a)
    {
        var shape = a.GetProperty("shape");
        var kind = shape.GetProperty("kind").GetString() switch
        {
            "target" => ShapeKind.Target,
            "self" => ShapeKind.Self,
            "cone" => ShapeKind.Cone,
            "circle" => ShapeKind.Circle,
            "line" => ShapeKind.Line,
            var k => throw new ArgumentException($"unknown shape kind {k}"),
        };
        var cost = a.GetProperty("cost");
        return new AbilityDef
        {
            Id = a.GetProperty("id").GetString()!,
            CastTicks = a.GetProperty("castTicks").GetInt32(),
            GcdTicks = a.GetProperty("gcdTicks").GetInt32(),
            CooldownTicks = a.GetProperty("cooldownTicks").GetInt32(),
            Charges = a.GetProperty("charges").GetInt64(),
            Cost = new AbilityCost(cost.GetProperty("resource").GetString()!, cost.GetProperty("amount").GetInt64()),
            Range = a.GetProperty("range").GetInt64(),
            Shape = new AbilityShape(kind, OptLong(shape, "radius"), OptLong(shape, "angle1024"), OptLong(shape, "length")),
            Interruptible = a.GetProperty("interruptible").GetBoolean(),
            Effects = Ops(a.GetProperty("effects")),
            Tags = a.TryGetProperty("tags", out var tags) ? tags.EnumerateArray().Select(x => x.GetString()!).ToList() : null,
        };
    }

    private static EffectDef Effect(JsonElement e)
    {
        var m = e.GetProperty("modifiers");
        EffectPeriodic? periodic = null;
        if (e.TryGetProperty("periodic", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            periodic = new EffectPeriodic(p.GetProperty("everyTicks").GetInt32(), Op(p.GetProperty("op")));
        }
        var tag = e.TryGetProperty("dispellableTag", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        return new EffectDef(
            e.GetProperty("id").GetString()!,
            e.GetProperty("maxStacks").GetInt64(),
            tag,
            new EffectModifiers(OptLong(m, "damageDoneBp"), OptLong(m, "damageTakenBp"), OptLong(m, "hasteBp"), OptLong(m, "movementBp"), OptLong(m, "absorb")),
            periodic);
    }

    private static IReadOnlyList<EffectOp> Ops(JsonElement arr) => arr.EnumerateArray().Select(Op).ToList();

    private static EffectOp Op(JsonElement o) => o.GetProperty("op").GetString() switch
    {
        "damage" => new DamageOp(o.GetProperty("amount").GetInt64(), o.GetProperty("school").GetString()!),
        "heal" => new HealOp(o.GetProperty("amount").GetInt64()),
        "applyEffect" => new ApplyEffectOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("stacks").GetInt64()),
        "removeEffect" => new RemoveEffectOp(o.GetProperty("effectId").GetString()!),
        "interrupt" => new InterruptOp(),
        "resource" => new ResourceOp(o.GetProperty("delta").GetInt64()),
        "threat" => new ThreatOp(OptLong(o, "amount"), OptLong(o, "multiplierBp")),
        "window" => new WindowOp(
            o.GetProperty("windowId").GetString()!,
            o.GetProperty("durationTicks").GetInt32(),
            o.GetProperty("onTarget").GetBoolean(),
            o.GetProperty("setupClass").GetString()!,
            OptBool(o, "self"),
            OptBool(o, "alternate"),
            OptLong(o, "radiusMm")),
        "payoff" => new PayoffOp(),
        var k => throw new ArgumentException($"unknown effect op {k} in an oracle setup"),
    };
}
