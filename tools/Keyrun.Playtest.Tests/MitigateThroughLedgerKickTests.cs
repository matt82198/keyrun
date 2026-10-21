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
    // 11 ticks into the kick-plan run's first boss cleave (8846-8886), as 4133 was into 4122-4162 and 5486 into
    // 5475-5515. R-BOTS-6 re-derivation (BOTS-INTEGRATE, #295 + #327 + #298 merged with main acac1b42; a measurement,
    // the assertions are unchanged): was 4133, where the run now stands in the Choir pull (76-6651) with no boss cast;
    // this run now ends Timed at 9313 (boss pull 8690-9312). Before main acac1b42 was merged it measured 9025 (first
    // cleave 9014-9054, Timed at 9588). Was 5486 before 4133 (D-PINNED-FIXTURES-362: with the healer press and Mending
    // 6000 the run ended Timed at 4519, boss pull 3966-4518).
    // R-BOTS-18 re-derivation (one re-derivation with the R-BOTS-17 oracle re-record on #449, measured on #449 merged
    // with main 0f6a8989; a measurement, the assertions are unchanged): was 8857, past this run's end. The kick-plan run
    // (Warden mitigating) now ends Timed at 8764 with 0 deaths, boss pull 8162-8763, first boss cleave 8318-8358, so 11
    // ticks in is 8329. (Before the main merge the same run ended Timed at 8633 with its first cleave at 8145-8185.)
    // PR-502 re-derivation (ruling G5 2026-10-21, MVP-MAP U8; a measurement on the merged tree: origin/main 1a7996a8 merged
    // into feat/mvp-u8-layout-forces, the assertions are unchanged). Was 8329, which the merged run no longer reaches: the
    // Hall Sentries are now an optional pack off the walking line (layout.json), so the kick-plan run no longer fights them
    // and now ends Timed at 7889, with boss cleaves 7395-7435, 7551-7591 and 7795-7835. 11 ticks into the first cleave is
    // 7406; the run's own state at 7406 has the boss casting hollowWarden.cleave, start 7395, end 7435.
    // R-DOOR-1 + R-TAUNT-1 re-derivation (WHY: the taunt fallthrough and the 100% door make the kick-plan run longer, so the
    // first boss cleave moved from 7395 to 10575): 10587 is the first cleave's kick-open tick (Ledger.KickOpensTick) on the
    // new trajectory, the same event this scene was built on. The run now ends Timed at 11015.
    private const int Tick = 10587;

    /// <summary>The second boss cleave's kick-open tick on the same trajectory (R-DOOR-1 + R-TAUNT-1): the healer scene's tick.</summary>
    private const int SecondCleaveKickOpenTick = 10743;
    private const int Boss = 900;
    private const int CleaveTicks = 40;

    private sealed record Scene(Policy Policy, ContentRegistry Registry, BotView View);

    /// <summary>What a scene tweak may read: the run's own sim abilities and the scene's view builder.</summary>
    /// <param name="Abilities">The run's sim ability table (run.Content.Abilities).</param>
    /// <param name="ViewOf">Builds the bot view of a state exactly as the scene's own view is built.</param>
    private sealed record SceneKit(System.Collections.Generic.IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef> Abilities, System.Func<SimState, BotView> ViewOf);

    /// <summary>The kick-plan run's state on <see cref="Tick"/>, the boss (and Twins) on the tank with a cleave under way.</summary>
    private static Scene MidCleave(System.Action<SimState> tweak) => MidCleave((state, _) => tweak(state));

    /// <inheritdoc cref="MidCleave(System.Action{SimState})"/>
    private static Scene MidCleave(System.Action<SimState, SceneKit> tweak) => MidCleave(Tick, tweak);

    /// <summary>The scene at kick-open tick <paramref name="at"/> (a boss cleave's Ledger.KickOpensTick on the trajectory).</summary>
    private static Scene MidCleave(int at, System.Action<SimState, SceneKit> tweak)
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(KickPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var run = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = at }, content).Run;
        Assert.Equal(at, run.State.Tick);
        var state = run.State.Clone();
        foreach (var enemy in state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive))
        {
            enemy.TargetId = PlaytestRunner.TankId;
        }
        // The scene's intent is "the ledger wants a kick NOW": the frozen tick is the cleave's kick-open tick (cast start
        // plus the BOT-KICK-TIMING hold, Ledger.KickOpensTick as RoleShared.LedgerKickCommand reads it), not a fixed
        // offset into the cast. A 40-tick cleave opens 12 ticks in, so it starts at 5474 and resolves at 5514.
        var boss = state.Actors[Boss];
        boss.CastId = "hollowWarden.cleave";
        boss.CastStartTick = at;
        boss.CastEndsTick = at + CleaveTicks;
        var hold = Ledger.KickOpensTick(boss) - at;
        boss.CastStartTick = at - hold;
        boss.CastEndsTick = at - hold + CleaveTicks;
        Assert.Equal(at, Ledger.KickOpensTick(boss));
        var kits = KitRegistry.From(registry);
        BotView ViewOf(SimState s) => BotViews.BuildBotView(s, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
        tweak(state, new SceneKit(run.Content.Abilities, ViewOf));
        return new Scene(policy, registry, ViewOf(state));
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

    /// <summary>
    /// R-BOTS-10 (a scene re-derivation; the healer fact's assertions are unchanged): under #327's party ledger
    /// (Ledger.ResolvePartyInterrupt: least recently kicked first, ties to the lowest seat id) the Warden, seat 1, wins
    /// every tie with a ready Chanter, so with both interrupts ready the ledger names the Warden at the cleave and the
    /// fact's premise (the ledger names the Chanter) is false. In this run the Warden has not kicked by <see cref="Tick"/>
    /// (Shield Bash cooldown entry 0), so the scene spends it, inside the ledger, on an earlier cast: the boss's previous
    /// cleave, one cleave cooldown (the content's hollowWarden.cleave cooldownTicks, 160) before this one. At that cast's
    /// kick-open tick the ledger is asked who kicks; it names the Warden, which spends Shield Bash there through the sim's
    /// own charge rule (Cooldowns.ConsumeCharge). Shield Bash's 480-tick cooldown then runs past this cleave, so here the
    /// ledger passes the Warden over and names the ready Chanter, genuinely, by its own rotation.
    /// </summary>
    private static void SpendWardenKickOnThePreviousCleave(SimState state, SceneKit sceneKit, int at)
    {
        var earlier = state.Clone();
        var boss = earlier.Actors[Boss];
        var period = sceneKit.Abilities[boss.CastId!].CooldownTicks;
        boss.CastStartTick -= period;
        boss.CastEndsTick -= period;
        earlier.Tick = Ledger.KickOpensTick(boss);
        Assert.True(earlier.Tick < at, "the previous cleave opens its kick before this one");
        var view = sceneKit.ViewOf(earlier);
        var warden = earlier.Actors[PlaytestRunner.TankId];
        var kit = view.Kits.ResolveKit(warden.ClassId, view.ClassKit);
        var kick = RoleShared.LedgerKickCommand(view, warden, kit);
        Assert.Equal(kit.InterruptAbilityId, kick?.AbilityId);
        var shieldBash = sceneKit.Abilities[kick!.AbilityId];
        Keyrun.Sim.Combat.Cooldowns.ConsumeCharge(state.Actors[PlaytestRunner.TankId], shieldBash, earlier.Tick);
        Assert.True(Readiness.CooldownReadyTick(state.Actors[PlaytestRunner.TankId], shieldBash.Id) > at, "the Warden's Shield Bash is still on cooldown at this cleave");
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

    // R-DOOR-3 re-anchor (content fact: hollowWarden.cleave is interruptible=false, so the Warden never kicks a boss cleave
    // and the earlier synthetic "Warden kicked the previous cleave" premise is stale). The real kick in the trajectory is the
    // Chanter's chanter.rebuke on the second Choral Mending (cast 4780-4860, ledger kick-open 4820, Warden at 245/400).
    private const int MendKickOpenTick = 4820;
    private const int MendCastStart = 4780;
    private const int MendCastEnd = 4860;

    [Fact(DisplayName = "A healer kicks the Choral Mending and heals the hurt tank in the same pull (R-DOOR-3: the real ledger kick, not a boss cleave)")]
    public void Healer_kicks_the_mending_and_heals_the_hurt_tank()
    {
        const int Chanter = 2;
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(KickPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var run = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = MendKickOpenTick }, content).Run;
        Assert.Equal(MendKickOpenTick, run.State.Tick);

        var kits = KitRegistry.From(registry);
        var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
        var healer = view.State.Actors[Chanter];
        var kit = view.Kits.ResolveKit(healer.ClassId, view.ClassKit);
        Assert.Equal(ActorRole.Healer, healer.Role);
        Assert.Equal("chanter.rebuke", kit.InterruptAbilityId);
        Assert.Equal(kit.InterruptAbilityId, RoleShared.LedgerKickCommand(view, healer, kit)?.AbilityId);

        // The plan kicks choir.healCast with the Chanter, so the seat takes the kick the ledger names. A kick waits the
        // human reaction (PolicyBot), so the real run's command stream is the check: the Chanter's rebuke lands in the window.
        var log = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy), content).Log;
        var kicks = log.Split('\n')
            .Where(l => l.StartsWith("C ", System.StringComparison.Ordinal) &&l.Contains($"ActorId = {Chanter}, Kind = cast, AbilityId = {kit.InterruptAbilityId},"))
            .Select(l => int.Parse(System.Text.RegularExpressions.Regex.Match(l, "Tick = (\\d+)").Groups[1].Value))
            .Where(t => t >= MendCastStart && t <= MendCastEnd)
            .ToList();
        Assert.NotEmpty(kicks);

        // The Chanter heals the hurt Warden inside the same Mending window of the real run.
        var heals = log.Split('\n')
            .Where(l => l.Contains("\"type\":\"heal\"") && l.Contains($"\"sourceId\":{Chanter},") && l.Contains($"\"targetId\":{PlaytestRunner.TankId},"))
            .Select(l => int.Parse(System.Text.RegularExpressions.Regex.Match(l, "\"tick\":(\\d+)").Groups[1].Value))
            .Where(t => t >= MendCastStart && t <= MendCastEnd)
            .ToList();
        Assert.NotEmpty(heals);
    }
}
