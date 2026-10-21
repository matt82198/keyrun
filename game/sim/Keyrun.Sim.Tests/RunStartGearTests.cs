using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Finding f-f9b80112 (BIBLE s7): the gear equipped on the between-runs screen must reach the run the host starts.
/// game/src/Host/SimDriver.cs StartRun builds its <see cref="RunOptions"/> through <see cref="RunStart.Of"/> (the screen's
/// <see cref="Progression"/>, the queued request, the human's base kit and the class's Facet patches), so these tests
/// drive the exact options the host hands <see cref="RunRoot.CreateRun"/>. Before the fix the host passed only
/// <c>request?.ToRunOptions()</c>, and every equipped item, keystone included, was dropped.
/// </summary>
public sealed class RunStartGearTests
{
    private const int HumanId = 1;

    private static readonly AbilityDef BaseZap =
        RunTestKit.Ability("zap", new EffectOp[] { new DamageOp(1, "physical") }, cooldownTicks: 100);

    private static readonly Facet SlowZap = new("test.weapon.slowZap", "Zap recovers slower.", "zap");

    private static readonly Facet KeystoneZap = new("test.keystone.longZap", "Zap reaches further.", "zap");

    private static readonly IReadOnlyDictionary<string, GearPatch> Patches = new Dictionary<string, GearPatch>(StringComparer.Ordinal)
    {
        [SlowZap.Id] = new GearPatch { CooldownTicks = 250 },
        [KeystoneZap.Id] = new GearPatch { Range = 123456 },
    };

    private static readonly IReadOnlyDictionary<string, AbilityDef> Abilities =
        new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = BaseZap };

    [Fact(DisplayName = "Run start: a run created from the screen's equipped loadout casts with that gear")]
    public void A_run_created_from_the_equipped_loadout_carries_the_gear()
    {
        var bare = Progression.Start(1);
        var geared = bare.Take(ItemOf("weapon", SlowZap));

        Assert.Equal(3, DamagesFrom(RunStart.Of(null, bare, HumanId, Abilities, Patches)));
        Assert.Equal(2, DamagesFrom(RunStart.Of(null, geared, HumanId, Abilities, Patches)));

        var start = RunStart.Of(null, geared, HumanId, Abilities, Patches);
        Assert.Equal(new[] { SlowZap.Id }, start.Gear!.Facets.Select(f => f.Id));
        var run = RunRoot.CreateRun(42, Party(), Dungeon(), RunTestKit.Content(Abilities), start.Options);
        Assert.Equal(250, run.Content.Abilities["zap"].CooldownTicks);
    }

    [Fact(DisplayName = "Run start: the equipped keystone's Facet reaches the run")]
    public void The_keystone_reaches_the_run()
    {
        var progression = Progression.Start(1).Take(ItemOf("keystone", KeystoneZap));

        var start = RunStart.Of(null, progression, HumanId, Abilities, Patches);
        var run = RunRoot.CreateRun(42, Party(), Dungeon(), RunTestKit.Content(Abilities), start.Options);

        Assert.Equal(123456, run.Content.Abilities["zap"].Range);
        Assert.Equal("keystone", Assert.Single(start.Gear!.Gear).Slot);
    }

    [Fact(DisplayName = "Run start: queued Clause and talents are kept and the gear stacks on the talented kit")]
    public void Gear_stacks_on_the_queued_talents_and_keeps_the_clause()
    {
        var talented = BaseZap with { CastTicks = 30 };
        var request = new QueueRequest("hollow-vault", 4, "tide")
        {
            Loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>
            {
                [HumanId] = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = talented },
            },
        };
        var progression = Progression.Start(1).Take(ItemOf("weapon", SlowZap));

        var options = RunStart.Of(request, progression, HumanId, Abilities, Patches).Options!;

        Assert.Equal("tide", options.ClauseId);
        var zap = options.Loadouts![HumanId]["zap"];
        Assert.Equal(30, zap.CastTicks);
        Assert.Equal(250, zap.CooldownTicks);
    }

    [Fact(DisplayName = "Run start: no progression (no screen yet) creates the run exactly as the request says")]
    public void No_progression_is_the_request_alone()
    {
        var request = new QueueRequest("hollow-vault", 4, "tide");

        var queued = RunStart.Of(request, null, HumanId, Abilities, Patches);
        var cli = RunStart.Of(null, null, HumanId, Abilities, Patches);

        Assert.Equal(request.ToRunOptions(), queued.Options);
        Assert.Null(queued.Gear);
        Assert.Null(cli.Options);
        Assert.Null(cli.Gear);
    }

    private static int DamagesFrom(RunStart start)
    {
        var run = RunRoot.CreateRun(42, Party(), Dungeon(), RunTestKit.Content(Abilities), start.Options);
        var commands = new[] { 0, 150, 260 }.Select(t => (Command)new CastCommand(t, HumanId, "zap", 2)).ToList();
        var (_, events) = RunTestKit.StepUntil(run, 300, commands);
        return events.Count(e => e.Type == "damage");
    }

    private static PartyMemberSpec[] Party() => new[]
    {
        RunTestKit.Member(HumanId, "warrior", ActorRole.Damage, -2000, 0, 200, 100),
        RunTestKit.Member(2, "target-dummy", ActorRole.Damage, 3000, 0, 300, 0),
    };

    private static DungeonDef Dungeon() => RunTestKit.OnePull("empty", "p", RunTestKit.Spawn(11, "boss", 0, 0, 1));

    private static Item ItemOf(string slot, Facet facet) =>
        new(FormattableString.Invariant($"reward-{slot}-{facet.Id}-t1-0"), slot, 1, facet, Rewards.StartingCharges);
}
