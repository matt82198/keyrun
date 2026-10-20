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
/// TELEGRAPH-CANCEL (wrong data shown is P0; BIBLE s3: "Every dangerous enemy action is a cast with a visible bar and a
/// floor telegraph (circle, cone, line) drawn ahead of resolution"). A <see cref="TelegraphSpec.SpansCast"/> telegraph
/// stands for a cast that is under way. When that cast ends without landing (kicked, stopped, or overwritten by the same
/// enemy's next cast) the telegraph is a promise of a hit that will not come, so the sim emits
/// <c>{type:"telegraph_cancel", telegraphId, actorId, cast}</c> on the tick the cast ends, drops it from the registry,
/// and never resolves it (no <c>telegraph_hit</c>). Everything runs through <see cref="RunRoot.StepRun"/>.
/// </summary>
public sealed class TelegraphCancelTests
{
    private const int Player = 1;
    private const int Enemy = 10;
    private const string Long = "e.long";   // 40-tick cast, not interruptible
    private const string Short = "e.short"; // 10-tick cast
    private const string Kickable = "e.kickable"; // 40-tick cast, interruptible
    private const string Kick = "p.kick";

    private static AbilityDef EnemyCast(string id, int castTicks, bool interruptible) => RunTestKit.Ability(
        id, new EffectOp[] { new DamageOp(10, "physical") }) with
    {
        CastTicks = castTicks,
        Interruptible = interruptible,
    };

    private static ScriptStep At(int tick, string cast, int lead) => new(StepTrigger.AtTick, cast)
    {
        AtTick = tick,
        Telegraph = new TelegraphSpec("circle", lead) { Radius = 2000, SpansCast = true },
    };

