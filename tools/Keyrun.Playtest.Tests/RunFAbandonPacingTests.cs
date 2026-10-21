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

    // Each event kind's lag: its stamp minus the pre-step tick of the step that emits it. A pull_start is stamped after the
    // tick advances (Tick.Step's next.Tick += 1, then Run.cs's Aggro.Engage(next, ..., next.Tick, emit); f-5813f5d2), so
    // lag 1. A death is stamped inside the rules, before the advance (Rules.cs -> Damage.cs's emit at next.Tick while it
    // is still the pre-step tick), and pull_clear from run.State.Tick (Run.cs's Pulls.CheckPullAdvance), so lag 0
    // (f-2a1a9173). A kind not listed throws: the double must not guess a stamp it has not been held to.
    private static readonly Dictionary<string, int> StampLag = new() { ["pull_start"] = 1, ["death"] = 0, ["pull_clear"] = 0 };

    // The events the step at pre-step tick preTick hands back: every trace event whose stamp minus its kind's lag is preTick.
    private static List<SimEvent> StepEvents(Trace trace, int preTick) =>
        StampLag.Values.Distinct()
            .SelectMany(lag => trace.Events.TryGetValue(preTick + lag, out var stamped) ? stamped : new List<SimEvent>())
            .Where(e => StampLag.TryGetValue(e.Type, out var lag) ? e.Tick - lag == preTick : throw new InvalidOperationException($"the double has no stamp lag for event kind {e.Type}"))
            .ToList();

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
            var events = StepEvents(trace, tick);
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

    // f-5813f5d2: the double must hand a pull_start to the same step the real sim does, so a pull_start-offset plan
    // abandons the same number of ticks after the pull_start in both. +0 is the offset the old double got wrong (it fed an
    // event stamped t to the step at pre-step t, one step late, so A@pull0+0 never fired: false finding f-854228f4).
    [Fact(DisplayName = "the double hands pull 0's pull_start to the step the real sim emits it from, and no earlier one")]
    public void The_double_hands_pull_start_to_the_real_sims_step()
    {
        var real = SeatAbandonZeroOffsetTests.RunReal("2@pull0+0");
        Assert.True(real.PullStart is not null && real.EmittedAtPreTick is not null, "the real sim never started pull 0");
        var lag = real.PullStart!.Value - real.EmittedAtPreTick!.Value;
        var trace = Choir(0, 0);
        static bool HasPullStart(List<SimEvent> events) =>
            events.Any(e => e.Type == "pull_start" && e.Get("pullIndex") is JsInt { Value: 0 });
        var carrying = Enumerable.Range(0, MaxTicks).Where(t => HasPullStart(StepEvents(trace, t))).ToList();
        Assert.True(carrying.SequenceEqual(new[] { trace.Start - lag }),
            $"the real sim emits pull_start {real.PullStart} from the step at pre-step {real.EmittedAtPreTick} (lag {lag}); the double hands pull_start {trace.Start} to the steps at pre-step [{string.Join(", ", carrying)}], expected [{trace.Start - lag}]");
    }

    // f-2a1a9173: a Choir death and pull 0's pull_clear must reach the same step in the double as in the real sim. The
    // real run (a fixed abandon past the Choir) gives each kind's lag, its event tick minus the pre-step tick of the step
    // that emitted it; the double must hand each of its Choir deaths and its pull_clear to the step at its stamp minus
    // that lag, and to no other step. A double that hands every event to the step at stamp - 1 (pull_start's lag) hands
    // a death stamped 1030 to the step at 1029, one step before the real sim's.
    [Fact(DisplayName = "the double hands each Choir death and pull 0's pull_clear to the step the real sim emits it from")]
    public void The_double_hands_deaths_and_pull_clear_to_the_real_sims_step()
    {
        var choirIds = Dungeon.Pulls[0].Spawns.Select(s => s.ActorId).ToHashSet();
        bool IsChoirDeath(SimEvent e) => e.Type == "death" && e.Get("actorId") is JsInt a && choirIds.Contains((int)a.Value);
        static bool IsChoirClear(SimEvent e) => e.Type == "pull_clear" && e.Get("pullIndex") is JsInt { Value: 0 };
        var realLags = new Dictionary<string, HashSet<int>> { ["death"] = new(), ["pull_clear"] = new() };
        SeatAbandonZeroOffsetTests.RunReal($"2@{MaxTicks - 1}", (preTick, events, _, _) =>
        {
            foreach (var e in events.Where(e => IsChoirDeath(e) || IsChoirClear(e)))
            {
                realLags[e.Type].Add(e.Tick - preTick);
            }
        });
        Assert.True(realLags.All(kv => kv.Value.Count == 1),
            $"the real sim must emit every Choir death and pull 0's pull_clear with one lag per kind; it gave {string.Join("; ", realLags.Select(kv => $"{kv.Key} [{string.Join(", ", kv.Value)}]"))}");
        var trace = Choir(0, 0);
        var mismatches = new List<string>();
        void Check(string what, int stamp, int lag, Func<SimEvent, bool> match)
        {
            var carrying = Enumerable.Range(0, MaxTicks).Where(t => StepEvents(trace, t).Any(match)).ToList();
            if (!carrying.SequenceEqual(new[] { stamp - lag }))
            {
                mismatches.Add($"{what} stamped {stamp}: real lag {lag} puts it on the step at pre-step {stamp - lag}, the double on [{string.Join(", ", carrying)}]");
            }
        }
        var deathLag = realLags["death"].Single();
        foreach (var (id, _, death) in trace.Kills)
        {
            Check($"enemy {id}'s death", death, deathLag, e => e.Type == "death" && e.Get("actorId") is JsInt { Value: var a } && a == id);
        }
        Check("pull 0's pull_clear", trace.Clear, realLags["pull_clear"].Single(), IsChoirClear);
        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    [Theory(DisplayName = "the double abandons a pull_start-offset plan the same ticks after pull_start as the real sim")]
    [InlineData("2@pull0+0")]
    [InlineData("2@pull0+1")]
    public void The_double_abandons_on_the_real_sims_tick(string abandon)
    {
        var real = SeatAbandonZeroOffsetTests.RunReal(abandon);
        Assert.True(real.PullStart is not null && real.Abandon is not null,
            $"the real sim never started pull 0 or never abandoned {abandon} (pull_start {real.PullStart?.ToString() ?? "none"}, abandon {real.Abandon?.ToString() ?? "never"})");
        var trace = Choir(0, 0);
        var at = AbandonTick(SeatAbandonPlan.Parse(abandon), trace);
        Assert.True(at - trace.Start == real.Abandon - real.PullStart,
            $"{abandon}: the real sim abandons {real.Abandon - real.PullStart} ticks after pull_start ({real.PullStart} -> {real.Abandon}), the double {(at is null ? "never" : (at - trace.Start).ToString())} ({trace.Start} -> {at?.ToString() ?? "never"})");
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

    // f-18509803: the double's health timeline must kill a Choir enemy on the same step the real sim does. The real run
    // (SeatAbandonZeroOffsetTests.RunReal, seed 1, key 2, a fixed abandon past the Choir) is stepped to the first Choir
    // death; for the step at the death event's tick and the steps either side, the double's ActorsAfter must report the
    // dying enemy alive or dead exactly as the real sim's actors after that step do (death event tick and kill tick
    // aligned, the double's first kill against the real sim's first).
    [Fact(DisplayName = "the double's ActorsAfter kills a Choir enemy after the same step as the real sim")]
    public void The_double_kills_on_the_real_sims_step()
    {
        int? deathActor = null;
        int? deathStamp = null;
        int? deathStep = null;
        var aliveAfter = new Dictionary<int, Dictionary<int, bool>>();
        SeatAbandonZeroOffsetTests.RunReal($"2@{MaxTicks - 1}", (preTick, events, dungeon, actors) =>
        {
            var choir = dungeon.Pulls[0].Spawns.Select(s => s.ActorId).ToHashSet();
            aliveAfter[preTick] = choir.Where(actors.ContainsKey).ToDictionary(id => id, id => actors[id].Alive);
            if (deathActor is null && events.FirstOrDefault(e => e.Type == "death" && e.Get("actorId") is JsInt a && choir.Contains((int)a.Value)) is { } d)
            {
                deathActor = (int)((JsInt)d.Get("actorId")!).Value;
                deathStamp = d.Tick;
                deathStep = preTick;
            }
        });
        Assert.True(deathActor is not null, "the real sim never killed a Choir enemy");
        var trace = Choir(0, 0);
        var kill = trace.Kills.OrderBy(k => k.Death).First();
        var mismatches = new List<string>();
        for (var o = -1; o <= 1; o++)
        {
            var realTick = deathStamp!.Value + o;
            var real = aliveAfter.TryGetValue(realTick, out var after) && after.TryGetValue(deathActor!.Value, out var alive) ? alive : (bool?)null;
            var stub = ActorsAfter(trace, kill.Death + o)[kill.ActorId].Alive;
            if (real != stub)
            {
                mismatches.Add($"death tick{o:+0;-0;+0} (real pre-step {realTick}: {(real is null ? "absent" : real.Value ? "alive" : "dead")}; double pre-step {kill.Death + o}: {(stub ? "alive" : "dead")})");
            }
        }
        Assert.True(mismatches.Count == 0,
            $"the real sim stamps enemy {deathActor}'s death {deathStamp} from the step at pre-step {deathStep}; the double's ActorsAfter for enemy {kill.ActorId} (death {kill.Death}) disagrees after the step at {string.Join("; ", mismatches)}");
    }
}
