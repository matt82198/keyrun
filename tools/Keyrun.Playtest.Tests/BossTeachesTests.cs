using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// BOSS-TEACHES, ruling R-BOSS-1 (Matt 2026-10-21, verbatim: "The bosses die faster than trash and dont teach
/// anything"; core dungeon loop defers to WoW Mythic+, where a low-key boss lasts 90 to 150 s and every mechanic cycles
/// several times). At key 2 with the default party the Hollow Warden fight (boss pull_start to pull_clear) lasts 90 to
/// 180 s, and each of its three mechanics (BIBLE s9) resolves at least twice with a visible answer by the party:
/// <list type="bullet">
/// <item>Cleave: a <c>hollowWarden.cleave</c> cast lands, and its damage reaches no party member but the boss's target
/// (the tank faced it away; nobody else stood in the cone).</item>
/// <item>The Sundered Twins: their shared Break bar staggers (<c>stagger</c> on <c>twinsBreak</c>), drained by
/// interrupt- or control-tagged casts on the Twins, and every stagger is answered: at least one party seat's kick or
/// control cast drained the bar inside that stagger's window (since the previous stagger). R-PINS-1 (R-ORACLE-1 applied,
/// ruling on #573): this replaces a single-seed pin that both staggers come from two different seats; on the
/// aggro-chase trajectory the second bar fills from Cinder's Quench and Cold Snap 2 ticks before the Chanter's Rebuke is
/// ready (3536 vs 3538), so "two seats" was timing luck, not the mechanic. The two-seat count is still printed.</item>
/// <item>The Lock: a Lock Window opens and the party breaks it (<c>lock_broken</c>, a Setup then another class's Payoff).</item>
/// </list>
/// The default run is driven headlessly exactly as <see cref="MovieFramesDefaultRunTests"/> does (SimDriver.StartRun /
/// StepOnce): HostRunSetup.Build at the default key and seed with bots on, BossRules.Arm, seat 1 the autorun
/// <see cref="ScriptedWarden"/>, seats 2 and 3 <see cref="BotCommandSource"/> at its default dial, each step through
/// <see cref="Backfill.Step"/>. The sim runs at <see cref="Tick.TickHz"/> ticks per second, so 90 s is 1800 ticks.
/// BIBLE test 1 (hard by what enemies do, not by health): the same fight with one seat that ignores the Cleave (it
/// stands in the boss's cone every time one is cast) loses that seat again and again (a non-tank's 250 health does not
/// take two 220 Cleaves) and the party cannot kill the Warden inside the 180 s window. A strict full wipe
/// (every seat dead on one tick) is not what one careless seat produces under R-BACKFILL-2's release: each dead seat
/// respawns at the entrance 100 ticks after it dies, so the rest of the party is never all down together.
/// </summary>
public sealed class BossTeachesTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const string DungeonId = "hollow-vault";
    private const int WardenId = 1;
    private const int BossId = 900;
    private const string Cleave = "hollowWarden.cleave";
    private const string TwinsBar = "twinsBreak";

    /// <summary>R-BOSS-1 lower bound: 90 s.</summary>
    private const int MinFightTicks = 90 * Tick.TickHz;

    /// <summary>R-BOSS-1 upper bound: 180 s.</summary>
    private const int MaxFightTicks = 180 * Tick.TickHz;

    /// <summary>Hard stop for the drive: well past the boss door plus the upper bound.</summary>
    private const int DriveCapTicks = 12000;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the measured numbers are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public BossTeachesTests(ITestOutputHelper output) => _output = output;

    /// <summary>What one drive measured.</summary>
    private sealed record Fight(
        int? Start,
        int? Clear,
        int? WipeTick,
        int Cleaves,
        int CleavesAnswered,
        int Staggers,
        int StaggersTwoPerson,
        int LocksOpened,
        int LocksBroken,
        int LocksFailed,
        int CarelessDeaths,
        int BossHealthAtEnd,
        int EndTick,
        int CarelessAliveTicks,
        int PartyDeaths,
        long PenaltyTicks,
        long ClockTicks,
        int ParTicks,
        string Result,
        string KeyLine,
        int KeyTier,
        int BossMisses)
    {
        /// <summary>R-PINS-1: staggers with at least one party drain (a kick or control cast on a Twin) in their window.</summary>
        public int StaggersAnswered { get; init; }

        /// <summary>R-BOSS-4: <c>cleaved</c> events fired in the boss fight (one per stack laid), on any seat.</summary>
        public int CleavedEvents { get; init; }

        /// <summary>R-BOSS-4: the highest stack count any <c>cleaved</c> event reported in the boss fight.</summary>
        public int MaxCleavedStacks { get; init; }

        /// <summary>R-BOSS-4: <c>cleaved</c> events that landed on the Warden seat (the boss's tank); must stay zero.</summary>
        public int CleavedOnTank { get; init; }

        /// <summary>R-BOSS-4: <c>cleaved</c> events on the careless seat (zero when no seat ignores the Cleave).</summary>
        public int CarelessCleaved { get; init; }

        public int? Ticks => Start is { } s && Clear is { } c ? c - s : null;

        /// <summary>Boss pull ticks so far: to the clear, else to where the drive stopped.</summary>
        public int? Elapsed => Start is { } s ? (Clear ?? EndTick) - s : null;

        public override string ToString() =>
            $"BOSS start={Start?.ToString() ?? "-"} clear={Clear?.ToString() ?? "-"} ticks={Ticks?.ToString() ?? "-"} wipe={WipeTick?.ToString() ?? "-"} " +
            $"cleaves={Cleaves} cleavesAnswered={CleavesAnswered} staggers={Staggers} staggersAnswered={StaggersAnswered} staggersTwoPerson={StaggersTwoPerson} " +
            $"locksOpened={LocksOpened} locksBroken={LocksBroken} locksFailed={LocksFailed} carelessDeaths={CarelessDeaths} bossHp={BossHealthAtEnd} endTick={EndTick} " +
            $"cleavedEvents={CleavedEvents} maxCleavedStacks={MaxCleavedStacks} cleavedOnTank={CleavedOnTank} carelessCleaved={CarelessCleaved} carelessAliveTicks={CarelessAliveTicks} partyDeaths={PartyDeaths} penaltyTicks={PenaltyTicks} clock={ClockTicks} par={ParTicks} result={Result} key=\"{KeyLine}\" bossMisses={BossMisses}";
    }

    /// <summary>
    /// Drives the default run until the boss pull clears, the party wipes inside the boss fight, or the cap.
    /// <paramref name="ignoreCleaveSeat"/>, when set, is a seat that ignores the Cleave: while a Cleave cast is under way
    /// its own moves are dropped and it walks to a point 1.5 m in front of the boss, inside the cone.
    /// </summary>
    private static Fight DriveRun(int? ignoreCleaveSeat = null)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, Key, Lobby.DefaultClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, Lobby.DefaultClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var backfill = BackfillState.Empty;
        var tags = run.Content.Abilities.ToDictionary(kv => kv.Key, kv => kv.Value.Tags ?? (IReadOnlyList<string>)Array.Empty<string>());
        var cleaveRaw = run.Content.Abilities.TryGetValue(Cleave, out var cleaveDef) ? cleaveDef : null;
        var players = new HashSet<int>(run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player).Select(a => a.Id));
        var twinIds = new HashSet<int>(setup.Boss.Twins.Select(t => t.ActorId));

        int? fightStart = null, fightClear = null, wipe = null;
        int cleaves = 0, cleavesAnswered = 0, staggers = 0, staggersAnswered = 0, staggersTwo = 0, locksOpened = 0, locksBroken = 0, locksFailed = 0, carelessDeaths = 0, carelessAlive = 0, bossMisses = 0;
        var drainers = new HashSet<int>();
        int cleavedEvents = 0, maxCleavedStacks = 0, cleavedOnTank = 0, carelessCleaved = 0;
        // FINISH-NEEDS-FORCES: the boss's death no longer ends the run short of 100% forces, so the drive stops on the kill.
        for (var i = 0; i < DriveCapTicks && run.RunEnd.Result is null && wipe is null && fightClear is null; i++)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            if (ignoreCleaveSeat is { } careless && fightStart is not null &&
                run.State.Actors.TryGetValue(BossId, out var b) && b.Alive && b.CastId == Cleave &&
                run.State.Actors.TryGetValue(careless, out var me) && me.Alive)
            {
                commands.RemoveAll(c => c.ActorId == careless && (c is MoveCommand or DashCommand
                    || c is CastCommand && Keyrun.Bots.Reasons.Of(c).RuleId == "bot:movement-escape"));
                var spot = InFront(b, 1500);
                if (Math.Abs(spot.X - me.Pos.X) + Math.Abs(spot.Z - me.Pos.Z) > 300 &&
                    Moves.MoveToward(tick, careless, new PointMm(me.Pos.X, me.Pos.Z), new PointMm(spot.X, spot.Z)) is { } walk)
                {
                    commands.Add(walk);
                }
            }
            if (fightStart is not null)
            {
                foreach (var cast in commands.OfType<CastCommand>())
                {
                    var target = cast.TargetId ?? (run.State.Actors.TryGetValue(cast.ActorId, out var caster) ? caster.TargetId : null);
                    if (players.Contains(cast.ActorId) && target is { } t && twinIds.Contains(t) &&
                        tags.TryGetValue(cast.AbilityId, out var tg) && (tg.Contains("interrupt") || tg.Contains("control")))
                    {
                        drainers.Add(cast.ActorId);
                    }
                }
            }
            var bossTargetBefore = run.State.Actors.TryGetValue(BossId, out var bb) ? bb.TargetId : null;
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            var events = step.Step.Events;
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case "pull_start" when Str(e, "pullId") == "boss":
                        fightStart = e.Tick;
                        break;
                    case "pull_clear" when Str(e, "pullId") == "boss":
                        fightClear = e.Tick;
                        break;
                    case "cast_complete" when Int(e, "actorId") == BossId && Str(e, "abilityId") == Cleave:
                        cleaves++;
                        var hitOthers = events.Any(d => d.Type == "damage" && d.Tick == e.Tick && Int(d, "sourceId") == BossId &&
                            Int(d, "rawAmount") is { } raw && cleaveRaw is not null && Int(d, "targetId") is { } tid &&
                            players.Contains((int)tid) && tid != bossTargetBefore && raw >= CleaveFloor(cleaveRaw));
                        if (!hitOthers)
                        {
                            cleavesAnswered++;
                        }
                        break;
                    case "cleaved" when fightStart is not null:
                        cleavedEvents++;
                        if (ignoreCleaveSeat is { } marked && Int(e, "actorId") == marked)
                        {
                            carelessCleaved++;
                        }
                        maxCleavedStacks = Math.Max(maxCleavedStacks, (int)(Int(e, "stacks") ?? 0));
                        if (Int(e, "actorId") == WardenId)
                        {
                            cleavedOnTank++;
                        }
                        break;
                    case "stagger" when Str(e, "barId") == TwinsBar:
                        staggers++;
                        if (drainers.Count >= 1)
                        {
                            staggersAnswered++;
                        }
                        if (drainers.Count >= 2)
                        {
                            staggersTwo++;
                        }
                        drainers.Clear();
                        break;
                    case "lock_open":
                        locksOpened++;
                        break;
                    case "lock_broken":
                        locksBroken++;
                        break;
                    case "lock_failed":
                        locksFailed++;
                        break;
                    case "miss" when fightStart is not null && Int(e, "sourceId") == BossId:
                        bossMisses++;
                        break;
                    case "death" when fightStart is not null && ignoreCleaveSeat is { } seat && Int(e, "actorId") == seat:
                        carelessDeaths++;
                        break;
                }
            }
            if (fightStart is not null && fightClear is null && ignoreCleaveSeat is { } watched &&
                run.State.Actors.TryGetValue(watched, out var w) && w.Alive)
            {
                carelessAlive++;
            }
            if (fightStart is not null && fightClear is null &&
                players.All(id => !run.State.Actors.TryGetValue(id, out var p) || !p.Alive))
            {
                wipe = run.State.Tick;
            }
        }
        var bossHp = run.State.Actors.TryGetValue(BossId, out var boss) ? (int)boss.Health : -1;
        // FINISH-NEEDS-FORCES (f-8c50a787): on main's layout the bots skip every optional pack, so the boss dies short of
        // 100% forces and the run stays open. The key is then judged as it would finish on the kill (the count full, as
        // the layout lane makes it): the same clock and par rule (KeyMove.Of) at the boss-clear tick.
        var key = KeyMove.ForRun(run) ?? (fightClear is { } killTick
            ? KeyMove.Of(Timer.IsTimed(run.Encounter, killTick) ? RunResult.Timed : RunResult.Depleted, run.Encounter.KeyLevel, Timer.ClockTicks(run.Encounter, killTick), run.Encounter.ParTicks)
            : null);
        var clock = Timer.ClockTicks(run.Encounter, run.RunEnd.ResultTick ?? fightClear ?? run.State.RunTimerTicks);
        return new Fight(fightStart, fightClear, wipe, cleaves, cleavesAnswered, staggers, staggersTwo, locksOpened, locksBroken, locksFailed, carelessDeaths, bossHp, run.State.Tick,
            carelessAlive, run.Encounter.Deaths, run.Encounter.PenaltyTicks, clock, run.Encounter.ParTicks,
            run.RunEnd.Result?.ToJs() ?? "none", key?.Line ?? "-", key?.Tier ?? -1, bossMisses)
        {
            StaggersAnswered = staggersAnswered,
            CleavedEvents = cleavedEvents,
            MaxCleavedStacks = maxCleavedStacks,
            CleavedOnTank = cleavedOnTank,
            CarelessCleaved = carelessCleaved,
        };
    }

    /// <summary>The smallest raw amount a Cleave hit carries (its first damage op), to tell it from a same-tick Slam.</summary>
    private static long CleaveFloor(Keyrun.Sim.Combat.AbilityDef cleave) =>
        cleave.Effects.OfType<Keyrun.Sim.Combat.DamageOp>().Select(d => (long)d.Amount).DefaultIfEmpty(1).Min();

    /// <summary>A point <paramref name="mm"/> in front of <paramref name="actor"/> along its yaw (0 = +x, 256 = +z, 1024 per turn).</summary>
    private static Vec3 InFront(ActorState actor, int mm)
    {
        var rad = (double)actor.Yaw * 2.0 * Math.PI / 1024.0;
        return new Vec3(actor.Pos.X + (long)Math.Round(Math.Cos(rad) * mm), actor.Pos.Y, actor.Pos.Z + (long)Math.Round(Math.Sin(rad) * mm));
    }

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    private static long? Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : null;

    [Fact(DisplayName = "R-BOSS-1: at key 2 the default party's Warden fight lasts 90 to 180 s and every mechanic resolves, answered, at least twice")]
    public void Warden_fight_lasts_the_window_and_every_mechanic_is_answered_twice()
    {
        var f = DriveRun();
        _output.WriteLine(f.ToString());
        Assert.True(f.Start is not null, $"the boss pull never started: {f}");
        Assert.True(f.Clear is not null, $"the boss pull never cleared: {f}");
        Assert.True(f.Ticks >= MinFightTicks && f.Ticks <= MaxFightTicks,
            $"fight lasted {f.Ticks} ticks ({f.Ticks / (double)Tick.TickHz:0.0} s), outside [{MinFightTicks}, {MaxFightTicks}]: {f}");
        Assert.True(f.CleavesAnswered >= 2, $"Cleave answered {f.CleavesAnswered} times (of {f.Cleaves}), need 2: {f}");
        Assert.True(f.Staggers >= 2 && f.StaggersAnswered == f.Staggers,
            $"Twins staggered {f.Staggers} times, {f.StaggersAnswered} answered by a party kick in their window; need at least 2, every one answered: {f}");
        Assert.True(f.LocksBroken >= 2, $"Lock broken {f.LocksBroken} times (of {f.LocksOpened} opened), need 2: {f}");
    }

    /// <summary>The damage seat (Cinder), which ignores the Cleave in <see cref="An_ignored_cleave_costs_the_key_tier"/>.</summary>
    private const int CarelessSeat = 3;

    /// <summary>
    /// BOSS-CARELESS-WINDOW: what Matt sees at the end of a run is the key line ("Key +2 -> +5  (timed +3)"), so the
    /// careless run is judged on the key result, not on fight ticks alone: a seat that stands in every Cleave dies
    /// repeatedly, each death adds <see cref="Timer.DeathSlackCostTicks"/> to the run clock, the fight runs past the
    /// window without that seat's damage, and the key earns a lower upgrade tier than the same run answered. The boss
    /// lands its hits: it refuses Steam's blind (BOSS-CC-IMMUNE), so it never misses (on main before the fix it was
    /// blinded for most of the fight, missed 31 of 48 hits, the careless seat died twice and the key result did not change).
    /// </summary>
    [Fact(DisplayName = "R-BOSS-1 / BIBLE test 1: a seat that ignores the Cleave dies repeatedly, the fight overruns 180 s and the key earns a lower tier than the answered run")]
    public void An_ignored_cleave_costs_the_key_tier()
    {
        var answered = DriveRun();
        var f = DriveRun(ignoreCleaveSeat: CarelessSeat);
        _output.WriteLine("answered " + answered);
        _output.WriteLine("careless " + f);
        Assert.True(f.Start is not null && f.Clear is not null, $"the careless boss pull never ran to a kill: {f}");
        Assert.True(answered.BossMisses == 0 && f.BossMisses == 0, $"the boss missed (blinded): answered {answered.BossMisses}, careless {f.BossMisses}: {f}");
        var unanswered = f.Cleaves - f.CleavesAnswered;
        Assert.True(unanswered >= 2, $"the careless seat stood in {unanswered} Cleaves, need 2: {f}");
        // A 250-health non-tank does not survive two Cleaves (220 each) without a heal between: it dies, releases and
        // runs back from the entrance, and the fight goes on without its damage.
        Assert.True(f.CarelessDeaths >= 3, $"the careless seat died {f.CarelessDeaths} times in {unanswered} Cleaves, need 3: {f}");
        Assert.True(f.Ticks > MaxFightTicks, $"ignoring the Cleave still killed the Warden inside {MaxFightTicks} ticks: {f}");
        Assert.True(answered.KeyTier >= 1 && f.KeyTier < answered.KeyTier,
            $"the careless key result \"{f.KeyLine}\" is no worse than the answered \"{answered.KeyLine}\": {f}");
    }

    /// <summary>
    /// R-BOSS-4 / f-6adaa92a: the careless tier loss is a property of the mechanism, not of a pacing gap. An unanswered Cleave
    /// stacks Cleaved on the seat it hits, so the careless seat gathers stacks while the answered run lays at most the stray one its bots let through, and the
    /// careless key tier is strictly lower. No tick count or death count is asserted here (the tests above keep those); this
    /// one fails when the Cleaved mechanism is absent.
    /// </summary>
    [Fact(DisplayName = "R-BOSS-4: the careless seat gathers Cleaved stacks and a strictly lower key tier, the answered run barely any")]
    public void Careless_seat_ends_with_cleaved_stacks_and_a_lower_tier_the_careful_run_has_none()
    {
        var answered = DriveRun();
        var careless = DriveRun(ignoreCleaveSeat: CarelessSeat);
        _output.WriteLine("answered " + answered);
        _output.WriteLine("careless " + careless);
        var unanswered = careless.Cleaves - careless.CleavesAnswered;
        Assert.True(unanswered >= 2, $"the careless seat stood in {unanswered} Cleaves, need 2: {careless}");
        // The mechanism, by count: every stack is one unanswered Cleave that hit a living non-target seat. The careless seat
        // collects several; the answered run lays no more than the Cleaves its own bots failed to answer (the bot healer is
        // clipped once on this seed) and strictly fewer than the careless run; the tank (the cast's target) never gains one.
        Assert.True(careless.CarelessCleaved >= 3, $"the careless seat gained {careless.CarelessCleaved} Cleaved stacks, need 3: {careless}");
        Assert.True(answered.CleavedEvents <= answered.Cleaves - answered.CleavesAnswered,
            $"the answered run laid {answered.CleavedEvents} Cleaved stacks from {answered.Cleaves - answered.CleavesAnswered} unanswered Cleaves: {answered}");
        Assert.True(answered.CleavedEvents + 2 <= careless.CleavedEvents,
            $"Cleaved stacks laid: answered {answered.CleavedEvents}, careless {careless.CleavedEvents}; need a margin of 2: {careless}");
        Assert.True(careless.CleavedEvents <= careless.Cleaves - careless.CleavesAnswered,
            $"{careless.CleavedEvents} Cleaved stacks from only {careless.Cleaves - careless.CleavesAnswered} unanswered Cleaves: {careless}");
        Assert.Equal(0, careless.CleavedOnTank);
        Assert.Equal(0, answered.CleavedOnTank);
        // What the stacks cost: deaths that add to the run clock, so the tier falls with a clock margin, not a tick gap.
        Assert.True(careless.PenaltyTicks > answered.PenaltyTicks && careless.ClockTicks > answered.ClockTicks,
            $"the careless run paid no clock for its Cleaved seat: {careless} vs {answered}");
        Assert.True(answered.KeyTier >= 1 && careless.KeyTier < answered.KeyTier,
            $"the careless key result \"{careless.KeyLine}\" is no worse than the answered \"{answered.KeyLine}\": {careless}");
    }
}
