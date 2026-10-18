using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Port of tests/bots/ping-wiring.test.ts (3 cases): pings-policy.ts is wired into bot.ts decideForActor
/// ahead of the skill-dial gate, and a ping cast suppresses role dispatch. The TS ran the real
/// roles/damage.ts (lane 3B); here role dispatch goes through <see cref="RoleSet"/>, so case 3 plugs in
/// <see cref="RotationCaster"/>, a stand-in damage role that always casts its first rotation ability (what
/// the real damage role does on that view: no kick is its turn, so it casts "filler").
/// </summary>
public sealed class PingWiringTests
{
    /// <summary>A stand-in damage role: casts rotation[0] at nothing, every time it is asked.</summary>
    private sealed class RotationCaster : IRole
    {
        public int Calls { get; private set; }

        public IReadOnlyList<Command> Decide(BotView view, ActorState self)
        {
            Calls++;
            return view.ClassKit.Rotation.Count == 0
                ? new List<Command>()
                : new List<Command> { new CastCommand(view.State.Tick, self.Id, view.ClassKit.Rotation[0]) };
        }
    }

    private static BotView MakeView(int tick, string[] rotation, params ActorState[] actors) =>
        View(MakeState(tick, actors), Kit("kick", rotation));

    [Fact(DisplayName = "bot.decide() ping wiring > reacts to a live interrupt ping even while the skill-dial gate is shut")]
    public void ReactsWhileGateShut()
    {
        var bot = Bot.MakeBot();
        var memory = new BotMemory(0, 1000);
        var enemy = MakeActor(id: 100, kind: ActorKind.Enemy, castId: "big-cast");
        var self = MakeActor(id: 2);
        var view = MakeView(10, System.Array.Empty<string>(), enemy, self);
        var ping = new PingEvent(10, 3, PingKind.Target) { TargetId = 100 };

        var result = bot.Decide(view, memory, self.Id, Skill.SeedPrng(1), new[] { ping });

        Assert.Equal(new Command[] { new CastCommand(10, 2, "kick", 100) }, result.Commands);
    }

    [Fact(DisplayName = "bot.decide() ping wiring > does not react to pings when none are passed (back-compat: pings defaults to none)")]
    public void NoPingsNoReaction()
    {
        var bot = Bot.MakeBot();
        var memory = new BotMemory(0, 1000);
        var enemy = MakeActor(id: 100, kind: ActorKind.Enemy, castId: "big-cast");
        var self = MakeActor(id: 2);
        var view = MakeView(10, System.Array.Empty<string>(), enemy, self);

        var result = bot.Decide(view, memory, self.Id, Skill.SeedPrng(1));

        Assert.Empty(result.Commands);
    }

    [Fact(DisplayName = "bot.decide() ping wiring > a ping-triggered cast suppresses this tick's role-dispatch cast (no double cast)")]
    public void PingCastSuppressesRoleCast()
    {
        var role = new RotationCaster();
        var bot = Bot.MakeBot(new RoleSet(null, null, role));
        var memory = new BotMemory(100, 0);
        var enemy = MakeActor(id: 100, kind: ActorKind.Enemy, castId: "big-cast");
        var self = MakeActor(id: 2);
        var view = MakeView(10, new[] { "filler" }, enemy, self);
        var ping = new PingEvent(10, 3, PingKind.Target) { TargetId = 100 };

        var result = bot.Decide(view, memory, self.Id, Skill.SeedPrng(1), new[] { ping });

        var casts = result.Commands.OfType<CastCommand>().ToList();
        Assert.Single(casts);
        Assert.Equal("kick", casts[0].AbilityId);
        Assert.Equal(0, role.Calls);

        // Control: with no ping the same gate-open tick does reach role dispatch (the stand-in is live).
        var plain = bot.Decide(view, memory, self.Id, Skill.SeedPrng(1));
        Assert.Equal(new Command[] { new CastCommand(10, 2, "filler") }, plain.Commands);
        Assert.Equal(1, role.Calls);
    }
}
