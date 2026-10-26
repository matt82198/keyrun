using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Guides;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-GCD-1 (c) (event seq 105, amended by ruling seq 26 R-GCD-1 (n)): a guided bot presses its class movement ability
/// (KitRegistry kit MovementAbilityId) to escape a telegraph only when that ability clears the danger before the cast
/// resolves and walking does not. Spec tests, written before the implementation (TDD red). Walk speed is 180 mm per tick
/// (run.json). chanter.glide applies gliding at movementBp 5000, so its speed is 270 mm per tick. cinder.blink displaces
/// 8000 mm along the caster's facing (Yaw). warden.charge displaces toward a target and does nothing without one.
/// Melee reach is 5000 mm (Roles/Damage.cs). Warden charge cast range is 8000 mm (warden.json).
/// </summary>
public sealed class BotMovementAbilityTests
{
    private const int Tick = 10;
    private const int DecisionsAllowed = 2;

    /// <summary>Walk speed in mm per tick (content run.json).</summary>
    private const long WalkMmPerTick = 180;

    /// <summary>Glide speed in mm per tick: walk speed plus chanter.glide's movementBp 5000 (+50 percent).</summary>
    private const long GlideMmPerTick = 270;

    /// <summary>Blink displacement in mm (content cinder.blink distanceMm).</summary>
    private const long BlinkMm = 8000;

    /// <summary>Melee reach in mm (Roles/Damage.cs MeleeRangeMm).</summary>
    private const long MeleeReachMm = 5000;

    /// <summary>Warden charge cast range in mm (warden.json warden.charge range).</summary>
    private const long ChargeRangeMm = 8000;

    /// <summary>Facing -X in the Yaw unit of 1024 steps per turn (FaceDir(512) points away from the safe side of the seat).</summary>
    private const int FacingNegativeX = 512;

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

    private static GuideSet WardenGuide() =>
        GuideSet.Parse(GuideText(), ContentLoader.Load().Abilities.Keys.ToHashSet(StringComparer.Ordinal));

    /// <summary>The first cast command of <paramref name="commands"/> with <paramref name="abilityId"/>, or null.</summary>
    private static CastCommand? CastOf(IEnumerable<Command> commands, string abilityId) =>
        commands.OfType<CastCommand>().FirstOrDefault(c => c.AbilityId == abilityId);

    /// <summary>
    /// Runs up to <see cref="DecisionsAllowed"/> decisions of <paramref name="bot"/> for <paramref name="self"/> and returns
    /// every command issued, in order. Each decision sees the tick it is made on (the view advances one tick per decision).
    /// </summary>
    private static List<Command> DecideTwice(IBot bot, ActorState self, IReadOnlyList<BotTelegraph> telegraphs, params ActorState[] others)
    {
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var issued = new List<Command>();
        var actors = new[] { self }.Concat(others).ToArray();
        for (var i = 0; i < DecisionsAllowed; i++)
        {
            var view = ViewOf(Tick + i, RealKits, null, telegraphs, actors);
            var result = bot.Decide(view, memory, self.Id, prng);
            memory = result.Memory;
            prng = result.Prng;
            issued.AddRange(result.Commands);
        }
        return issued;
    }

    /// <summary>A danger circle the bot stands in, at (1000, 0), that resolves at <paramref name="resolvesTick"/>.</summary>
    private static BotTelegraph DangerCircleAroundSelf(int resolvesTick) =>
        Circle("guided-puddle", 0, 0, 5000, danger: true, resolvesTick: resolvesTick);

    /// <summary>Walk-out time in ticks from (1000, 0) to the edge of the 5000 mm circle at (0, 0): (5000 - 1000) / 180, rounded up.</summary>
    private static int WalkOutTicks() => (int)((5000 - 1000 + WalkMmPerTick - 1) / WalkMmPerTick);

    /// <summary>Glide-out time in ticks from (1000, 0) to the same edge at 270 mm per tick, rounded up (15).</summary>
    private static int GlideOutTicks() => (int)((5000 - 1000 + GlideMmPerTick - 1) / GlideMmPerTick);

