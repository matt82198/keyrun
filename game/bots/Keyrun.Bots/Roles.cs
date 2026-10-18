using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Bots;

/// <summary>
/// One role state machine (bots/src/roles.ts re-exports <c>tankDecide</c>, <c>healerDecide</c>,
/// <c>damageDecide</c>, each <c>(view, self) =&gt; Command[]</c>). Implementations live in roles/** (lane 3B).
/// </summary>
public interface IRole
{
    /// <summary>This tick's role commands for <paramref name="self"/>. Pure: never mutates the view.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor (alive).</param>
    /// <returns>Commands, possibly empty.</returns>
    IReadOnlyList<Command> Decide(BotView view, ActorState self);
}

/// <summary>
/// The role registry bot.ts decideForActor dispatches through: tank, healer and damage. The TS imports the
/// three functions statically; the port plugs them in here so the bot runner (lane 3A) and the roles
/// (lane 3B) land separately. A null slot contributes no commands.
/// </summary>
/// <param name="Tank">Tank role, or null.</param>
/// <param name="Healer">Healer role, or null.</param>
/// <param name="Damage">Damage role, or null.</param>
public sealed record RoleSet(IRole? Tank, IRole? Healer, IRole? Damage)
{
    /// <summary>No roles: only the danger tier and ping reactions act.</summary>
    public static RoleSet None { get; } = new(null, null, null);

    /// <summary>
    /// The real roles (lane 3B, roles/**): bot.ts's static <c>tankDecide</c>, <c>healerDecide</c> and
    /// <c>damageDecide</c> imports. This is what the TS <c>makeBot()</c> dispatches through.
    /// </summary>
    public static RoleSet Default { get; } = new(Roles.TankRole.Instance, Roles.HealerRole.Instance, Roles.DamageRole.Instance);

    /// <summary>The role for <paramref name="role"/>; null for <see cref="ActorRole.None"/> (bot.ts dispatches nothing).</summary>
    /// <param name="role">Actor role tag.</param>
    /// <returns>The role, or null.</returns>
    public IRole? For(ActorRole role) => role switch
    {
        ActorRole.Tank => Tank,
        ActorRole.Healer => Healer,
        ActorRole.Damage => Damage,
        _ => null,
    };
}
