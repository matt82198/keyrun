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

namespace Keyrun.Playtest.Tests;

/// <summary>
/// D66 boss arena (R-BOSS-1 proof for a boss whose dungeon has no laid-out map yet): the default party fights one boss
/// in the tutorial's boss arena, driven exactly as <see cref="BossTeachesTests"/> drives the Hollow Warden (seat 1 the
/// autorun <see cref="ScriptedWarden"/>, seats 2 and 3 <see cref="BotCommandSource"/> at its default dial, every step
/// through <see cref="Backfill.Step"/>), but started at the boss pull (<c>--pull N</c>, <see cref="HostRunFactory"/>)
/// with the boss swapped in (<see cref="HostRunSetup.Build"/> <c>bossEnemyId</c>). Every tick of the fight is kept, with
/// the commands that went in and the boss's target before the step, so a test can count each mechanic and its answer.
/// </summary>
internal static class BossArena
{
    public const int Key = 2;
    public const long Seed = 1;
    public const int BossId = HostRunSetup.BossActorId;

    /// <summary>R-BOSS-1 lower bound: 90 s.</summary>
    public const int MinFightTicks = 90 * Tick.TickHz;

    /// <summary>R-BOSS-1 upper bound: 180 s.</summary>
    public const int MaxFightTicks = 180 * Tick.TickHz;

    /// <summary>Hard stop: well past the upper bound.</summary>
    public const int DriveCapTicks = 6000;

    /// <summary>One stepped tick of the fight.</summary>
    /// <param name="Tick">The pre-step tick.</param>
    /// <param name="Commands">Every command fed in.</param>
    /// <param name="Events">The step's events.</param>
    /// <param name="BossTargetBefore">The boss's target before the step.</param>
    /// <param name="After">The state after the step.</param>
    public sealed record Step(int Tick, IReadOnlyList<Command> Commands, IReadOnlyList<SimEvent> Events, int? BossTargetBefore, SimState After);

    /// <summary>One drive: start and clear ticks, every step, and the run's players.</summary>
    public sealed record Fight(int Start, int? Clear, int EndTick, IReadOnlyList<Step> Steps, IReadOnlySet<int> Players, Run Run, HostRunSetup Setup)
    {
        /// <summary>Fight length in ticks, to the clear (null if the boss never died).</summary>
        public int? Ticks => Clear is { } c ? c - Start : null;

        /// <summary>Every event of the fight.</summary>
        public IEnumerable<SimEvent> Events => Steps.SelectMany(s => s.Events);

        /// <summary>Player deaths in the fight.</summary>
        public int PartyDeaths => Events.Count(e => e.Type == "death" && Int(e, "actorId") is { } id && Players.Contains((int)id));

        /// <summary>Casts of <paramref name="abilityId"/> by the boss that landed.</summary>
        public int Landed(string abilityId) =>
            Events.Count(e => e.Type == "cast_complete" && Int(e, "actorId") == BossId && Str(e, "abilityId") == abilityId);

        /// <summary>Kicks that stopped a boss cast of <paramref name="abilityId"/>.</summary>
        public int Kicked(string abilityId) =>
            Events.Count(e => e.Type == "interrupt" && Int(e, "targetId") == BossId && Str(e, "abilityId") == abilityId);

        /// <summary>
        /// Landed casts of <paramref name="abilityId"/> whose damage (raw at least the ability's own smallest damage op, so
        /// a same-tick hit of another cast is not counted) reached no player but the one the boss was aimed at before the
        /// step: the cone was turned away, the circle was left.
        /// </summary>
        public (int Landed, int Answered) AvoidedHits(string abilityId)
        {
            var floor = Run.Content.Abilities[abilityId].Effects.OfType<Keyrun.Sim.Combat.DamageOp>().Select(d => (long)d.Amount).DefaultIfEmpty(1).Min();
            int landed = 0, answered = 0;
            foreach (var s in Steps)
            {
                foreach (var e in s.Events.Where(e => e.Type == "cast_complete" && Int(e, "actorId") == BossId && Str(e, "abilityId") == abilityId))
                {
                    landed++;
                    var hitOthers = s.Events.Any(d => d.Type == "damage" && d.Tick == e.Tick && Int(d, "sourceId") == BossId &&
                        Int(d, "targetId") is { } tid && Players.Contains((int)tid) && tid != s.BossTargetBefore && Int(d, "rawAmount") >= floor);
                    if (!hitOthers)
                    {
                        answered++;
                    }
                }
            }
            return (landed, answered);
        }

