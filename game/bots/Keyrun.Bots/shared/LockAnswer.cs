using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using C = Keyrun.Content;

namespace Keyrun.Bots.Shared;

/// <summary>
/// BOSS-TEACHES (ruling R-BOSS-1, BIBLE s9 "The Lock"): the bots' answer to a boss Lock. The Lock is broken only by a
/// Setup that lands on the boss INSIDE its Window and a Payoff from another class closing THAT Setup's Window, and the
/// boss's Window remembers only whichever Setup last tagged it (Encounter/Boss.cs AfterCombat). So the party spends
/// exactly one Setup into each Lock and lets a partner pay it off:
/// <list type="bullet">
/// <item>One designated setter: of the living non-tank seats with a ready Setup that opens a Window on its target, the
/// one whose Setup lands first inside the Window (on or after the Lock's cast lands), then the lower actor id. It casts
/// that Setup on the boss while the boss casts the Lock (the cast bar is the warning, BIBLE s3) or once the Window is up
/// with no Setup of its own yet.</item>
/// <item>Everyone else holds Setups on the boss for the Lock: a Setup that would land before the Window opens, any Setup
/// while another seat's Setup is on its way to the boss or a Setup opened inside the Window is already on it (a second
/// one would replace the first and void its Payoff).</item>
/// </list>
/// The Lock is found from content alone: the ability tagged <see cref="LockTag"/>, the effect its applyEffect op puts on
/// the boss and its window op's lifetime. Layout runs only (the dungeon); the oracle runner is unchanged.
/// </summary>
public static class LockAnswer
{
    /// <summary>The content tag of a boss Lock ability (hollowWarden.lock).</summary>
    public const string LockTag = "setup:lock";

    /// <summary>The Lock's content: ability id, shield effect id, Window lifetime.</summary>
    private sealed record LockDef(string AbilityId, string ShieldEffectId, int WindowTicks);

    /// <summary>A Lock under way on <paramref name="Boss"/>: the first tick a Setup's landing counts.</summary>
    private sealed record LockPhase(ActorState Boss, int CountsFrom, LockDef Def);

    private static LockDef? LockOf(BotView view)
    {
        if (view.Layout is null || view.Kits.Content is not { } content)
        {
            return null;
        }
        foreach (var a in content.Abilities.Values)
        {
            if (a.Tags.Contains(LockTag) && a.Effects.OfType<C.ApplyEffectOp>().FirstOrDefault() is { } shield)
            {
                return new LockDef(a.Id, shield.EffectId, a.Effects.OfType<C.WindowOp>().Select(w => w.DurationTicks).DefaultIfEmpty(0).First());
            }
        }
        return null;
    }

    /// <summary>The Lock the boss is casting (counts from its landing) or holding open (counts from its open tick), or null.</summary>
    private static LockPhase? PhaseOf(BotView view)
    {
        if (LockOf(view) is not { } def)
        {
            return null;
        }
        var tick = view.State.Tick;
        foreach (var a in view.State.Actors.Values)
        {
            if (a.Kind != ActorKind.Enemy || !a.Alive)
            {
                continue;
            }
            if (string.Equals(a.CastId, def.AbilityId, StringComparison.Ordinal) && a.CastEndsTick is { } lands)
            {
                return new LockPhase(a, lands, def);
            }
            var ends = a.Effects.Where(e => e.EffectId == def.ShieldEffectId && e.EndsTick > tick).Select(e => e.EndsTick).DefaultIfEmpty(0).Max();
            if (ends > 0)
            {
                return new LockPhase(a, ends - def.WindowTicks, def);
            }
        }
        return null;
    }

    /// <summary>
    /// The content lifetime of Window <paramref name="windowId"/> (the effect id after "window:", which carries the
    /// setup class after a colon, e.g. <c>kindled:cinder</c>): the first ability whose window op opens it, or 0.
    /// </summary>
    private static int WindowTicks(C.ContentRegistry content, string windowId)
    {
        var colon = windowId.IndexOf(':', StringComparison.Ordinal);
        var id = colon < 0 ? windowId : windowId[..colon];
        return content.Abilities.Values
            .SelectMany(a => a.Effects.OfType<C.WindowOp>())
            .Where(w => string.Equals(w.WindowId, id, StringComparison.Ordinal))
            .Select(w => w.DurationTicks)
            .DefaultIfEmpty(0)
            .First();
    }

    /// <summary>A Setup that opens a Window on its target (not a floor patch).</summary>
    private static bool OpensTargetWindow(C.ContentRegistry content, string abilityId) =>
        content.Abilities.TryGetValue(abilityId, out var def) && def.Effects.OfType<C.WindowOp>().Any(w => w.OnTarget);

    private static int CastTicksOf(C.ContentRegistry content, string abilityId) =>
        content.Abilities.TryGetValue(abilityId, out var def) ? Math.Max(0, def.CastTicks) : 0;

