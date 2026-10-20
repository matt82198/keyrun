using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using C = Keyrun.Content;

namespace Keyrun.Bots.Shared;

/// <summary>
/// PATH-PACKS: the content layout's packs (layout.json <c>packs</c>, <see cref="C.LayoutPack"/>) as the sim's
/// <see cref="PackGroup"/>s, with their members' scripts. It lives beside <see cref="NavLayouts"/> because Keyrun.Bots is
/// the Godot-free assembly that sees both Keyrun.Content and Keyrun.Sim: the host (HostRunSetup) arms runs with it and the
/// route tests use the same conversion.
/// </summary>
public static class LayoutPackGroups
{
    /// <summary>The host's first pack actor id: above the teaching pulls (from 100) and below the boss (900).</summary>
    public const int FirstActorId = 500;

    /// <summary>
    /// The packs of <paramref name="layout"/> as sim groups. A member is a copy of the first spawn of the same enemy in
    /// <paramref name="dungeon"/> (pull order), so it carries that spawn's Tempo- and affix-scaled health, speed and chase
    /// margin and runs that spawn's script; it stands at the pack centre plus its group's offset, yaw 0. Actor ids run from
    /// <paramref name="firstActorId"/> in pack, group, count order. Fails closed on a member whose enemy no pull fields
    /// (and that <paramref name="packOnly"/> does not give either), on an enemy both a pull and <paramref name="packOnly"/>
    /// give (two sources of scaling), and on an id the dungeon or the scripts already use.
    /// </summary>
    /// <param name="layout">A validated content layout.</param>
    /// <param name="dungeon">The placed sim dungeon (<see cref="NavLayouts.PlaceDungeon"/>).</param>
    /// <param name="scripts">The run's enemy scripts, keyed by actor id.</param>
    /// <param name="firstActorId">The first pack member's actor id.</param>
    /// <param name="packOnly">
    /// Templates for enemies only a pack fields (Mythic+ trash such as <c>hall.sentry</c>), keyed by enemy id: a spawn
    /// already Tempo- and affix-scaled by the caller exactly as a pull spawn is (its actor id and position are replaced)
    /// and its Tempo-scaled script. Null for none.
    /// </param>
    /// <returns>The packs, and the scripts with every pack member's added.</returns>
    /// <exception cref="InvalidOperationException">A member has no template or two, or an id is taken.</exception>
    public static (IReadOnlyList<PackGroup> Packs, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts) Build(
        C.LayoutDef layout, DungeonDef dungeon, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts, int firstActorId = FirstActorId,
        IReadOnlyDictionary<string, PackTemplate>? packOnly = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(scripts);
        var templates = new Dictionary<string, PullSpawn>(StringComparer.Ordinal);
        foreach (var spawn in dungeon.Pulls.SelectMany(p => p.Spawns))
        {
            templates.TryAdd(spawn.ClassId, spawn);
        }
        foreach (var enemyId in (packOnly?.Keys ?? Enumerable.Empty<string>()).Where(templates.ContainsKey))
        {
            throw new InvalidOperationException($"pack template \"{enemyId}\": a pull already fields this enemy, so it would have two scaled spawns");
        }
        var taken = dungeon.Pulls.SelectMany(p => p.Spawns).Select(s => s.ActorId).Concat(scripts.Keys).ToHashSet();
        var merged = new SortedDictionary<int, IReadOnlyList<ScriptStep>>(scripts.ToDictionary(kv => kv.Key, kv => kv.Value));
        var packs = new List<PackGroup>();
        var nextId = firstActorId;
        foreach (var pack in layout.Packs)
        {
            var spawns = new List<PullSpawn>();
            // STATE.md D47: pack-only members swing on their own timers, spread over one swing period (StaggerSwing).
            var packOnlyCount = packOnly is null
                ? 0
                : pack.Enemies.Where(g => !templates.ContainsKey(g.EnemyId) && packOnly.ContainsKey(g.EnemyId)).Sum(g => g.Count);
            var packOnlyIndex = 0;
            foreach (var group in pack.Enemies)
            {
                PullSpawn template;
                IReadOnlyList<ScriptStep>? script;
                var staggered = false;
                if (templates.TryGetValue(group.EnemyId, out var pullTemplate))
                {
                    template = pullTemplate;
                    if (!scripts.TryGetValue(template.ActorId, out script))
                    {
                        throw new InvalidOperationException($"pack \"{pack.Id}\": enemy \"{group.EnemyId}\" (template actor {template.ActorId}) has no script");
                    }
                }
                else if (packOnly is not null && packOnly.TryGetValue(group.EnemyId, out var direct))
                {
                    if (!string.Equals(direct.Spawn.ClassId, group.EnemyId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"pack \"{pack.Id}\": template for \"{group.EnemyId}\" spawns \"{direct.Spawn.ClassId}\"");
                    }
                    template = direct.Spawn;
                    script = direct.Script;
                    staggered = true;
                }
                else
                {
                    throw new InvalidOperationException($"pack \"{pack.Id}\": no pull fields enemy \"{group.EnemyId}\" and no pack template gives it, so it has no scaled spawn to copy");
                }
                for (var i = 0; i < group.Count; i++)
                {
                    var actorId = nextId++;
                    if (!taken.Add(actorId))
                    {
                        throw new InvalidOperationException($"pack \"{pack.Id}\": actor id {actorId} is already used by the run");
                    }
                    spawns.Add(template with
                    {
                        ActorId = actorId,
                        Pos = new Vec3(pack.Center.X + group.Spawn.X, 0, pack.Center.Z + group.Spawn.Z),
                        Yaw = 0,
                    });
                    merged[actorId] = staggered ? StaggerSwing(script, packOnlyIndex++, packOnlyCount) : script;
                }
            }
            packs.Add(new PackGroup(pack.Id, spawns, new Vec3(pack.Center.X, 0, pack.Center.Z), pack.AggroMm));
        }
        return (packs, merged);
    }

