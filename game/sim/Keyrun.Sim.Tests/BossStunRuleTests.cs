using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.BossTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// PURPOSE: this artifact exists to make a stun stop every cast of a stunnable actor in the keyrun sim (finding
/// f-c63b43bf, ruling: defer to WoW), with the uninterruptible flag meaning kick-immune only and boss stun-immunity a
/// separate flag; status: real.
/// On the armed Hollow Warden under the host's stop wiring (stunned and silenced stop casts, stunned is a stun, the boss
/// class is stun-immune): a stun on the boss leaves its uninterruptible cast running, the same stun on a stunnable boss
/// ends it (control: the flag is what keeps the boss casting), and the Twins' Break-bar stagger still ends their slams
/// through the #344 stagger path (<c>cast_stopped</c>), not as an <c>interrupt</c>, so the Twins' mechanics and Echo's
/// interrupt count are unchanged by the stun rule.
/// f-94749699 (WoW: an immune target takes no stun, "Immune"): the stun-immune boss never holds the stunned effect at
/// all (an <c>immune</c> event instead), while a stunnable boss and a Twin beside the immune boss still take it.
/// </summary>
public sealed class BossStunRuleTests
{
    private const string BossClass = "hollowWarden.boss";
    private const string TwinSlam = "twins.slam";
    private const string ColdSnap = "cinder.coldSnap"; // instant, target, applies stunned (content/data/abilities/cinder.json)

    private static Run WithHostStops(Run run, bool bossStunImmune) => run with
    {
        Content = run.Content with
        {
            StopEffectIds = new[] { "stunned", "silenced" },
            StunEffectIds = new[] { "stunned" },
            StunImmuneClassIds = bossStunImmune ? new[] { BossClass } : null,
        },
    };

    private static Run StepOne(Run run, List<SimEvent> events, params Command[] commands)
    {
        var stepped = RunRoot.StepRun(run, commands);
        events.AddRange(stepped.Events);
        return stepped.Run;
    }

    private static bool By(SimEvent e, string key, int actorId) => e.Get(key) is JsInt { Value: var id } && id == actorId;

    /// <summary>Steps until the boss is mid a cast with at least 3 ticks to go; returns the run and that cast.</summary>
    private static (Run Run, string Cast, int EndsTick) UntilBossMidCast(Run run, List<SimEvent> events)
    {
        while (true)
        {
            Assert.True(run.State.Tick < 2000, "the boss never started a cast");
            var boss = run.State.Actors[Boss];
            if (boss.CastId is { } cast && boss.CastEndsTick is { } ends && ends - run.State.Tick >= 3)
            {
                return (run, cast, ends);
            }
            run = StepOne(run, events);
        }
    }

    /// <summary>
    /// Stuns the boss mid-cast with Cold Snap and steps past the cast's end; returns the events from the stun, the run
    /// right after the stun's tick, and every tick's boss effect ids from the stun to the cast's end.
    /// </summary>
    private static (string Cast, List<SimEvent> FromStun, Run AfterStun, List<string> BossEffectsSeen) StunTheBossMidCast(bool bossStunImmune)
    {
        var events = new List<SimEvent>();
        var (run, cast, ends) = UntilBossMidCast(WithHostStops(BuildRun(), bossStunImmune), events);
        var from = events.Count;
        run = StepOne(run, events, new CastCommand(run.State.Tick, Cinder, ColdSnap, Boss));
        var afterStun = run;
        var seen = new List<string>(run.State.Actors[Boss].Effects.Select(e => e.EffectId));
        while (run.State.Tick <= ends + 1)
        {
            run = StepOne(run, events);
            seen.AddRange(run.State.Actors[Boss].Effects.Select(e => e.EffectId));
        }
        return (cast, events.Skip(from).ToList(), afterStun, seen);
    }

    [Fact(DisplayName = "A stun on the stun-immune boss is refused: no stunned effect lands, an immune event, the cast runs on (f-94749699)")]
    public void Stun_immune_boss_refuses_the_stunned_effect()
    {
        var (cast, fromStun, afterStun, seen) = StunTheBossMidCast(bossStunImmune: true);
        Assert.DoesNotContain(afterStun.State.Actors[Boss].Effects, e => e.EffectId == "stunned");
        Assert.DoesNotContain("stunned", seen);
        var immune = Assert.Single(fromStun, e => e.Type == "immune" && By(e, "targetId", Boss));
        Assert.True(By(immune, "sourceId", Cinder));
        Assert.Equal("stunned", immune.Get("effectId") is JsString s ? s.Value : null);
        Assert.Equal(cast, afterStun.State.Actors[Boss].CastId);
        Assert.Contains(fromStun, e => e.Type == "cast_complete" && By(e, "actorId", Boss) && e.Get("abilityId") is JsString c && c.Value == cast);
    }

