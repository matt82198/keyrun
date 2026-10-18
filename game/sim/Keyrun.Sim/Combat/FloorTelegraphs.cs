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
}
