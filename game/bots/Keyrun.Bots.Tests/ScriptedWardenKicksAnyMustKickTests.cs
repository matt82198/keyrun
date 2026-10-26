using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// f-2b38938f (G7 D66 R-REMNANT-3): the autorun <see cref="ScriptedWarden"/> must interrupt a must-kick cast whatever the
/// caster's class. The Remnant Cantor (content/data/enemies/vault.json, class id remnant.cantor) casts remnant.healCast
/// (Remnant Mending, interruptible, heals), the cast the D66 rule says must be kicked. The premise under test: the Warden
/// only kicks choir.lieutenant, so a remnant.cantor mid-cast inside Shield Bash range goes unkicked.
/// <para>
/// Shape: the hollow-vault k2 host run (the setup WardenAnswersCleaveTests builds), one enemy actor re-tagged as a
/// remnant.cantor, engaged, mid-way through remnant.healCast, 1000 mm from the Warden (inside Shield Bash's 3000 mm reach).
/// The control is the same shape with choir.lieutenant, which the Warden must kick on main.
/// </para>
/// </summary>
public sealed class ScriptedWardenKicksAnyMustKickTests
{
    private const string Kick = "warden.shieldBash";
    private const string Heal = "remnant.healCast";
    private const string ChoirLieutenant = "choir.lieutenant";
    private const string RemnantCantor = "remnant.cantor";
    private const long OffsetMm = 1000;
    private const int Tick = 200;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public ScriptedWardenKicksAnyMustKickTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Builds the hollow-vault k2 run, re-tags the first enemy as <paramref name="classId"/>, engages it and starts
    /// remnant.healCast on it half-way through, then returns the Warden's scripted commands for <see cref="Tick"/>.
    /// </summary>
    private static (List<Command> Commands, int CasterId) SampleKickFor(string classId)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, Lobby.DefaultClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, Lobby.DefaultClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var run = RunRoot.CreateRun(1, party, setup.Dungeon, setup.Content, options);

        var warden = run.State.Actors[HostRunSetup.HumanId];
        var caster = run.State.Actors.Values.First(a => a.Kind == ActorKind.Enemy);
        caster.ClassId = classId;
        caster.Alive = true;
        caster.Idle = false;
        caster.Pos = warden.Pos with { X = warden.Pos.X + OffsetMm };
        caster.CastId = Heal;
        caster.CastStartTick = Tick - 60;
        caster.CastEndsTick = Tick + 20;

