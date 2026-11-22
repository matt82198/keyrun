using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using C = Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// f-fee76c81: a Cantor Vigil pack engaged beside a chamber wall must deal and take damage within 200 ticks of engage,
/// rather than stalling with no damage in either direction.
/// </summary>
public sealed class CantorWallStallTests
{
    private static NavLayout RealLayoutWithWalls()
    {
        var registry = C.ContentLoader.Load();
        var boxes = C.DungeonPlan.BoxesOf(registry.Layout);
        var walls = C.DungeonPlan.WallBoxes(boxes)
            .Concat(C.DungeonPlan.FrameBoxes(boxes))
            .Select(w => new Bounds(w.MinX, w.MaxX, w.MinZ, w.MaxZ))
            .ToList();
        return NavLayouts.ToSim(registry.Layout) with { Obstacles = walls };
    }

    private static AbilityDef MeleeSwing() => new()
    {
        Id = "choir.meleeSwing",
        CastTicks = 0,
        GcdTicks = 0,
        CooldownTicks = 40,
        Charges = 1,
        Cost = new AbilityCost("none", 0),
        Range = 3000,
        Shape = new AbilityShape(ShapeKind.Target),
        Interruptible = false,
        Effects = new EffectOp[] { new DamageOp(70, "physical") },
    };

    private static AbilityDef HealCast() => new()
    {
        Id = "choir.healCast",
        CastTicks = 80,
        GcdTicks = 0,
        CooldownTicks = 200,
        Charges = 1,
        Cost = new AbilityCost("none", 0),
        Range = 25000,
        Shape = new AbilityShape(ShapeKind.Circle, Radius: 25000),
        Interruptible = true,
        Effects = new EffectOp[] { new HealOp(6000) },
    };

    private static AbilityDef WardenStrike() => new()
    {
        Id = "warden.strike",
        CastTicks = 0,
        GcdTicks = 20,
        CooldownTicks = 0,
        Charges = 1,
        Cost = new AbilityCost("none", 0),
        Range = 3000,
        Shape = new AbilityShape(ShapeKind.Target),
        Interruptible = false,
        Effects = new EffectOp[] { new DamageOp(100, "physical") },
    };

    private static PackGroup CantorVigil(Vec3 center, int baseActorId, long? leashMm = null)
    {
        var spawns = new List<PullSpawn>();
        var id = baseActorId;
        for (var i = 0; i < 3; i++)
        {
            spawns.Add(new PullSpawn(id++, "choir.lieutenant", new Vec3(center.X, 0, center.Z - 1000), 0, 6000)
            {
                LeashMm = leashMm,
            });
        }
        for (var i = 0; i < 4; i++)
        {
            spawns.Add(new PullSpawn(id++, "choir.melee", new Vec3(center.X, 0, center.Z + 1000), 0, 6000)
            {
                SpeedMmPerTick = 207,
                LeashMm = leashMm,
            });
        }
        return new PackGroup("cantor-vigil", spawns, center, 4000);
    }

