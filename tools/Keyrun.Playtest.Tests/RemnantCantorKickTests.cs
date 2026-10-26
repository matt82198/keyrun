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
/// D66 / R-REMNANT-3 (f-73d9d4e2, f-2b38938f, G7). The scripted Warden selects its kick target only by
/// ClassId == "choir.lieutenant" (ScriptedWarden.cs KickableLieutenant), so a remnant.cantor mid-way through the
/// interruptible remnant.healCast (Remnant Mending) is never kicked. Modelled on ScriptedWardenKickTests (#515): the
/// cantor case is the red pin, the lieutenant case is the green control.
/// </summary>
public sealed class RemnantCantorKickTests
{
    private const int WardenId = 1;
    private const string Kick = "warden.shieldBash";
    private const string CantorClass = "remnant.cantor";
    private const string RemnantMending = "remnant.healCast";

    private static Run Build()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        return created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
    }

    /// <summary>Builds a scene with one engaged enemy of <paramref name="classId"/> half-way through <paramref name="castId"/> in kick reach, then samples the Warden.</summary>
    private static (List<CastCommand> Kicks, int CasterId) Sample(string classId, string castId)
    {
        var run = Build();
        var donor = run.State.Actors.Values.First(a => a.Kind == ActorKind.Enemy && a.ClassId == ScriptedWarden.LieutenantClass);
        var state = run.State.Clone();
        var warden = state.Actors[WardenId];
        var tick = state.Tick;
        Assert.True(run.Content.Abilities.TryGetValue(castId, out var cast), $"ability {castId} is not in the run content");
        Assert.True(cast!.Interruptible, $"{castId} must be interruptible for this scene");

        var caster = state.Actors[donor.Id];
        caster.ClassId = classId;
        caster.Idle = false;
        caster.Alive = true;
        caster.Pos = new Vec3(warden.Pos.X + 1500, warden.Pos.Y, warden.Pos.Z);
        caster.TargetId = WardenId;
        caster.CastId = castId;
        caster.CastStartTick = tick - (cast.CastTicks / 2) - 1;
        caster.CastEndsTick = caster.CastStartTick + cast.CastTicks;

        var commands = new List<Command>();
        new ScriptedWarden().Sample(tick, run with { State = state }, commands);
        return (commands.OfType<CastCommand>().Where(c => c.AbilityId == Kick).ToList(), caster.Id);
    }

    [Fact(DisplayName = "D66 R-REMNANT-3: a remnant.cantor half-way through Remnant Mending inside Shield Bash reach gets the Warden's kick")]
    public void Warden_kicks_the_casting_remnant_cantor()
    {
        var (kicks, casterId) = Sample(CantorClass, RemnantMending);
        Assert.True(kicks.Count == 1 && kicks[0].TargetId == casterId,
            $"expected exactly one {Kick} on remnant.cantor actor {casterId} mid-{RemnantMending}; got [{string.Join(", ", kicks.Select(k => k.TargetId))}]");
    }

    [Fact(DisplayName = "D66 R-REMNANT-3 control: the casting choir.lieutenant is still kicked")]
    public void Warden_still_kicks_the_casting_choir_lieutenant()
    {
        var (kicks, casterId) = Sample(ScriptedWarden.LieutenantClass, "choir.healCast");
        Assert.True(kicks.Count == 1 && kicks[0].TargetId == casterId,
            $"expected exactly one {Kick} on lieutenant actor {casterId}; got [{string.Join(", ", kicks.Select(k => k.TargetId))}]");
    }
}
