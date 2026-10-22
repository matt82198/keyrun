using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Guides;

/// <summary>
/// The Guided tank role (G2.4 K3b): the Tank slot of a guided party's registry. It plays the guide's rotation in order
/// (the first castable entry whose condition holds), after the same kick-ledger, floor and engage answers the scripted
/// tank gives first, so the guide replaces only the rotation. Every rotation cast is tagged with its guide entry id
/// (<c>guide:&lt;class&gt;:&lt;entry&gt;</c>) so the receipt names the rule that fired. A seat of another class fails
/// closed: it has no guide.
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

    private readonly BotGuide _guide;

    /// <summary>Builds the role over one class guide.</summary>
    /// <param name="guide">The guide; its class is the only class this role decides for.</param>
    public GuidedTankRole(BotGuide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);
        _guide = guide;
    }

    /// <summary>The guide's rotation for <paramref name="self"/>; empty when no entry is castable and holding.</summary>
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

        if (RoleShared.LedgerKickCommand(view, self, kit) is { } kick)
        {
            return new Command[] { Reasons.Tag(kick, "tank:ledger-kick") };
        }
        if (Floor.FloorEscapeCommand(view, self) is { } escape)
        {
            return new Command[] { Reasons.Tag(escape, "tank:floor-escape") };
        }
        // The engage walk holds the seat until the pack is in reach. A pull cast in reach is left to the guide's rotation
        // (the guide's Strike is the Setup), so a guided seat never pulls out of reach.
        var walk = TankEngage.TankEngageCommands(view, self, kit)?.OfType<MoveCommand>().Cast<Command>().ToList();
        if (walk is { Count: > 0 })
        {
            Reasons.TagAll(walk, "tank:engage");
            return walk;
        }

        var ctx = new GuideContext(view, self, kit);
        foreach (var rule in _guide.Rotation)
        {
            if (!rule.When(ctx) || !RoleShared.ReadyAndAffordable(self, rule.Press, tick, kit))
            {
                continue;
            }
            var target = TargetedPresses.Contains(rule.Press) ? self.TargetId : null;
            var cast = Reasons.TagGuided(new CastCommand(tick, self.Id, rule.Press, target), $"guide:{_guide.ClassId}:{rule.Id}", rule.Reason);
            return new Command[] { cast };
        }
        return Array.Empty<Command>();
    }
}
