using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// A bot-consumable floor-patch telegraph (sim/combat/floor-telegraphs.ts:63-70 FloorTelegraph): always a circle,
/// always <c>danger: false</c>. Centre and radius are raw mm, passed through unconverted (floor-telegraphs.ts:26-44).
/// </summary>
/// <param name="Id">The bots/src/shared/floor.ts id.</param>
/// <param name="CenterX">Centre x, mm.</param>
/// <param name="CenterZ">Centre z, mm.</param>
/// <param name="Radius">Radius, mm.</param>
/// <param name="ResolvesTick">The patch's end tick.</param>
public sealed record FloorTelegraph(string Id, long CenterX, long CenterZ, long Radius, int ResolvesTick)
{
    /// <summary>Always "circle".</summary>
    public string Shape => "circle";

    /// <summary>Always false: a floor patch is not the reflex-dodge tier.</summary>
    public bool Danger => false;

    /// <summary>The TS object, as window_opened's <c>telegraph</c> field carries it.</summary>
    /// <returns>A JSON object.</returns>
    public JsObject ToJs() => JsValue.Obj(
        ("id", JsValue.Str(Id)),
        ("shape", JsValue.Str(Shape)),
        ("center", JsValue.Obj(("x", JsValue.Int(CenterX)), ("z", JsValue.Int(CenterZ)))),
        ("radius", JsValue.Int(Radius)),
        ("resolvesTick", JsValue.Int(ResolvesTick)),
        ("danger", JsValue.Bool(Danger)));
}

/// <summary>
/// Port of sim/combat/floor-telegraphs.ts: derives the floor-patch telegraphs bots read from
/// <see cref="SimState.FloorWindows"/>. A patch opened by a player is friendly and nests the Window effect id
/// (<c>floor:window:&lt;windowId&gt;:&lt;setupClass&gt;</c>); one opened by an enemy, or whose opener is gone, fails
/// closed to hostile (<c>floor:&lt;windowId&gt;</c>).
/// </summary>
public static class FloorTelegraphs
{
    /// <summary>floor-telegraphs.ts:54 FLOOR_PATCH_PREFIX.</summary>
    public const string FloorPatchPrefix = "floor:";

    /// <summary>floor-telegraphs.ts:78-80 isFriendlyFloorWindow: the setup actor exists and is a player.</summary>
    /// <param name="state">Sim state.</param>
    /// <param name="fw">The patch.</param>
    /// <returns>True when friendly.</returns>
    public static bool IsFriendlyFloorWindow(SimState state, FloorWindow fw) =>
        state.Actors.TryGetValue(fw.SetupActorId, out var a) && a.Kind == ActorKind.Player;

    /// <summary>floor-telegraphs.ts:83-87 floorTelegraphId.</summary>
    /// <param name="fw">The patch.</param>
    /// <param name="friendly">Friendliness.</param>
    /// <returns>The id.</returns>
    public static string FloorTelegraphId(FloorWindow fw, bool friendly) =>
        friendly ? FloorPatchPrefix + WindowState.WindowPrefix + fw.WindowId + ":" + fw.SetupClass : FloorPatchPrefix + fw.WindowId;

    private static FloorTelegraph ToFloorTelegraph(SimState state, FloorWindow fw) =>
        new(FloorTelegraphId(fw, IsFriendlyFloorWindow(state, fw)), fw.Pos.X, fw.Pos.Z, fw.RadiusMm, fw.EndsTick);

    /// <summary>
    /// floor-telegraphs.ts:109-118 deriveFloorTelegraphs: one telegraph per patch still open (<c>endsTick &gt; tick</c>),
    /// in JS key order. The TS returns [] for an absent floorWindows; the C# field is always present.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <returns>The telegraphs.</returns>
    public static List<FloorTelegraph> DeriveFloorTelegraphs(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var output = new List<FloorTelegraph>();
        foreach (var key in state.FloorWindows.Keys)
        {
            var fw = state.FloorWindows[key];
            if (fw.EndsTick <= state.Tick)
            {
                continue;
            }
            output.Add(ToFloorTelegraph(state, fw));
        }
        return output;
    }

    /// <summary>floor-telegraphs.ts:126-128 floorWindowOpenedTelegraph: the single-patch telegraph window_opened carries.</summary>
    /// <param name="state">Sim state.</param>
    /// <param name="fw">The patch.</param>
    /// <returns>Its telegraph.</returns>
    public static FloorTelegraph FloorWindowOpenedTelegraph(SimState state, FloorWindow fw) => ToFloorTelegraph(state, fw);

