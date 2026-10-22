using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// PURPOSE: this artifact exists to build N1 PACK-LINKS (game/design/MPLUS-DUNGEON-PLAN.md, the remaining half of
/// FORCES-ROUTE-PLAN S3 PACK-SCHEMA) so a chain pull is a route decision the way WoW Mythic+ has it (BIBLE section 9.1
/// links; finding f-82d9390e); status: real.
/// A layout pack's <see cref="PackGroup.Links"/> reach <see cref="Aggro.GroupsOf"/>, so engaging one linked pack (by its
/// ring or by damage to one member) engages its partner on the same tick, in either direction, with one
/// <c>pack_engage</c> each. An unlinked pack stays idle.
/// </summary>
public sealed class PackLinkTests
{
    private const int WatchA = 301, WatchB = 302, KennelA = 401, KennelB = 402;
    private const long Ring = 3000;
    private static readonly Vec3 WatchCenter = new(15000, 0, 0);

    // The kennel stands 4000 mm off the walking line (z = 0), so its own 3000 mm ring is never reached on the walk.
    private static readonly Vec3 KennelCenter = new(25000, 0, 4000);

    private static readonly Bounds Hall = new(-5000, 30000, -5000, 5000);
    private static readonly Bounds Corridor = new(29000, 36000, -1000, 1000);
    private static readonly Bounds End = new(35000, 50000, -5000, 5000);

    private static NavLayout Layout() => new(
        new[]
        {
            new NavBox("hall", Hall, true, null),
            new NavBox("corridor", Corridor, false, null),
            new NavBox("end", End, true, null),
        },
        new[] { new NavTrigger("p1", new Bounds(37000, 50000, -5000, 5000)) },
        new[] { new Vec3(29500, 0, 0), new Vec3(35500, 0, 0), new Vec3(45000, 0, 0) });

    private static readonly DungeonDef Dungeon = new("packs", new[]
    {
        new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 48000, 0, 10) }),
    });

    private static PackGroup Watch(IReadOnlyList<string>? links) => new("watch", new[]
    {
        RunTestKit.Spawn(WatchA, "dummy", 15000, -1000, 10),
        RunTestKit.Spawn(WatchB, "dummy", 15000, 1000, 10),
    }, WatchCenter, Ring) { Links = links ?? Array.Empty<string>() };

    private static PackGroup Kennel(IReadOnlyList<string>? links) => new("kennel", new[]
    {
        RunTestKit.Spawn(KennelA, "dummy", 25000, 3500, 10),
        RunTestKit.Spawn(KennelB, "dummy", 25000, 4500, 10),
    }, KennelCenter, Ring) { Links = links ?? Array.Empty<string>() };

    private static Run Create(PackGroup watch, PackGroup kennel)
    {
        var layout = Layout();
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal), bounds: layout.Extent) with
        {
            Layout = layout,
            Packs = new[] { watch, kennel },
        };
        return RunRoot.CreateRun(
            1,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 400, 100),
                RunTestKit.Member(2, "chanter", ActorRole.Healer, -2000, 0, 250, 100),
            },
            Dungeon,
            content);
    }

    private static MoveCommand East(int tick, int actor) => new(tick, actor, new Dir2(1000, 0), 0);

    private static string PackId(SimEvent e) => ((JsString)e.Get("packId")!).Value;

    // Walks the Warden east along z = 0 until some pack engages (or 300 ticks pass).
    private static (Run After, IReadOnlyList<SimEvent> EngageTick) WalkIn(Run run)
    {
        for (var i = 0; i < 300; i++)
        {
            var s = RunRoot.StepRun(run, new Command[] { East(run.State.Tick, 1) });
            run = s.Run;
            if (s.Events.Any(e => e.Type == "pack_engage"))
            {
                return (run, s.Events);
            }
        }
        return (run, Array.Empty<SimEvent>());
    }

    [Fact(DisplayName = "Pack links (N1): walking into one linked pack's ring engages its partner on the same tick, one pack_engage each")]
    public void Walking_into_a_linked_pack_engages_its_partner_on_the_same_tick()
    {
        var (after, engageTick) = WalkIn(Create(Watch(new[] { "kennel" }), Kennel(null)));
        var engaged = engageTick.Where(e => e.Type == "pack_engage").ToList();
        Assert.Equal(new[] { "watch", "kennel" }, engaged.Select(PackId));
        Assert.Single(engaged.Select(e => e.Tick).Distinct());
        foreach (var id in new[] { WatchA, WatchB, KennelA, KennelB })
        {
            Assert.False(after.State.Actors[id].Idle, $"member {id} is still idle after the chain pull");
        }
    }

    [Fact(DisplayName = "Pack links (N1): without a link the far pack stays idle when the near one is pulled (control)")]
    public void Without_a_link_the_far_pack_stays_idle()
    {
        var (after, engageTick) = WalkIn(Create(Watch(null), Kennel(null)));
        Assert.Equal(new[] { "watch" }, engageTick.Where(e => e.Type == "pack_engage").Select(PackId));
        Assert.True(after.State.Actors[KennelA].Idle);
        Assert.True(after.State.Actors[KennelB].Idle);
    }

    [Theory(DisplayName = "Pack links (N1): damage to one member of a linked pack engages both packs, whichever side declares the link")]
    [InlineData(true)]
    [InlineData(false)]
    public void Damage_to_one_linked_pack_engages_its_partner(bool watchDeclaresLink)
    {
        var run = watchDeclaresLink
            ? Create(Watch(new[] { "kennel" }), Kennel(null))
            : Create(Watch(null), Kennel(new[] { "watch" }));
        var groups = Aggro.GroupsOf(run.Dungeon, run.State.Tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols);
        var hurt = run.State.Clone();
        hurt.Actors[KennelB].Health -= 1;
        Assert.Equal(new[] { "watch", "kennel" }, Aggro.Triggered(run.State, hurt, groups, run.Encounter));

        var events = new List<SimEvent>();
        Aggro.Engage(hurt, run.Encounter, groups, Aggro.Triggered(run.State, hurt, groups, run.Encounter), 7, events.Add);
        Assert.Equal(new[] { "watch", "kennel" }, events.Where(e => e.Type == "pack_engage").Select(PackId));
        Assert.All(events, e => Assert.Equal(7, e.Tick));
    }
}