    [Fact(DisplayName = "guided movement > a guided cinder inside a telegraph that resolves before its walk-out issues cinder.blink within 2 decisions")]
    public void Guided_cinder_in_telegraph_with_no_time_to_walk_presses_blink()
    {
        // Resolve 5 ticks ahead; the walk out takes about 23 ticks, so walking cannot save it. Blink at the default yaw 0 lands
        // at (9000, 0), outside the circle, so the blink clears the danger.
        var telegraph = DangerCircleAroundSelf(resolvesTick: Tick + 5);
        Assert.True(WalkOutTicks() > 5, "premise: walking out must take longer than the telegraph's remaining ticks");
        var self = Actor(id: 2, classId: "cinder", role: ActorRole.Damage, x: 1000, z: 0);

        var issued = DecideTwice(Bot.MakeBot(RoleSet.None), self, new[] { telegraph });

        var cast = CastOf(issued, "cinder.blink");
        Assert.True(cast is not null, $"expected a cinder.blink cast within {DecisionsAllowed} decisions; issued: {string.Join(", ", issued.Select(c => c.GetType().Name))}");
    }

    [Fact(DisplayName = "guided movement > a guided chanter whose telegraph resolves in 20 ticks, after glide clears in 15 but walking needs 23, presses chanter.glide")]
    public void Guided_chanter_glides_when_glide_clears_and_walking_does_not()
    {
        // Resolve 20 ticks ahead. Glide clears the 4000 mm to the edge in 15 ticks; walking needs 23, so only glide saves it.
        var telegraph = DangerCircleAroundSelf(resolvesTick: Tick + 20);
        Assert.True(GlideOutTicks() < 20, "premise: glide must clear before the telegraph resolves");
        Assert.True(WalkOutTicks() > 20, "premise: walking out must not fit before the telegraph resolves");
        var self = Actor(id: 2, classId: "chanter", role: ActorRole.Healer, x: 1000, z: 0);

        var issued = DecideTwice(Bot.MakeBot(RoleSet.None), self, new[] { telegraph });

        var cast = CastOf(issued, "chanter.glide");
        Assert.True(cast is not null, $"expected a chanter.glide cast within {DecisionsAllowed} decisions; issued: {string.Join(", ", issued.Select(c => c.GetType().Name))}");
    }

    [Fact(DisplayName = "guided movement > a guided chanter whose telegraph resolves in 5 ticks, before glide's 15-tick clear, does not press chanter.glide")]
    public void Guided_chanter_does_not_glide_when_glide_cannot_clear_before_resolve()
    {
        // Same geometry as the glide case, resolving 5 ticks ahead: glide needs 15 ticks to reach the edge, so it cannot clear
        // the danger before the cast resolves, and walking cannot either. No movement cast is worth pressing.
        var telegraph = DangerCircleAroundSelf(resolvesTick: Tick + 5);
        Assert.True(GlideOutTicks() > 5, "premise: glide must not clear before the telegraph resolves");
        var self = Actor(id: 2, classId: "chanter", role: ActorRole.Healer, x: 1000, z: 0);

        var issued = DecideTwice(Bot.MakeBot(RoleSet.None), self, new[] { telegraph });

        Assert.True(CastOf(issued, "chanter.glide") is null, "chanter.glide must not be cast when glide cannot clear before the telegraph resolves");
    }

    [Fact(DisplayName = "guided movement > a guided cinder facing away from its safe landing turns to face it before blink fires")]
    public void Guided_cinder_faces_safe_landing_before_blink()
    {
        // Circle of radius 8500 about the origin, seat at (1000, 0): walking out takes 42 ticks, the telegraph resolves in 5.
        // Blink displaces 8000 mm along the seat's yaw. The seat starts facing -X, where the landing (-7000, 0) is still inside
        // the circle. Facing +X lands at (9000, 0), outside it. The bot must face +X (yaw 0) before blink fires.
        const long Radius = 8500;
        var telegraph = Circle("guided-puddle", 0, 0, Radius, danger: true, resolvesTick: Tick + 5);
        Assert.True((Radius - 1000 + WalkMmPerTick - 1) / WalkMmPerTick > 5, "premise: walking out must take longer than 5 ticks");
        var self = Actor(id: 2, classId: "cinder", role: ActorRole.Damage, x: 1000, z: 0, yaw: FacingNegativeX);

        var issued = DecideTwice(Bot.MakeBot(RoleSet.None), self, new[] { telegraph });

        var castIndex = issued.FindIndex(c => c is CastCommand { AbilityId: "cinder.blink" });
        Assert.True(castIndex >= 0, "expected cinder.blink within 2 decisions");

        // The facing the seat holds when blink fires: the last move or dash yaw issued before it, else the seat's own yaw.
        var facing = self.Yaw;
        foreach (var command in issued.Take(castIndex))
        {
            if (command is MoveCommand move)
            {
                facing = move.Yaw;
            }
            else if (command is DashCommand dash)
            {
                facing = dash.Yaw;
            }
        }

        var face = Facing.FaceDir(facing);
        var landX = self.Pos.X + (face.X * BlinkMm / Facing.Q);
        var landZ = self.Pos.Z + (face.Z * BlinkMm / Facing.Q);
        Assert.True(
            (landX * landX) + (landZ * landZ) > Radius * Radius,
            $"blink fires facing yaw {facing} and lands at ({landX}, {landZ}), inside the {Radius} mm danger circle");
    }