    /// <summary>True when a Setup Window opened inside the Lock (open tick at or after it counts) is on the boss.</summary>
    private static bool FreshWindowOnBoss(BotView view, C.ContentRegistry content, LockPhase phase) =>
        BotWindows.OpenWindowsOn(view.State.Actors, view.State.Tick)
            .Any(w => w.TargetId == phase.Boss.Id && w.EndsTick - WindowTicks(content, w.WindowId) >= phase.CountsFrom);

    /// <summary>
    /// True when a player other than <paramref name="selfId"/> is casting a target-Window Setup at the boss that lands
    /// inside the Lock (one landing before the Window opens does not count for it).
    /// </summary>
    private static bool OtherSetupInFlight(BotView view, C.ContentRegistry content, LockPhase phase, int selfId) =>
        view.State.Actors.Values.Any(a => a.Kind == ActorKind.Player && a.Alive && a.Id != selfId &&
            a.CastId is { } cast && a.TargetId == phase.Boss.Id && OpensTargetWindow(content, cast) &&
            a.CastEndsTick is { } ends && ends >= phase.CountsFrom);

    /// <summary>
    /// The designated setter and its Setup: of the living non-tank players, each one's ready target-Window Setup in range
    /// whose landing (now plus its cast time) counts for the Lock, the earliest landing, then the lower actor id.
    /// </summary>
    private static (int ActorId, string SetupId)? Designated(BotView view, C.ContentRegistry content, LockPhase phase)
    {
        var tick = view.State.Tick;
        (int ActorId, string SetupId, int Lands)? best = null;
        foreach (var a in view.State.Actors.Values)
        {
            if (a.Kind != ActorKind.Player || !a.Alive || a.Role == ActorRole.Tank)
            {
                continue;
            }
            var kit = RoleShared.KitOf(view, a.ClassId);
            var dist = RoleShared.Dist(a, phase.Boss);
            foreach (var id in kit.SetupAbilityIds)
            {
                var lands = tick + CastTicksOf(content, id);
                if (!OpensTargetWindow(content, id) || lands < phase.CountsFrom || !RoleShared.ReadyAndAffordable(a, id, tick, kit) ||
                    (content.Abilities.TryGetValue(id, out var def) && def.Range > 0 && dist > def.Range))
                {
                    continue;
                }
                if (best is not { } b || lands < b.Lands || (lands == b.Lands && a.Id < b.ActorId))
                {
                    best = (a.Id, id, lands);
                }
            }
        }
        return best is { } d ? (d.ActorId, d.SetupId) : null;
    }

    /// <summary>
    /// True when <paramref name="self"/> should not start <paramref name="setupId"/> on <paramref name="targetId"/> now:
    /// that target is the boss in a Lock, and the Setup would land before the Window opens, another seat's Setup is on its
    /// way, a Setup opened inside the Window is already on the boss, or another seat is the designated setter.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="targetId">The Setup's target, or null.</param>
    /// <param name="setupId">The Setup ability.</param>
    /// <returns>True to hold.</returns>
    public static bool HoldsSetup(BotView view, ActorState self, int? targetId, string setupId)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(setupId);
        if (PhaseOf(view) is not { } phase || targetId != phase.Boss.Id || view.Kits.Content is not { } content)
        {
            return false;
        }
        if (view.State.Tick + CastTicksOf(content, setupId) < phase.CountsFrom || OtherSetupInFlight(view, content, phase, self.Id) ||
            FreshWindowOnBoss(view, content, phase))
        {
            return true;
        }
        return Designated(view, content, phase) is { } d && d.ActorId != self.Id;
    }

    /// <summary>
    /// True when <paramref name="commands"/> start a target-Window Setup that <see cref="HoldsSetup"/> holds (the cast's
    /// target, else the seat's own target).
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="commands">The commands a role would return.</param>
    /// <returns>True when they must not be sent this tick.</returns>
    public static bool Holds(BotView view, ActorState self, IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(commands);
        return view.Kits.Content is { } content && commands.OfType<CastCommand>().Any(c =>
            OpensTargetWindow(content, c.AbilityId) && HoldsSetup(view, self, c.TargetId ?? self.TargetId, c.AbilityId));
    }

    /// <summary>
    /// The designated setter's Setup into the Lock this tick (a target switch when needed, then the cast), or empty when
    /// <paramref name="self"/> is not the designated setter, a Setup is already on its way or a fresh one is on the boss.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The commands, possibly empty.</returns>
    public static IReadOnlyList<Command> SetupCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var tick = view.State.Tick;
        if (!self.Alive || kit.SetupAbilityIds.Count == 0 || PhaseOf(view) is not { } phase || view.Kits.Content is not { } content ||
            OtherSetupInFlight(view, content, phase, self.Id) || FreshWindowOnBoss(view, content, phase) ||
            Designated(view, content, phase) is not { } d || d.ActorId != self.Id)
        {
            return Array.Empty<Command>();
        }
        var commands = new List<Command>();
        if (self.TargetId != phase.Boss.Id)
        {
            commands.Add(new TargetCommand(tick, self.Id, phase.Boss.Id));
        }
        commands.Add(new CastCommand(tick, self.Id, d.SetupId, phase.Boss.Id));
        return commands;
    }
}
