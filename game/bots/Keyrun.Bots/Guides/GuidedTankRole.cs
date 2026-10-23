using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Keyrun.Sim.Nav;

namespace Keyrun.Bots.Guides;

/// <summary>
/// The Guided tank role (G2.4 K3b): the Tank slot of a guided party's registry. It answers the guide's telegraphs section
/// first (a stepOut telegraph the guide names, covering the seat, is answered by moving before any cast, R-GUIDE-4). Then
/// it runs the same kick-ledger, floor and engage answers the scripted tank gives, with the ledger kick filtered to the
/// guide's kicks section, and plays the guide's rotation in order (the first castable entry whose condition holds). Every
/// rotation cast is tagged with its guide entry id (<c>guide:&lt;class&gt;:&lt;entry&gt;</c>) so the receipt names the rule that
/// fired. A seat of another class fails closed: it has no guide.
/// </summary>
public sealed class GuidedTankRole : IRole
{
    // Abilities the guide's rotation aims at the seat's target; every other press is cast with no target (a ground or self ability).
    private static readonly HashSet<string> TargetedPresses = new(StringComparer.Ordinal)
    {
        "warden.strike",
        "warden.guardBreak",
        "warden.reckoning",
    };

    /// <summary>Margin added to each named telegraph's radius when choosing the step-out point, mm (the Floor escape margin).</summary>
    private const long StepOutMarginMm = 500;

    private readonly BotGuide _guide;
    private readonly IReadOnlySet<string> _kickCasts;
    private readonly IReadOnlySet<string> _stepOutTelegraphs;

    /// <summary>Builds the role over one class guide.</summary>
    /// <param name="guide">The guide; its class is the only class this role decides for.</param>
    public GuidedTankRole(BotGuide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);
        _guide = guide;
        _kickCasts = guide.Kicks.Select(k => k.Cast).ToHashSet(StringComparer.Ordinal);
        _stepOutTelegraphs = guide.Telegraphs
            .Where(t => t.Response == "stepOut")
            .Select(t => t.Cast)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The guide's answers for <paramref name="self"/>; empty when no entry is castable and holding.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor (alive).</param>
    /// <returns>Commands, possibly empty.</returns>
    /// <exception cref="InvalidOperationException">The seat's class has no loaded guide.</exception>
    public IReadOnlyList<Command> Decide(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        if (self.ClassId != _guide.ClassId)
        {
            throw new InvalidOperationException($"guided tank seat {self.Id} is class '{self.ClassId}': no guide is loaded for that class");
        }
        var tick = view.State.Tick;
        var kit = RoleShared.KitOf(view, self.ClassId);
        var ctx = new GuideContext(view, self, kit, _kickCasts);

        if (ListedStepOut(view, self) is { } stepOut)
        {
            return new Command[] { Reasons.Tag(stepOut, "tank:floor-escape") };
        }
        if (GuideSet.ListedKickCommand(ctx) is { } kick)
        {
            return new Command[] { Reasons.Tag(kick, "tank:ledger-kick") };
        }
        if (Floor.FloorEscapeCommand(view, self) is { } escape)
        {
            return new Command[] { Reasons.Tag(escape, "tank:floor-escape") };
        }
        // The engage walk holds the seat while the pack is out of reach: an approach move (non-zero Dir) returns alone. In reach
        // the walk is at most a yaw turn (zero Dir), which never blocks a cast (R-GUIDE-1 (b)): the guide's rotation runs the
        // same tick and its cast goes out alongside the turn, as the scripted Tank orders move and cast in one decision.
        var walk = TankEngage.TankEngageCommands(view, self, kit)?.OfType<MoveCommand>().Cast<Command>().ToList() ?? new List<Command>();
        var approaching = walk.Any(c => c is MoveCommand m && (m.Dir.X != 0 || m.Dir.Z != 0));
        if (approaching)
        {
            Reasons.TagAll(walk, "tank:engage");
            return walk;
        }
        Reasons.TagAll(walk, "tank:engage");

        foreach (var rule in _guide.Rotation)
        {
            if (!rule.When(ctx) || !RoleShared.ReadyAndAffordable(self, rule.Press, tick, kit))
            {
                continue;
            }
            var target = TargetedPresses.Contains(rule.Press) ? self.TargetId : null;
            var cast = Reasons.TagGuided(new CastCommand(tick, self.Id, rule.Press, target), $"guide:{_guide.ClassId}:{rule.Id}", rule.Reason);
            walk.Add(cast);
            return walk;
        }
        return walk;
    }

    /// <summary>
    /// The step-out for a live telegraph the guide's telegraphs section names with response stepOut, when it covers the seat:
    /// the nearest point clear of every such telegraph (plus the margin), walked to along the sim's path on a layout run
    /// (the Floor escape point rule). Null when the seat is clear of them. Unlike the Floor escape, this is not deferred by an
    /// ordinary danger telegraph: the guide names the telegraph, so the guide's answer comes first.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <returns>The move, or null.</returns>
    private MoveCommand? ListedStepOut(BotView view, ActorState self)
    {
        if (_stepOutTelegraphs.Count == 0)
        {
            return null;
        }
        var tick = view.State.Tick;
        var selfMm = PointMm.Of(self.Pos);
        var listed = view.Telegraphs
            .Where(t => t.Danger && t.ResolvesTick > tick && _stepOutTelegraphs.Contains(t.Id) && !Positioning.IsSpent(t, view.State))
            .ToList();
        if (!Positioning.IsInAnyDanger(selfMm, listed))
        {
            return null;
        }
        var danger = listed.Select(t => t with { Radius = (t.Radius ?? 0) + StepOutMarginMm }).ToList();
        if (view.Layout is { } layout)
        {
            var grid = NavGrid.Of(layout);
            var walkable = Positioning.NearestSafePoint(selfMm, danger, extraCheck: p => grid.Walkable(new Vec3(p.X, self.Pos.Y, p.Z)));
            return walkable is { } point
                ? PathMoves.Along(tick, self, layout, new Vec3(point.X, self.Pos.Y, point.Z))
                : null;
        }
        return Positioning.NearestSafePoint(selfMm, danger) is { } safe
            ? Moves.MoveToward(tick, self.Id, selfMm, safe)
            : null;
    }
}
