using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// Port of bots/src/pings-policy.ts: deterministic, skill-dial-free reactions to party G-pings, called by
/// bot.ts decideForActor ahead of the skill-dial gate. Priority: interrupt &gt; Window payoff &gt; spot move.
/// Reads only <see cref="BotView.ClassKit"/> (never the content-backed kits), exactly as the TS does.
/// </summary>
public static class PingsPolicy
{
    /// <summary>pings-policy.ts PAYOFF_ABILITY_PREFIX: a scripted kit marks its Payoff ability id "payoff:".</summary>
    public const string PayoffAbilityPrefix = "payoff:";

    /// <summary>pings-policy.ts PING_MOVE_TICKS: a spot ping draws healer/damage bots for 40 ticks (2 s).</summary>
    public const int PingMoveTicks = 40;

    /// <summary>pings-policy.ts SPOT_ARRIVE_MM: within 500 mm of the point counts as arrived.</summary>
    private const double SpotArriveMm = 500;

    private static ActorState? CastingEnemy(SortedDictionary<int, ActorState> actors, int targetId) =>
        actors.TryGetValue(targetId, out var t) && t.Alive && t.Kind == ActorKind.Enemy && t.CastId is not null ? t : null;

    /// <summary>
    /// pings-policy.ts interruptPingCommands: on a live target ping naming a casting enemy, the lowest-id alive
    /// damage player whose interrupt is ready kicks that enemy. Empty otherwise.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="pings">Live party pings.</param>
    /// <returns>Zero or one cast.</returns>
    public static List<Command> InterruptPingCommands(BotView view, ActorState self, IReadOnlyList<PingEvent> pings)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        var interruptId = view.ClassKit.InterruptAbilityId;
        if (string.IsNullOrEmpty(interruptId) || self.Role != ActorRole.Damage || !self.Alive)
        {
            return new List<Command>();
        }
        var ping = Pings.LatestLivePing(pings, tick, Pings.PingReactionWindowTicks, PingKind.Target);
        if (ping?.TargetId is not { } targetId)
        {
            return new List<Command>();
        }
        var target = CastingEnemy(view.State.Actors, targetId);
        if (target is null)
        {
            return new List<Command>();
        }
        var selfKit = view.Kits.ResolveKit(self.ClassId, view.ClassKit);
        var eligible = view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Player && a.Alive && a.Role == ActorRole.Damage)
            .Where(a => Readiness.AbilityReady(a, interruptId, tick, selfKit))
            .Select(a => a.Id)
            .ToList();
        if (!Readiness.AbilityReady(self, interruptId, tick, selfKit) || !Priority.IsInterruptResponsible(self.Id, eligible))
        {
            return new List<Command>();
        }
        return new List<Command> { new CastCommand(tick, self.Id, interruptId, target.Id) };
    }

    /// <summary>
    /// pings-policy.ts windowPingPayoffCommands: on a live target ping naming an alive Window carrier, a bot whose
    /// rotation holds a "payoff:" ability targets the carrier (unless already) and casts it, when ready.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="pings">Live party pings.</param>
    /// <returns>Zero, one or two commands (target, then cast).</returns>
    public static List<Command> WindowPingPayoffCommands(BotView view, ActorState self, IReadOnlyList<PingEvent> pings)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        var none = new List<Command>();
        if (!self.Alive)
        {
            return none;
        }
        var payoffId = view.ClassKit.Rotation.FirstOrDefault(id => id.StartsWith(PayoffAbilityPrefix, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(payoffId))
        {
            return none;
        }
        var ping = Pings.LatestLivePing(pings, tick, Pings.PingReactionWindowTicks, PingKind.Target);
        if (ping?.TargetId is not { } targetId)
        {
            return none;
        }
        if (!view.State.Actors.TryGetValue(targetId, out var target) || !target.Alive)
        {
            return none;
        }
        var hasWindow = target.Effects.Any(e => e.EffectId.StartsWith(Pings.WindowEffectPrefix, StringComparison.Ordinal));
        if (!hasWindow || !Readiness.AbilityReady(self, payoffId, tick))
        {
            return none;
        }
        var commands = new List<Command>();
        if (self.TargetId != target.Id)
        {
            commands.Add(new TargetCommand(tick, self.Id, target.Id));
        }
        commands.Add(new CastCommand(tick, self.Id, payoffId, target.Id));
        return commands;
    }

    /// <summary>
    /// pings-policy.ts spotPingMoveCommands: healer/damage bots walk toward a spot ping live within
    /// <see cref="PingMoveTicks"/> until within 500 mm. The tank holds position.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="pings">Live party pings.</param>
    /// <returns>Zero or one move.</returns>
    public static List<Command> SpotPingMoveCommands(BotView view, ActorState self, IReadOnlyList<PingEvent> pings)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        if (!self.Alive || (self.Role != ActorRole.Healer && self.Role != ActorRole.Damage))
        {
            return new List<Command>();
        }
        var ping = Pings.LatestLivePing(pings, tick, PingMoveTicks, PingKind.Spot);
        if (ping?.Point is not { } point)
        {
            return new List<Command>();
        }
        double dx = point.X - self.Pos.X;
        double dz = point.Z - self.Pos.Z;
        if (Math.Sqrt((dx * dx) + (dz * dz)) <= SpotArriveMm)
        {
            return new List<Command>();
        }
        var move = Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), point);
        return move is null ? new List<Command>() : new List<Command> { move };
    }

    /// <summary>pings-policy.ts pingReactionCommands: the first non-empty of interrupt, Window payoff, spot move.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="pings">Live party pings.</param>
    /// <returns>The reaction commands.</returns>
    public static List<Command> PingReactionCommands(BotView view, ActorState self, IReadOnlyList<PingEvent> pings)
    {
        var interrupt = InterruptPingCommands(view, self, pings);
        if (interrupt.Count > 0)
        {
            return interrupt;
        }
        var payoff = WindowPingPayoffCommands(view, self, pings);
        return payoff.Count > 0 ? payoff : SpotPingMoveCommands(view, self, pings);
    }
}
