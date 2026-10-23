using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using C = Keyrun.Content;

namespace Keyrun.Host;

public sealed partial record HostRunSetup
{
    /// <summary>
    /// AGGRO-CHASE (finding f-f0c886eb): every pull, pack and patrol spawn with its run.json <c>enemyAggro</c> values
    /// (<see cref="C.EnemyAggroConfig.For"/>; 0 = off, so null on the spawn). Every enemy assists. A group whose ring the
    /// layout authors keeps that ring as its whole aggro rule, so its members carry no radius of their own: every pack and
    /// patrol (layout.json <c>aggroMm</c>, the ring LayoutValidator proves skippable or disjoint from the pulls) and a pull
    /// whose room sets <c>aggroMm</c> (the boss arena). The other pulls (the Choir, the Pair, the Seed on its own radius, R-SEED-2)
    /// aggro on each member's own radius beside the default ring. Patrol members leash too (L1): their home follows the moving
    /// loop until the pull, so the leash is measured from the node they were pulled at. Unchanged when the content has no block.
    /// </summary>
    /// <param name="aggro">The run.json block, or null.</param>
    /// <param name="layout">The content layout (which rooms author a ring).</param>
    /// <param name="dungeon">The placed dungeon.</param>
    /// <param name="packs">The layout packs.</param>
    /// <param name="patrols">The layout patrols.</param>
    /// <returns>The three with the values set.</returns>
    public static (DungeonDef Dungeon, IReadOnlyList<PackGroup> Packs, IReadOnlyList<PatrolPack> Patrols) WithEnemyAggro(
        C.EnemyAggroConfig? aggro, C.LayoutDef layout, DungeonDef dungeon, IReadOnlyList<PackGroup> packs, IReadOnlyList<PatrolPack> patrols)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(patrols);
        if (aggro is null)
        {
            return (dungeon, packs, patrols);
        }
        var authored = layout.Boxes.Where(b => b.Pull is not null && b.AggroMm is not null).Select(b => b.Pull!).ToHashSet(StringComparer.Ordinal);
        static long? On(int mm) => mm > 0 ? mm : null;
        List<PullSpawn> Tuned(IEnumerable<PullSpawn> spawns, bool ownRadius, bool leash) => spawns.Select(s =>
        {
            var (aggroMm, assistMm, leashMm) = aggro.For(s.ClassId);
            return s with { AggroMm = ownRadius ? On(aggroMm) : null, AssistMm = On(assistMm), LeashMm = leash ? On(leashMm) : null };
        }).ToList();
        return (
            dungeon with { Pulls = dungeon.Pulls.Select(p => p with { Spawns = Tuned(p.Spawns, !authored.Contains(p.Id), leash: true) }).ToList() },
            packs.Select(k => k with { Spawns = Tuned(k.Spawns, ownRadius: false, leash: true) }).ToList(),
            patrols.Select(k => k with { Spawns = Tuned(k.Spawns, ownRadius: false, leash: true) }).ToList());
    }

    /// <summary>
    /// R-PAIR-1 (ruling on #573): every placed enemy's script with each run.json <c>enemyScriptAt</c> cast moved to fire
    /// first on the given tick from engage (a numeric <c>at</c>; the step keeps its own <c>every</c>, so a loop keeps its
    /// period). The Pair's Spread Mark at 80 and Stack Call at 280 put its mechanic inside a fight the party wins in about
    /// 400 ticks. Unchanged when the content has no block.
    /// </summary>
    /// <param name="scriptAt">The run.json block, or null.</param>
    /// <param name="dungeon">The placed dungeon.</param>
    /// <param name="packs">The layout packs.</param>
    /// <param name="patrols">The layout patrols.</param>
    /// <param name="scripts">Actor id to its script.</param>
    /// <returns>The scripts with the casts moved.</returns>
    public static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> WithScriptAt(
        C.ValueMap<C.ValueMap<int>>? scriptAt, DungeonDef dungeon, IReadOnlyList<PackGroup> packs, IReadOnlyList<PatrolPack> patrols,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(patrols);
        ArgumentNullException.ThrowIfNull(scripts);
        if (scriptAt is null || scriptAt.Count == 0)
        {
            return scripts;
        }
        var classOf = dungeon.Pulls.SelectMany(p => p.Spawns)
            .Concat(packs.SelectMany(k => k.Spawns))
            .Concat(patrols.SelectMany(k => k.Spawns))
            .GroupBy(s => s.ActorId)
            .ToDictionary(g => g.Key, g => g.First().ClassId);
        var moved = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var (actorId, script) in scripts)
        {
            moved[actorId] = classOf.TryGetValue(actorId, out var cls) && scriptAt.TryGetValue(cls, out var casts)
                ? script.Select(s => s.Trigger != StepTrigger.OnDeath && casts.TryGetValue(s.Cast, out var at) ? s with { Trigger = StepTrigger.AtTick, AtTick = at } : s).ToList()
                : script;
        }
        return moved;
    }
}