        var commands = new List<Command>();
        new ScriptedWarden().Sample(Tick, run, commands);
        return (commands, caster.Id);
    }

    [Fact(DisplayName = "ScriptedWardenKicksAnyMustKickTests: control, the Warden kicks an in-range choir.lieutenant mid-cast")]
    public void Warden_kicks_a_choir_lieutenant_mid_cast()
    {
        var (commands, casterId) = SampleKickFor(ChoirLieutenant);
        _out.WriteLine($"control choir.lieutenant commands={string.Join(";", commands.Select(c => c.GetType().Name + ":" + c.ActorId))}");
        Assert.Contains(commands, c => c is CastCommand k && k.AbilityId == Kick && k.TargetId == casterId);
    }

    [Fact(DisplayName = "ScriptedWardenKicksAnyMustKickTests: the Warden kicks an in-range remnant.cantor mid Remnant Mending (D66 must-kick)")]
    public void Warden_kicks_a_remnant_cantor_mid_remnant_mending()
    {
        var (commands, casterId) = SampleKickFor(RemnantCantor);
        _out.WriteLine($"remnant.cantor commands={string.Join(";", commands.Select(c => c.GetType().Name + ":" + c.ActorId))}");
        Assert.Contains(commands, c => c is CastCommand k && k.AbilityId == Kick && k.TargetId == casterId);
    }

    /// <summary>The caster's distance from the Warden on the X axis for the out-of-range shape (well past Shield Bash's 3000 mm).</summary>
    private const long FarOffsetMm = 6000;

    /// <summary>The nearer distractor's distance from the Warden on the Z axis (inside the approach hold, outside nothing).</summary>
    private const long NearOffsetMm = 1500;

    /// <summary>
    /// The out-of-range shape (WoW M+: the tank walks to a must-kick caster). Builds the same hollow-vault k2 run, puts the
    /// first enemy as <paramref name="classId"/> mid remnant.healCast 6000 mm out on the Warden's X axis, and a second engaged
    /// enemy that casts nothing 1500 mm away on the Warden's Z axis. Samples the Warden at <see cref="Tick"/>. The run state
    /// is not stepped, so this is one decision at one tick, not a 20-tick trace.
    /// </summary>
    private static (List<Command> Commands, ActorState Warden, ActorState Caster) SampleOutOfRangeFor(string classId)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, Lobby.DefaultClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, Lobby.DefaultClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var run = RunRoot.CreateRun(1, party, setup.Dungeon, setup.Content, options);

        var warden = run.State.Actors[HostRunSetup.HumanId];
        var enemies = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy).OrderBy(a => a.Id).ToList();
        if (enemies.Count < 2)
        {
            throw new InvalidOperationException($"hollow-vault k2 must place at least two enemies, found {enemies.Count}");
        }

        var caster = enemies[0];
        caster.ClassId = classId;
        caster.Alive = true;
        caster.Idle = false;
        caster.Pos = warden.Pos with { X = warden.Pos.X + FarOffsetMm };
        caster.CastId = Heal;
        caster.CastStartTick = Tick - 60;
        caster.CastEndsTick = Tick + 20;

        var distractor = enemies[1];
        distractor.Alive = true;
        distractor.Idle = false;
        distractor.CastId = null;
        distractor.CastStartTick = null;
        distractor.CastEndsTick = null;
        distractor.Pos = warden.Pos with { Z = warden.Pos.Z + NearOffsetMm };

        var commands = new List<Command>();
        new ScriptedWarden().Sample(Tick, run, commands);
        return (commands, warden, caster);
    }

    /// <summary>True when any Warden move command points with a positive dot product toward <paramref name="target"/>.</summary>
    private static bool MovesToward(List<Command> commands, ActorState warden, ActorState target)
    {
        var dx = (long)target.Pos.X - warden.Pos.X;
        var dz = (long)target.Pos.Z - warden.Pos.Z;
        return commands.OfType<MoveCommand>().Any(m => (m.Dir.X * dx) + (m.Dir.Z * dz) > 0);
    }

    [Fact(DisplayName = "ScriptedWardenKicksAnyMustKickTests: control, the Warden walks toward an out-of-range choir.lieutenant")]
    public void Warden_walks_toward_an_out_of_range_choir_lieutenant()
    {
        var (commands, warden, caster) = SampleOutOfRangeFor(ChoirLieutenant);
        _out.WriteLine($"out-of-range choir.lieutenant commands={string.Join(";", commands.Select(c => c.GetType().Name + ":" + c.ActorId))}");
        Assert.True(MovesToward(commands, warden, caster), "control: the Warden must walk toward the out-of-range lieutenant");
    }

    [Fact(DisplayName = "ScriptedWardenKicksAnyMustKickTests: the Warden walks to (or kicks) an out-of-range remnant.cantor mid Remnant Mending, not the nearer quiet enemy (f-2b38938f)")]
    public void Warden_walks_to_an_out_of_range_remnant_cantor_mid_remnant_mending()
    {
        var (commands, warden, caster) = SampleOutOfRangeFor(RemnantCantor);
        _out.WriteLine($"out-of-range remnant.cantor commands={string.Join(";", commands.Select(c => c.GetType().Name + ":" + c.ActorId))}");
        var kicked = commands.OfType<CastCommand>().Any(k => k.AbilityId == Kick && k.TargetId == caster.Id);
        Assert.True(kicked || MovesToward(commands, warden, caster),
            "the Warden must walk toward the must-kick remnant.cantor, not idle on the nearer quiet enemy");
    }
}
