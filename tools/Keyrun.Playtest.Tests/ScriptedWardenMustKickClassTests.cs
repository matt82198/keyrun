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
/// G7 / D66 R-REMNANT-3 (finding f-2b38938f). The autorun <see cref="ScriptedWarden"/> gates its kick on the class id
/// choir.lieutenant alone, so an interruptible must-kick cast from any other enemy class (the Remnant Cantor's
/// Remnant Mending) is never kicked. Here a Remnant Cantor (a non-lieutenant caster, class id remnant.cantor) is
/// halfway through an interruptible cast inside Shield Bash range while engaged on the Warden: the Warden must kick it.
/// The cast reuses choir.healCast as the interruptible ability, so only the caster's class id differs from the Choir.
/// </summary>
public sealed class ScriptedWardenMustKickClassTests
{
    private const int WardenId = 1;
    private const string Kick = "warden.shieldBash";
    private const string Mending = "choir.healCast";
    private const string RemnantCantorClass = "remnant.cantor";

    private static Run Build()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        return created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
    }

    [Fact(DisplayName = "G7 R-REMNANT-3: a Remnant Cantor mid-way through an interruptible cast inside Shield Bash range is kicked by the Warden")]
    public void Warden_kicks_a_must_kick_cast_from_a_non_lieutenant_class()
    {
        var run = Build();
        var enemy = run.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.ClassId == ScriptedWarden.LieutenantClass)
            .OrderBy(a => a.Id)
            .First();
        var state = run.State.Clone();
        var warden = state.Actors[WardenId];
        var tick = state.Tick;
        var mending = run.Content.Abilities[Mending];
        Assert.True(mending.Interruptible, "the content must make the cast interruptible for this scene");

        var cantor = state.Actors[enemy.Id];
        cantor.ClassId = RemnantCantorClass;
        cantor.Idle = false;
        cantor.Alive = true;
        cantor.Pos = new Vec3(warden.Pos.X + 1500, warden.Pos.Y, warden.Pos.Z);
        cantor.TargetId = WardenId;
        // Halfway through the cast: the kick window is open (KickableLieutenant requires tick >= start + CastTicks / 2).
        cantor.CastId = Mending;
        cantor.CastStartTick = tick - (mending.CastTicks / 2) - 1;
        cantor.CastEndsTick = cantor.CastStartTick + mending.CastTicks;

        var scene = run with { State = state };
        var commands = new List<Command>();
        new ScriptedWarden().Sample(tick, scene, commands);
        var kicks = commands.OfType<CastCommand>().Where(c => c.AbilityId == Kick).ToList();

        Assert.Single(kicks);
        Assert.Equal(cantor.Id, kicks[0].TargetId);
    }
}