        /// <summary>
        /// Staggers of Break bar <paramref name="barId"/> drained by interrupt- or control-tagged casts from at least two
        /// different players at the bar's actors since the previous stagger (the two-seat timing), and all staggers.
        /// </summary>
        public (int Staggers, int TwoSeat) Staggers(string barId)
        {
            var tags = Run.Content.Abilities.ToDictionary(kv => kv.Key, kv => kv.Value.Tags ?? (IReadOnlyList<string>)Array.Empty<string>());
            var adds = Setup.Boss.Twins.Select(t => t.ActorId).ToHashSet();
            var drainers = new HashSet<int>();
            int staggers = 0, two = 0;
            foreach (var s in Steps)
            {
                foreach (var cast in s.Commands.OfType<CastCommand>())
                {
                    if (Players.Contains(cast.ActorId) && cast.TargetId is { } t && adds.Contains(t) &&
                        tags.TryGetValue(cast.AbilityId, out var tg) && (tg.Contains("interrupt") || tg.Contains("control")))
                    {
                        drainers.Add(cast.ActorId);
                    }
                }
                foreach (var e in s.Events.Where(e => e.Type == "stagger" && Str(e, "barId") == barId))
                {
                    staggers++;
                    if (drainers.Count >= 2)
                    {
                        two++;
                    }
                    drainers.Clear();
                }
            }
            return (staggers, two);
        }

        /// <summary>
        /// Landed casts of <paramref name="abilityId"/> after which at least one player other than the boss's target stood
        /// within <paramref name="radiusMm"/> of the boss (the Undertow dragged someone into the ground the Surge boils), so
        /// escaping the Surge that follows is a real answer, not distance the party already kept.
        /// </summary>
        public int DraggedIn(string abilityId, long radiusMm)
        {
            var dragged = 0;
            foreach (var s in Steps)
            {
                if (!s.Events.Any(e => e.Type == "cast_complete" && Int(e, "actorId") == BossId && Str(e, "abilityId") == abilityId) ||
                    !s.After.Actors.TryGetValue(BossId, out var boss))
                {
                    continue;
                }
                var inside = s.After.Actors.Values.Any(a => Players.Contains(a.Id) && a.Alive && a.Id != s.BossTargetBefore &&
                    ((a.Pos.X - boss.Pos.X) * (a.Pos.X - boss.Pos.X)) + ((a.Pos.Z - boss.Pos.Z) * (a.Pos.Z - boss.Pos.Z)) <= radiusMm * radiusMm);
                if (inside)
                {
                    dragged++;
                }
            }
            return dragged;
        }

        /// <summary>Ticks the boss's adds died, for the PR line.</summary>
        public string AddDeaths => string.Join(",", Events.Where(e => e.Type == "death" && Int(e, "actorId") is { } id && Setup.Boss.Twins.Any(t => t.ActorId == id))
            .Select(e => $"{Int(e, "actorId")}@{e.Tick}"));

        /// <summary>Locks opened and broken.</summary>
        public (int Opened, int Broken) Locks => (Events.Count(e => e.Type == "lock_open"), Events.Count(e => e.Type == "lock_broken"));

        /// <summary>A summary line for the PR.</summary>
        public string Line(string boss) =>
            $"BOSS {boss} start={Start} clear={Clear?.ToString() ?? "-"} ticks={Ticks?.ToString() ?? "-"} ({(Ticks ?? 0) / (double)Tick.TickHz:0.0} s) end={EndTick} partyDeaths={PartyDeaths}";
    }

    /// <summary>Drives the default party against <paramref name="bossEnemyId"/> until it dies, the party wipes, or the cap.</summary>
    public static Fight Drive(string bossEnemyId, long seed = Seed)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, Lobby.DefaultClassId, withBots: true, bossEnemyId: bossEnemyId);
        var created = HostRunFactory.Create(setup, new RunRequest(seed, registry, Lobby.DefaultClassId) { StartPull = setup.Dungeon.Pulls.Count });
        var run = created.Run;
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var backfill = BackfillState.Empty;
        var players = new HashSet<int>(run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player).Select(a => a.Id));
        var start = run.State.Tick;
        int? clear = null;
        var steps = new List<Step>();
        var wiped = false;
        for (var i = 0; i < DriveCapTicks && run.RunEnd.Result is null && clear is null && !wiped; i++)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var bossTarget = run.State.Actors.TryGetValue(BossId, out var b) ? b.TargetId : null;
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            steps.Add(new Step(tick, commands, step.Step.Events, bossTarget, run.State));
            if (step.Step.Events.FirstOrDefault(e => e.Type == "death" && Int(e, "actorId") == BossId) is { } death)
            {
                clear = death.Tick;
            }
            wiped = players.All(id => !run.State.Actors.TryGetValue(id, out var p) || !p.Alive);
        }
        return new Fight(start, clear, run.State.Tick, steps, players, run, setup);
    }

    public static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    public static long? Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : null;
}
