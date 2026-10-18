using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Extra direct tests (NOT counted in the tests/bots/** 1:1 mapping) for shared/** behaviour whose only TS
/// coverage runs through roles/** (lane 3B): floor.ts (floor-escape.test.ts, floor-reapproach.test.ts),
/// priority.ts target pickers, command.ts, view.ts, and bot.ts's gate, mistake roll and runBots threading.
/// </summary>
public sealed class SharedSeamTests
{
    private const long StepMm = 2000;

    private static BotTelegraph Patch(string id, long cx, long cz, long radius) =>
        new(id, TelegraphShape.Circle, new PointMm(cx, cz), 1000, false) { Radius = radius };

    [Fact(DisplayName = "extra floor > floorEscapeCommand walks a bot out of a hostile patch (plus margin) within 15 ticks")]
    public void FloorEscapeLoop()
    {
        var self = MakeActor(id: 1);
        var patch = Patch(Floor.FloorPatchPrefix + "enemy-fire", 0, 0, 5000);
        var ticks = 0;
        for (; ticks < 15; ticks++)
        {
            if (!Positioning.IsInAnyDanger(PointMm.Of(self.Pos), new[] { patch with { Danger = true, Radius = 5500 } }))
            {
                break;
            }
            var move = Floor.FloorEscapeCommand(View(MakeState(ticks, self), null, patch), self);
            Assert.NotNull(move);
            SetPos(self, ApplyMove(PointMm.Of(self.Pos), move, StepMm));
        }
        Assert.True(ticks < 15);
        Assert.Null(Floor.FloorEscapeCommand(View(MakeState(ticks, self), null, patch), self));
    }

    [Fact(DisplayName = "extra floor > a friendly floor Window is never fled, and a closer walks into it")]
    public void FriendlyWindowNeverFled()
    {
        var window = Patch(Floor.FloorPatchPrefix + Pings.WindowEffectPrefix + "hymn:chanter", 0, 0, 3);
        var center = MakeActor(id: 1, classId: "warden");
        Assert.Null(Floor.FloorEscapeCommand(View(MakeState(10, center), null, window), center));
        Assert.Null(Floor.FriendlyWindowApproachCommand(View(MakeState(10, center), null, window), center));

        var near = MakeActor(id: 1, classId: "warden", x: 20);
        var move = Floor.FriendlyWindowApproachCommand(View(MakeState(10, near), null, window), near);
        Assert.NotNull(move);
        Assert.True(move!.Dir.X < 0);
    }

    [Fact(DisplayName = "extra floor > The Lock: no approach to a friendly floor Window the bot's own class opened")]
    public void FloorLock()
    {
        var window = Patch(Floor.FloorPatchPrefix + Pings.WindowEffectPrefix + "sundered:warden", 0, 0, 3);
        var near = MakeActor(id: 1, classId: "warden", x: 20);
        Assert.Null(Floor.FriendlyWindowApproachCommand(View(MakeState(10, near), null, window), near));
    }

    [Fact(DisplayName = "extra floor > an ordinary danger telegraph outranks floor escape (floor tier returns null)")]
    public void OrdinaryDangerOutranksFloor()
    {
        var self = MakeActor(id: 1);
        var ordinary = new BotTelegraph("boss-puddle", TelegraphShape.Circle, new PointMm(0, 0), 1000, true) { Radius = 5 };
        var patch = Patch(Floor.FloorPatchPrefix + "enemy-fire", 0, 0, 5);
        var view = View(MakeState(10, self), null, ordinary, patch);
        Assert.Null(Floor.FloorEscapeCommand(view, self));
        Assert.Single(Bot.MakeBot().Decide(view, BotMemory.Default(100), 1, Skill.SeedPrng(1)).Commands.OfType<MoveCommand>());
    }

    [Fact(DisplayName = "extra floor > meleeReapproachCommand: a warden beyond 5 m walks back in; a cinder does not")]
    public void MeleeReapproach()
    {
        var target = MakeActor(id: 100, kind: ActorKind.Enemy);
        var warden = MakeActor(id: 1, classId: "warden", z: 10000, targetId: 100);
        var move = Floor.MeleeReapproachCommand(View(MakeState(10, warden, target)), warden);
        Assert.NotNull(move);
        Assert.True(move!.Dir.Z < 0);

        var cinder = MakeActor(id: 1, classId: "cinder", z: 10000, targetId: 100);
        Assert.Null(Floor.MeleeReapproachCommand(View(MakeState(10, cinder, target)), cinder));

        var inReach = MakeActor(id: 1, classId: "warden", z: 5000, targetId: 100);
        Assert.Null(Floor.MeleeReapproachCommand(View(MakeState(10, inReach, target)), inReach));
    }

    [Fact(DisplayName = "extra floor > keepNear steers the escape point inside the anchor radius")]
    public void KeepNearSteersEscape()
    {
        var patch = Patch(Floor.FloorPatchPrefix + "fire", -29000, 0, 2000);
        var healer = MakeActor(id: 1, classId: "chanter", role: ActorRole.Healer, x: -29000);
        var opts = new FloorEscapeOptions { KeepNear = new KeepNear(new PointMm(0, 0), 30000) };
        var move = Floor.FloorEscapeCommand(View(MakeState(10, healer), null, patch), healer, opts);
        // Patch radius 2000 + margin 500 = 2500. Ring 4 is the first ring with a clear point: only its corners
        // (2828 mm out) clear it. Unconstrained, the first corner in grid order is (-31000, -2000), away from the
        // anchor and 31.06 m from it; keepNear skips it and (-31000, 2000) and takes (-27000, -2000).
        var plain = Floor.FloorEscapeCommand(View(MakeState(10, healer), null, patch), healer);
        Assert.Equal(Moves.MoveToward(10, 1, new PointMm(-29000, 0), new PointMm(-31000, -2000)), plain);
        Assert.Equal(Moves.MoveToward(10, 1, new PointMm(-29000, 0), new PointMm(-27000, -2000)), move);
    }

    [Fact(DisplayName = "extra priority > pickTankTarget, pickHealTarget, pickDispelTarget, pickRotationAbility")]
    public void TargetPickers()
    {
        var e1 = MakeActor(id: 100, kind: ActorKind.Enemy);
        e1.Threat[1] = 50;
        e1.Threat[2] = 80;
        var e2 = MakeActor(id: 101, kind: ActorKind.Enemy);
        e2.Threat[1] = 10;
        e2.Threat[3] = 200;
        Assert.Equal(190, Priority.ThreatDeficit(e2, 1));
        Assert.Equal(101, Priority.PickTankTarget(Table(e1, e2), 1));
        Assert.Null(Priority.PickTankTarget(Table(MakeActor(id: 1)), 1));

        var a = MakeActor(id: 2);
        a.Health = 50;
        var b = MakeActor(id: 3);
        b.Health = 50;
        var c = MakeActor(id: 4);
        c.Health = 69;
        var d = MakeActor(id: 5);
        d.Health = 70;
        Assert.Equal(2, Priority.PickHealTarget(Table(d, c, b, a)));
        Assert.Null(Priority.PickHealTarget(Table(d)));

        var carrier = MakeActor(id: 6, effects: new[] { Effect("curse", 100, 99) });
        Assert.Equal(6, Priority.PickDispelTarget(Table(a, carrier), new HashSet<string> { "curse" }));
        Assert.Equal("b", Priority.PickRotationAbility(new[] { "a", "b", "c" }, id => id != "a"));
        Assert.Null(Priority.PickRotationAbility(new[] { "a" }, _ => false));
    }

    [Fact(DisplayName = "extra command > moveToward builds an integer unit dir and yaw; coincident points give null")]
    public void MoveTowardIntegers()
    {
        var move = Moves.MoveToward(5, 1, new PointMm(0, 0), new PointMm(0, 10000));
        Assert.Equal(new MoveCommand(5, 1, new Dir2(0, 1000), 256), move);
        Assert.Null(Moves.MoveToward(5, 1, new PointMm(7, 7), new PointMm(7, 7)));
        Assert.Equal(512, Moves.YawToward(new PointMm(0, 0), new PointMm(-5, 0)));
        Assert.False(Moves.YawChanged(10, 10));
        Assert.True(Moves.YawChanged(10, 11));
    }

    [Fact(DisplayName = "extra view > buildBotView appends the derived floor telegraphs after the encounter ones")]
    public void BuildBotViewMergesFloor()
    {
        var state = MakeState(10, MakeActor(id: 1), MakeActor(id: 9, kind: ActorKind.Enemy));
        state.FloorWindows["seedbloom"] = new FloorWindow { WindowId = "seedbloom", SetupClass = "boss", SetupActorId = 9, Pos = new Vec3(100, 0, 200), EndsTick = 300, RadiusMm = 2000 };
        state.FloorWindows["bulwark"] = new FloorWindow { WindowId = "bulwark", SetupClass = "warden", SetupActorId = 1, Pos = new Vec3(0, 0, 0), EndsTick = 5, RadiusMm = 900 };
        var enc = new BotTelegraph("7", TelegraphShape.Cone, new PointMm(1, 2), 40, true) { Radius = 4000, Angle1024 = 200, Yaw = 12 };
        var view = BotViews.BuildBotView(state, new[] { enc }, ClassKitView.Empty);
        Assert.Equal(2, view.Telegraphs.Count);
        Assert.Same(enc, view.Telegraphs[0]);
        Assert.Equal(new BotTelegraph("floor:seedbloom", TelegraphShape.Circle, new PointMm(100, 200), 300, false) { Radius = 2000 }, view.Telegraphs[1]);
        Assert.Same(KitRegistry.Empty, view.Kits);
    }

    [Fact(DisplayName = "extra view > toBotTelegraph maps an encounter telegraph (danger from the telegraph-map.ts class table)")]
    public void ToBotTelegraphMapsEncounter()
    {
        var cleave = new Keyrun.Sim.Encounter.Telegraph(3, 9, "hollowWarden.cleave", "cone", new Vec3(10, 0, 20), 300, 77) { Radius = 4000, Angle1024 = 200 };
        Assert.Equal(new BotTelegraph("3", TelegraphShape.Cone, new PointMm(10, 20), 77, true) { Radius = 4000, Angle1024 = 200, Yaw = 300 }, BotViews.ToBotTelegraph(cleave));
        var stack = new Keyrun.Sim.Encounter.Telegraph(4, 9, "pair.stackCall", "circle", new Vec3(0, 0, 0), 0, 50) { Radius = 2500 };
        Assert.False(BotViews.ToBotTelegraph(stack).Danger);
        var unknown = new Keyrun.Sim.Encounter.Telegraph(5, 9, "who.knows", "line", new Vec3(0, 0, 0), 0, 50) { Length = 9000, Width = 1000 };
        Assert.True(BotViews.ToBotTelegraph(unknown).Danger);
    }

    [Fact(DisplayName = "extra bot > the gate holds role dispatch until nextActionTick, then re-arms by reactionDelayTicks")]
    public void GateAndDelay()
    {
        var calls = new List<int>();
        var bot = Bot.MakeBot(new RoleSet(null, null, new CountingRole(calls)));
        var self = MakeActor(id: 2);
        var shut = bot.Decide(View(MakeState(10, self)), new BotMemory(100, 11), 2, Skill.SeedPrng(1));
        Assert.Empty(calls);
        Assert.Equal(new BotMemory(100, 11), shut.Memory);
        Assert.Equal(Skill.SeedPrng(1), shut.Prng);

        var open = bot.Decide(View(MakeState(11, self)), shut.Memory, 2, shut.Prng);
        Assert.Equal(new[] { 11 }, calls);
        Assert.Equal(new BotMemory(100, 11 + Skill.ReactionDelayTicks(100)), open.Memory);
        Assert.Equal(Skill.StepPrng(Skill.SeedPrng(1)).Next, open.Prng);

        var dead = MakeActor(id: 3, alive: false);
        var none = bot.Decide(View(MakeState(11, dead)), BotMemory.Default(), 3, Skill.SeedPrng(1));
        Assert.Empty(none.Commands);
        Assert.Equal(Skill.SeedPrng(1), none.Prng);
    }

    [Fact(DisplayName = "extra bot > a mistake roll skips role dispatch but still consumes the PRNG and re-arms the gate")]
    public void MistakeSkipsRole()
    {
        var calls = new List<int>();
        var bot = Bot.MakeBot(new RoleSet(null, null, new CountingRole(calls)));
        var self = MakeActor(id: 2);
        var prng = Skill.SeedPrng(1);
        var mistakes = 0;
        for (var tick = 0; tick < 200; tick++)
        {
            var roll = Skill.RollMistake(0, prng);
            var result = bot.Decide(View(MakeState(tick, self)), new BotMemory(0, 0), 2, prng);
            Assert.Equal(roll.Next, result.Prng);
            Assert.Equal(tick + Skill.ReactionDelayTicks(0), result.Memory.NextActionTick);
            if (roll.Mistake)
            {
                mistakes++;
            }
            prng = result.Prng;
        }
        Assert.True(mistakes > 0);
        Assert.Equal(200 - mistakes, calls.Count);
    }

    [Fact(DisplayName = "extra bot > runBots runs bots in ascending actorId order and threads one PRNG through them")]
    public void RunBotsOrderAndPrng()
    {
        var calls = new List<int>();
        var roles = new RoleSet(null, null, new CountingRole(calls));
        var bots = new Dictionary<int, IBot> { [7] = Bot.MakeBot(roles), [3] = Bot.MakeBot(roles), [5] = Bot.MakeBot(roles) };
        var memories = new Dictionary<int, BotMemory> { [5] = new BotMemory(100, 999) };
        var view = View(MakeState(10, MakeActor(id: 3), MakeActor(id: 5), MakeActor(id: 7)));
        var seed = Skill.SeedPrng(11);

        var batch = Bot.RunBots(view, bots, memories, seed);

        Assert.Equal(new[] { 10, 10 }, calls);
        Assert.Equal(new Command[] { new CastCommand(10, 3, "role"), new CastCommand(10, 7, "role") }, batch.Commands);
        Assert.Equal(new[] { 3, 5, 7 }, batch.Memories.Keys);
        Assert.Equal(new BotMemory(100, 999), batch.Memories[5]);
        Assert.Equal(Skill.StepPrng(Skill.StepPrng(seed).Next).Next, batch.Prng);
    }

    /// <summary>A stand-in role that records the tick it ran and casts "role".</summary>
    private sealed class CountingRole : IRole
    {
        private readonly List<int> _calls;

        public CountingRole(List<int> calls) => _calls = calls;

        public IReadOnlyList<Command> Decide(BotView view, ActorState self)
        {
            _calls.Add(view.State.Tick);
            return new Command[] { new CastCommand(view.State.Tick, self.Id, "role") };
        }
    }
}
