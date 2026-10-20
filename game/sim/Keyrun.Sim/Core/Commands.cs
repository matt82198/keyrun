using System;

namespace Keyrun.Sim.Core;

/// <summary>
/// A per-tick caller command (sim/core/commands.ts Command): plain immutable data addressed to one tick and
/// one actor. The TS union is modelled as one sealed record per <c>kind</c>.
/// </summary>
/// <param name="Tick">The tick the command applies to; <see cref="Core.Tick.Step"/> drops any other.</param>
/// <param name="ActorId">The acting actor.</param>
public abstract record Command(int Tick, int ActorId)
{
    /// <summary>The TS <c>kind</c> tag.</summary>
    public abstract string Kind { get; }
}

/// <summary>"move": walk one tick along <paramref name="Dir"/> and face <paramref name="Yaw"/>.</summary>
/// <param name="Tick">Tick.</param>
/// <param name="ActorId">Actor.</param>
/// <param name="Dir">Direction, each component -1000..1000 (normalised by movement, not here).</param>
/// <param name="Yaw">Facing, 0..1023.</param>
public sealed record MoveCommand(int Tick, int ActorId, Dir2 Dir, int Yaw) : Command(Tick, ActorId)
{
    /// <inheritdoc />
    public override string Kind => "move";
}

/// <summary>"dash" (Space, unrecorded placeholder): a one-tick burst of Movement.DashDistanceMm along <paramref name="Dir"/>.</summary>
/// <param name="Tick">Tick.</param>
/// <param name="ActorId">Actor.</param>
/// <param name="Dir">Direction, each component -1000..1000.</param>
/// <param name="Yaw">Facing, 0..1023.</param>
public sealed record DashCommand(int Tick, int ActorId, Dir2 Dir, int Yaw) : Command(Tick, ActorId)
{
    /// <inheritdoc />
    public override string Kind => "dash";
}

/// <summary>"cast": start casting an ability, optionally at a target or a ground point.</summary>
/// <param name="Tick">Tick.</param>
/// <param name="ActorId">Actor.</param>
/// <param name="AbilityId">Ability id (non-empty).</param>
/// <param name="TargetId">Target actor, or null (TS: absent).</param>
/// <param name="Point">Ground point, or null (TS: absent).</param>
public sealed record CastCommand(int Tick, int ActorId, string AbilityId, int? TargetId = null, Vec3? Point = null) : Command(Tick, ActorId)
{
    /// <inheritdoc />
    public override string Kind => "cast";
}

/// <summary>"stopcast": cancel the actor's own cast.</summary>
/// <param name="Tick">Tick.</param>
/// <param name="ActorId">Actor.</param>
public sealed record StopCastCommand(int Tick, int ActorId) : Command(Tick, ActorId)
{
    /// <inheritdoc />
    public override string Kind => "stopcast";
}

/// <summary>"target": set (or clear, with null) the actor's target.</summary>
/// <param name="Tick">Tick.</param>
/// <param name="ActorId">Actor.</param>
/// <param name="TargetId">New target, or null to clear (the field is always present in the TS).</param>
public sealed record TargetCommand(int Tick, int ActorId, int? TargetId) : Command(Tick, ActorId)
{
    /// <inheritdoc />
    public override string Kind => "target";
}

/// <summary>Validation and ordering rules for <see cref="Command"/> (sim/core/commands.ts, sim/core/tick.ts).</summary>
public static class Commands
{
    /// <summary>Lowest accepted direction component.</summary>
    public const long DirMin = -1000;

    /// <summary>Highest accepted direction component.</summary>
    public const long DirMax = 1000;

    /// <summary>
    /// validateCommand over an already-typed command: returns the same instance when it is well formed and
    /// null otherwise (never throws). The type system already rules out the TS's non-integer and wrong-type
    /// cases; untrusted JSON goes through <see cref="CommandJson.Parse"/>, which checks those too.
    /// </summary>
    public static Command? Validate(Command? c)
    {
        if (c is null || c.Tick < 0)
        {
            return null;
        }
        switch (c)
        {
            case MoveCommand m:
                return ValidMotion(m.Dir, m.Yaw) ? c : null;
            case DashCommand d:
                return ValidMotion(d.Dir, d.Yaw) ? c : null;
            case CastCommand cast:
                return string.IsNullOrEmpty(cast.AbilityId) ? null : c;
            case StopCastCommand:
            case TargetCommand:
                return c;
            default:
                return null;
        }
    }

    private static bool ValidMotion(Dir2 dir, int yaw) =>
        dir.X >= DirMin && dir.X <= DirMax && dir.Z >= DirMin && dir.Z <= DirMax && yaw >= 0 && yaw <= 1023;

    /// <summary>
    /// sim/core/tick.ts KIND_ORDER: cast 0, move 1, dash 2, stopcast 3, target 4 (dash sorts right after move).
    /// </summary>
    public static int KindOrder(Command c) => c switch
    {
        CastCommand => 0,
        MoveCommand => 1,
        DashCommand => 2,
        StopCastCommand => 3,
        TargetCommand => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(c), "unknown command kind"),
    };
}
