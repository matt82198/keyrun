using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// SEED-BLOOM PATHING (R-PATH-1, G7 walk-around): <see cref="EncounterWindow.Decide"/> walks a bot toward an open
/// encounter floor Window (the Seed's Death Bloom) with a straight <see cref="Moves.MoveToward"/> aimed at the patch
/// centre. Run.cs slides a straight move off obstacles, so a bloom behind a wall stalls the bot. The bloom walk must take
/// the same routed step as the tank's approach (<see cref="PathMoves.Toward"/>), which goes around the obstacle. The
/// fixture is test-only: one hall with a pillar between the Warden and the bloom centre (the shipped layout has no
/// props), the same shape as <see cref="FollowPathingTests"/>.
/// </summary>
public sealed class SeedBloomPathsAroundObstacleTests
{
    private static readonly Bounds Hall = new(-5000, 25000, -6000, 6000);
    private static readonly Bounds Room2 = new(24000, 40000, -5000, 5000);
    private static readonly Bounds Pillar = new(8000, 12000, -4000, 4000);
    private static readonly Vec3 Warden = new(4000, 0, 0);
    private static readonly Vec3 BloomCentre = new(16000, 0, 0);
    private const int WardenId = 1;
    private const long BloomRadiusMm = 2000;

    private static NavLayout Layout() => new(
        new[] { new NavBox("hall", Hall, true, null), new NavBox("room-2", Room2, true, null) },
        new[] { new NavTrigger("p1", new Bounds(30000, 40000, -5000, 5000)) },
        new[] { new Vec3(24500, 0, 0), new Vec3(35000, 0, 0) })
    {
        Obstacles = new[] { Pillar },
    };

    private static Run Create(NavLayout layout)
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
        var party = new[] { new PartyMemberSpec(WardenId, "warden", ActorRole.Tank, Warden, 400, 100) };
        return RunRoot.CreateRun(1, party, dungeon, content);
    }

    [Fact(DisplayName = "G7 R-PATH-1: a seed-bloom walk routes around the pillar between the Warden and the bloom, as the tank approach does")]
    public void Seed_bloom_walk_paths_around_the_obstacle()
    {
        var layout = Layout();
        var run = Create(layout);
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
        var kits = KitRegistry.From(Keyrun.Content.ContentLoader.Load());
        var view = BotViews.BuildBotView(state, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = layout };
        var self = state.Actors[WardenId];

        var commands = EncounterWindow.Decide(view, self);

        Assert.True(commands is not null, "EncounterWindow.Decide returned null: the Warden has no usable Payoff for the bloom, so the fixture is wrong, not the walk");
        var move = Assert.IsType<MoveCommand>(Assert.Single(commands!));

        var straight = Moves.MoveToward(state.Tick, WardenId, PointMm.Of(self.Pos), PointMm.Of(BloomCentre))!;
        var routed = PathMoves.Toward(view, state.Tick, self, BloomCentre);
        Assert.True(routed is not null, "PathMoves.Toward returned null for a bloom centre outside the Warden's reach");
        Assert.True(
            straight.Dir != routed!.Dir,
            $"precondition: the straight move and the routed move must differ across the pillar, both were {straight.Dir}");

        Assert.True(
            move.Dir == routed.Dir,
            $"the bloom walk must take the routed step {routed.Dir} (PathMoves.Toward around the pillar at x 8000..12000), but Decide returned {move.Dir} (the straight step {straight.Dir} runs into the pillar)");
    }
}
