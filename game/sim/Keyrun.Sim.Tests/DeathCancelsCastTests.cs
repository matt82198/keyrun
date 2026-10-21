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
/// DEATH-CANCELS-CAST (finding f-42df9d4f; WoW: death cancels every cast in progress). On #326's tree the careless
/// Cinder died at tick 583 and its Flare, begun before the death, still completed at 595 and dealt 110 to actor 102. An
/// actor whose health reaches 0 has its in-progress cast ended on that tick in the death path: no
/// <c>cast_complete</c> ever fires for it and its damage or heal never lands. Each case has a control in which the
/// caster lives and the same cast lands. Everything runs through <see cref="RunRoot.StepRun"/>.
/// </summary>
public sealed class DeathCancelsCastTests
{
    private const int Caster = 1;   // the tank: the enemy's auto target, so its nuke lands on the caster
    private const int Ally = 2;     // the heal's target
    private const int Foe = 10;     // the bolt's target
    private const int Killer = 11;  // casts the nuke at KillTick
    private const string Bolt = "p.bolt";   // cast 40, 500 damage
    private const string Mend = "p.mend";   // cast 40, heals 300
    private const string Nuke = "e.nuke";   // instant, 1000 damage
    private const int CastTick = 100;
    private const int CastTicks = 40;
    private const int KillTick = 120;       // inside the cast: it would land at 140

    private static ScriptStep At(int tick, string cast) => new(StepTrigger.AtTick, cast) { AtTick = tick };

    private static Run BuildRun(long casterHealth)
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            [Bolt] = RunTestKit.Ability(Bolt, new EffectOp[] { new DamageOp(500, "physical") }) with { CastTicks = CastTicks },
            [Mend] = RunTestKit.Ability(Mend, new EffectOp[] { new HealOp(300) }) with { CastTicks = CastTicks },
            [Nuke] = RunTestKit.Ability(Nuke, new EffectOp[] { new DamageOp(1000, "physical") }),
        };
        var content = RunTestKit.Content(abilities) with
        {
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Killer] = new[] { At(KillTick, Nuke) } },
        };
        var party = new[]
        {
            RunTestKit.Member(Caster, "warden", ActorRole.Tank, 0, 1000, casterHealth, 100),
            // Far from the killer, so the nuke's nearest-target pick is always the caster.
            RunTestKit.Member(Ally, "cinder", ActorRole.Damage, -3000, 1000, 400, 100),
        };
        var dungeon = RunTestKit.OnePull("d", "p", RunTestKit.Spawn(Foe, "foe", 0, 0, 1_000_000), RunTestKit.Spawn(Killer, "foe", 500, 0, 1_000_000));
        return RunRoot.CreateRun(7, party, dungeon, content);
    }

    private static List<SimEvent> Play(long casterHealth, string ability, int targetId)
    {
        var cmds = new Command[] { new CastCommand(CastTick, Caster, ability, targetId) };
        return RunTestKit.StepUntil(BuildRun(casterHealth), 200, cmds).Events;
    }

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static bool CompletedBy(SimEvent e, string ability) =>
        e.Type == "cast_complete" && Int(e, "actorId") == Caster && Str(e, "abilityId") == ability;

    [Fact(DisplayName = "A caster that dies mid-cast never completes its damage cast and the damage never lands")]
    public void Damage_cast_of_a_caster_that_dies_mid_cast_never_lands()
    {
        var events = Play(400, Bolt, Foe);

        var death = Assert.Single(events, e => e.Type == "death" && Int(e, "actorId") == Caster);
        Assert.Equal(KillTick, death.Tick);
        Assert.DoesNotContain(events, e => CompletedBy(e, Bolt));
        Assert.DoesNotContain(events, e => e.Type == "damage" && Int(e, "sourceId") == Caster);
    }

    [Fact(DisplayName = "A caster that dies mid-cast never completes its heal cast and the heal never lands")]
    public void Heal_cast_of_a_caster_that_dies_mid_cast_never_lands()
    {
        var events = Play(400, Mend, Ally);

        Assert.Single(events, e => e.Type == "death" && Int(e, "actorId") == Caster && e.Tick == KillTick);
        Assert.DoesNotContain(events, e => CompletedBy(e, Mend));
        Assert.DoesNotContain(events, e => e.Type == "heal" && Int(e, "sourceId") == Caster);
    }

    [Fact(DisplayName = "A caster that dies mid-cast holds no cast from its death tick on")]
    public void Dead_caster_holds_no_cast_after_its_death_tick()
    {
        var run = RunTestKit.StepUntil(BuildRun(400), KillTick + 1, new Command[] { new CastCommand(CastTick, Caster, Bolt, Foe) }).Run;

        var caster = run.State.Actors[Caster];
        Assert.False(caster.Alive);
        Assert.Null(caster.CastId);
        Assert.Null(caster.CastEndsTick);
    }

    [Fact(DisplayName = "Control: a caster that survives the nuke completes its damage cast on time and the damage lands")]
    public void Damage_cast_of_a_surviving_caster_lands()
    {
        var events = Play(100_000, Bolt, Foe);

        Assert.DoesNotContain(events, e => e.Type == "death" && Int(e, "actorId") == Caster);
        var done = Assert.Single(events, e => CompletedBy(e, Bolt));
        Assert.Equal(CastTick + CastTicks, done.Tick);
        Assert.Contains(events, e => e.Type == "damage" && Int(e, "sourceId") == Caster && Int(e, "targetId") == Foe && e.Tick == done.Tick);
    }

    [Fact(DisplayName = "Control: a caster that survives the nuke completes its heal cast on time and the heal lands")]
    public void Heal_cast_of_a_surviving_caster_lands()
    {
        var events = Play(100_000, Mend, Ally);

        var done = Assert.Single(events, e => CompletedBy(e, Mend));
        Assert.Equal(CastTick + CastTicks, done.Tick);
        Assert.Contains(events, e => e.Type == "heal" && Int(e, "sourceId") == Caster && Int(e, "targetId") == Ally && e.Tick == done.Tick);
    }
}
