using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Dungeon;

/// <summary>A generated dungeon: every stage's output for one seed.</summary>
/// <param name="Seed">The run seed.</param>
/// <param name="Graph">Stage 1: the room DAG.</param>
/// <param name="Grid">Stage 2: the walkable grid.</param>
/// <param name="Quotas">Stage 3: the trash rooms' forces quotas and slots.</param>
/// <param name="Packs">Stage 4: the placed packs.</param>
/// <param name="ForcesRequired">The forces bar the packs were budgeted against.</param>
public sealed record GeneratedDungeon(long Seed, RoomGraph Graph, WalkGrid Grid, IReadOnlyList<RoomQuota> Quotas, IReadOnlyList<PackPlacement> Packs, int ForcesRequired);

/// <summary>
/// The seeded dungeon pipeline (game/design/ARCHITECTURE.md section 2): seed -> room graph -> walkable grid -> pack
/// slots -> fill. Patrol paths and the boss encounter are the next stages and are not built yet. Not wired into the
/// live run: the next slice converts a <see cref="GeneratedDungeon"/> into the run's layout and packs.
/// </summary>
public static class DungeonPipeline
{
    /// <summary>Runs every built stage for <paramref name="seed"/>.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="roster">The pack roster.</param>
    /// <param name="p">Params, or null for <see cref="DungeonGenParams.Default"/>.</param>
    /// <returns>The dungeon.</returns>
    /// <exception cref="ArgumentException">Bad params or roster.</exception>
    public static GeneratedDungeon Generate(long seed, IReadOnlyList<RosterPack> roster, DungeonGenParams? p = null)
    {
        p ??= DungeonGenParams.Default;
        PackSlots.ValidateRoster(roster);
        var graph = RoomGraphGen.Generate(seed, p);
        var grid = WalkGridGen.Emit(graph, p);
        var quotas = PackSlots.Plan(graph, grid, p, roster);
        var packs = PackFiller.Fill(seed, quotas, roster);
        return new GeneratedDungeon(seed, graph, grid, quotas, packs, p.ForcesRequired);
    }

    /// <summary>
    /// The PRNG stream for one stage: each stage draws from its own stream, so changing how many draws one stage makes
    /// never reshuffles another stage's output for the same seed.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="stage">The stage number (1 graph, 4 fill; 5 patrols and 6 boss are reserved).</param>
    /// <returns>The stage's initial state.</returns>
    public static PrngState StageRng(long seed, uint stage)
    {
        unchecked
        {
            return Prng.SeedState(((uint)seed * 0x9E3779B1u) ^ ((uint)(seed >> 32) * 0x85EBCA77u) ^ (stage * 0xC2B2AE3Du));
        }
    }
}