    private static Run BuildRun(params ScriptStep[] script)
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            [Long] = EnemyCast(Long, 40, interruptible: false),
            [Short] = EnemyCast(Short, 10, interruptible: false),
            [Kickable] = EnemyCast(Kickable, 40, interruptible: true),
            [Kick] = RunTestKit.Ability(Kick, new EffectOp[] { new InterruptOp() }),
        };
        var content = RunTestKit.Content(abilities) with
        {
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Enemy] = script },
        };
        var party = new[] { RunTestKit.Member(Player, "warden", ActorRole.Tank, 0, 1000, 1_000_000, 100) };
        var dungeon = RunTestKit.OnePull("d", "p", RunTestKit.Spawn(Enemy, "foe", 0, 0, 1_000_000));
        return RunRoot.CreateRun(7, party, dungeon, content);
    }

    /// <summary>Steps to <paramref name="until"/>, recording events and the registry left pending after each tick.</summary>
    private static (List<SimEvent> Events, Dictionary<int, IReadOnlyList<Telegraph>> Pending) Play(Run run, int until, params Command[] commands)
    {
        var events = new List<SimEvent>();
        var pending = new Dictionary<int, IReadOnlyList<Telegraph>>();
        while (run.State.Tick < until)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, commands.Where(c => c.Tick == tick).ToList());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            pending[tick] = run.Encounter.Telegraphs;
        }
        return (events, pending);
    }

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    /// <summary>The telegraph drawn for <paramref name="cast"/> and the id the registry minted for it.</summary>
    private static (SimEvent Drawn, int Id) Telegraphed(List<SimEvent> events, Dictionary<int, IReadOnlyList<Telegraph>> pending, string cast)
    {
        var drawn = Assert.Single(events, e => e.Type == "telegraph" && Str(e, "cast") == cast);
        var id = pending[drawn.Tick].Single(t => t.AbilityId == cast && t.ResolvesTick == Int(drawn, "resolvesTick")).Id;
        return (drawn, id);
    }

    /// <summary>
    /// The cancelled telegraph: exactly one telegraph_cancel for it on <paramref name="cancelTick"/>, naming its caster
    /// and cast; no telegraph_hit for it ever; gone from the registry from that tick on; and its cast never lands.
    /// </summary>
    private static void AssertCancelled(List<SimEvent> events, Dictionary<int, IReadOnlyList<Telegraph>> pending, string cast, int cancelTick)
    {
        var (drawn, id) = Telegraphed(events, pending, cast);
        var cancels = events.Where(e => e.Type == "telegraph_cancel" && Int(e, "telegraphId") == id).ToList();
        var cancel = Assert.Single(cancels);
        Assert.Equal(cancelTick, cancel.Tick);
        Assert.Equal(Enemy, Int(cancel, "actorId"));
        Assert.Equal(cast, Str(cancel, "cast"));
        Assert.DoesNotContain(events, e => e.Type == "telegraph_hit" && Int(e, "telegraphId") == id);
        Assert.DoesNotContain(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == cast);
        for (var t = cancelTick; t < Int(drawn, "resolvesTick") + 5; t++)
        {
            Assert.DoesNotContain(pending[t], tg => tg.Id == id);
        }
    }

    /// <summary>The telegraph that was NOT cancelled resolves with its cast landing on the same tick.</summary>
    private static void AssertLands(List<SimEvent> events, Dictionary<int, IReadOnlyList<Telegraph>> pending, string cast)
    {
        var (drawn, id) = Telegraphed(events, pending, cast);
        var resolves = Int(drawn, "resolvesTick");
        Assert.DoesNotContain(events, e => e.Type == "telegraph_cancel" && Int(e, "telegraphId") == id);
        Assert.Contains(events, e => e.Type == "telegraph_hit" && Int(e, "telegraphId") == id && e.Tick == resolves);
        Assert.Contains(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == cast && e.Tick == resolves);
    }

    [Fact(DisplayName = "A kicked cast's floor telegraph is cancelled on the kick tick and never resolves")]
    public void Kicked_cast_cancels_its_telegraph_on_the_kick_tick()
    {
        var run = BuildRun(At(20, Kickable, 40));
        var (events, pending) = Play(run, 90, new CastCommand(30, Player, Kick, Enemy));
        Assert.Contains(events, e => e.Type == "interrupt" && e.Tick == 30 && Int(e, "targetId") == Enemy);
        AssertCancelled(events, pending, Kickable, 30);
    }

    [Fact(DisplayName = "A stopped cast's floor telegraph is cancelled on the stop tick and never resolves")]
    public void Stopped_cast_cancels_its_telegraph_on_the_stop_tick()
    {
        var run = BuildRun(At(20, Long, 40));
        var (events, pending) = Play(run, 90, new StopCastCommand(35, Enemy));
        Assert.Contains(events, e => e.Type == "cast_stopped" && e.Tick == 35 && Int(e, "actorId") == Enemy);
        AssertCancelled(events, pending, Long, 35);
    }

    [Fact(DisplayName = "A cast overwritten mid-bar by the same enemy's next cast cancels its telegraph on the overwrite tick")]
    public void Cast_overwritten_mid_bar_cancels_its_telegraph()
    {
        // e.long starts on 20 (lands 60); e.short starts on 40 and replaces it (lands 50).
        var run = BuildRun(At(20, Long, 40), At(40, Short, 10));
        var (events, pending) = Play(run, 90);
        AssertCancelled(events, pending, Long, 40);
        AssertLands(events, pending, Short);
    }

    [Fact(DisplayName = "A cast overwritten on the tick it starts cancels its telegraph that same tick")]
    public void Cast_overwritten_on_its_start_tick_cancels_its_telegraph()
    {
        // Both fire on 20, short first in script order: e.short starts and e.long replaces it on the same tick.
        var run = BuildRun(At(20, Short, 10), At(20, Long, 40));
        var (events, pending) = Play(run, 90);
        AssertCancelled(events, pending, Short, 20);
        AssertLands(events, pending, Long);
    }

    [Fact(DisplayName = "A cast that runs its whole bar keeps its telegraph and resolves it, with no cancel")]
    public void Uninterrupted_cast_is_never_cancelled()
    {
        var run = BuildRun(At(20, Long, 40), At(100, Kickable, 40));
        var (events, pending) = Play(run, 160, new CastCommand(150, Player, Kick, Enemy));
        Assert.DoesNotContain(events, e => e.Type == "telegraph_cancel");
        AssertLands(events, pending, Long);
        AssertLands(events, pending, Kickable);
    }
}