    /// <summary>Id prefix of a combo cloud's telegraph (<c>cloud:&lt;cloudId&gt;</c>).</summary>
    public const string CloudPrefix = "cloud:";

    /// <summary>
    /// One circle telegraph per open combo cloud (<see cref="SimState.FloorClouds"/>, <c>endsTick &gt; tick</c>), in
    /// opening order, for views to draw. Kept apart from <see cref="DeriveFloorTelegraphs"/> on purpose: bots flee
    /// every non-Window floor patch, and a friendly cloud must not pull them off their targets.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <returns>The telegraphs.</returns>
    public static List<FloorTelegraph> DeriveCloudTelegraphs(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var output = new List<FloorTelegraph>();
        foreach (var cloud in state.FloorClouds)
        {
            if (cloud.EndsTick > state.Tick)
            {
                output.Add(new FloorTelegraph(CloudPrefix + cloud.Id, cloud.Pos.X, cloud.Pos.Z, cloud.RadiusMm, cloud.EndsTick));
            }
        }
        return output;
    }

    /// <summary>
    /// One drawable <see cref="CloudShape"/> per open combo cloud (<see cref="SimState.FloorClouds"/>,
    /// <c>endsTick &gt; tick</c>), in opening order: what the arena view draws for a Steam blind cloud. Centre, radius
    /// and end come straight from the sim's <see cref="FloorCloud"/>; the opening tick is read from the cloud id
    /// (effectId:sourceId:openingTick, see <see cref="FloorCloud.Id"/>). A cloud whose id carries no readable opening
    /// tick is taken as opened on the current tick, so it draws full rather than not at all.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <returns>The shapes; empty once every cloud has ended.</returns>
    public static List<CloudShape> DeriveCloudShapes(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var output = new List<CloudShape>();
        foreach (var cloud in state.FloorClouds)
        {
            if (cloud.EndsTick <= state.Tick)
            {
                continue;
            }
            var opened = OpeningTickOf(cloud.Id) is { } t && t <= state.Tick ? t : state.Tick;
            output.Add(new CloudShape(CloudPrefix + cloud.Id, cloud.EffectId, cloud.Pos.X, cloud.Pos.Z, cloud.RadiusMm, opened, cloud.EndsTick));
        }
        return output;
    }

    // The trailing ":<digits>" of a cloud id, or null when there is none.
    private static int? OpeningTickOf(string id)
    {
        var colon = id.LastIndexOf(':');
        if (colon < 0 || colon == id.Length - 1 || id.Length - colon - 1 > 9)
        {
            return null;
        }
        var value = 0;
        for (var i = colon + 1; i < id.Length; i++)
        {
            var c = id[i];
            if (c < '0' || c > '9')
            {
                return null;
            }
            value = (value * 10) + (c - '0');
        }
        return value;
    }
}

/// <summary>
/// The drawable shape of one open combo cloud (BIBLE section 4 Steam: "a cloud that blinds enemies inside (they miss)
/// for 4 s"): a floor circle of <paramref name="RadiusMm"/> at its centre, alive from <paramref name="OpenedTick"/> until
/// <paramref name="EndsTick"/>. Integer math only, like the rest of the sim.
/// </summary>
/// <param name="Id">The cloud telegraph id (<c>cloud:&lt;cloudId&gt;</c>).</param>
/// <param name="EffectId">The effect the cloud gives, e.g. "blinded".</param>
/// <param name="CenterX">Centre x, mm.</param>
/// <param name="CenterZ">Centre z, mm.</param>
/// <param name="RadiusMm">Radius, mm.</param>
/// <param name="OpenedTick">The tick it opened.</param>
/// <param name="EndsTick">The tick it ends.</param>
public sealed record CloudShape(string Id, string EffectId, long CenterX, long CenterZ, long RadiusMm, int OpenedTick, int EndsTick)
{
    /// <summary>Its whole life, ticks (at least 1).</summary>
    public int LifeTicks => Math.Max(1, EndsTick - OpenedTick);

    /// <summary>Ticks left at <paramref name="tick"/>, never below 0.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Ticks left.</returns>
    public int TicksLeft(int tick) => Math.Max(0, EndsTick - tick);

    /// <summary>The share of its life left at <paramref name="tick"/>, permille: 1000 at open, 0 at the end.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>0..1000.</returns>
    public int LeftPermille(int tick) => (int)Math.Clamp(TicksLeft(tick) * 1000L / LifeTicks, 0L, 1000L);
}
