using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-0fd3e6fb: run F's abandon must land inside the Choir whatever the party's pacing. A fixed run tick (1500,
/// 1700) and a fixed offset from the Choir's pull_start (2@pull0+1350, R-415-D) each fell outside it when a pacing
/// change moved the Choir (the setup lesson, #364 and #326 each flipped it). This is a pacing test double: the Choir
/// run F records on main (pull_start 76; enemy 102 worn down from the engage to its death at 1030, 100 to 1157, then 101
/// from 1157 to 1608; pull_clear 1609; RunFJoinerActsTests) with its engage shifted by <c>engageShift</c> ticks and every
/// kill moved by <c>fightShift</c> more, fed step by step to <see cref="SeatAbandonPlan"/> exactly as SimDriver.StepOnce
/// does. Run F now leaves one tick after the Choir is down to a tenth of its health (<c>2@pull0.hp10+1</c>), which stays
/// inside the Choir for every move; the old fixtures are kept here to show the double does move them out.
/// </summary>
public sealed class RunFAbandonPacingTests
{
    private const int ChoirStart = 76;
    private const int PairStart = 1722;
    private const long ChoirHealth = 6180;
    private const int MaxTicks = 3500;

    // Each Choir enemy: worn down linearly from tick From to its death at tick Death (run F's undisturbed trace on main).
    private static readonly (int ActorId, int From, int Death)[] ChoirKills = { (102, 76, 1030), (100, 76, 1157), (101, 1157, 1608) };

    private sealed record Trace(int Start, int Clear, Dictionary<int, List<SimEvent>> Events, (int ActorId, int From, int Death)[] Kills);

    // The double's dungeon: pull 0 is the Choir (spawns 100..102, as hollow-vault's), pull 1 the Pair.
    private static readonly DungeonDef Dungeon = new("hollow-vault", new[]
    {
        new PullDef("choir", new[] { 100, 101, 102 }.Select(id => new PullSpawn(id, "choir.melee", new Vec3(0, 0, 0), 0, ChoirHealth)).ToList()),
        new PullDef("pair", new[] { 103, 104 }.Select(id => new PullSpawn(id, "pair.melee", new Vec3(0, 0, 0), 0, ChoirHealth)).ToList()),
    });

    // The double: run F's Choir, its engage moved by engageShift and every kill (and the clear) by engageShift + fightShift.
    private static Trace Choir(int engageShift, int fightShift)
    {
        var events = new Dictionary<int, List<SimEvent>>();
        void Add(SimEvent e)
        {
            if (!events.TryGetValue(e.Tick, out var list))
            {
                events[e.Tick] = list = new List<SimEvent>();
            }
            list.Add(e);
        }
        var start = ChoirStart + engageShift;
        var kills = ChoirKills
            .Select(k => (k.ActorId, From: k.From == ChoirStart ? start : k.From + engageShift + fightShift, Death: k.Death + engageShift + fightShift))
            .ToArray();
        var clear = kills.Max(k => k.Death) + 1;
        Add(new SimEvent(start, "pull_start", ("pullIndex", JsValue.Int(0)), ("pullId", JsValue.Str("choir")),
            ("actorIds", JsValue.Arr(new[] { JsValue.Int(100), JsValue.Int(101), JsValue.Int(102) }))));
        foreach (var k in kills)
        {
            Add(new SimEvent(k.Death, "death", ("actorId", JsValue.Int(k.ActorId)), ("sourceId", JsValue.Int(1))));
        }
        Add(new SimEvent(clear, "pull_clear", ("pullIndex", JsValue.Int(0)), ("pullId", JsValue.Str("choir"))));
        Add(new SimEvent(PairStart + engageShift + fightShift, "pull_start", ("pullIndex", JsValue.Int(1)), ("pullId", JsValue.Str("pair")),
            ("actorIds", JsValue.Arr(new[] { JsValue.Int(103), JsValue.Int(104) }))));
        return new Trace(start, clear, events, kills);
    }

    // The Choir's actors after the step at pre-step tick t.
    private static Dictionary<int, ActorState> ActorsAfter(Trace trace, int t)
    {
        var actors = new Dictionary<int, ActorState>();
        foreach (var (id, from, death) in trace.Kills)
        {
            var left = t < from ? ChoirHealth : t >= death ? 0 : ChoirHealth * (death - t) / Math.Max(1, death - from);
            actors[id] = new ActorState { Id = id, Kind = ActorKind.Enemy, ClassId = "choir.melee", Health = left, MaxHealth = ChoirHealth, Alive = t < death };
        }
        return actors;
    }

    // SimDriver.StepOnce's order: MaybeScriptedAbandon at the pre-step tick, then the step, then the anchor from its
    // events and the run's state after it.
    private static int? AbandonTick(SeatAbandonPlan plan, Trace trace)
    {
        int? anchor = null;
        for (var tick = 0; tick < MaxTicks; tick++)
        {
            if (tick == plan.ResolveTick(anchor))
            {
                return tick;
            }
            var events = trace.Events.TryGetValue(tick, out var step) ? step : new List<SimEvent>();
            anchor ??= plan.AnchorAt(tick, events, Dungeon, ActorsAfter(trace, tick));
        }
        return null;
    }

    public static IEnumerable<object[]> Pacings => new[]
    {
        new object[] { 0, 0 },
        new object[] { 300, 0 },
        new object[] { -40, 0 },
        new object[] { 0, -300 },
        new object[] { 0, 400 },
        new object[] { 250, -250 },
        new object[] { -60, -500 },
    };

    [Theory(DisplayName = "Run F's abandon falls inside the Choir however the Choir's engage and fight length move")]
    [MemberData(nameof(Pacings))]
    public void Run_F_abandon_stays_inside_the_choir(int engageShift, int fightShift)
    {
        var trace = Choir(engageShift, fightShift);
        var at = AbandonTick(SeatAbandonPlan.Parse(RunFJoinerActsTests.Abandon), trace);
        Assert.True(at is not null && at > trace.Start && at < trace.Clear,
            $"{RunFJoinerActsTests.Abandon} abandons at {at?.ToString() ?? "never"}, outside the Choir ({trace.Start}..{trace.Clear}) with engage {engageShift:+0;-0;0} and fight {fightShift:+0;-0;0}");
    }

    [Theory(DisplayName = "the fixed-tick and pull_start-offset fixtures run F used land outside the Choir under a pacing change")]
    [InlineData("2@1426", 0, -300)]
    [InlineData("2@1500", 0, -300)]
    [InlineData("2@pull0+1350", 0, -300)]
    [InlineData("2@pull0+1350", 250, -250)]
    public void The_old_fixtures_flip_under_a_pacing_change(string abandon, int engageShift, int fightShift)
    {
        var undisturbed = Choir(0, 0);
        var plan = SeatAbandonPlan.Parse(abandon);
        var before = AbandonTick(plan, undisturbed);
        Assert.True(before > undisturbed.Start && before < undisturbed.Clear, $"{abandon} at {before} is not inside the undisturbed Choir");
        var moved = Choir(engageShift, fightShift);
        var after = AbandonTick(plan, moved);
        Assert.False(after > moved.Start && after < moved.Clear,
            $"{abandon} at {after} stayed inside the moved Choir ({moved.Start}..{moved.Clear}): this double no longer shows the fragility");
    }
}