    private static (bool PlayerDealtDamage, bool EnemyDealtDamage, int? EngageTick) RunEngagement(
        NavLayout layout,
        PackGroup pack,
        Vec3 wardenStart,
        bool idleWander = false)
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            ["choir.meleeSwing"] = MeleeSwing(),
            ["choir.healCast"] = HealCast(),
            ["warden.strike"] = WardenStrike(),
        };
        var scripts = new Dictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var s in pack.Spawns)
        {
            scripts[s.ActorId] = s.ClassId == "choir.lieutenant"
                ? new[] { new ScriptStep(StepTrigger.Loop, "choir.healCast") { Every = 200 } }
                : new[] { new ScriptStep(StepTrigger.Loop, "choir.meleeSwing") { Every = 40 } };
        }
        var content = RunTestKit.Content(abilities, bounds: layout.Extent) with
        {
            Layout = layout,
            Packs = new[] { pack },
            EnemyScripts = scripts,
            IdleWander = idleWander,
        };
        var dungeon = new DungeonDef("wall-test", new[]
        {
            new PullDef("boss", new[] { new PullSpawn(900, "dummy", new Vec3(layout.Extent.MaxX - 1000, 0, 0), 0, 10000) }),
        });
        var party = new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, wardenStart.X, wardenStart.Z, 4000, 100) };
        var run = RunRoot.CreateRun(1, party, dungeon, content);
        var grid = NavGrid.Of(layout);

        int? engageTick = null;
        var playerDealt = false;
        var enemyDealt = false;
        var packIds = pack.Spawns.Select(s => s.ActorId).ToHashSet();

        for (var t = 0; t < 400; t++)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            var warden = run.State.Actors[1];
            if (warden.Alive)
            {
                var target = run.State.Actors.Values
                    .Where(a => packIds.Contains(a.Id) && a.Alive)
                    .OrderBy(a =>
                    {
                        var dx = a.Pos.X - warden.Pos.X;
                        var dz = a.Pos.Z - warden.Pos.Z;
                        return (dx * dx) + (dz * dz);
                    })
                    .FirstOrDefault();
                if (target is not null)
                {
                    if (NavFollow.MoveAlong(tick, warden, grid, target.Pos, 2000) is { } move)
                    {
                        commands.Add(move);
                    }
                    commands.Add(new CastCommand(tick, 1, "warden.strike", target.Id, null));
                }
            }

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "pack_engage" && ((JsString)e.Get("packId")!).Value == "cantor-vigil")
                {
                    engageTick ??= e.Tick;
                }
                if (e.Type == "damage" && engageTick is { } et && e.Tick <= et + 200)
                {
                    var src = (int)((JsInt)e.Get("sourceId")!).Value;
                    var tgt = (int)((JsInt)e.Get("targetId")!).Value;
                    if (src == 1 && packIds.Contains(tgt))
                    {
                        playerDealt = true;
                    }
                    if (packIds.Contains(src) && tgt == 1)
                    {
                        enemyDealt = true;
                    }
                }
            }

            if (engageTick is { } et2 && tick >= et2 + 200)
            {
                break;
            }
        }

        return (playerDealt, enemyDealt, engageTick);
    }

    [Fact(DisplayName = "f-fee76c81: Cantor Vigil engaged beside choir-room south wall deals and takes damage within 200 ticks")]
    public void Cantor_vigil_beside_choir_room_south_wall_exchanges_damage_within_200_ticks()
    {
        var layout = RealLayoutWithWalls();
        var pack = CantorVigil(new Vec3(14000, 0, -6000), 500);

        var (playerDealt, enemyDealt, engageTick) = RunEngagement(layout, pack, new Vec3(14000, 0, -2500));

        Assert.NotNull(engageTick);
        Assert.True(playerDealt, "player did not deal damage to Cantor Vigil within 200 ticks of engage");
        Assert.True(enemyDealt, "Cantor Vigil did not deal damage to player within 200 ticks of engage");
    }

    [Fact(DisplayName = "f-fee76c81: Cantor Vigil engaged beside seed-chamber south wall from entrance corner deals and takes damage within 200 ticks")]
    public void Cantor_vigil_beside_seed_chamber_south_wall_from_entrance_exchanges_damage_within_200_ticks()
    {
        var layout = RealLayoutWithWalls();
        var pack = CantorVigil(new Vec3(63500, 0, -6000), 500);

        var (playerDealt, enemyDealt, engageTick) = RunEngagement(layout, pack, new Vec3(61500, 0, -4500));

        Assert.NotNull(engageTick);
        Assert.True(playerDealt, "player did not deal damage to Cantor Vigil within 200 ticks of engage");
        Assert.True(enemyDealt, "Cantor Vigil did not deal damage to player within 200 ticks of engage");
    }

    [Fact(DisplayName = "f-fee76c81: Cantor Vigil flush on choir-room south wall with idle wander and leash exchanges damage within 200 ticks")]
    public void Cantor_vigil_flush_on_wall_with_wander_and_leash_exchanges_damage_within_200_ticks()
    {
        var layout = RealLayoutWithWalls();
        var pack = CantorVigil(new Vec3(14000, 0, -7000), 500, leashMm: 12000);

        var (playerDealt, enemyDealt, engageTick) = RunEngagement(layout, pack, new Vec3(14000, 0, -3500), idleWander: true);

        Assert.NotNull(engageTick);
        Assert.True(playerDealt, "player did not deal damage to Cantor Vigil within 200 ticks of engage");
        Assert.True(enemyDealt, "Cantor Vigil did not deal damage to player within 200 ticks of engage");
    }
}
