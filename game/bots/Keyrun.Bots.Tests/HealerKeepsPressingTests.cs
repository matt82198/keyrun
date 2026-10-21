using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using static Keyrun.Bots.Tests.RolesTestKit;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// HEALER-KEEPS-PRESSING (orchestrator ruling for PR #362; the dungeon loop defers to WoW Mythic+: a healer with nobody
/// to heal keeps contributing, it never idles). In combat, when no ally is under the heal threshold, the healer bot
/// presses its non-heal buttons (the rotation abilities that are not heals and not the verse swap; for the Chanter that
/// is Dissonance) on the party's current target. Cadence and Hymn stay heals: they are never pressed by this rule.
/// </summary>
public sealed class HealerKeepsPressingTests
{
    /// <summary>The Chanter's global cooldown (content/data/abilities/chanter.json gcdTicks), the press budget.</summary>
    public const int PressWithinTicks = 30;

    private const int Tank = 1;
    private const int Chanter = 2;
    private const int Cinder = 3;
    private const int Enemy = 10;
    private const long Seed = 1;
    private const int MaxTicks = 6000;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public HealerKeepsPressingTests(ITestOutputHelper output) => _output = output;

    private static ActorState Foe(bool idle = false)
    {
        var foe = Actor(id: Enemy, kind: ActorKind.Enemy, classId: "choir.melee", role: ActorRole.None, x: 3000, health: 300, maxHealth: 300);
        foe.Idle = idle;
        return foe;
    }

    private static ActorState HealthyChanter() =>
        Actor(id: Chanter, classId: "chanter", role: ActorRole.Healer, health: 250, maxHealth: 250, resource: 1, maxResource: 1);

    private static ActorState HealthyTank(int? targetId) =>
        Actor(id: Tank, classId: "warden", role: ActorRole.Tank, x: 2000, health: 400, maxHealth: 400, targetId: targetId);

    [Fact(DisplayName = "Healer keeps pressing > nobody under 70% in a fight: the Chanter casts Dissonance at the tank's target")]
    public void HealthyPartyChanterPressesDissonanceOnTheTanksTarget()
    {
        var healer = HealthyChanter();
        var view = ViewOf(10, RealKits, null, HealthyTank(Enemy), healer, Foe());
        Assert.Equal(
            new Command[] { new TargetCommand(10, Chanter, Enemy), new CastCommand(10, Chanter, "chanter.dissonance", Enemy) },
            HealerRole.HealerDecide(view, healer));
    }

    [Fact(DisplayName = "Healer keeps pressing > the press never reaches for a heal (Cadence, Hymn, Chorus) or the verse swap (Refrain)")]
    public void PressSkipsHealsWhenDissonanceIsDown()
    {
        var healer = Actor(id: Chanter, classId: "chanter", role: ActorRole.Healer, health: 250, maxHealth: 250, resource: 1, maxResource: 1,
            cooldowns: new[] { ("chanter.dissonance", 999) });
        var view = ViewOf(10, RealKits, null, HealthyTank(Enemy), healer, Foe());
        Assert.Empty(HealerRole.HealerDecide(view, healer));
    }

    [Fact(DisplayName = "Healer keeps pressing > an idle (not yet pulled) enemy is never pressed")]
    public void IdleEnemyIsNotPressed()
    {
        var healer = HealthyChanter();
        var view = ViewOf(10, RealKits, null, HealthyTank(null), healer, Foe(idle: true));
        Assert.Empty(HealerRole.HealerDecide(view, healer));
    }

    [Fact(DisplayName = "Healer keeps pressing > a hurt ally still wins over the press")]
    public void HurtAllyStillHealed()
    {
        var healer = HealthyChanter();
        var tank = Actor(id: Tank, classId: "warden", role: ActorRole.Tank, x: 2000, health: 100, maxHealth: 400, targetId: Enemy);
        var cmds = HealerRole.HealerDecide(ViewOf(10, RealKits, null, tank, healer, Foe()), healer);
        Assert.DoesNotContain("chanter.dissonance", CastIds(cmds));
        Assert.NotEmpty(cmds);
    }

    [Fact(DisplayName = "Healer keeps pressing > in Mend with an enemy on the tank and Dissonance ready, the Chanter presses Dissonance before the Refrain into Ward")]
    public void EngagedFightInMendPressesDissonanceBeforeRefrain()
    {
        var healer = HealthyChanter();
        var foe = Foe();
        foe.TargetId = Tank;
        var cmds = HealerRole.HealerDecide(ViewOf(10, RealKits, null, HealthyTank(Enemy), healer, foe), healer);
        Assert.Equal(
            new Command[] { new TargetCommand(10, Chanter, Enemy), new CastCommand(10, Chanter, "chanter.dissonance", Enemy) },
            cmds);
    }

    [Fact(DisplayName = "Healer keeps pressing > in Mend with Dissonance on cooldown, the Chanter still Refrains into Ward")]
    public void EngagedFightInMendRefrainsWhenDissonanceIsDown()
    {
        var healer = Actor(id: Chanter, classId: "chanter", role: ActorRole.Healer, health: 250, maxHealth: 250, resource: 1, maxResource: 1,
            cooldowns: new[] { ("chanter.dissonance", 999) });
        var foe = Foe();
        foe.TargetId = Tank;
        var cmds = HealerRole.HealerDecide(ViewOf(10, RealKits, null, HealthyTank(Enemy), healer, foe), healer);
        Assert.Equal(new Command[] { new CastCommand(10, Chanter, "chanter.refrain") }, cmds);
    }