    [Fact(DisplayName = "guided movement > a guided Warden with no living enemy target does not press warden.charge to escape a telegraph")]
    public void Guided_warden_with_no_living_target_does_not_charge_to_escape()
    {
        // The circle resolves in 5 ticks and the walk out takes 23, so the escape tier is reached. Charge does nothing without a
        // target, so pressing it cannot clear the danger.
        var telegraph = DangerCircleAroundSelf(resolvesTick: Tick + 5);
        Assert.True(WalkOutTicks() > 5, "premise: walking out must take longer than the telegraph's remaining ticks");
        var warden = Actor(id: 2, classId: "warden", role: ActorRole.Tank, x: 1000, z: 0, health: 400, maxHealth: 400, resource: 100, maxResource: 100);

        var issued = DecideTwice(Bot.MakeBot(RoleSet.None), warden, new[] { telegraph });

        Assert.True(CastOf(issued, "warden.charge") is null, "warden.charge must not be cast to escape with no living enemy target");
    }

    [Fact(DisplayName = "guided movement > a guided Warden holding a target whose gap beyond melee reach exceeds ChargeThresholdMm (2000 mm) presses warden.charge on it")]
    public void Guided_warden_charges_held_target_beyond_walk_threshold()
    {
        // The Warden charges when the gap beyond melee reach exceeds ChargeThresholdMm (2000 mm).
        // The charge cast range of 8000 mm caps the target distance, so the gap here is 3000 mm.
        var targetDistance = ChargeRangeMm;
        Assert.True(targetDistance - MeleeReachMm > 0, "premise: target lies beyond melee reach");
        var warden = Actor(id: 2, classId: "warden", role: ActorRole.Tank, health: 400, maxHealth: 400, resource: 100, maxResource: 100,
            targetId: 100, cooldowns: new[] { ("warden.brace", 999) }, threat: new[] { (100, 1L) });
        var enemy = Actor(id: 100, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: targetDistance, z: 0,
            health: 6720, maxHealth: 6720, targetId: 2, threat: new[] { (2, 100L) });

        var issued = DecideTwice(Bot.MakeBot(RoleSet.Guided(WardenGuide())), warden, Array.Empty<BotTelegraph>(), enemy);

        var cast = CastOf(issued, "warden.charge");
        Assert.True(cast is not null, $"expected warden.charge on target 100 within {DecisionsAllowed} decisions");
        Assert.Equal(100, cast!.TargetId);
    }

    [Fact(DisplayName = "guided movement control > a bot whose walk-out fits before the telegraph resolves does not press its movement ability")]
    public void Bot_whose_walk_out_fits_before_resolve_does_not_press_movement()
    {
        // Resolve 60 ticks ahead; the walk out takes about 23 ticks, so walking is enough and no movement cast is needed.
        var telegraph = DangerCircleAroundSelf(resolvesTick: Tick + 60);
        Assert.True(WalkOutTicks() < 60, "premise: walking out must fit before the telegraph resolves");
        var self = Actor(id: 2, classId: "cinder", role: ActorRole.Damage, x: 1000, z: 0);

        var issued = DecideTwice(Bot.MakeBot(RoleSet.None), self, new[] { telegraph });

        Assert.Null(CastOf(issued, "cinder.blink"));
    }
}
