using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Sim.Tests.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// HOLDER-LOCK (STATE.md D30, Matt 2026-10-20: "no aggro meters ... but no pulling aggro unless they intentionally do",
/// "the classic butt pull should still be possible"). On engage each enemy locks a holder: the nearest living player
/// when it engages (the body pull). It keeps that holder until a taunt, the holder's death (then the nearest living
/// player) or a wipe. Damage, healing and threat never move it; the threat table still fills (whether the Warden's
/// Momentum threat stays is Matt's open question on #306) but no longer picks the target.
/// Driven through RunRoot.CreateRun / StepRun with one enemy whose script swings a target-shaped melee hit every tick.
/// </summary>
public sealed class HolderLockTests
{
    private const long Seed = 7;
    private const int Tank = 1;
    private const int Healer = 2;
    private const int Dps = 3;
    private const int Enemy = 100;
    private const long SwingDamage = 10;

    private static readonly AbilityDef Swing = RunTestKit.Ability("e.swing", new EffectOp[] { new DamageOp(SwingDamage, "physical") }, range: 2000);

    // A big hit that also adds a lot of threat: the "DPS out-damaging the tank" a threat meter would hand the enemy to.
    private static readonly AbilityDef Nuke = RunTestKit.Ability("p.nuke", new EffectOp[] { new DamageOp(50, "physical"), new ThreatOp(1000) });

    // warden.taunt's shape (content/data/abilities/warden.json): a target-shaped utility ability carrying a threat op.
    private static readonly AbilityDef Taunt = RunTestKit.Ability("p.taunt", new EffectOp[] { new ThreatOp(1, 100000) }, range: 3000) with
    {
        Tags = new[] { "utility" },
    };