    /// <summary>
    /// R-BOTS-17 (the WoW Mythic+ answer: a healer's damage is filler and never displaces the setup that lets the pull
    /// end): in Mend the Payoff takes the fight's FIRST idle GCD only. Mid-fight here: Dissonance took the first idle GCD
    /// at tick 10 (ready again at 70, cooldownTicks 60) and a heal at 40 holds the GCD to 70, so on the next idle GCD at
    /// tick 70 the Chanter Refrains into Ward even though Dissonance is ready again. 633a80cc's order pressed Dissonance
    /// here and starved the Ward (the headless key 2 seed 1 kick-plan run wiped on the Choir).
    /// </summary>
    [Fact(DisplayName = "Healer keeps pressing > in Mend, after the Payoff on the first idle GCD, the second idle GCD presses the Refrain into Ward (R-BOTS-17)")]
    public void SecondIdleGcdInMendRefrainsIntoWard()
    {
        const int SecondIdleGcd = 70;
        var healer = Actor(id: Chanter, classId: "chanter", role: ActorRole.Healer, health: 250, maxHealth: 250, resource: 1, maxResource: 1,
            targetId: Enemy, gcdReadyTick: SecondIdleGcd, cooldowns: new[] { ("chanter.dissonance", SecondIdleGcd) });
        var foe = Foe();
        foe.TargetId = Tank;
        var cmds = HealerRole.HealerDecide(ViewOf(SecondIdleGcd, RealKits, null, HealthyTank(Enemy), healer, foe), healer);
        Assert.Equal(new Command[] { new CastCommand(SecondIdleGcd, Chanter, "chanter.refrain") }, cmds);
    }

    private sealed record PullTally(int PullStart, int PullClear, int FirstEligible, int FirstPress, int Presses, int HealButtonPresses);

    [Fact(DisplayName = "Healer keeps pressing > Choir pull, real bots: with every ally above 70% the Chanter presses a non-heal button within one GCD")]
    public void ChoirPullChanterPressesWithinOneGcd()
    {
        var t = DriveChoir();
        _output.WriteLine($"CHOIR pullStart={t.PullStart} clear={t.PullClear} firstEligible={t.FirstEligible} firstPress={t.FirstPress} dissonance={t.Presses} cadenceOrHymnWhileHealthy={t.HealButtonPresses}");
        Assert.True(t.PullStart >= 0, "the Choir pull never started");
        Assert.True(t.FirstEligible >= 0, "no Choir-pull tick had every ally above 70% with the Chanter free to act; the press check would be vacuous");
        Assert.True(t.FirstPress >= 0 && t.FirstPress - t.FirstEligible <= PressWithinTicks,
            $"the Chanter first pressed Dissonance at tick {t.FirstPress}; it was free with nobody under 70% from tick {t.FirstEligible} (want within {PressWithinTicks})");
    }

    private static bool NobodyUnderThreshold(SimState s) =>
        s.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive).All(a => a.Health * 10 >= a.MaxHealth * 7);

    private static PullTally DriveChoir()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        Assert.Equal("chanter", party.Single(p => p.Id == Chanter).ClassId);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);

        var kits = KitRegistry.From(registry);
        var ids = new[] { Tank, Chanter, Cinder };
        var bots = ids.ToDictionary(id => id, _ => Bot.MakeBot(RoleSet.Default));
        IReadOnlyDictionary<int, BotMemory> mem = ids.ToDictionary(id => id, _ => BotMemory.Default(80));
        var prng = Skill.SeedPrng(Seed);

        int pullStart = -1, clear = -1, firstEligible = -1, firstPress = -1, presses = 0, healButtons = 0;
        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && clear < 0)
        {
            var s = run.State;
            if (pullStart >= 0 && firstEligible < 0 && s.Actors[Chanter] is { Alive: true, CastId: null } c && c.GcdReadyTick <= s.Tick
                && Readiness.CooldownReadyTick(c, "chanter.dissonance") <= s.Tick && NobodyUnderThreshold(s)
                && s.Actors.Values.Any(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle))
            {
                firstEligible = s.Tick;
            }
            var healthy = NobodyUnderThreshold(s);
            var view = BotViews.BuildBotView(s, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "choir")
                {
                    pullStart = e.Tick;
                }
                else if (e.Type == "pull_clear" && ((JsString)e.Get("pullId")!).Value == "choir")
                {
                    clear = e.Tick;
                }
                else if (pullStart >= 0 && e.Type == "cast_complete" && ((JsInt)e.Get("actorId")!).Value == Chanter)
                {
                    var ability = ((JsString)e.Get("abilityId")!).Value;
                    if (ability == "chanter.dissonance")
                    {
                        presses++;
                        if (firstPress < 0)
                        {
                            firstPress = e.Tick;
                        }
                    }
                    else if (healthy && ability is "chanter.cadence" or "chanter.hymn")
                    {
                        healButtons++;
                    }
                }
            }
        }
        return new PullTally(pullStart, clear, firstEligible, firstPress, presses, healButtons);
    }
}
