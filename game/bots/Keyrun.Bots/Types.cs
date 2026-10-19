using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Bots;

/// <summary>
/// An integer point on the arena floor in mm (the bots' <c>{x, z}</c> shape: bots/src/shared/positioning.ts
/// PointMM, bots/src/shared/command.ts PointLike, bots/src/shared/pings.ts PingPoint). y is never read.
/// </summary>
/// <param name="X">East-west, mm.</param>
/// <param name="Z">North-south, mm.</param>
public readonly record struct PointMm(long X, long Z)
{
    /// <summary>The xz projection of a sim position.</summary>
    /// <param name="v">A sim Vec3.</param>
    /// <returns>Its x and z.</returns>
    public static PointMm Of(Vec3 v) => new(v.X, v.Z);
}

/// <summary>bots/src/types.ts Telegraph.shape: "circle" | "cone" | "line".</summary>
public enum TelegraphShape
{
    /// <summary>"circle": reach is <see cref="BotTelegraph.Radius"/>.</summary>
    Circle,

    /// <summary>"cone": apex at the centre, reach <see cref="BotTelegraph.Radius"/>, width <see cref="BotTelegraph.Angle1024"/>.</summary>
    Cone,

    /// <summary>"line": a rectangle <see cref="BotTelegraph.Length"/> by <see cref="BotTelegraph.Width"/> from the centre.</summary>
    Line,
}

/// <summary>
/// bots/src/types.ts Telegraph: a boss/enemy AoE warning or a floor patch, evaluated in the xz plane. Every
/// distance is integer mm. The optional TS fields are nullable init properties (null = TS absent, read as
/// <c>?? 0</c> by positioning.ts).
/// </summary>
/// <param name="Id">Telegraph id (floor patches carry the <c>floor:</c> prefix, shared/floor.ts).</param>
/// <param name="Shape">Shape.</param>
/// <param name="Center">Centre (the apex for cones, the start for lines), mm.</param>
/// <param name="ResolvesTick">Tick it resolves; active while <c>resolvesTick &gt; tick</c>.</param>
/// <param name="Danger">True for the reflex-dodge tier (bot.ts); floor patches are always false.</param>
public sealed record BotTelegraph(string Id, TelegraphShape Shape, PointMm Center, int ResolvesTick, bool Danger)
{
    /// <summary>Circle radius or cone reach, mm.</summary>
    public long? Radius { get; init; }

    /// <summary>Cone full width, 1/1024 turn.</summary>
    public long? Angle1024 { get; init; }

    /// <summary>Facing, 1/1024 turn.</summary>
    public long? Yaw { get; init; }

    /// <summary>Line length, mm.</summary>
    public long? Length { get; init; }

    /// <summary>Line width, mm.</summary>
    public long? Width { get; init; }
}

/// <summary>
/// bots/src/types.ts BotView.classKit: the thin, integration-supplied kit (scripted test views and the oracle
/// runner's EMPTY_KIT). Content-backed kits come from <see cref="KitRegistry"/> instead.
/// </summary>
/// <param name="Rotation">Ability ids in priority order.</param>
/// <param name="InterruptAbilityId">Interrupt ability, or null.</param>
/// <param name="DefensiveAbilityId">Defensive ability, or null.</param>
/// <param name="HealAbilityId">Heal ability, or null.</param>
public sealed record ClassKitView(IReadOnlyList<string> Rotation, string? InterruptAbilityId, string? DefensiveAbilityId, string? HealAbilityId)
{
    /// <summary>tests/oracle/traces.ts EMPTY_KIT: <c>{rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null}</c>.</summary>
    public static ClassKitView Empty { get; } = new(Array.Empty<string>(), null, null, null);
}

/// <summary>
/// bots/src/types.ts BotView: what a bot's decide() is handed each tick. It never mutates the state.
/// </summary>
/// <remarks>
/// Port deviation (no behaviour change): the TS kits.ts holds the content registry in module state set once by
/// <c>setContentRegistry()</c>. Here the view carries it as <see cref="Kits"/>, so bots stay pure functions of
/// their inputs and parallel tests cannot race on a global. <see cref="KitRegistry.Empty"/> is the TS
/// "no registry injected" case (every <c>getClassKit</c> is null).
/// </remarks>
/// <param name="State">The sim state snapshot.</param>
/// <param name="Telegraphs">Encounter telegraphs plus derived floor patches (<see cref="BotViews.BuildBotView"/>).</param>
/// <param name="ClassKit">The fallback class kit.</param>
public sealed record BotView(SimState State, IReadOnlyList<BotTelegraph> Telegraphs, ClassKitView ClassKit)
{
    /// <summary>The content-backed kits (the TS injected ContentRegistry).</summary>
    public KitRegistry Kits { get; init; } = KitRegistry.Empty;

    /// <summary>
    /// The run's rooms-and-corridors layout (<c>run.Content.Layout</c>, STATE.md D18) when the host runs one, else null.
    /// Bots travel between rooms (<see cref="Shared.Follow"/>) only with a layout, so the oracle runner (null) is
    /// unchanged: there the next pull spawns the step after a clear, leaving one enemy-free tick the bots must not act on.
    /// </summary>
    public Keyrun.Sim.Encounter.NavLayout? Layout { get; init; }
}
