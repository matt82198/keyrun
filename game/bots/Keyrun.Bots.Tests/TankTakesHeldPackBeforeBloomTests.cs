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
/// R-PATH-5 (y) and R-BOTTANK-LEASH-1: <see cref="Bot"/> runs <see cref="EncounterWindow.Decide"/> before the tank's
/// <see cref="TankEngage.ApproachMove"/>. A seed-bloom walk keeps priority while it makes progress (BIBLE s4 synergy
/// window). A Warden whose bloom walk is stuck (position unchanged for <see cref="StuckTicks"/> consecutive decisions) drops
/// the bloom when its engage target is a pack already held by another living party seat, and takes the TankEngage approach
/// on the call after that. An idle (unheld) pack never pulls the tank ahead of the bloom walk. Fixture copied from
/// <see cref="SeedBloomPathsAroundObstacleTests"/>: one hall with a pillar between the Warden and the bloom centre, the
/// seed-bloom FloorWindow open, plus a second party seat (id 2, the holder) and one enemy placed off the bloom's direction.
/// Each decision's returned memory and PRNG are fed back into the next call, as the runner does.
/// </summary>
public sealed class TankTakesHeldPackBeforeBloomTests
{
    /// <summary>Unchanged-position decisions before a held-pack tank drops the bloom (R-PATH-5 (y)).</summary>
    private const int StuckTicks = 10;

    private static readonly Bounds Hall = new(-5000, 25000, -6000, 6000);
    private static readonly Bounds Room2 = new(24000, 40000, -5000, 5000);
    private static readonly Bounds Pillar = new(8000, 12000, -4000, 4000);
    private static readonly Vec3 Warden = new(4000, 0, 0);
    private static readonly Vec3 Cinder = new(2000, 0, 1500);
    private static readonly Vec3 HeldPack = new(4000, 0, -2800);
    private static readonly Vec3 BloomCentre = new(16000, 0, 0);
    private const int WardenId = 1;
    private const int HolderId = 2;
    private const int EnemyId = 150;
    private const long BloomRadiusMm = 2000;

    private static NavLayout Layout() => new(
        new[] { new NavBox("hall", Hall, true, null), new NavBox("room-2", Room2, true, null) },
        new[] { new NavTrigger("p1", new Bounds(30000, 40000, -5000, 5000)) },
        new[] { new Vec3(24500, 0, 0), new Vec3(35000, 0, 0) })
    {
        Obstacles = new[] { Pillar },
    };

