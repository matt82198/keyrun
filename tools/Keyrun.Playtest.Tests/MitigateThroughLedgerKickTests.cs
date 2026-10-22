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
    // KF3-SEED-SPAWN re-derivation (ruling R6, the Seed activates on room entry; measured on #505 before main's PR-502
    // merged: was 8329, the run then ended Timed at 8684 with boss cleaves 8289-8329 and 8555-8595, so it pinned 8300).
    // Merge re-derivation (#505 merged with origin/main 8e147f02 carrying PR-502; a measurement on the merged tree, the
    // assertions are unchanged, neither side's literal reused): the kick-plan run now ends Timed at 7777 with boss cleaves
    // 7341-7381, 7497-7537 and 7653-7693, so 11 ticks into the first cleave is 7352; the run's own state at 7352 has the
    // boss casting hollowWarden.cleave, start 7341, end 7381.
    // R-PATROL-3b merge re-derivation (#528 onto main; a measurement by a probe on the merged tree): the first boss cleave
    // telegraphs at 7480 and resolves at 7520 on that tree, so 11 ticks in is 7491. WHY: a pin that passes only because the
    // test places the boss mid-cleave itself is not on the trajectory; the event-derived tick is what counts.
    // R-DECLUTTER-1 merge (main 1f8fed7f, #515 taunt and kick, #537): the probe on this tree puts the first boss cleave
    // telegraph at 7480 (resolves 7520, the run ends Timed at 7903), so the event-derived tick is 7491 (11 ticks in).
    // AGGRO-CHASE re-derivation (f-f0c886eb; a measurement by a probe on this tree, the assertions are unchanged): the Choir
    // aggroes on its members' own radius at tick 51 and runs at the party, so the kick-plan run's Choir clears at 7644, the
    // boss pull runs 9106-12387 and the run ends Timed at 12388. The first boss cleave telegraphs at 9262 and resolves at
    // 9302 (the run's own state at 9273 has the boss casting hollowWarden.cleave, start 9262, end 9302), so 11 ticks in is 9273.
    private const int Tick = 9273;
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

    // R-PINS-1 (R-ORACLE-1 applied, ruling on #573), replacing the R-DOOR-3 pin on one Choral Mending (cast 4780-4860,
    // kick-open 4820 on main): that scene was the one Mending of the run whose kick came after kick-open, so any trajectory
    // change (aggro-chase moved the Choir fight) removed it. The property it guards, on whichever Mending occurs: at a
    // Mending's kick-open tick, every seat the scripted ledger names to kick (RoleShared.LedgerKickCommand) is the seat
    // that kicks that cast in the real run, inside the cast (start to end: a seat reacting a tick before the hold opens
    // still kicks it). At least one such named kick must occur (non-vacuity).
    [Fact(DisplayName = "R-PINS-1: on every Choral Mending of the kick-plan run, the seat the ledger names at kick-open is the seat that kicks it")]
    public void The_seat_the_ledger_names_at_kick_open_kicks_the_mending()
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(KickPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var kits = KitRegistry.From(registry);
        var named = new System.Collections.Generic.List<(int Seat, int Caster, string Ability, int Start, int Open, int End)>();
        var casts = new System.Collections.Generic.List<(int Tick, CastCommand Cast, int? Target)>();
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policy)
        {
            Observe = (tick, run, commands) =>
            {
                foreach (var c in commands.OfType<CastCommand>())
                {
                    casts.Add((tick, c, c.TargetId ?? (run.State.Actors.TryGetValue(c.ActorId, out var a) ? a.TargetId : null)));
                }
                var mends = run.State.Actors.Values.Where(a => a.Alive && a.CastId == "choir.healCast" && a.CastEndsTick is not null && Ledger.KickOpensTick(a) == tick).ToList();
                if (mends.Count == 0)
                {
                    return;
                }
                var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
                foreach (var seat in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive))
                {
                    var kit = view.Kits.ResolveKit(seat.ClassId, view.ClassKit);
                    if (RoleShared.LedgerKickCommand(view, seat, kit) is { } kick && mends.FirstOrDefault(m => m.Id == kick.TargetId) is { } mend)
                    {
                        named.Add((seat.Id, mend.Id, kick.AbilityId, mend.CastStartTick ?? tick, tick, mend.CastEndsTick!.Value));
                    }
                }
            },
        }, content);

        Assert.NotEmpty(named);
        foreach (var n in named)
        {
            var kicked = casts.Any(c => c.Cast.ActorId == n.Seat && c.Cast.AbilityId == n.Ability && c.Target == n.Caster && c.Tick >= n.Start && c.Tick <= n.End);
            var seen = string.Join(",", casts.Where(c => c.Target == n.Caster && c.Tick >= n.Start && c.Tick <= n.End).Select(c => $"{c.Tick}:{c.Cast.ActorId}:{c.Cast.AbilityId}"));
            Assert.True(kicked, $"the ledger named seat {n.Seat} ({n.Ability}) at kick-open {n.Open} of the Mending by {n.Caster} (cast {n.Start}-{n.End}), and it never kicked it; casts on it [{seen}]; named {named.Count}: {string.Join(" ", named.Select(x => $"{x.Open}:{x.Seat}"))}");
        }
    }
}
