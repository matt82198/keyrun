using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// FILLER-LEAST-RECENTLY-CAST (finding f-c356528f, "root cause why they spam one ability"; G2.1). The rotation was a
/// stateless priority list: every cooldown-0, cost-0 filler is ready on every global, and the filler order preferred the
/// first Setup filler whenever no Window was open, with no memory of the last cast (capture hv-1911c618: Cinder cast
/// flare 106 times of 215). Among READY fillers the bot now casts the least recently cast one, carried in the bot's
/// memory between decisions.
/// </summary>
public sealed class FillerVarietyTests
{
    private const string Flare = "cinder.flare";
    private const string EmberBolt = "cinder.emberBolt";

    private static ActorState Cinder(IEnumerable<(string, int)> cooldowns) =>
        Actor(id: 3, classId: "cinder", role: ActorRole.Damage, x: 1000, z: 0, health: 250, maxHealth: 250, resource: 0, maxResource: 100,
            targetId: 50, cooldowns: cooldowns);

    private static ActorState Enemy() =>
        Actor(id: 50, kind: ActorKind.Enemy, classId: "enemy", role: ActorRole.Damage, x: 0, z: 8000, health: 100000, maxHealth: 100000);

    /// <summary>Every Cinder rotation ability except its two fillers on cooldown far past the decisions below.</summary>
    private static IEnumerable<(string, int)> NonFillersDown() =>
        new[] { "cinder.coldSnap", "cinder.detonate", "cinder.cinderfield" }.Select(id => (id, 999999));

    [Fact(DisplayName = "Filler variety > one seat with two always-ready fillers casts both over 20 decisions and never the same one twice running")]
    public void Two_ready_fillers_alternate_over_twenty_decisions()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var casts = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var tick = 10 + (i * 100);
            var view = ViewOf(tick, RealKits, null, Cinder(NonFillersDown()), Enemy());
            var decision = bot.Decide(view, memory, 3, prng);
            memory = decision.Memory;
            prng = decision.Prng;
            casts.AddRange(CastIds(decision.Commands));
        }
        Assert.Equal(20, casts.Count);
        Assert.Contains(Flare, casts);
        Assert.Contains(EmberBolt, casts);
        for (var i = 1; i < casts.Count; i++)
        {
            Assert.True(casts[i] != casts[i - 1], $"decision {i} repeats {casts[i]} while the other filler is ready: {string.Join(",", casts)}");
        }
    }

    [Fact(DisplayName = "Filler variety > an empty memory still opens with the window-state preference (the Setup filler on a bare target)")]
    public void Fresh_memory_keeps_the_window_preference()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var view = ViewOf(10, RealKits, null, Cinder(NonFillersDown()), Enemy());
        var decision = bot.Decide(view, BotMemory.Default(100), 3, Skill.SeedPrng(1));
        Assert.Equal(new[] { Flare }, CastIds(decision.Commands));
    }
}
