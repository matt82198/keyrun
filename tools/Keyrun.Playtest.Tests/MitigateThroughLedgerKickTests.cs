using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// MITIGATE-THROUGH-LEDGER (the Choir lesson red once LOCK-KEEPS-TARGET kept the boss on its tank): a policy seat's kicks
/// come only from its plan, so <see cref="PolicyBot"/> drops the scripted role's ledger kick. Every role decides the
/// ledger kick FIRST and returns it alone, so the dropped kick also swallowed what the role would have pressed instead:
/// at seed 1 key 2 (kick plan) the boss's cleave was under way (5475-5515), the ledger named the Warden and the Chanter,
/// the Warden pressed a Reckoning fallback instead of Bulwark and the Chanter pressed nothing for the whole bar, and the
/// cleave killed the Chanter. In WoW a player who is not on kicks still heals and presses their defensive. Each case
/// takes the real run's boss-pull state, puts the boss mid-cleave on the tank, and asks a fresh seat.
/// </summary>
public sealed class MitigateThroughLedgerKickTests
{
    private const string KickPlan = "{\"mitigate\": [\"warden\"], \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";
    // 11 ticks into the kick-plan run's first boss cleave (4122-4162), as 5486 was into 5475-5515. Was 5486
    // (D-PINNED-FIXTURES-362): with the healer press and Mending 6000 this run ends Timed at 4519 (boss pull 3966-4518).
    private const int Tick = 4133;
    private const int Boss = 900;

    private sealed record Scene(Policy Policy, ContentRegistry Registry, BotView View);

    /// <summary>The kick-plan run's state on <see cref="Tick"/>, the boss (and Twins) on the tank with a cleave under way.</summary>
    private static Scene MidCleave(System.Action<SimState> tweak)
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(KickPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var run = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = Tick }, content).Run;
        Assert.Equal(Tick, run.State.Tick);
        var state = run.State.Clone();
        foreach (var enemy in state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive))
        {
            enemy.TargetId = PlaytestRunner.TankId;
        }
        var boss = state.Actors[Boss];
        boss.CastId = "hollowWarden.cleave";
        boss.CastStartTick = Tick - 10;
        boss.CastEndsTick = Tick + 30;
        tweak(state);
        var view = BotViews.BuildBotView(state, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, KitRegistry.From(registry)) with { Layout = run.Content.Layout };
        return new Scene(policy, registry, view);
    }

    private static void Ready(ActorState a, params string[] abilityIds)
    {
        foreach (var id in abilityIds)
        {
            a.Cooldowns[id] = 0;
        }
        a.GcdReadyTick = 0;
        a.CastId = null;
        a.CastStartTick = null;
        a.CastEndsTick = null;
    }

    private static System.Collections.Generic.List<string> PolicyCasts(Scene scene, int seatId)
    {
        var seat = new PolicyBot(scene.Policy, new PolicyContext { PullId = "boss" }, scene.Registry, 1, seatId, PlaytestRunner.TankId);
        var decision = seat.Decide(scene.View, BotMemory.Default(PlaytestRunner.ScriptedSkillDial), seatId, Skill.SeedPrng(1));
        return decision.Commands.OfType<CastCommand>().Select(c => c.AbilityId).ToList();
    }

    [Fact(DisplayName = "A mitigating tank presses Bulwark under pack pressure even while the scripted kick ledger names it to kick")]
    public void Mitigating_tank_presses_bulwark_while_the_ledger_wants_a_kick()
    {
        var scene = MidCleave(state =>
        {
            var tank = state.Actors[PlaytestRunner.TankId];
            Ready(tank, "warden.bulwark", "warden.shieldBash");
            tank.Effects.RemoveAll(e => e.EffectId == "braced");
        });
        var tank = scene.View.State.Actors[PlaytestRunner.TankId];
        var kit = scene.View.Kits.ResolveKit(tank.ClassId, scene.View.ClassKit);

        // The scene: under pack pressure with Bulwark ready, while the scripted ledger hands the tank the kick.
        Assert.Equal("warden.bulwark", TankPressure.PressureMitigationCommand(scene.View, tank, kit)?.AbilityId);
        Assert.Equal(kit.InterruptAbilityId, RoleShared.LedgerKickCommand(scene.View, tank, kit)?.AbilityId);

        var casts = PolicyCasts(scene, tank.Id);
        Assert.True(casts.Contains("warden.bulwark"), "the mitigating tank pressed " + (casts.Count == 0 ? "nothing" : string.Join(",", casts)) + ", not Bulwark");
        Assert.DoesNotContain(kit.InterruptAbilityId, casts);
    }

    [Fact(DisplayName = "A healer heals the hurt tank while the scripted kick ledger names it to kick, instead of standing idle")]
    public void Healer_heals_while_the_ledger_wants_a_kick()
    {
        const int Chanter = 2;
        var scene = MidCleave(state =>
        {
            var tank = state.Actors[PlaytestRunner.TankId];
            tank.Health = tank.MaxHealth / 2;
            var healer = state.Actors[Chanter];
            healer.Health = healer.MaxHealth;
            healer.Resource = healer.MaxResource;
            Ready(healer, healer.Cooldowns.Select(kv => kv.Key).ToArray());
            Ready(healer, "chanter.rebuke");
        });
        var healer = scene.View.State.Actors[Chanter];
        var kit = scene.View.Kits.ResolveKit(healer.ClassId, scene.View.ClassKit);
        Assert.Equal(ActorRole.Healer, healer.Role);
        Assert.Equal(kit.InterruptAbilityId, RoleShared.LedgerKickCommand(scene.View, healer, kit)?.AbilityId);

        var casts = PolicyCasts(scene, Chanter);
        Assert.True(casts.Count > 0, "the healer pressed nothing with the tank at half health");
        Assert.DoesNotContain(kit.InterruptAbilityId, casts);
    }
}
