using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-WARDEN-KICK-1 (core loop defers to WoW: the tank interrupts the caster that is casting). With two engaged
/// choir.lieutenant casters in a fight, the autorun <see cref="ScriptedWarden"/> must kick whichever one is mid-way
/// through an interruptible cast and inside Shield Bash range, not the first in actor-list order. Before the fix it
/// kicked the first engaged lieutenant (ClassId, engaged) and so never kicked the healer that was casting, which is why
/// the warrior-remnant pull's Choral Mending healed the pack faster than the Warden could damage it (Run C, #515).
/// </summary>
public sealed class ScriptedWardenKickTests
{
    private const int WardenId = 1;
    private const string Kick = "warden.shieldBash";
    private const string Mending = "choir.healCast";

    private static Run Build()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        return created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
    }

    [Fact(DisplayName = "R-WARDEN-KICK-1: with two engaged lieutenants and only the second casting Choral Mending, the Warden's Shield Bash lands on the caster")]
    public void Warden_kicks_the_lieutenant_that_is_casting_not_the_first_in_list()
    {
        var run = Build();
        var lieutenants = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.ClassId == ScriptedWarden.LieutenantClass).OrderBy(a => a.Id).Take(2).ToList();
        Assert.Equal(2, lieutenants.Count);
        var state = run.State.Clone();
        var warden = state.Actors[WardenId];
        var tick = state.Tick;
        var mending = run.Content.Abilities[Mending];
        Assert.True(mending.Interruptible, "the content must make Choral Mending interruptible for this scene");

        var idle = state.Actors[lieutenants[0].Id];
        var caster = state.Actors[lieutenants[1].Id];
        foreach (var e in new[] { idle, caster })
        {
            e.Idle = false;
            e.Alive = true;
            e.Pos = new Vec3(warden.Pos.X + 1500, warden.Pos.Y, warden.Pos.Z);
            e.TargetId = WardenId;
        }
        // The second lieutenant is halfway through Choral Mending: the kick opens at the cast's midpoint.
        caster.CastId = Mending;
        caster.CastStartTick = tick - (mending.CastTicks / 2) - 1;
        caster.CastEndsTick = caster.CastStartTick + mending.CastTicks;

        var scene = run with { State = state };
        var commands = new List<Command>();
        new ScriptedWarden().Sample(tick, scene, commands);
        var kicks = commands.OfType<CastCommand>().Where(c => c.AbilityId == Kick).ToList();

        Assert.Single(kicks);
        Assert.Equal(caster.Id, kicks[0].TargetId);
    }
}
