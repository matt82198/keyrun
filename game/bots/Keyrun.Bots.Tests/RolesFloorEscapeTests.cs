using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Port of tests/bots/floor-escape.test.ts (6 cases). The TS file never imports real-content-setup, so the views
/// carry <see cref="KitRegistry.Empty"/>; its <c>makeBot()</c> wires the real roles, here <see cref="RoleSet.Default"/>.
/// </summary>
public sealed class RolesFloorEscapeTests
{
    private const int MaxEscapeTicks = 15;
    private const long StepMm = 2000;

    private static BotView View(int tick, ActorState self, params BotTelegraph[] telegraphs) =>
        ViewOf(tick, KitRegistry.Empty, null, telegraphs, self);

    [Fact(DisplayName = "hostile floor patch avoidance > moves a damage bot out of a hostile floor patch within a bounded number of ticks")]
    public void EscapesHostilePatch()
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var self = Actor(id: 1);
        var patch = Circle(Floor.FloorPatchPrefix + "enemy-fire", 0, 0, 5000);
        var inflated = patch with { Danger = true, Radius = 5500 };

        var ticks = 0;
        for (; ticks < MaxEscapeTicks; ticks++)
        {
            var view = View(ticks, self, patch);
            if (!Positioning.IsInAnyDanger(PointMm.Of(self.Pos), new[] { inflated }))
            {
                break;
            }
            var result = bot.Decide(view, memory, self.Id, prng);
            memory = result.Memory;
            prng = result.Prng;
            var move = BotTestKit.FirstMove(result.Commands);
            Assert.NotNull(move);
            BotTestKit.SetPos(self, BotTestKit.ApplyMove(PointMm.Of(self.Pos), move, StepMm));
        }

        Assert.True(ticks < MaxEscapeTicks);
        Assert.False(Positioning.IsInAnyDanger(PointMm.Of(self.Pos), new[] { patch }));
    }

    [Fact(DisplayName = "hostile floor patch avoidance > issues no escape move for a patch the bot never stands in")]
    public void NoMoveForFarPatch()
    {
        var self = Actor(id: 1, x: 100000, z: 100000);
        var patch = Circle(Floor.FloorPatchPrefix + "far-fire", 0, 0, 5000);
        Assert.DoesNotContain(DamageRole.DamageDecide(View(10, self, patch), self), c => c is MoveCommand);
    }

    private static BotTelegraph WindowPatch(string tail) => Circle(Floor.FloorPatchPrefix + Pings.WindowEffectPrefix + tail, 0, 0, 3);

    [Fact(DisplayName = "friendly floor Window: never fled > issues no move at all standing dead-center of a Window it can close")]
    public void StaysInFriendlyWindow()
    {
        var self = Actor(id: 1, classId: "warden");
        Assert.Empty(DamageRole.DamageDecide(View(10, self, WindowPatch("hymn:chanter")), self));
    }

    [Fact(DisplayName = "friendly floor Window: never fled > walks toward a friendly Window it can close, once nearby")]
    public void WalksToFriendlyWindow()
    {
        var self = Actor(id: 1, classId: "warden", x: 20);
        var move = BotTestKit.FirstMove(DamageRole.DamageDecide(View(10, self, WindowPatch("hymn:chanter")), self));
        Assert.NotNull(move);
        Assert.True(move!.Dir.X < 0);
    }

    [Fact(DisplayName = "friendly floor Window: never fled > The Lock: never approaches a Window its own class opened")]
    public void LockNeverApproachesOwnWindow()
    {
        var self = Actor(id: 1, classId: "warden", x: 20);
        Assert.Empty(DamageRole.DamageDecide(View(10, self, WindowPatch("sundered:warden")), self));
    }

    [Fact(DisplayName = "priority: telegraph escape outranks floor escape > produces exactly one move -- the ordinary telegraph's escape -- when both overlap")]
    public void TelegraphOutranksFloor()
    {
        var self = Actor(id: 1);
        var ordinary = Circle("boss-puddle", 0, 0, 5, danger: true);
        var patch = Circle(Floor.FloorPatchPrefix + "enemy-fire", 0, 0, 5);
        var result = Bot.MakeBot(RoleSet.Default).Decide(View(10, self, ordinary, patch), BotMemory.Default(100), self.Id, Skill.SeedPrng(1));
        Assert.Single(result.Commands.OfType<MoveCommand>());
    }
}
