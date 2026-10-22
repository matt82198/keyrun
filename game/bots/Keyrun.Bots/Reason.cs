using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Keyrun.Sim.Core;

namespace Keyrun.Bots;

/// <summary>
/// Why a bot issued one command: a stable rule id of the shape <c>role:rule</c> (for example
/// <c>tank:taunt-off-healer</c>) and a fixed short human phrase for that id. The id is never free text, so a receipt built
/// from it stays deterministic (G2.4, R-EVENTSTORE-2).
/// </summary>
/// <param name="RuleId">Stable rule id, <c>&lt;role&gt;:&lt;rule&gt;</c>; empty only for <see cref="Untagged"/>.</param>
/// <param name="Why">Fixed short phrase for the rule id.</param>
public sealed record Reason(string RuleId, string Why)
{
    /// <summary>A command nobody tagged: an empty rule id, which the reason tests reject.</summary>
    public static Reason Untagged { get; } = new(string.Empty, string.Empty);

    /// <summary>True when <see cref="RuleId"/> is non-empty (the receipt contract).</summary>
    public bool IsTagged => !string.IsNullOrEmpty(RuleId);
}

/// <summary>
/// The rule catalog and the command-to-reason side table. A decision site calls <see cref="Tag{T}(T, string)"/> on the
/// command it returns; the runner reads the reason back with <see cref="Of"/>. The table is keyed by command identity and
/// never touches the command record, so equality, ordering and the host's FNV fingerprint (commands only) are unchanged:
/// the same seed gives the same hash with or without a reason sink. Tagging an id that is not in <see cref="Catalog"/>
/// throws, so a free-text sentence can never become an id.
/// </summary>
public static class Reasons
{
    /// <summary>Every rule id with its fixed phrase. Adding a decision site means adding one row here.</summary>
    public static IReadOnlyDictionary<string, string> Catalog { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Bot.cs tiers (role prefix "bot": decided before any class role).
        ["bot:danger-dodge"] = "step out of a danger telegraph",
        ["bot:ping-reaction"] = "answer a party ping",
        ["bot:seed-bloom"] = "land a payoff in the open bloom ring",
        ["bot:kick-approach"] = "walk at the caster the kick ledger names",
        ["bot:lead-route"] = "lead the party along the route",
        ["bot:follow-route"] = "follow the leader along the route",
        ["bot:chain-pull"] = "chain-pull the next pack",
        ["bot:tank-approach"] = "approach the engaged pack",
        ["bot:healer-top-off"] = "top off a hurt ally between pulls",
        // Shared interrupt and floor answers that every role runs first.
        ["tank:ledger-kick"] = "interrupt the cast the ledger names",
        ["tank:floor-escape"] = "leave a floor patch",
        ["tank:break-bar-kick"] = "kick to break the bar",
        ["tank:melee-reapproach"] = "walk back into melee reach",
        ["tank:defensive"] = "use a defensive under pressure",
        ["tank:engage"] = "engage the pack",
        ["tank:retarget"] = "switch to the priority target",
        ["tank:face-away"] = "face the pack away from the party",
        ["healer:ledger-kick"] = "interrupt the cast the ledger names",
        ["healer:break-bar-kick"] = "kick to break the bar",
        ["healer:floor-escape"] = "leave a floor patch",
        ["healer:stance-swap"] = "swap stance to mend the party",
        ["healer:lock-setup"] = "set up the boss lock",
        ["healer:setup"] = "open a setup for a payoff partner",
        ["healer:idle-payoff"] = "use an idle button with nobody to heal",
        ["healer:press"] = "press a rotation button with nobody to heal",
        ["healer:stance-back"] = "swap stance back before a heal",
        ["healer:heal"] = "heal the hurt ally",
        ["healer:dispel-approach"] = "walk into dispel range",
        ["healer:dispel"] = "dispel the effect",
        ["damage:ledger-kick"] = "interrupt the cast the ledger names",
        ["damage:floor-escape"] = "leave a floor patch",
        ["damage:break-bar-kick"] = "kick to break the bar",
        ["damage:lock-setup"] = "set up the boss lock",
        ["damage:payoff"] = "cash a payoff on an open window",
        ["damage:setup"] = "set up a window for an ally payoff",
        ["damage:rotation"] = "cast the next rotation ability",
        ["damage:melee-reapproach"] = "walk back into melee reach",
        ["damage:window-approach"] = "walk to a friendly window",
    };

    private static readonly ConditionalWeakTable<Command, Reason> Table = new();

    /// <summary>Records why <paramref name="command"/> exists and returns the same instance.</summary>
    /// <typeparam name="T">Command type.</typeparam>
    /// <param name="command">The command a decision site returns.</param>
    /// <param name="ruleId">A key of <see cref="Catalog"/>.</param>
    /// <returns><paramref name="command"/>, unchanged.</returns>
    public static T Tag<T>(T command, string ruleId) where T : Command
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Catalog.TryGetValue(ruleId ?? string.Empty, out var why))
        {
            throw new ArgumentException($"rule id '{ruleId}' is not in the Reasons catalog", nameof(ruleId));
        }
        Table.AddOrUpdate(command, new Reason(ruleId!, why));
        return command;
    }

    /// <summary>Tags every command of <paramref name="commands"/> with <paramref name="ruleId"/>.</summary>
    /// <param name="commands">Commands from one decision site.</param>
    /// <param name="ruleId">A key of <see cref="Catalog"/>.</param>
    /// <returns><paramref name="commands"/>, unchanged.</returns>
    public static IReadOnlyList<Command> TagAll(IReadOnlyList<Command> commands, string ruleId)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var c in commands)
        {
            Tag(c, ruleId);
        }
        return commands;
    }

    /// <summary>The reason recorded for <paramref name="command"/>, or <see cref="Reason.Untagged"/>.</summary>
    /// <param name="command">Any command.</param>
    /// <returns>The reason.</returns>
    public static Reason Of(Command command) => Table.TryGetValue(command, out var r) ? r : Reason.Untagged;

    /// <summary>The reasons of <paramref name="commands"/>, index-aligned with it.</summary>
    /// <param name="commands">Commands in issue order.</param>
    /// <returns>One reason per command.</returns>
    public static IReadOnlyList<Reason> OfAll(IEnumerable<Command> commands) => commands.Select(Of).ToList();
}
