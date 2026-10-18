using System;
using System.Collections.Generic;
using System.Globalization;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using EncounterTelegraph = Keyrun.Sim.Encounter.Telegraph;

namespace Keyrun.Bots;

/// <summary>
/// Port of bots/src/view.ts buildBotView, plus the bot half of client/src/sim-host/telegraph-map.ts
/// (toBotTelegraph), which the TS oracle runner uses to build every bot view:
/// <c>buildBotView(state, encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT)</c> (tests/oracle/traces.ts).
/// </summary>
public static class BotViews
{
    /// <summary>
    /// telegraph-map.ts TELEGRAPH_CLASS_BY_ABILITY: the render class of every telegraphed cast the shipped content
    /// emits. Only "stack" is not danger (you stand in it); an unknown ability falls back to "avoid" (danger).
    /// </summary>
    private static readonly Dictionary<string, string> TelegraphClassByAbility = new(StringComparer.Ordinal)
    {
        ["pair.spreadMark"] = "spread",
        ["pair.stackCall"] = "stack",
        ["hollowWarden.cleave"] = "boss",
        ["seed.slam"] = "avoid",
    };

    /// <summary>telegraph-map.ts telegraphClassFor: the class of <paramref name="abilityId"/>, "avoid" when unlisted.</summary>
    /// <param name="abilityId">Telegraphed ability.</param>
    /// <returns>"avoid", "spread", "stack" or "boss".</returns>
    public static string TelegraphClassFor(string abilityId) =>
        TelegraphClassByAbility.TryGetValue(abilityId, out var c) ? c : "avoid";

    private static TelegraphShape ParseShape(string shape) => shape switch
    {
        "circle" => TelegraphShape.Circle,
        "cone" => TelegraphShape.Cone,
        "line" => TelegraphShape.Line,
        _ => throw new ArgumentException($"unknown telegraph shape {shape}", nameof(shape)),
    };

    /// <summary>
    /// telegraph-map.ts toBotTelegraph: an encounter telegraph as the bots read it. The id is the decimal
    /// <c>String(t.id)</c>, the centre is the origin's xz, yaw is always present, and danger is every class but
    /// "stack". Optional geometry is copied only when set.
    /// </summary>
    /// <param name="t">Encounter telegraph.</param>
    /// <returns>The bot telegraph.</returns>
    public static BotTelegraph ToBotTelegraph(EncounterTelegraph t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return new BotTelegraph(
            t.Id.ToString(CultureInfo.InvariantCulture),
            ParseShape(t.Shape),
            PointMm.Of(t.Origin),
            t.ResolvesTick,
            TelegraphClassFor(t.AbilityId) != "stack")
        {
            Yaw = t.Yaw,
            Radius = t.Radius,
            Angle1024 = t.Angle1024,
            Length = t.Length,
            Width = t.Width,
        };
    }

    /// <summary>A derived floor patch (sim/combat/floor-telegraphs.ts) as a bot telegraph: a circle, danger false, no yaw.</summary>
    /// <param name="f">Floor telegraph.</param>
    /// <returns>The bot telegraph.</returns>
    public static BotTelegraph FromFloorTelegraph(FloorTelegraph f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new BotTelegraph(f.Id, TelegraphShape.Circle, new PointMm(f.CenterX, f.CenterZ), f.ResolvesTick, f.Danger) { Radius = f.Radius };
    }

    /// <summary>
    /// view.ts buildBotView: <c>{state, telegraphs: [...encounterTelegraphs, ...deriveFloorTelegraphs(state)], classKit}</c>.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="encounterTelegraphs">Encounter telegraphs, already mapped (<see cref="ToBotTelegraph"/>).</param>
    /// <param name="classKit">Fallback class kit.</param>
    /// <param name="kits">Content-backed kits (null = <see cref="KitRegistry.Empty"/>).</param>
    /// <returns>The view.</returns>
    public static BotView BuildBotView(SimState state, IEnumerable<BotTelegraph> encounterTelegraphs, ClassKitView classKit, KitRegistry? kits = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(encounterTelegraphs);
        ArgumentNullException.ThrowIfNull(classKit);
        var telegraphs = new List<BotTelegraph>(encounterTelegraphs);
        foreach (var f in FloorTelegraphs.DeriveFloorTelegraphs(state))
        {
            telegraphs.Add(FromFloorTelegraph(f));
        }
        return new BotView(state, telegraphs, classKit) { Kits = kits ?? KitRegistry.Empty };
    }
}
