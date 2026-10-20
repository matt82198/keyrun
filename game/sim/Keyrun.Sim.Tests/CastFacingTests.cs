using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane CAST-FACING (D10, Matt: "chanter stands backwards while casting"). The 3D view drew every actor at its sim
/// <see cref="ActorState.Yaw"/>, which only a move command sets, so a caster that walked one way and then cast at
/// someone behind it was drawn facing away for the whole cast (the k2-s11 oracle trace has Cinders 177 degrees off
/// their flare target). <see cref="CastFacing.RenderYaw"/> is the facing the view draws. Every case starts a REAL cast
/// through <see cref="Rules.Make"/> with the shapes, cast times and ranges read from content/data/abilities/*.json.
/// </summary>
public class CastFacingTests
{
    private const int CasterId = 1;
    private const int TargetId = 2;
    private const int Backwards = 768; // facing -z: the caster just walked away from a target at +z

    /// <summary>Every ability with a cast time whose shape is "target" or "circle", across all classes and enemies.</summary>
    public static IEnumerable<object[]> AimedCasts() =>
        Content().Values.Where(d => d.CastTicks > 0 && d.Shape.Kind is ShapeKind.Target or ShapeKind.Circle).Select(d => new object[] { d.Id });

    [Fact]
    public void ChanterFacesTheAllySheHealsWhileCastingCadence()
    {
        var (state, abilities) = CastAt("chanter.cadence", ActorKind.Player, ActorKind.Player, targetX: 0, targetZ: 3000);

        var chanter = state.Actors[CasterId];
        Assert.Equal("chanter.cadence", chanter.CastId);
        Assert.Equal(Backwards, chanter.Yaw); // the sim facing is untouched: still walking away
        Assert.Equal(256, CastFacing.RenderYaw(state, chanter, abilities)); // drawn facing +z, the tank
    }

    [Theory]
    [MemberData(nameof(AimedCasts))]
    public void EveryAimedCastIsDrawnFacingItsTarget(string abilityId)
    {
        var (state, abilities) = CastAt(abilityId, ActorKind.Player, ActorKind.Enemy, targetX: -1000, targetZ: 0); // inside seed.lash 1500 mm, the shortest range

        var caster = state.Actors[CasterId];
        Assert.Equal(abilityId, caster.CastId);
        Assert.Equal(512, CastFacing.RenderYaw(state, caster, abilities)); // drawn facing -x, the target
    }

    [Fact]
    public void AConeCastKeepsTheSimFacingItAimsAlong()
    {
        var (state, abilities) = CastAt("hollowWarden.cleave", ActorKind.Enemy, ActorKind.Player, targetX: 0, targetZ: 3000);

        var warden = state.Actors[CasterId];
        Assert.Equal("hollowWarden.cleave", warden.CastId);
        Assert.Equal(Backwards, CastFacing.RenderYaw(state, warden, abilities)); // the cone resolves along Yaw
    }

    [Fact]
    public void ASelfCastKeepsTheMovementFacing()
    {
        var self = Def("test.selfSong", new AbilityShape(ShapeKind.Self), new EffectOp[] { new HealOp(1) }, castTicks: 20);
        var abilities = AbilityMap(self);
        var state = Arena(abilities, ActorKind.Player, ActorKind.Player, 0, 3000, out var rules);
        state.Actors[CasterId].TargetId = TargetId;
        Tick(state, rules, Cmds(Cast(CasterId, self.Id)), _ => { });

        var caster = state.Actors[CasterId];
        Assert.Equal(self.Id, caster.CastId);
        Assert.Equal(Backwards, CastFacing.RenderYaw(state, caster, abilities));
    }

    [Fact]
    public void AChanterHealingHerselfKeepsTheMovementFacing()
    {
        var abilities = Content();
        var state = Arena(abilities, ActorKind.Player, ActorKind.Player, 0, 3000, out var rules);
        Tick(state, rules, Cmds(Cast(CasterId, "chanter.cadence", CasterId)), _ => { });

        var chanter = state.Actors[CasterId];
        Assert.Equal("chanter.cadence", chanter.CastId);
        Assert.Equal(Backwards, CastFacing.RenderYaw(state, chanter, abilities));
    }

    [Fact]
    public void ACastWhoseTargetDiedKeepsTheMovementFacing()
    {
        var (state, abilities) = CastAt("cinder.flare", ActorKind.Player, ActorKind.Enemy, targetX: 0, targetZ: 3000);
        state.Actors[TargetId].Alive = false;

        Assert.Equal(Backwards, CastFacing.RenderYaw(state, state.Actors[CasterId], abilities));
    }

    [Fact]
    public void AnIdleActorAndAnUnloadedRunKeepTheMovementFacing()
    {
        var (state, abilities) = CastAt("chanter.cadence", ActorKind.Player, ActorKind.Player, targetX: 0, targetZ: 3000);
        var chanter = state.Actors[CasterId];

        Assert.Equal(Backwards, CastFacing.RenderYaw(state, chanter, null));
        Assert.Equal(Backwards, CastFacing.RenderYaw(state, state.Actors[TargetId], abilities)); // not casting
    }

    private static (SimState State, IReadOnlyDictionary<string, AbilityDef> Abilities) CastAt(
        string abilityId, ActorKind casterKind, ActorKind targetKind, long targetX, long targetZ)
    {
        var abilities = Content();
        var state = Arena(abilities, casterKind, targetKind, targetX, targetZ, out var rules);
        Tick(state, rules, Cmds(Cast(CasterId, abilityId, TargetId)), _ => { });
        return (state, abilities);
    }

    private static SimState Arena(
        IReadOnlyDictionary<string, AbilityDef> abilities, ActorKind casterKind, ActorKind targetKind, long targetX, long targetZ, out StepRules rules)
    {
        rules = Rules.Make(Config(abilities));
        var caster = Make(CasterId, casterKind, "caster", maxHealth: 1000, maxResource: 100, yaw: Backwards);
        caster.Resource = 100;
        var target = Make(TargetId, targetKind, "target", x: targetX, z: targetZ, maxHealth: 1000, yaw: Backwards);
        return At(0, caster, target);
    }

    // The facing-relevant fields of every ability in content/data/abilities/*.json of both content roots (repo root and
    // game/content/data; id, castTicks, range, shape), at no cost and no cooldown so each cast starts; the ops are a
    // 1-point heal (no case lets a cast complete).
    private static IReadOnlyDictionary<string, AbilityDef> Content()
    {
        var map = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        foreach (var file in ContentRoots.KindFiles(AppContext.BaseDirectory, "abilities"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var a in doc.RootElement.EnumerateArray())
            {
                var shape = a.GetProperty("shape");
                var kind = shape.GetProperty("kind").GetString() switch
                {
                    "self" => ShapeKind.Self,
                    "target" => ShapeKind.Target,
                    "circle" => ShapeKind.Circle,
                    "cone" => ShapeKind.Cone,
                    "line" => ShapeKind.Line,
                    var other => throw new InvalidDataException($"{file}: unknown shape {other}"),
                };
                var def = Def(
                    a.GetProperty("id").GetString()!,
                    new AbilityShape(kind, Opt(shape, "radius"), Opt(shape, "angle1024"), Opt(shape, "length")),
                    new EffectOp[] { new HealOp(1) },
                    castTicks: a.GetProperty("castTicks").GetInt32(),
                    range: a.GetProperty("range").GetInt64());
                map[def.Id] = def;
            }
        }
        Assert.Contains("chanter.cadence", map.Keys);
        return map;
    }

    private static long? Opt(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetInt64() : null;
}