    private static Run BuildRun(long tankX, long healerX, long dpsX, long tankHealth = 100000)
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            [Swing.Id] = Swing,
            [Nuke.Id] = Nuke,
            [Taunt.Id] = Taunt,
        };
        var content = RunTestKit.Content(abilities) with
        {
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>
            {
                [Enemy] = EncounterJson.ParseScript(JsValue.Parse("[{\"at\":\"loop\",\"every\":1,\"cast\":\"e.swing\"}]")),
            },
        };
        var party = new[]
        {
            RunTestKit.Member(Tank, "warden", ActorRole.Tank, tankX, 0, tankHealth, 100),
            RunTestKit.Member(Healer, "chanter", ActorRole.Healer, healerX, 0, 100000, 100),
            RunTestKit.Member(Dps, "cinder", ActorRole.Damage, dpsX, 0, 100000, 100),
        };
        var dungeon = RunTestKit.OnePull("holder-lock", "p0", RunTestKit.Spawn(Enemy, "test.brute", 0, 0, 1000000));
        return RunRoot.CreateRun(Seed, party, dungeon, content);
    }

    private static IEnumerable<long> SwingTargets(IEnumerable<SimEvent> events) =>
        events.Where(e => e.Type == "damage" && ((JsInt)e.Get("sourceId")!).Value == Enemy)
            .Select(e => ((JsInt)e.Get("targetId")!).Value);

    private static Command[] Every(int from, int to, Func<int, Command> make) =>
        Enumerable.Range(from, to - from + 1).Select(make).ToArray();

    [Fact]
    public void A_dps_out_damaging_and_out_threatening_the_tank_never_takes_the_enemy()
    {
        // Tank nearest on engage; the DPS then nukes every tick and walks right up to the enemy.
        var run = BuildRun(tankX: 1000, healerX: -1800, dpsX: 1800);
        var commands = Every(0, 39, t => new CastCommand(t, Dps, Nuke.Id, Enemy))
            .Concat(Every(0, 5, t => new MoveCommand(t, Dps, new Dir2(-1000, 0), 0)))
            .ToArray();

        var (after, events) = RunTestKit.StepUntil(run, 40, commands);

        var enemy = after.State.Actors[Enemy];
        Assert.True(enemy.Threat[Dps] > (enemy.Threat.TryGetValue(Tank, out var tankThreat) ? tankThreat : 0),
            "the DPS tops the threat table, so a threat meter would hand it the enemy");
        Assert.True(ScriptTarget.RangeMm(enemy, after.State.Actors[Dps]) < ScriptTarget.RangeMm(enemy, after.State.Actors[Tank]),
            "the DPS stands nearer the enemy than the tank");
        var targets = SwingTargets(events).ToList();
        Assert.NotEmpty(targets);
        Assert.All(targets, t => Assert.Equal(Tank, t));
        Assert.Equal(Tank, enemy.HolderId);
        Assert.Equal(100000, after.State.Actors[Dps].Health);
    }

    [Fact]
    public void A_taunt_moves_the_enemy_to_the_taunter_and_a_taunt_back_returns_it()
    {
        var run = BuildRun(tankX: 1000, healerX: -1800, dpsX: 1500);
        var commands = new Command[]
        {
            new CastCommand(10, Dps, Taunt.Id, Enemy),
            new CastCommand(20, Tank, Taunt.Id, Enemy),
        };

        var (mid, early) = RunTestKit.StepUntil(run, 20, commands.Where(c => c.Tick < 20).ToList());
        Assert.Equal(Dps, mid.State.Actors[Enemy].HolderId);
        var afterTaunt = early.Where(e => e.Tick > 11);
        Assert.NotEmpty(SwingTargets(afterTaunt));
        Assert.All(SwingTargets(afterTaunt), t => Assert.Equal(Dps, t));

        var (end, late) = RunTestKit.StepUntil(mid, 30, commands);
        Assert.Equal(Tank, end.State.Actors[Enemy].HolderId);
        var afterTauntBack = late.Where(e => e.Tick > 21);
        Assert.NotEmpty(SwingTargets(afterTauntBack));
        Assert.All(SwingTargets(afterTauntBack), t => Assert.Equal(Tank, t));
    }

    [Fact]
    public void The_holders_death_hands_the_enemy_to_the_nearest_living_player()
    {
        // The tank holds with 25 hp (dies to the third swing); the healer stands nearer than the DPS, but the DPS
        // has nuked the enemy to the top of its threat table.
        var run = BuildRun(tankX: 1000, healerX: -1500, dpsX: 1900, tankHealth: 25);
        var commands = Every(0, 9, t => new CastCommand(t, Dps, Nuke.Id, Enemy));

        var (after, events) = RunTestKit.StepUntil(run, 20, commands);

        Assert.False(after.State.Actors[Tank].Alive);
        Assert.Equal(Healer, after.State.Actors[Enemy].HolderId);
        var deathTick = events.Single(e => e.Type == "death").Tick;
        var afterDeath = SwingTargets(events.Where(e => e.Tick > deathTick + 1)).ToList();
        Assert.NotEmpty(afterDeath);
        Assert.All(afterDeath, t => Assert.Equal(Healer, t));
        Assert.Equal(100000, after.State.Actors[Dps].Health);
    }

    [Fact]
    public void A_body_pull_still_pulls_the_nearest_player_holds_and_threat_cannot_take_it_back()
    {
        // The classic butt pull: the DPS is nearest when the pack engages, so the DPS holds it. The tank's threat
        // (nukes, the Momentum stand-in) cannot move it; only the tank's taunt does.
        var run = BuildRun(tankX: 1900, healerX: -1800, dpsX: 800);
        Assert.Equal(Dps, run.State.Actors[Enemy].HolderId);

        var commands = Every(0, 14, t => new CastCommand(t, Tank, Nuke.Id, Enemy))
            .Append(new CastCommand(15, Tank, Taunt.Id, Enemy))
            .ToArray();
        var (mid, early) = RunTestKit.StepUntil(run, 15, commands.Where(c => c.Tick < 15).ToList());
        Assert.True(mid.State.Actors[Enemy].Threat[Tank] > 0);
        Assert.NotEmpty(SwingTargets(early));
        Assert.All(SwingTargets(early), t => Assert.Equal(Dps, t));
        Assert.Equal(Dps, mid.State.Actors[Enemy].HolderId);

        var (end, _) = RunTestKit.StepUntil(mid, 17, commands);
        Assert.Equal(Tank, end.State.Actors[Enemy].HolderId);
    }

    [Fact]
    public void A_locked_holder_out_of_range_blocks_the_cast_instead_of_switching_to_a_player_in_range()
    {
        var enemy = EncounterTestKit.Actor(Enemy, ActorKind.Enemy, new Vec3(0, 0, 0), true, 1000);
        enemy.HolderId = Tank;
        enemy.Threat[Dps] = 5000;
        var state = EncounterTestKit.State(10, new PrngState(1, 2, 3, 4),
            enemy,
            EncounterTestKit.Actor(Tank, ActorKind.Player, new Vec3(5000, 0, 0), true, 1000),
            EncounterTestKit.Actor(Dps, ActorKind.Player, new Vec3(500, 0, 0), true, 1000));
        var ability = new ScriptAbility("e.swing", 2000, "target", false);

        Assert.Equal(new ScriptTargetResult(null, true), ScriptTarget.Resolve(state, state.Actors[Enemy], null, ability));
        Assert.Equal(new ScriptTargetResult(Tank, false), ScriptTarget.Resolve(state, state.Actors[Enemy]));
        Assert.Equal(new ScriptTargetResult(Tank, false), ScriptTarget.Resolve(state, state.Actors[Enemy], ScriptTargetOverride.Threat));
    }
}
