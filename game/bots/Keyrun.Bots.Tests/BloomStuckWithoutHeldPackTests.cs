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
/// Probe: a Warden walking to the Seed bloom with NO pack held by another party seat must not keep emitting
/// <c>bot:seed-bloom</c> forever once its bloom walk stops making progress (R-PATH-5 (z) BloomStuckLimit). Today the
/// drop branch only fires when <see cref="Bot"/> finds a held pack, so with every pack unheld the no-progress counter
/// climbs and the walk is never dropped. The fixture is the oscillating-position hall from
/// <see cref="TankTakesHeldPackBeforeBloomTests"/> with the enemy left idle and unheld (HolderId null) and the second
/// party seat (id 2) present but holding nothing.
/// </summary>
public sealed class BloomStuckWithoutHeldPackTests
{
    /// <summary>R-PATH-5 (z): no-progress decisions in a row that drop a bloom walk.</summary>
    private const int BloomStuckLimit = 10;

    /// <summary>The reason tag the bloom walk emits (EncounterWindow bloom move, Bot.cs).</summary>
    private const string BloomTag = "bot:seed-bloom";

    private static readonly Bounds Hall = new(-5000, 25000, -6000, 6000);
    private static readonly Bounds Room2 = new(24000, 40000, -5000, 5000);
    private static readonly Bounds Pillar = new(8000, 12000, -4000, 4000);
    private static readonly Vec3 Warden = new(4000, 0, 0);
    private static readonly Vec3 Cinder = new(2000, 0, 1500);
    private static readonly Vec3 IdlePack = new(4000, 0, -2800);
    private static readonly Vec3 BloomCentre = new(16000, 0, 0);
    private static readonly Vec3 WardenOneMmAside = new(4001, 0, 0);
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
    /// The bot view of the seed-bloom hall with the seed-bloom FloorWindow open and one idle, unheld enemy pack
    /// (HolderId null, so no party seat holds any pack). <paramref name="wardenAt"/> places the Warden.
    /// </summary>
    private static BotView Fixture(Vec3 wardenAt)
    {
        var layout = Layout();
        var run = Create(layout, wardenAt);
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
        var enemy = Actors.Create(EnemyId, ActorKind.Enemy, "test.brute", ActorRole.None, IdlePack, 1000, 0);
        enemy.Idle = true;
        enemy.HolderId = null;
        state.Actors[EnemyId] = enemy;
        var kits = KitRegistry.From(Keyrun.Content.ContentLoader.Load());
        return BotViews.BuildBotView(state, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = layout };
    }

    /// <summary>Printable reason tags of a decision's commands.</summary>
    private static IReadOnlyList<string> TagsOf(BotDecision decision) =>
        Reasons.OfAll(decision.Commands).Select(r => r.RuleId).ToList();

    /// <summary>
    /// The 1-based decision number on which R-PATH-5 (z) would drop a walk fed <paramref name="positions"/> in order (decision n
    /// is made at positions[(n - 1) % positions.Length]): the <see cref="BloomStuckLimit"/>th no-progress decision after the
    /// last new best, where a new best is a strictly smaller squared distance to <see cref="BloomCentre"/>.
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
            if (n - lastNewBest == BloomStuckLimit)
            {
                return n;
            }
        }
    }

    [Fact(DisplayName = "R-PATH-5 (z): a bloom walk stuck on an oscillation with no pack held by another seat stops emitting bot:seed-bloom at the BloomStuckLimit decision")]
    public void Stuck_bloom_walk_without_held_pack_stops_emitting_seed_bloom()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var positions = new[] { Warden, WardenOneMmAside };
        var drop = DropDecisionFor(positions);

        for (var n = 1; n < drop; n++)
        {
            var step = bot.Decide(Fixture(positions[(n - 1) % positions.Length]), memory, WardenId, prng);
            memory = step.Memory;
            prng = step.Prng;
        }

        var view = Fixture(positions[(drop - 1) % positions.Length]);
        Assert.True(
            view.State.Actors[EnemyId].HolderId is null,
            "precondition: the enemy pack must be unheld (HolderId null) so no party seat holds any pack, otherwise the fixture is wrong, not the rule");
        var tags = TagsOf(bot.Decide(view, memory, WardenId, prng));
        Assert.DoesNotContain(BloomTag, tags);
    }

    [Fact(DisplayName = "R-PATH-5 (z) control: a bloom walk with real progress every decision still emits bot:seed-bloom at the BloomStuckLimit decision")]
    public void Progressing_bloom_walk_still_emits_seed_bloom_at_the_limit()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        // The Warden advances 100 mm toward the bloom centre on every decision, so each decision is a new best.
        var decisions = BloomStuckLimit + 2;
        BotDecision? last = null;
        for (var n = 1; n <= decisions; n++)
        {
            var view = Fixture(new Vec3(4000 + 100 * (n - 1), 0, 0));
            last = bot.Decide(view, memory, WardenId, prng);
            memory = last.Memory;
            prng = last.Prng;
        }

        Assert.True(last is not null, "precondition: no decision was made");
        Assert.Contains(BloomTag, TagsOf(last!));
    }
}