    [Fact(DisplayName = "Control: a stunnable boss still takes the stunned effect from the same stun, and no immune event")]
    public void Stunnable_boss_takes_the_stunned_effect()
    {
        var (_, fromStun, afterStun, _) = StunTheBossMidCast(bossStunImmune: false);
        Assert.Contains(afterStun.State.Actors[Boss].Effects, e => e.EffectId == "stunned" && e.SourceId == Cinder);
        Assert.DoesNotContain(fromStun, e => e.Type == "immune");
    }

    [Fact(DisplayName = "With the boss stun-immune, a stunnable Twin still takes the stunned effect, and no immune event")]
    public void Stunnable_twin_takes_the_stunned_effect_beside_an_immune_boss()
    {
        var run = WithHostStops(BuildRun(), bossStunImmune: true);
        var events = new List<SimEvent>();
        while (!run.State.Actors.ContainsKey(TwinA))
        {
            Assert.True(run.State.Tick < 300, "TwinA never spawned");
            run = StepOne(run, events);
        }
        var from = events.Count;
        run = StepOne(run, events, new CastCommand(run.State.Tick, Cinder, ColdSnap, TwinA));
        Assert.Contains(run.State.Actors[TwinA].Effects, e => e.EffectId == "stunned" && e.SourceId == Cinder);
        Assert.DoesNotContain(events.Skip(from), e => e.Type == "immune");
    }

    [Fact(DisplayName = "A stun on the stun-immune boss leaves its uninterruptible cast running to completion (f-c63b43bf)")]
    public void Stun_immune_boss_keeps_casting_through_a_stun()
    {
        var (cast, fromStun, _, _) = StunTheBossMidCast(bossStunImmune: true);
        Assert.False(BuildRun().Content.Abilities[cast].Interruptible);
        Assert.DoesNotContain(fromStun, e => e.Type == "interrupt" && By(e, "targetId", Boss));
        Assert.Contains(fromStun, e => e.Type == "cast_complete" && By(e, "actorId", Boss) && e.Get("abilityId") is JsString s && s.Value == cast);
    }

    [Fact(DisplayName = "Control: the same stun on a stunnable boss ends its uninterruptible cast (the flag is what keeps it casting)")]
    public void Stunnable_boss_is_stopped_by_the_same_stun()
    {
        var (cast, fromStun, _, _) = StunTheBossMidCast(bossStunImmune: false);
        var interrupt = Assert.Single(fromStun, e => e.Type == "interrupt" && By(e, "targetId", Boss));
        Assert.Equal("stunned", interrupt.Get("stop") is JsString s ? s.Value : null);
        Assert.DoesNotContain(fromStun, e => e.Type == "cast_complete" && By(e, "actorId", Boss) && e.Get("abilityId") is JsString c && c.Value == cast);
    }

    [Fact(DisplayName = "Under the host's stun wiring a Twins stagger still ends both slams by cast_stopped, not interrupt (#344 path)")]
    public void Twins_stagger_keeps_the_stagger_path_under_host_stun_wiring()
    {
        var run = WithHostStops(BuildRun(), bossStunImmune: true);
        var events = new List<SimEvent>();
        while (!(run.State.Actors.TryGetValue(TwinA, out var twinA) && twinA.CastId == TwinSlam))
        {
            Assert.True(run.State.Tick < 300, "TwinA never started a slam");
            run = StepOne(run, events);
        }
        var t = run.State.Tick;
        Assert.Equal(TwinSlam, run.State.Actors[TwinB].CastId);
        var from = events.Count;
        run = StepOne(run, events, new CastCommand(t, Warden, "warden.shieldBash", TwinA));
        run = StepOne(run, events, new CastCommand(t + 1, Chanter, "chanter.rebuke", TwinB));
        run = StepOne(run, events, new CastCommand(t + 2, Cinder, "cinder.quench", TwinA));
        var fromStagger = events.Skip(from).ToList();
        Assert.Single(fromStagger, e => e.Type == "stagger");
        Assert.Null(run.State.Actors[TwinA].CastId);
        Assert.Null(run.State.Actors[TwinB].CastId);
        var stopped = fromStagger.Where(e => e.Type == "cast_stopped" && e.Get("abilityId") is JsString { Value: TwinSlam }).ToList();
        Assert.Contains(stopped, e => By(e, "actorId", TwinA));
        Assert.Contains(stopped, e => By(e, "actorId", TwinB));
        Assert.DoesNotContain(fromStagger, e => e.Type == "interrupt" && (By(e, "targetId", TwinA) || By(e, "targetId", TwinB)));
    }
}