    private static Run Create(NavLayout layout, Vec3 wardenAt)
    {
        var dungeon = new DungeonDef("nav", new[]
        {
            new PullDef("p1", new[] { new PullSpawn(101, "dummy", new Vec3(38000, 0, 4000), 0, 10) }),
        });
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
            new PartyMemberSpec(WardenId, "warden", ActorRole.Tank, wardenAt, 400, 100),
            new PartyMemberSpec(HolderId, "cinder", ActorRole.Damage, Cinder, 250, 100),
        };
        return RunRoot.CreateRun(1, party, dungeon, content);
    }

    /// <summary>
    /// The bot view of the seed-bloom hall. <paramref name="held"/> true: the enemy is live, engaged and locked on seat 2
    /// (HolderId). False: the enemy is idle with no holder (a fresh pack). <paramref name="wardenAt"/> places the Warden
    /// (default: <see cref="Warden"/>).
    /// </summary>
    private static BotView Fixture(bool held, Vec3? wardenAt = null, int holderId = HolderId)
    {
        var layout = Layout();
        var run = Create(layout, wardenAt ?? Warden);
        var state = run.State;
        state.FloorWindows["seedbloom"] = new FloorWindow
        {
            WindowId = "seedbloom",
            SetupClass = EncounterWindow.EncounterSetupClass,
            SetupActorId = 101,
            Pos = BloomCentre,
            EndsTick = state.Tick + 1000,
            RadiusMm = BloomRadiusMm,
        };
        var enemy = Actors.Create(EnemyId, ActorKind.Enemy, "test.brute", ActorRole.None, HeldPack, 1000, 0);
        enemy.Idle = !held;
        enemy.HolderId = held ? holderId : null;
        state.Actors[EnemyId] = enemy;
        var kits = KitRegistry.From(Keyrun.Content.ContentLoader.Load());
        return BotViews.BuildBotView(state, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = layout };
    }

    /// <summary>
    /// Feeds the Warden's decision <paramref name="calls"/> times on the same view, threading memory and PRNG, and returns
    /// the decision made on the call after them (call number <paramref name="calls"/> + 1).
    /// </summary>
    private static BotDecision DecideAfterStuckTicks(BotView view, int calls)
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        for (var i = 0; i < calls; i++)
        {
            var step = bot.Decide(view, memory, WardenId, prng);
            memory = step.Memory;
            prng = step.Prng;
        }
        return bot.Decide(view, memory, WardenId, prng);
    }

    [Fact(DisplayName = "R-PATH-5 (y): a held-pack tank whose bloom walk is stuck for 10 ticks takes the TankEngage approach on the 11th")]
    public void Tank_takes_held_pack_approach_after_stuck_bloom()
    {
        var view = Fixture(held: true);
        var self = view.State.Actors[WardenId];
        var approach = TankEngage.ApproachMove(view, self, RoleShared.KitOf(view, self.ClassId));
        Assert.True(
            approach is not null,
            "precondition: TankEngage.ApproachMove returned null for the held pack 2800 mm off the Warden, so the fixture is wrong, not the rule");

        var call = DecideAfterStuckTicks(view, StuckTicks);
        var move = Assert.Single(call.Commands.OfType<MoveCommand>().ToList());

        Assert.True(
            move.Dir == approach!.Dir,
            $"after {StuckTicks} unchanged-position bloom ticks the tank must take the TankEngage approach {approach.Dir} toward the held pack on tick {StuckTicks + 1}, but Decide returned {move.Dir} (the seed-bloom step toward the bloom centre at x 16000)");
    }

    [Fact(DisplayName = "R-PATH-5 (y): a held-pack tank whose bloom walk makes progress keeps the bloom Dir")]
    public void Held_pack_progressing_bloom_walk_keeps_priority()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        Vec3? previous = null;
        BotView view = Fixture(held: true, wardenAt: Warden);
        for (var i = 0; i <= StuckTicks; i++)
        {
            // The Warden advances 100 mm along x between every decision, so the bloom walk is never stuck.
            var at = new Vec3(4000 + 100 * i, 0, 0);
            view = Fixture(held: true, wardenAt: at);
            if (previous is { } prev)
            {
                Assert.NotEqual(prev, at);
            }
            previous = at;
            var step = bot.Decide(view, memory, WardenId, prng);
            memory = step.Memory;
            prng = step.Prng;
        }

        var self = view.State.Actors[WardenId];
        var bloom = PathMoves.Toward(view, view.State.Tick, self, BloomCentre);
        Assert.True(bloom is not null, "precondition: PathMoves.Toward returned null for the bloom centre, so the fixture is wrong, not the rule");

        var call = bot.Decide(view, memory, WardenId, prng);
        var move = Assert.Single(call.Commands.OfType<MoveCommand>().ToList());

        Assert.True(
            move.Dir == bloom!.Dir,
            $"a progressing bloom walk must keep priority over a held pack: expected the bloom step {bloom.Dir} on tick {StuckTicks + 1} (the Warden moved on every one of the {StuckTicks + 1} decisions), Decide returned {move.Dir}");
    }

    [Fact(DisplayName = "R-BOTTANK-LEASH-1: an idle pack does not pull the tank ahead of a stuck seed-bloom walk")]
    public void Idle_pack_leaves_the_bloom_walk_ahead()
    {
        var view = Fixture(held: false);
        var self = view.State.Actors[WardenId];
        var bloom = PathMoves.Toward(view, view.State.Tick, self, BloomCentre);
        Assert.True(bloom is not null, "precondition: PathMoves.Toward returned null for the bloom centre, so the fixture is wrong, not the rule");

        var call = DecideAfterStuckTicks(view, StuckTicks);
        var move = Assert.Single(call.Commands.OfType<MoveCommand>().ToList());

        Assert.True(
            move.Dir == bloom!.Dir,
            $"an idle pack must not pull the tank ahead of the bloom walk: after {StuckTicks} unchanged-position bloom ticks, tick {StuckTicks + 1} expected the bloom step {bloom.Dir}, Decide returned {move.Dir}");
    }

    /// <summary>The second Warden position for the oscillation fact: 1 mm along x from <see cref="Warden"/>.</summary>
    private static readonly Vec3 WardenOneMmAside = new(4001, 0, 0);

    /// <summary>The TankEngage approach the Warden takes on <paramref name="view"/> (null only if the fixture is wrong).</summary>
    private static MoveCommand? ApproachOn(BotView view)
    {
        var self = view.State.Actors[WardenId];
        return TankEngage.ApproachMove(view, self, RoleShared.KitOf(view, self.ClassId));
    }

    /// <summary>Printable Dir for a failure message; "none" when the move is null.</summary>
    private static string DirText(MoveCommand? move) => move is null ? "none" : move.Dir.ToString();

    /// <summary>
    /// The 1-based decision number on which R-PATH-5 (z) drops a walk fed <paramref name="positions"/> in order (decision n is
    /// made at positions[(n - 1) % positions.Length]): the <see cref="StuckTicks"/>th decision after the last new best, where a
    /// new best is a strictly smaller squared distance to <see cref="BloomCentre"/> than every earlier decision's.
    /// </summary>
    private static int DropDecisionFor(Vec3[] positions)
    {
        long best = long.MaxValue;
        var lastNewBest = 0;
        for (var n = 1; ; n++)
        {
            var at = positions[(n - 1) % positions.Length];
            var dx = at.X - BloomCentre.X;
            var dy = at.Y - BloomCentre.Y;
            var dz = at.Z - BloomCentre.Z;
            var d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < best)
            {
                best = d2;
                lastNewBest = n;
            }
            if (n - lastNewBest == StuckTicks)
            {
                return n;
            }
        }
    }

    [Fact(DisplayName = "R-PATH-5 (z): a bloom walk that oscillates 1 mm between two positions is stuck 10 decisions after its last new best, and that drop decision takes the TankEngage approach")]
    public void Oscillating_bloom_walk_is_stuck_and_takes_held_pack_approach()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var positions = new[] { Warden, WardenOneMmAside };
        // R-PATH-5 (z): decision 2 (x 4001) is a new best, so the drop is the 10th no-progress decision after it. The drop
        // decision is computed from the fixture's positions, not hardcoded.
        var drop = DropDecisionFor(positions);
        for (var n = 1; n < drop; n++)
        {
            var step = bot.Decide(Fixture(held: true, wardenAt: positions[(n - 1) % positions.Length]), memory, WardenId, prng);
            memory = step.Memory;
            prng = step.Prng;
        }

        var view = Fixture(held: true, wardenAt: positions[(drop - 1) % positions.Length]);
        var approach = ApproachOn(view);
        Assert.True(approach is not null, "precondition: TankEngage.ApproachMove returned null for the held pack on the oscillation view, so the fixture is wrong, not the rule");
        var self = view.State.Actors[WardenId];
        var bloom = PathMoves.Toward(view, view.State.Tick, self, BloomCentre);
        Assert.True(bloom is not null, "precondition: PathMoves.Toward returned null for the bloom centre on the oscillation view, so the fixture is wrong, not the rule");
        Assert.True(
            approach!.Dir != bloom!.Dir,
            $"precondition: the approach Dir {approach.Dir} equals the bloom Dir {bloom.Dir} on the oscillation view, so the fact cannot tell them apart");

        var call = bot.Decide(view, memory, WardenId, prng);
        var move = Assert.Single(call.Commands.OfType<MoveCommand>().ToList());

        Assert.True(
            move.Dir == approach.Dir,
            $"an oscillating bloom walk (1 mm between two positions) is stuck: decision {drop} must take the TankEngage approach Dir {approach.Dir} toward the held pack, but Decide returned {move.Dir} (the seed-bloom Dir is {bloom.Dir})");
    }

    [Fact(DisplayName = "R-PATH-5 (z): after the drop the TankEngage approach persists toward the held pack until the tank holds it")]
    public void Approach_persists_after_drop_until_tank_holds_pack()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var fixedView = Fixture(held: true);
        for (var i = 0; i < StuckTicks; i++)
        {
            var step = bot.Decide(fixedView, memory, WardenId, prng);
            memory = step.Memory;
            prng = step.Prng;
        }

        // The drop of (a): decision 11 on the same view takes the approach.
        var dropApproach = ApproachOn(fixedView);
        Assert.True(dropApproach is not null, "precondition: TankEngage.ApproachMove returned null on the fixed view, so the fixture is wrong, not the rule");
        var drop = bot.Decide(fixedView, memory, WardenId, prng);
        var dropMove = Assert.Single(drop.Commands.OfType<MoveCommand>().ToList());
        Assert.True(
            dropMove.Dir == dropApproach!.Dir,
            $"precondition for (e): decision {StuckTicks + 1} must be the drop of (a) and take the approach Dir {dropApproach.Dir}, but Decide returned {dropMove.Dir}");
        memory = drop.Memory;
        prng = drop.Prng;

        // The walk closes 50 mm per decision straight at the held pack (x 4000, z -2800) and stays outside TankEngage's reach:
        // ApproachMove is null at distance <= DefaultReachMm - ReachMarginMm, so every step sits at least 1 mm outside that boundary.
        var reachBoundaryMm = TankEngage.DefaultReachMm - TankEngage.ReachMarginMm;
        const long approachStepMm = 50;
        var index = StuckTicks + 1;
        for (var k = 1; k <= 5; k++)
        {
            index++;
            var distMm = reachBoundaryMm + 1 + (5 - k) * approachStepMm;
            var view = Fixture(held: true, wardenAt: new Vec3(4000, 0, HeldPack.Z + distMm));
            var approach = ApproachOn(view);
            Assert.True(approach is not null, $"precondition: TankEngage.ApproachMove returned null on decision {index} (Warden {distMm} mm from the held pack), so the fixture is wrong, not the rule");
            var step = bot.Decide(view, memory, WardenId, prng);
            var move = Assert.Single(step.Commands.OfType<MoveCommand>().ToList());
            var self = view.State.Actors[WardenId];
            var bloom = PathMoves.Toward(view, view.State.Tick, self, BloomCentre);
            Assert.True(
                move.Dir == approach!.Dir,
                $"the approach persists after the drop until the tank holds the pack: decision {index} (Warden {distMm} mm from the held pack, pack still held by seat {HolderId}) must return the approach Dir {approach.Dir}, but Decide returned {move.Dir} (the seed-bloom Dir is {DirText(bloom)})");
            memory = step.Memory;
            prng = step.Prng;
        }

        // Last step: the Warden IS the holder. The held-pack rule no longer applies (HeldPackApproach is null for a seat that
        // holds the pack), the open bloom window is the normal logic, so the decision is the bloom Dir with no
        // bot:tank-approach reason tag (the tag the held-pack drop emits).
        index++;
        var holderView = Fixture(held: true, wardenAt: new Vec3(4000, 0, -500), holderId: WardenId);
        var holderSelf = holderView.State.Actors[WardenId];
        var holderBloom = PathMoves.Toward(holderView, holderView.State.Tick, holderSelf, BloomCentre);
        Assert.True(holderBloom is not null, "precondition: PathMoves.Toward returned null for the bloom centre on the holder view, so the fixture is wrong, not the rule");
        var holderStep = bot.Decide(holderView, memory, WardenId, prng);
        var holderMove = Assert.Single(holderStep.Commands.OfType<MoveCommand>().ToList());
        var reasonTags = Reasons.OfAll(holderStep.Commands).Select(r => r.RuleId).ToList();
        Assert.False(
            reasonTags.Contains("bot:tank-approach"),
            $"decision {index}: the Warden holds the pack, so the held-pack approach must not fire, but the reason tags were [{string.Join(", ", reasonTags)}] (the approach Dir was {DirText(ApproachOn(holderView))}, the seed-bloom Dir {DirText(holderBloom)}, Decide returned {holderMove.Dir})");
        Assert.True(
            holderMove.Dir == holderBloom!.Dir,
            $"decision {index}: the Warden holds the pack, so the normal logic gives the open seed-bloom walk Dir {holderBloom.Dir}, but Decide returned {holderMove.Dir} (the approach Dir was {DirText(ApproachOn(holderView))})");
    }

    [Fact(DisplayName = "R-PATH-5 (z): with the held pack recorded and the Warden inside reach, Decide does not return the bloom-walk move")]
    public void Held_pack_recorded_inside_reach_yields_the_bloom_walk()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);

        // The stuck bloom walk drops on decision StuckTicks + 1 (R-PATH-5 (z)) and takes the held-pack approach.
        var stuckView = Fixture(held: true);
        for (var i = 0; i < StuckTicks; i++)
        {
            var step = bot.Decide(stuckView, memory, WardenId, prng);
            memory = step.Memory;
            prng = step.Prng;
        }
        var dropApproach = ApproachOn(stuckView);
        Assert.True(dropApproach is not null, "precondition: TankEngage.ApproachMove returned null on the stuck view, so the fixture is wrong, not the rule");
        var drop = bot.Decide(stuckView, memory, WardenId, prng);
        var dropMove = Assert.Single(drop.Commands.OfType<MoveCommand>().ToList());
        Assert.True(
            dropMove.Dir == dropApproach!.Dir,
            $"precondition for (f): decision {StuckTicks + 1} must drop the bloom walk and take the approach Dir {dropApproach.Dir} (recording the held pack), but Decide returned {dropMove.Dir}");
        memory = drop.Memory;
        prng = drop.Prng;

        // The Warden now stands 1000 mm from the held pack: inside TankEngage's reach, so ApproachMove is null.
        var inReach = Fixture(held: true, wardenAt: new Vec3(4000, 0, HeldPack.Z + 1000));
        Assert.True(ApproachOn(inReach) is null, "precondition: TankEngage.ApproachMove returned a move inside reach, so the fixture is wrong, not the rule");
        var self = inReach.State.Actors[WardenId];
        var bloom = PathMoves.Toward(inReach, inReach.State.Tick, self, BloomCentre);
        Assert.True(bloom is not null, "precondition: PathMoves.Toward returned null for the bloom centre on the in-reach view, so the fixture is wrong, not the rule");

        var call = bot.Decide(inReach, memory, WardenId, prng);
        var bloomMoves = call.Commands.OfType<MoveCommand>().Where(m => m.Dir == bloom!.Dir).ToList();
        Assert.True(
            bloomMoves.Count == 0,
            $"with the held pack recorded and the Warden inside reach, the bloom branch must yield: Decide returned the seed-bloom move {bloom!.Dir} (R-PATH-5 (z) bloom branch yields while a held pack is recorded)");
    }
}
