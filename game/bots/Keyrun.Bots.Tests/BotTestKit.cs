using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Shared fixtures for the tests/bots/** ports: the TS files' local <c>makeActor</c> / <c>table</c> /
/// <c>makeState</c> / <c>makeView</c> helpers and tests/bots/apply-move.ts.
/// </summary>
public static class BotTestKit
{
    /// <summary>The TS makeActor defaults (id 1, player, damage, 100/100 hp, everything else empty).</summary>
    public static ActorState MakeActor(
        int id = 1,
        ActorKind kind = ActorKind.Player,
        string classId = "warrior",
        ActorRole role = ActorRole.Damage,
        long x = 0,
        long z = 0,
        bool alive = true,
        int? targetId = null,
        string? castId = null,
        IEnumerable<(string Ability, int Ready)>? cooldowns = null,
        IEnumerable<EffectInstance>? effects = null)
    {
        var cd = new JsRecord<int>();
        foreach (var (ability, ready) in cooldowns ?? Array.Empty<(string, int)>())
        {
            cd[ability] = ready;
        }
        return new ActorState
        {
            Id = id,
            Kind = kind,
            ClassId = classId,
            Role = role,
            Pos = new Vec3(x, 0, z),
            Yaw = 0,
            Health = 100,
            MaxHealth = 100,
            Resource = 0,
            MaxResource = 0,
            Alive = alive,
            TargetId = targetId,
            CastId = castId,
            Cooldowns = cd,
            GcdReadyTick = 0,
            Effects = (effects ?? Array.Empty<EffectInstance>()).ToList(),
        };
    }

    /// <summary>A Window-style effect instance (<c>{effectId, sourceId, stacks: 1, endsTick, nextPeriodicTick: null}</c>).</summary>
    public static EffectInstance Effect(string effectId, int sourceId, int endsTick) => new()
    {
        EffectId = effectId,
        SourceId = sourceId,
        Stacks = 1,
        EndsTick = endsTick,
        NextPeriodicTick = null,
    };

    /// <summary>The TS <c>table(actors)</c>: an id-keyed actor map (ascending, as JS integer keys iterate).</summary>
    public static SortedDictionary<int, ActorState> Table(params ActorState[] actors)
    {
        var table = new SortedDictionary<int, ActorState>();
        foreach (var a in actors)
        {
            table[a.Id] = a;
        }
        return table;
    }

    /// <summary>The TS <c>makeState</c>: <c>{tick, seed: 1, prng: [1,2,3,4], actors, runTimerTicks: 0, phase: "combat"}</c>.</summary>
    public static SimState MakeState(int tick, params ActorState[] actors) => new()
    {
        Tick = tick,
        Seed = 1,
        Prng = new PrngState(1, 2, 3, 4),
        Actors = Table(actors),
        RunTimerTicks = 0,
        Phase = "combat",
    };

    /// <summary>A BotView over <paramref name="state"/> with the given telegraphs and classKit.</summary>
    public static BotView View(SimState state, ClassKitView? classKit = null, params BotTelegraph[] telegraphs) =>
        new(state, telegraphs, classKit ?? ClassKitView.Empty);

    /// <summary>A scripted classKit (the TS <c>{rotation, interruptAbilityId, defensiveAbilityId, healAbilityId}</c>).</summary>
    public static ClassKitView Kit(string? interrupt = null, params string[] rotation) =>
        new(rotation, interrupt, null, null);

    /// <summary>
    /// tests/bots/apply-move.ts applyMove: steps <paramref name="pos"/> by <c>trunc(dir * stepMm / 1000)</c> per
    /// axis for a move command; any other command (or none) leaves it unchanged.
    /// </summary>
    public static PointMm ApplyMove(PointMm pos, Command? cmd, long stepMm)
    {
        if (cmd is not MoveCommand move)
        {
            return pos;
        }
        return new PointMm(pos.X + IntMath.TruncDiv(move.Dir.X * stepMm, 1000), pos.Z + IntMath.TruncDiv(move.Dir.Z * stepMm, 1000));
    }

    /// <summary>Moves an actor to <paramref name="p"/> (y kept).</summary>
    public static void SetPos(ActorState actor, PointMm p) => actor.Pos = new Vec3(p.X, actor.Pos.Y, p.Z);

    /// <summary>The first move command in <paramref name="commands"/>, or null.</summary>
    public static MoveCommand? FirstMove(IEnumerable<Command> commands) => commands.OfType<MoveCommand>().FirstOrDefault();
}
