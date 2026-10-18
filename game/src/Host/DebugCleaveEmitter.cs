// TEMPORARY (ENGINE-PLAN phase 1, lane 1E): the only throwaway code in the engine plan.
// Lane 2E DELETES this file once the ported encounter step (Encounter/** scripts, lane 2C) emits
// hollowWarden.cleave's telegraph from the boss script itself. Do not build on it.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Host;

/// <summary>
/// TEMPORARY stand-in for the encounter script step: puts a static box actor (the Hollow Warden boss
/// stand-in) in the arena and, every Cleave cycle, emits the boss's cleave telegraph and drives the
/// ported Telegraphs.cs lifecycle (register, resolve, telegraph_hit, drop).
/// </summary>
/// <remarks>
/// The telegraph comes from content, not constants: <c>hollowWarden.boss</c>'s script step casting
/// <c>hollowWarden.cleave</c> in content/data/enemies/hollow-warden.json (cone, radius 4000 mm,
/// angle1024 200, leadTicks 40, loop every 160), read through the Keyrun.Content loader. Timing follows
/// sim/encounter/scripts.ts: a loop step fires at ticks every, 2*every, ...; its telegraph event is
/// emitted leadTicks before, with resolvesTick on the cast tick and origin/yaw snapshotted from the caster.
/// The cast itself is not issued (abilities arrive with 2B); only the telegraph lifecycle runs.
/// </remarks>
public partial class DebugCleaveEmitter : Node, ITickHook
{
    /// <summary>The box's actor id.</summary>
    public const int BoxId = 2;

    /// <summary>The enemy whose script carries the cleave.</summary>
    public const string EnemyId = "hollowWarden.boss";

    /// <summary>The scripted cast whose telegraph is shown.</summary>
    public const string CastId = "hollowWarden.cleave";

    /// <summary>Box position, mm: 6 m ahead of the Warden's spawn.</summary>
    public static readonly Vec3 BoxPos = new(0, 0, 6000);

    /// <summary>Box yaw: 768 = facing -z, back toward the Warden's spawn.</summary>
    public const int BoxYaw = 768;

    private IReadOnlyList<Telegraph> _active = Array.Empty<Telegraph>();
    private int _nextTelegraphId = 1;

    /// <summary>The cleave's telegraph spec as loaded from content.</summary>
    public Keyrun.Content.TelegraphSpec? Spec { get; private set; }

    /// <summary>The cleave's loop period, ticks.</summary>
    public int Every { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<Telegraph> ActiveTelegraphs => _active;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The content lacks the boss, its cleave step, or a cone
    /// telegraph with a loop period (fail closed; the arena does not invent a fallback shape).</exception>
    public void Install(SimState state)
    {
        var content = ContentLoader.Load();
        if (!content.Enemies.TryGetValue(EnemyId, out var boss))
        {
            throw new InvalidOperationException($"content has no enemy {EnemyId}");
        }
        var step = boss.Script.FirstOrDefault(s => s.Cast == CastId)
            ?? throw new InvalidOperationException($"{EnemyId} has no script step casting {CastId}");
        if (step.At.Kind != ScriptAtKind.Loop || step.Every is not { } every || every <= 0)
        {
            throw new InvalidOperationException($"{CastId} is not a loop step with a positive 'every'");
        }
        if (step.Telegraph is not { Shape: TelegraphShape.Cone, Radius: not null, Angle1024: not null } spec)
        {
            throw new InvalidOperationException($"{CastId} has no cone telegraph with radius and angle1024");
        }
        Spec = spec;
        Every = every;
        state.Actors.Add(BoxId, new ActorState
        {
            Id = BoxId,
            Kind = ActorKind.Enemy,
            ClassId = EnemyId,
            Role = ActorRole.None,
            Pos = BoxPos,
            Yaw = BoxYaw,
            Health = boss.MaxHealth,
            MaxHealth = boss.MaxHealth,
        });
        GD.Print($"DebugCleaveEmitter: {CastId} cone r={spec.Radius} angle1024={spec.Angle1024} lead={spec.LeadTicks} every={every}");
    }

    /// <inheritdoc />
    public void OnStep(SimState state, Action<SimEvent> emit)
    {
        if (Spec is not { } spec)
        {
            return;
        }
        var newEvents = new List<SimEvent>();
        var tick = state.Tick;
        if (state.Actors.TryGetValue(BoxId, out var box) && box.Alive && FiresAt(tick + spec.LeadTicks))
        {
            var e = new SimEvent(tick, "telegraph",
                ("actorId", JsValue.Int(BoxId)),
                ("cast", JsValue.Str(CastId)),
                ("shape", JsValue.Str("cone")),
                ("leadTicks", JsValue.Int(spec.LeadTicks)),
                ("resolvesTick", JsValue.Int(tick + spec.LeadTicks)),
                ("origin", JsValue.Vec(box.Pos)),
                ("yaw", JsValue.Int(box.Yaw)),
                ("radius", JsValue.Int(spec.Radius!.Value)),
                ("angle1024", JsValue.Int(spec.Angle1024!.Value)));
            emit(e);
            newEvents.Add(e);
        }
        var advance = Telegraphs.Advance(state, _active, newEvents, _nextTelegraphId, emit);
        _active = advance.Telegraphs;
        _nextTelegraphId = advance.NextTelegraphId;
    }

    // scripts.ts firesAt for at:"loop": every > 0, tick > 0, tick % every == 0.
    private bool FiresAt(int tick) => Every > 0 && tick > 0 && tick % Every == 0;
}
