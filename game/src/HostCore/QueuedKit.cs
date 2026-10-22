using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// The Godot-free half of the between-runs screen's Queue (game/src/Hud/Screen.cs Queue): the request one press hands
/// <c>SimDriver.StartQueuedRun</c>. It carries the Key band's dungeon, key and Clause, the human's talent-resolved
/// kit (BIBLE s6) as <see cref="QueueRequest.Loadouts"/>, and the equipped gear's summed stats (BIBLE s7 "Tier is power")
/// as <see cref="QueueRequest.Gear"/>. The Facets ride the progression, not the request: <see cref="HostRunSetup.StartFor"/>
/// (<see cref="RunStart.Of"/>) is the one place a Facet patches its ability, so each applies exactly once.
/// </summary>
public static class QueuedKit
{
    /// <summary>Queues <paramref name="state"/>: the locked state and the request, or null when already queued.</summary>
    /// <param name="state">The Key band.</param>
    /// <param name="registry">The content.</param>
    /// <param name="talents">The human's talent picks, or null.</param>
    /// <param name="progression">The screen's progression (equipped gear).</param>
    /// <param name="classId">The human's class.</param>
    /// <returns>The locked state and the request.</returns>
    public static (ScreenState State, QueueRequest? Request) Queue(
        ScreenState state,
        C.ContentRegistry registry,
        C.TalentPicks? talents,
        Progression progression,
        string classId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(progression);
        if (state.Locked)
        {
            return (state, null);
        }
        // Talents only (finding f-494ea8ef): the equipped Facets are not patched in here. The host's RunStart.Of stacks
        // each Facet's GearPatch on this kit from the progression; a Facet applied here as well (the Screen did, before
        // this) ran twice, so an appended op such as Chilling Bash's slow played two times per cast.
        var talented = talents is { Count: > 0 } picks ? picks.Resolve(registry.Abilities) : new C.ValueMap<C.AbilityDef>();
        var setup = HostRunSetup.Build(registry with { Abilities = Merge(registry.Abilities, talented) }, state.DungeonId, state.KeyLevel, classId, withBots: false);
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>>? loadouts = talented.Count == 0
            ? null
            : new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>
            {
                [setup.HumanActorId] = talented.Keys.ToDictionary(id => id, id => setup.Content.Abilities[id], StringComparer.Ordinal),
            };
        var (locked, request) = state.Queue(loadouts);
        if (request is not null && registry.Gear.Count > 0)
        {
            var gear = GearPower.Sum(progression.Equipped, (slot, tier) =>
            {
                var s = C.GearItems.StatsAt(registry.Gear, Enum.Parse<C.GearSlot>(slot, ignoreCase: true), tier);
                return new GearStats(s.DamageDoneBp, s.DamageTakenBp);
            });
            request = request with { Gear = new Dictionary<int, GearStats> { [setup.HumanActorId] = gear } };
        }
        return (locked, request);
    }

    private static C.ValueMap<C.AbilityDef> Merge(
        IReadOnlyDictionary<string, C.AbilityDef> abilities, IReadOnlyDictionary<string, C.AbilityDef> patched) =>
        new(abilities.Select(kv => patched.TryGetValue(kv.Key, out var p) ? new KeyValuePair<string, C.AbilityDef>(kv.Key, p) : kv));
}
