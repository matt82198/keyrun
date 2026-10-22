using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Keyrun.Bots;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// PATH-PACKS Hall Sentries are an OPTIONAL pack off the walking line (ruling G5 2026-10-21, MVP-MAP U8; layout.json
/// <c>hall-sentries</c>, optional, in pair-hall at (51500, 4500), clear of the walking line at z 0). A default run walks
/// the route and never engages them. A party that pulls the pack (<see cref="PullingHallPack"/>: the leader walks into the
/// pack's aggro ring after the Pair is cleared) fights the five sentries like Mythic+ trash: a kicking party kills every
/// one of them, and each sentry's swing timer runs on its own. The never-kick plan's failure is pinned by
/// <see cref="ChoirLessonTests"/>.
/// </summary>
public sealed class HallSentriesTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    // The same competent kick plan ChoirLessonTests plays (STATE.md D48: the tank mitigates as the scripted tank does).
    private const string KickPlan = "{\"mitigate\": [\"warden\"], \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";

    private const string HallPackId = "hall-sentries";

    private static PlaytestResult Play(string? policyJson) =>
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policyJson is null ? null : PolicyReader.Parse(policyJson, Registry)), Registry);

    private static IReadOnlyList<int> Sentries(PlaytestResult r) =>
        r.Setup.Content.Packs!.Single(p => p.Id == HallPackId).Spawns.Select(s => s.ActorId).ToList();

    private static HashSet<int> DeadIn(string log) => log.Split('\n')
        .Where(l => l.Contains("\"type\":\"death\""))
        .Select(l => int.Parse(Regex.Match(l, "\"actorId\":(\\d+)").Groups[1].Value))
        .ToHashSet();

    /// <summary>The run's first swing tick per sentry (-1 when it never swung).</summary>
    private static Dictionary<int, int> FirstSwings(string log, IReadOnlyList<int> sentries)
    {
        var lines = log.Split('\n');
        return sentries.ToDictionary(id => id, id => lines
            .Where(l => l.Contains("\"type\":\"cast_complete\"") && l.Contains($"\"actorId\":{id},") && l.Contains("\"abilityId\":\"choir.meleeSwing\""))
            .Select(l => int.Parse(Regex.Match(l, "\"tick\":(\\d+)").Groups[1].Value))
            .DefaultIfEmpty(-1)
            .Min());
    }

    [Fact(DisplayName = "Hall Sentries: the default route never engages them (an optional pack off the walking line, G5)")]
    public void Default_route_does_not_engage_the_hall_sentries()
    {
        foreach (var r in new[] { Play(KickPlan), Play(null) })
        {
            var sentries = Sentries(r);
            // R-PINS-1: a pack of several sentries (the loops below are vacuous on an empty or one-member pack), not the literal 5.
            Assert.True(sentries.Count >= 2, $"the Hall Sentries are a pack of several; content gave {sentries.Count}");
            Assert.DoesNotContain($"\"packId\":\"{HallPackId}\"", r.Log);
            var swings = FirstSwings(r.Log, sentries);
            Assert.True(swings.Values.All(t => t < 0), "a Hall Sentry swung on the default route: " + string.Join(", ", swings.Select(kv => $"{kv.Key}@{kv.Value}")));
            Assert.Empty(DeadIn(r.Log).Intersect(sentries));
        }
    }

    [Fact(DisplayName = "Hall Sentries: a party that pulls the pack and kicks Choral Mending kills every Hall Sentry (a kick plan and the scripted party)")]
    public void Kicking_party_kills_every_hall_sentry()
    {
        foreach (var r in new[] { PullingHallPack(KickPlan), PullingHallPack(null) })
        {
            var members = Sentries(r);
            Assert.True(members.Count >= 2, $"the Hall Sentries are a pack of several; content gave {members.Count}");
            var dead = DeadIn(r.Log);
            var alive = members.Where(id => !dead.Contains(id)).ToList();
            Assert.True(alive.Count == 0,
                $"{alive.Count} of {members.Count} Hall Sentries never died (alive: {string.Join(", ", alive)}); run ended {r.Run.RunEnd.Result}");
        }
    }

    [Fact(DisplayName = "Hall Sentries: each sentry's swing timer runs on its own, so the five first swings land on five distinct ticks")]
    public void Sentry_first_swings_land_on_distinct_ticks()
    {
        var r = PullingHallPack(KickPlan);
        var members = Sentries(r);
        var first = FirstSwings(r.Log, members);
        var timeline = string.Join(", ", first.Select(kv => $"{kv.Key}@{kv.Value}"));
        Assert.True(first.Values.All(t => t >= 0), $"a Hall Sentry never swung: {timeline}");
        Assert.True(first.Values.Distinct().Count() == members.Count, $"Hall Sentry first swings share a tick: {timeline}");
    }

    /// <summary>
    /// The optional-pack pull: the same run <see cref="PlaytestRunner.Play"/> builds (HostRunSetup, the shipped layout,
    /// BossRules.Arm, the seats driven as PlaytestRunner drives them), except that once the leader stands in the pair-hall
    /// room with no engaged enemy alive, it walks to the Hall Sentries' pack centre instead of the route. The pack engages
    /// on the sim's own aggro ring (the leader stops 300 mm from the centre, inside its 3000 mm ring). Once the sentries
    /// are dead the walk returns to the route. The leader's fight handling is PlaytestRunner's, line for line.
    /// </summary>
    private static PlaytestResult PullingHallPack(string? policyJson)
    {
        var policy = policyJson is null ? null : PolicyReader.Parse(policyJson, Registry);
        var registry = policy is null ? Registry : PlaytestRunner.WithTalents(Registry, policy);
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, HostRunSetup.Roster[0].ClassId, withBots: true);
        var created = RunRoot.CreateRun(1, setup.Party, setup.Dungeon, setup.Content, null);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var kits = KitRegistry.From(registry);
        var pack = setup.Content.Packs!.Single(p => p.Id == HallPackId);
        var sentries = pack.Spawns.Select(s => s.ActorId).ToList();

        var bots = new SortedDictionary<int, IBot>();
        var memories = new SortedDictionary<int, BotMemory>();
        var policyBots = new Dictionary<int, PolicyBot>();
        var context = new PolicyContext();
        var scripted = Bot.MakeBot(RoleSet.Default);
        foreach (var p in setup.Party)
        {
            if (policy is null)
            {
                bots[p.Id] = scripted;
            }
            else
            {
                var pb = new PolicyBot(policy, context, registry, 1, p.Id, PlaytestRunner.TankId);
                policyBots[p.Id] = pb;
                bots[p.Id] = pb;
            }
            memories[p.Id] = BotMemory.Default(PlaytestRunner.ScriptedSkillDial);
        }
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(1);
        var maxTicks = 2 * (setup.Content.ParTicks ?? 24000);
        var log = new StringBuilder();

        while (run.State.Tick < maxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            context.PullId = PlaytestRunner.PullIdOf(run);
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();
            if (Follow.Leader(run.State) is { } leader)
            {
                var leaderId = leader.Id;
                var dodge = policyBots.TryGetValue(leaderId, out var lb) ? lb.LastDodge : null;
                if (!Follow.AnyLivingEnemy(run.State))
                {
                    commands.RemoveAll(c => c.ActorId == leaderId);
                    var walk = PackPullWalk(tick, leader, run, sentries, pack.Center);
                    if (walk is null)
                    {
                        walk = Follow.LeadRoute(tick, leader, run);
                    }
                    if (walk is not null)
                    {
                        commands.Add(walk);
                    }
                }
                else
                {
                    // PlaytestRunner's fight rule: the leader keeps its casts, its dodge and its tank approach move.
                    var approach = leader.Role == ActorRole.Tank
                        ? TankEngage.ApproachMove(view, leader, view.Kits.ResolveKit(leader.ClassId, view.ClassKit))
                        : null;
                    commands.RemoveAll(c => c.ActorId == leaderId && c is MoveCommand or DashCommand && !Equals(c, dodge) && !Equals(c, approach));
                }
            }

            foreach (var c in commands)
            {
                log.Append("C ").Append(c).Append('\n');
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                log.Append("E ").Append(e.ToCanonicalJson()).Append('\n');
            }
        }
        return new PlaytestResult(run, log.ToString(), new RunStats(registry, setup.Party), setup, registry);
    }

    /// <summary>
    /// The leader's walk toward the optional pack while it stands in pair-hall, no enemy is engaged, and a sentry lives;
    /// null otherwise (the route takes over).
    /// </summary>
    private static MoveCommand? PackPullWalk(int tick, ActorState leader, Run run, IReadOnlyList<int> sentries, Keyrun.Sim.Core.Vec3 centre)
    {
        var layout = run.Content.Layout!;
        var hall = layout.Boxes.First(b => b.Id == "pair-hall");
        if (!Movement.Contains(hall.Box, leader.Pos) || !sentries.Any(id => run.State.Actors.TryGetValue(id, out var s) && s.Alive))
        {
            return null;
        }
        return Follow.WalkTo(tick, leader, layout, centre);
    }
}
