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
    /// (there is no scaled template to copy) and on an id the dungeon or the scripts already use.
    /// </summary>
    /// <param name="layout">A validated content layout.</param>
    /// <param name="dungeon">The placed sim dungeon (<see cref="NavLayouts.PlaceDungeon"/>).</param>
    /// <param name="scripts">The run's enemy scripts, keyed by actor id.</param>
    /// <param name="firstActorId">The first pack member's actor id.</param>
    /// <returns>The packs, and the scripts with every pack member's added.</returns>
    /// <exception cref="InvalidOperationException">A member has no template, or an id is taken.</exception>
    public static (IReadOnlyList<PackGroup> Packs, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts) Build(
        C.LayoutDef layout, DungeonDef dungeon, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts, int firstActorId = FirstActorId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(scripts);
        var templates = new Dictionary<string, PullSpawn>(StringComparer.Ordinal);
        foreach (var spawn in dungeon.Pulls.SelectMany(p => p.Spawns))
        {
            templates.TryAdd(spawn.ClassId, spawn);
        }
        var taken = dungeon.Pulls.SelectMany(p => p.Spawns).Select(s => s.ActorId).Concat(scripts.Keys).ToHashSet();
        var merged = new SortedDictionary<int, IReadOnlyList<ScriptStep>>(scripts.ToDictionary(kv => kv.Key, kv => kv.Value));
        var packs = new List<PackGroup>();
        var nextId = firstActorId;
        foreach (var pack in layout.Packs)
        {
            var spawns = new List<PullSpawn>();
            foreach (var group in pack.Enemies)
            {
                if (!templates.TryGetValue(group.EnemyId, out var template))
                {
                    throw new InvalidOperationException($"pack \"{pack.Id}\": no pull fields enemy \"{group.EnemyId}\", so it has no scaled spawn to copy");
                }
                if (!scripts.TryGetValue(template.ActorId, out var script))
                {
                    throw new InvalidOperationException($"pack \"{pack.Id}\": enemy \"{group.EnemyId}\" (template actor {template.ActorId}) has no script");
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
                    merged[actorId] = script;
                }
            }
            packs.Add(new PackGroup(pack.Id, spawns, new Vec3(pack.Center.X, 0, pack.Center.Z), pack.AggroMm));
        }
        return (packs, merged);
    }
}
