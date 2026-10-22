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
    /// <summary>
    /// content/sim-adapt.ts toSimDungeon (ids from <see cref="PullActorIdStart"/> in pull, group, count order; yaw 0;
    /// speed and chase margin only when the enemy sets them) plus content-dungeon.ts buildDungeon: Tempo-scaled health
    /// and scripts, and each boss appended as one more pull (R-MULTIBOSS-1: one per segment, in fight order, each at its
    /// own actor id; the Hollow Vault's single boss is pull "boss" at <see cref="BossActorId"/>).
    /// </summary>
    private static (DungeonDef Dungeon, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts) ToSimDungeon(
        C.ContentRegistry registry, C.DungeonDef dungeonDef, IReadOnlyList<BossSlot> bosses, int keyLevel, int trashHealthBp, int bossHealthBp)
    {
        // Plan M5: Fortified (trash) and Tyrannical (boss) health on top of Tempo, applied at spawn.
        var nextId = PullActorIdStart;
        var scripts = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        var pulls = new List<PullDef>();
        foreach (var pull in dungeonDef.Pulls)
        {
            var spawns = new List<PullSpawn>();
            foreach (var group in pull.Enemies)
            {
                var def = Enemy(registry, group.EnemyId, $"pull \"{pull.Id}\"");
                for (var i = 0; i < group.Count; i++)
                {
                    var actorId = nextId++;
                    spawns.Add(ScaledTrashSpawn(def, actorId, new Vec3(group.Spawn.X, 0, group.Spawn.Z), keyLevel, trashHealthBp));
                    scripts[actorId] = Tempo.ScaleScript(ToSim(def.Script), keyLevel, CastFloors(registry, keyLevel));
                }
            }
            pulls.Add(new PullDef(pull.Id, spawns));
        }

        // A boss pull's id is the layout's boss room key; NavLayouts.PlaceDungeon (Build, after this) stands it at the
        // arena origin and gives it the arena's aggroMm (layout.json), the same helper the bots use.
        foreach (var slot in bosses)
        {
            var boss = slot.Def;
            pulls.Add(new PullDef(slot.PullId, new[]
            {
                new PullSpawn(slot.ActorId, boss.Id, Vec3.Zero, BossYaw, Affixes.ScaleHealth(Tempo.ScaleHealth(boss.MaxHealth, keyLevel), bossHealthBp)),
            }));
            scripts[slot.ActorId] = Tempo.ScaleScript(ToSim(boss.Script), keyLevel, CastFloors(registry, keyLevel));
        }
        return (new DungeonDef(dungeonDef.Id, pulls), scripts);
    }

    /// <summary>One boss of a run: its pull id (<see cref="C.LayoutDef.BossPullIds"/>), enemy definition and sim actor id.</summary>
    /// <param name="PullId">The boss pull's id.</param>
    /// <param name="Def">The boss enemy.</param>
    /// <param name="ActorId">The boss actor's id (<see cref="BossActorIdOf"/>).</param>
    private sealed record BossSlot(string PullId, C.EnemyDef Def, int ActorId);

    /// <summary>
    /// D66: the sim script an enemy runs at <paramref name="keyLevel"/>, exactly as a pull or pack member gets it
    /// (Tempo-scaled, each loop floored against its cast's key-scaled cooldown, gates kept). For the seeded fill, which
    /// places roster packs the setup's layout does not carry, and for scenario tests of a single enemy kind.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="enemyId">The enemy.</param>
    /// <param name="keyLevel">The key level.</param>
    /// <returns>The scaled script.</returns>
    /// <exception cref="InvalidOperationException">The enemy is unknown.</exception>
    public static IReadOnlyList<ScriptStep> EnemyScript(C.ContentRegistry registry, string enemyId, int keyLevel)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var def = Enemy(registry, enemyId, "enemy script");
        return Tempo.ScaleScript(ToSim(def.Script), keyLevel, CastFloors(registry, keyLevel));
    }

    /// <summary>
    /// TEMPO-CAST-FLOOR: ability id to <see cref="Tempo.ScaledCastFloorTicks"/> (the key-scaled cooldown and the cast
    /// time), so Tempo never schedules a cast before its ability is ready (BIBLE s3) while the cooldown itself scales
    /// with the key (<see cref="Tempo.ScaleEnemyCooldowns"/>, BIBLE s8). An id the registry lacks gets 0 (no extra floor).
    /// </summary>
    private static Func<string, int> CastFloors(C.ContentRegistry registry, int keyLevel) =>
        id => registry.Abilities.TryGetValue(id, out var a) ? Tempo.ScaledCastFloorTicks(a.CooldownTicks, a.CastTicks, keyLevel) : 0;

    /// <summary>
    /// One trash spawn as content/sim-adapt.ts toSimDungeon builds it (yaw 0; speed and chase margin only when the enemy
    /// sets them), Tempo-scaled, then Fortified (trash health affix) on top.
    /// </summary>
    private static PullSpawn ScaledTrashSpawn(C.EnemyDef def, int actorId, Vec3 pos, int keyLevel, int trashHealthBp)
    {
        var spawn = new PullSpawn(actorId, def.Id, pos, 0, def.MaxHealth)
        {
            SpeedMmPerTick = def.SpeedMmPerTick,
            ChaseMarginMm = def.ChaseMarginMm,
        };
        var scaled = Tempo.ScaleSpawn(spawn, keyLevel);
        return scaled with { Health = Affixes.ScaleHealth(scaled.Health, trashHealthBp) };
    }

    /// <summary>
    /// PATH-PACKS: a scaled template (<see cref="ScaledTrashSpawn"/>, Tempo-scaled script floored against each cast's
    /// key-scaled cooldown, <see cref="CastFloors"/>, as a pull's script is) for every layout pack enemy no
    /// pull of <paramref name="dungeonDef"/> fields, such as the Hall Sentry; an enemy a pull fields is copied from that
    /// pull's spawn by <see cref="LayoutPackGroups.Build"/> instead. Fails closed on an enemy id content does not define.
    /// </summary>
    private static IReadOnlyDictionary<string, PackTemplate> PackOnlyTemplates(
        C.ContentRegistry registry, C.LayoutDef layout, C.DungeonDef dungeonDef, int keyLevel, int trashHealthBp)
    {
        var fielded = dungeonDef.Pulls.SelectMany(p => p.Enemies).Select(e => e.EnemyId).ToHashSet(StringComparer.Ordinal);
        var templates = new SortedDictionary<string, PackTemplate>(StringComparer.Ordinal);
        foreach (var pack in layout.Packs)
        {
            foreach (var group in pack.Enemies.Where(g => !fielded.Contains(g.EnemyId) && !templates.ContainsKey(g.EnemyId)))
            {
                var def = Enemy(registry, group.EnemyId, $"pack \"{pack.Id}\"");
                templates[def.Id] = new PackTemplate(
                    ScaledTrashSpawn(def, 0, Vec3.Zero, keyLevel, trashHealthBp),
                    Tempo.ScaleScript(ToSim(def.Script), keyLevel, CastFloors(registry, keyLevel)));
            }
        }
        return templates;
    }

    private static C.EnemyDef Enemy(C.ContentRegistry registry, string id, string where) =>
        registry.Enemies.TryGetValue(id, out var def)
            ? def
            : throw new InvalidOperationException($"run setup: {where} references unknown enemy \"{id}\"");

    private static IReadOnlyList<ScriptStep> ToSim(IEnumerable<C.ScriptStep> script) => script.Select(ToSim).ToList();

    private static ScriptStep ToSim(C.ScriptStep s)
    {
        var step = s.At.Kind switch
        {
            C.ScriptAtKind.Loop => ScriptStep.Loop(s.Every, s.Cast),
            C.ScriptAtKind.Tick => ScriptStep.At(s.At.Tick, s.Cast, s.Every),
            C.ScriptAtKind.OnDeath => ScriptStep.OnDeath(s.Cast),
            _ => throw new InvalidOperationException($"run setup: script step casting \"{s.Cast}\" has unknown trigger {s.At.Kind}"),
        };
        return step with
        {
            Telegraph = s.Telegraph is { } t
                ? new TelegraphSpec(ShapeName(t.Shape), t.LeadTicks) { Radius = t.Radius, Angle1024 = t.Angle1024, Length = t.Length, Width = t.Width, SpansCast = t.SpansCast ?? false, AnchorTarget = t.Anchor == C.TelegraphAnchor.Target }
                : null,
            Target = s.Target switch
            {
                null => null,
                C.ScriptTarget.Self => ScriptTargetOverride.Self,
                C.ScriptTarget.Threat => ScriptTargetOverride.Threat,
                C.ScriptTarget.Random => ScriptTargetOverride.Random,
                _ => throw new InvalidOperationException($"run setup: script step casting \"{s.Cast}\" has unknown target {s.Target}"),
            },
            When = s.When is { } w ? new ScriptGate(w.HasEffect, w.HealthBelowBp) : null,
        };
    }

    private static string ShapeName(C.TelegraphShape shape) => shape switch
    {
        C.TelegraphShape.Circle => "circle",
        C.TelegraphShape.Cone => "cone",
        C.TelegraphShape.Line => "line",
        _ => throw new InvalidOperationException($"run setup: unknown telegraph shape {shape}"),
    };
}
