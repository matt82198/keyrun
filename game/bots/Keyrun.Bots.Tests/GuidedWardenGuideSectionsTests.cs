using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Guides;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G2.4 S1 (design/PLAYTESTER-GUIDE.md sections 3.3 and 3.4): a guided Warden must consume its guide's kicks and
/// telegraphs sections, not only the rotation. GuideSet validates both sections at load but drops them, so these facts
/// are expected red until GuidedTankRole reads them.
/// </summary>
public sealed class GuidedWardenGuideSectionsTests
{
    private const int Tick = 10;

    /// <summary>A narrowed warden guide: its kicks section lists only Temper, so Choral Mending is not a listed cast.</summary>
    private const string NarrowedKicksGuide =
        "{\"classId\":\"warden\",\"version\":1," +
        "\"rotation\":[{\"id\":\"strike\",\"press\":\"warden.strike\",\"when\":{\"all\":[]},\"reason\":\"Filler Setup.\"}]," +
        "\"kicks\":[{\"cast\":\"ommet.temper\",\"order\":[\"warden\"],\"reason\":\"Narrowed for this fact.\"}]}";

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private static string GuideText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "content", "data", "guides", "warden.json")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "game/content/data/guides/warden.json not found above the test binary");
        return File.ReadAllText(Path.Combine(dir!.FullName, "game", "content", "data", "guides", "warden.json"));
    }

    private static IReadOnlySet<string> AbilityIds() => Registry.Abilities.Keys.ToHashSet(StringComparer.Ordinal);

    private static BotGuide LiveWardenGuide() => GuideSet.Parse(GuideText(), AbilityIds()).Require("warden");

    private static BotGuide NarrowedWardenGuide() => GuideSet.Parse(NarrowedKicksGuide, AbilityIds()).Require("warden");

    private static ActorState Warden(long resource = 0, long health = 400) =>
        Actor(id: 2, classId: "warden", role: ActorRole.Tank, health: health, maxHealth: 400, resource: resource, maxResource: 100, targetId: 100,
            cooldowns: new[] { ("warden.brace", 999) });

    private static ActorState Enemy(int id, int targetId, string? castId = null, int? castStart = null, int? castEnds = null) =>
        Actor(id: id, kind: ActorKind.Enemy, classId: castId is null ? "pair.melee" : "choir.melee", role: ActorRole.None, z: 0, yaw: 512,
            health: 6720, maxHealth: 6720, resource: 0, maxResource: 0, targetId: targetId, threat: new[] { (2, 100L) },
            castId: castId, castStartTick: castStart, castEndsTick: castEnds);

    private static IReadOnlyList<Command> Decide(BotGuide guide, BotView view, ActorState self) =>
        new GuidedTankRole(guide).Decide(view, self);

    [Fact(DisplayName = "Guided Warden guide sections > kicks a cast its kicks section lists: Shield Bash on Choral Mending in its window")]
    public void Kicks_a_cast_the_kicks_section_lists()
    {
        var self = Warden();
        var view = ViewOf(Tick, RealKits, null,
            self,
            Enemy(100, 2, castId: "choir.healCast", castStart: Tick - 74, castEnds: Tick + 4),
            Enemy(101, 2),
            Enemy(102, 2));

        var cmds = Decide(LiveWardenGuide(), view, self);

        var first = Assert.IsType<CastCommand>(cmds.First());
        Assert.Equal("warden.shieldBash", first.AbilityId);
        Assert.Equal(100, first.TargetId);
    }

    [Fact(DisplayName = "Guided Warden guide sections > does not kick an interruptible cast its kicks section omits: Choral Mending is not listed in the narrowed guide")]
    public void Does_not_kick_a_cast_the_kicks_section_omits()
    {
        var self = Warden();
        var view = ViewOf(Tick, RealKits, null,
            self,
            Enemy(100, 2, castId: "choir.healCast", castStart: Tick - 74, castEnds: Tick + 4),
            Enemy(101, 2),
            Enemy(102, 2));

        var cmds = Decide(NarrowedWardenGuide(), view, self);

        Assert.DoesNotContain(cmds, c => c is CastCommand { AbilityId: "warden.shieldBash" });
    }

    [Fact(DisplayName = "Guided Warden guide sections > steps out of a telegraph its telegraphs section lists: a non-zero move away from the shape")]
    public void Steps_out_of_a_listed_telegraph()
    {
        // The telegraph is a circle centred 1000 mm west of the Warden, radius 2000 mm: the Warden stands inside it, away from its centre to the east.
        var self = Warden();
        var pack = Enemy(100, 2);
        var shape = Circle("ingotBrute.anvilDrop", -1000, 0, 2000, danger: true, resolvesTick: Tick + 10) with { SourceId = 100 };
        var view = ViewOf(Tick, RealKits, null, new[] { shape }, self, pack, Enemy(101, 2), Enemy(102, 2));

        Assert.True(Positioning.InTelegraph(PointMm.Of(self.Pos), shape), "precondition: the Warden stands inside the listed telegraph");

        var cmds = Decide(LiveWardenGuide(), view, self);

        var first = cmds.FirstOrDefault();
        Assert.True(
            first is MoveCommand { Dir: var dir } && (dir.X != 0 || dir.Z != 0),
            $"guided Warden did not step out of the listed telegraph (first command: {first?.GetType().Name ?? "none"}); the role does not read the telegraphs section");

        var move = (MoveCommand)first!;
        var away = move.Dir.X * (self.Pos.X - shape.Center.X) + move.Dir.Z * (self.Pos.Z - shape.Center.Z);
        Assert.True(away > 0, "the step must leave the shape, moving away from its centre, not across it");
    }
}