    /// <summary>
    /// STATE.md D47 (WoW-faithful swing timers): in WoW each mob's swing timer is its own, so a pack does not land every
    /// auto-attack on one tick. Member <paramref name="index"/> of <paramref name="count"/> pack-only members runs its
    /// script on a clock offset by <c>index * period / count</c> ticks, where period is the script's shortest repeat
    /// period: the pack's swings spread evenly over one swing period, deterministically, in member order. The sim keeps
    /// no per-actor "reached melee" tick (a swing fired out of range is simply refused), so the stagger is by member
    /// index, not arrival. <see cref="Keyrun.Sim.Encounter.Scripts.StartingAt"/> shifts the clock, so the run root's own
    /// engage shift (onto the pack's engage tick) still composes on top. A script with no repeating step, and member 0,
    /// are returned unchanged. Applies only to pack-only templates, never to a member copied from a pull (the frozen
    /// pulls and the TS oracle twin keep their scripts).
    /// </summary>
    /// <param name="script">The member's Tempo-scaled script.</param>
    /// <param name="index">The member's index among the pack's pack-only members, in pack order.</param>
    /// <param name="count">How many pack-only members the pack has.</param>
    /// <returns>The script on the member's own clock.</returns>
    internal static IReadOnlyList<ScriptStep> StaggerSwing(IReadOnlyList<ScriptStep> script, int index, int count)
    {
        var period = script
            .Where(s => s.Trigger != StepTrigger.OnDeath && s.Every is > 0)
            .Select(s => s.Every!.Value)
            .DefaultIfEmpty(0)
            .Min();
        var offset = count > 0 ? index * period / count : 0;
        return offset == 0 ? script : script.Select(s => Scripts.StartingAt(s, offset)).ToList();
    }
}

/// <summary>
/// A scaled spawn and script for an enemy only a layout pack fields (<see cref="LayoutPackGroups.Build"/>): the caller
/// scales it exactly as it scales a pull spawn (Tempo, then the trash health affix), so a pack-only enemy is no softer or
/// harder at a key level than one copied from a pull.
/// </summary>
/// <param name="Spawn">The scaled spawn; its actor id, position and yaw are replaced per member.</param>
/// <param name="Script">The Tempo-scaled script every member runs.</param>
public sealed record PackTemplate(PullSpawn Spawn, IReadOnlyList<ScriptStep> Script);
