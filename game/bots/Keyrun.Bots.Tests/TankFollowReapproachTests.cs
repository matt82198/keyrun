using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Probe for the suspected bot tank flip-flop (static recon, unproven): a Warden seat standing just inside its leader's room
/// inset (<see cref="Follow.FightInsetMm"/>, 1500 mm) with one engaged enemy more than 5000 mm away, outside that room.
/// Inside the inset <see cref="Follow.FollowCommand"/> returns null, so the role runs and <c>tank:melee-reapproach</c> steps
/// the Warden toward the enemy, out of the inset. Outside the inset <c>bot:follow-route</c> walks it back in. If the two
/// alternate, the tank oscillates. Ten consecutive decisions are run, each one applying the issued move (180 mm per tick,
/// <c>BaseSpeedMmPerTick</c>) to the Warden before the next decision.
/// </summary>
public sealed class TankFollowReapproachTests
{
    /// <summary>Consecutive decisions run per scenario.</summary>
    private const int Decisions = 10;

    /// <summary>The leader seat: lowest living seat in the party, standing in the room.</summary>
    private const int LeaderId = 1;

    /// <summary>The Warden under test.</summary>
    private const int WardenId = 2;

    /// <summary>The engaged enemy: outside the room, targeting the Warden.</summary>
    private const int EngagedId = 100;

    /// <summary>Reason tag of the follow walk (Bot.cs).</summary>
    private const string FollowTag = "bot:follow-route";

    /// <summary>Per-tick travel step, mm (RunContent.BaseSpeedMmPerTick).</summary>
    private const long StepMm = 180;

    /// <summary>Corridor (outside the room); its x-max overlaps the room by 1000 mm along the doorway.</summary>
    private static readonly Bounds Corridor = new(0, 21000, -1000, 1000);

    /// <summary>The leader's room. Its inset (1500 mm inside every wall) is x 21500..38500, z -3500..3500.</summary>
    private static readonly Bounds Room = new(20000, 40000, -5000, 5000);

    /// <summary>The Warden: inside the room inset, 100 mm past its edge, so one 180 mm step toward the enemy leaves the inset.</summary>
    private static readonly Vec3 WardenStart = new(21600, 0, 0);

    /// <summary>The engaged enemy: 7600 mm from the Warden, in the corridor, outside the room.</summary>
    private static readonly Vec3 EnemyAt = new(14000, 0, 0);

    /// <summary>The leader: in the room, deep inside it.</summary>
    private static readonly Vec3 LeaderAt = new(30000, 0, 0);

    private static NavLayout Layout() => new(
        new[] { new NavBox("corridor", Corridor, true, null), new NavBox("room", Room, true, null) },
        new[] { new NavTrigger("p1", Room) },
        new[] { new Vec3(21000, 0, 0), new Vec3(30000, 0, 0) });

    private static Run Create(NavLayout layout)
    {
        var dungeon = new DungeonDef("nav", Array.Empty<PullDef>());
        var content = new RunContent
        {
            Abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal),
            Effects = new Dictionary<string, EffectDef>(StringComparer.Ordinal),
            LockoutTicks = 40,
            BaseSpeedMmPerTick = 180,
            ActorRadiusMm = 400,
            Bounds = layout.Extent,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>(),
            Layout = layout,
        };
        var party = new[]
        {
            new PartyMemberSpec(LeaderId, "chanter", ActorRole.Healer, LeaderAt, 300, 100),
            new PartyMemberSpec(WardenId, "warden", ActorRole.Tank, WardenStart, 400, 100),
        };
        return RunRoot.CreateRun(1, party, dungeon, content);
    }

    /// <summary>Reason tag of the first move in <paramref name="decision"/>, or "none" when it issues no move.</summary>
    private static string MoveTagOf(BotDecision decision)
    {
        var reasons = Reasons.OfAll(decision.Commands);
        for (var i = 0; i < decision.Commands.Count; i++)
        {
            if (decision.Commands[i] is MoveCommand)
            {
                return reasons[i].RuleId;
            }
        }
        return "none";
    }

    [Fact(DisplayName = "Tank flip-flop probe: a Warden inside its leader's room inset with an engaged enemy outside the room does not alternate follow-route and melee-reapproach, and its move direction never reverses")]
    public void Warden_inside_leader_inset_does_not_alternate_follow_route_and_reapproach()
    {
        var layout = Layout();
        var run = Create(layout);
        var state = run.State;
        var enemy = Actors.Create(EngagedId, ActorKind.Enemy, "pair.melee", ActorRole.None, EnemyAt, 1000, 0);
        enemy.TargetId = WardenId;
        state.Actors[EngagedId] = enemy;
        var warden = state.Actors[WardenId];

        var kits = KitRegistry.From(Keyrun.Content.ContentLoader.Load());
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);

        var tags = new List<string>();
        var dirs = new List<Dir2>();
        for (var n = 0; n < Decisions; n++)
        {
            var view = BotViews.BuildBotView(state, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = layout };
            var decision = bot.Decide(view, memory, WardenId, prng);
            memory = decision.Memory;
            prng = decision.Prng;

            tags.Add(MoveTagOf(decision));
            var move = BotTestKit.FirstMove(decision.Commands);
            if (move is not null)
            {
                dirs.Add(move.Dir);
            }
            BotTestKit.SetPos(warden, BotTestKit.ApplyMove(new PointMm(warden.Pos.X, warden.Pos.Z), move, StepMm));
            state.Tick++;
        }

        var sequence = string.Join(", ", tags);
        // R-TANKFLIP-1 (3): no alternation. Among the engage and follow-route tags, at most one transition may occur.
        var engageOrFollow = tags.Where(t => t == "tank:engage" || t == FollowTag).ToList();
        var transitions = 0;
        for (var i = 1; i < engageOrFollow.Count; i++)
        {
            if (engageOrFollow[i] != engageOrFollow[i - 1])
            {
                transitions++;
            }
        }
        Assert.True(transitions <= 1,
            $"tank:engage and bot:follow-route alternate ({transitions} transitions, at most 1 allowed) " +
            $"(tag sequence over {Decisions} decisions: {sequence})");

        for (var i = 1; i < dirs.Count; i++)
        {
            var prev = dirs[i - 1];
            var cur = dirs[i];
            var reversed = Math.Sign(prev.X) * Math.Sign(cur.X) < 0 || Math.Sign(prev.Z) * Math.Sign(cur.Z) < 0;
            Assert.False(reversed,
                $"the Warden's move direction reverses between consecutive moves: ({prev.X},{prev.Z}) then ({cur.X},{cur.Z}) " +
                $"(tag sequence over {Decisions} decisions: {sequence})");
        }
    }
}
