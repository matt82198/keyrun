using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Shared fixtures for the roles/** test ports (lane 3B): a full-field actor builder (the TS files' local
/// <c>makeActor(overrides)</c> with every ActorState field settable), state/view builders, and the real-content
/// kit registry (tests/bots/real-content-setup.ts). A TS test file that imports real-content-setup gets
/// <see cref="RealKits"/>; one that does not runs with <see cref="KitRegistry.Empty"/>, matching vitest's
/// per-file module isolation of kits.ts's registry.
/// </summary>
public static class RolesTestKit
{
    private static readonly Lazy<KitRegistry> Real = new(() => KitRegistry.From(ContentLoader.Load()));

    /// <summary>tests/bots/real-content-setup.ts: <c>setContentRegistry(loadContent())</c>.</summary>
    public static KitRegistry RealKits => Real.Value;

    /// <summary>An actor with every field explicit (TS <c>{...defaults, ...overrides}</c>).</summary>
    public static ActorState Actor(
        int id = 1,
        ActorKind kind = ActorKind.Player,
        string classId = "warrior",
        ActorRole role = ActorRole.Damage,
        long x = 0,
        long z = 0,
        int yaw = 0,
        long health = 100,
        long maxHealth = 100,
        long resource = 0,
        long maxResource = 0,
        bool alive = true,
        int? targetId = null,
        string? castId = null,
        int? castStartTick = null,
        int? castEndsTick = null,
        int gcdReadyTick = 0,
        IEnumerable<(string Ability, int Ready)>? cooldowns = null,
        IEnumerable<EffectInstance>? effects = null,
        IEnumerable<(int Id, long Value)>? threat = null)
    {
        var cd = new JsRecord<int>();
        foreach (var (ability, ready) in cooldowns ?? Array.Empty<(string, int)>())
        {
            cd[ability] = ready;
        }
        var th = new SortedDictionary<int, long>();
        foreach (var (tid, value) in threat ?? Array.Empty<(int, long)>())
        {
            th[tid] = value;
        }
        return new ActorState
        {
            Id = id,
            Kind = kind,
            ClassId = classId,
            Role = role,
            Pos = new Vec3(x, 0, z),
            Yaw = yaw,
            Health = health,
            MaxHealth = maxHealth,
            Resource = resource,
            MaxResource = maxResource,
            Alive = alive,
            TargetId = targetId,
            CastId = castId,
            CastStartTick = castStartTick,
            CastEndsTick = castEndsTick,
            Cooldowns = cd,
            GcdReadyTick = gcdReadyTick,
            Effects = (effects ?? Array.Empty<EffectInstance>()).ToList(),
            Threat = th,
        };
    }

    /// <summary>A view over <paramref name="actors"/> at <paramref name="tick"/> (TS makeView/decide helpers).</summary>
    public static BotView ViewOf(int tick, KitRegistry kits, ClassKitView? classKit, IReadOnlyList<BotTelegraph> telegraphs, params ActorState[] actors) =>
        new(BotTestKit.MakeState(tick, actors), telegraphs, classKit ?? ClassKitView.Empty) { Kits = kits };

    /// <summary>A view with no telegraphs.</summary>
    public static BotView ViewOf(int tick, KitRegistry kits, ClassKitView? classKit, params ActorState[] actors) =>
        ViewOf(tick, kits, classKit, Array.Empty<BotTelegraph>(), actors);

    /// <summary>The TS circle floor patch / telegraph literal.</summary>
    public static BotTelegraph Circle(string id, long cx, long cz, long radius, bool danger = false, int resolvesTick = 1000) =>
        new(id, TelegraphShape.Circle, new PointMm(cx, cz), resolvesTick, danger) { Radius = radius };

    /// <summary>An effect instance (<c>{effectId, sourceId, stacks: 1, endsTick, nextPeriodicTick: null}</c>).</summary>
    public static EffectInstance Effect(string effectId, int sourceId, int endsTick) => BotTestKit.Effect(effectId, sourceId, endsTick);

    /// <summary>Ability ids of every cast command.</summary>
    public static List<string> CastIds(IEnumerable<Command> commands) => commands.OfType<CastCommand>().Select(c => c.AbilityId).ToList();

    /// <summary>A scripted classKit literal.</summary>
    public static ClassKitView ClassKit(IReadOnlyList<string>? rotation = null, string? interrupt = null, string? defensive = null, string? heal = null) =>
        new(rotation ?? Array.Empty<string>(), interrupt, defensive, heal);
}
