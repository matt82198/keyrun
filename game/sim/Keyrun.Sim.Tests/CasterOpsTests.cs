using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// FORCES-ROUTE-PLAN increment 5 CASTER-OPS-SIM (design/BIBLE.md section 9.3): the effect ops the new pack casters
/// need, driven through <see cref="Rules.Make"/>. Undertow (<see cref="PullOp"/> plus a root), Stoked (damage and
/// cast speed), Searing Hex (a <see cref="DamagePctOp"/> magic burn that Purge and Cleanse remove), Drenched (a slow),
/// and Purge on a Stoked enemy (the Mythic+ soothe). The effect numbers are the bible's; PackContentTests pins the
/// shipped content (game/content/data/effects/vault.json) to the same numbers. Purge and Cleanse are the real
/// abilities from the hollow-vault-k2 oracle setup.
/// </summary>
public class CasterOpsTests
{
    private const int PlayerId = 1;
    private const int CinderId = 2;
    private const int ChanterId = 3;
    private const int CasterId = 10;
    private const int AllyId = 11;

    private static readonly OracleSetupContent Content = OracleSetupContent.Load("hollow-vault-k2.setup.json");

    private static readonly EffectDef Rooted = new("rooted", 1, null, new EffectModifiers(MovementBp: -10000));
    private static readonly EffectDef Stoked = new("stoked", 1, "enrage", new EffectModifiers(DamageDoneBp: 3000, HasteBp: 2500));
    private static readonly EffectDef Drenched = new("drenched", 1, "magic", new EffectModifiers(MovementBp: -3000));
    private static readonly EffectDef SearingHex = new("searingHex", 1, "magic", new EffectModifiers(), new EffectPeriodic(24, new DamagePctOp(500, "shadow")));

    private static readonly AbilityDef Undertow = Def("drowned.undertow", new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(10, "frost"), new PullOp(5000), new ApplyEffectOp("rooted", 20, 1) }, castTicks: 60, costResource: "none", range: 30000, interruptible: true);
    private static readonly AbilityDef Stoke = Def("bellows.stoke", new AbilityShape(ShapeKind.Circle, Radius: 4000),
        new EffectOp[] { new ApplyEffectOp("stoked", 160, 1) }, castTicks: 70, costResource: "none", range: 0, interruptible: true);
    private static readonly AbilityDef Hex = Def("ash.searingHex", new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new ApplyEffectOp("searingHex", 120, 1) }, castTicks: 50, costResource: "none", range: 30000, interruptible: true);
    private static readonly AbilityDef Soak = Def("test.soak", new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new ApplyEffectOp("drenched", 60, 1) }, costResource: "none", range: 30000);
    private static readonly AbilityDef Swing = Def("test.swing", new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(1000, "physical") }, costResource: "none", range: 30000);
    private static readonly AbilityDef Probe = Def("test.probe", new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(1, "physical") }, castTicks: 70, costResource: "none", range: 30000);

    [Fact]
    public void Undertow_pulls_its_target_5000_mm_toward_the_caster_and_roots_it_for_20_ticks()
    {
        var run = Arrange(playerX: 8000);
        run.CastAndFinish(CasterId, Undertow.Id, PlayerId);
        Assert.Equal(3000, run.Player.Pos.X);
        Assert.Equal(0, run.Player.Pos.Z);
        var rootedAt = run.State.Tick;
        for (var t = 0; t < 20; t++)
        {
            run.Step(new MoveCommand(0, PlayerId, new Dir2(1, 0), 0));
        }
        Assert.Equal(3000, run.Player.Pos.X);
        Assert.Equal(rootedAt + 20, run.State.Tick);
        run.Step(new MoveCommand(0, PlayerId, new Dir2(1, 0), 0));
        Assert.Equal(3300, run.Player.Pos.X);
    }

    [Fact]
    public void Undertow_never_pulls_its_target_past_the_caster()
    {
        var run = Arrange(playerX: 3000);
        run.CastAndFinish(CasterId, Undertow.Id, PlayerId);
        Assert.InRange(run.Player.Pos.X, -200, 200);
    }

    [Fact]
    public void A_pull_toward_a_caster_beyond_a_wall_stops_at_the_wall()
    {
        var target = Make(PlayerId, ActorKind.Player, x: 8000);
        var caster = Make(CasterId, ActorKind.Enemy, x: -2000);
        var rooms = new List<Bounds> { new(5000, 12000, -2000, 2000), new(-6000, 0, -2000, 2000) };
        Movement.PullTowardWalkable(target, caster, 5000, rooms);
        Assert.Equal(5000, target.Pos.X);
        var open = Make(PlayerId, ActorKind.Player, x: 8000);
        Movement.PullTowardWalkable(open, caster, 5000, new List<Bounds> { new(-6000, 12000, -2000, 2000) });
        Assert.Equal(3000, open.Pos.X);
    }

    [Fact]
    public void Stoked_raises_damage_by_3000_bp_and_shortens_casts_by_2000_bp_for_160_ticks()
    {
        var run = Arrange(playerX: 2000);
        run.CastAndFinish(CasterId, Stoke.Id, CasterId);
        Assert.Contains(run.Caster.Effects, e => e.EffectId == "stoked");
        Assert.Contains(run.Ally.Effects, e => e.EffectId == "stoked");
        Assert.DoesNotContain(run.Player.Effects, e => e.EffectId == "stoked");
        var stokedAt = run.State.Tick;

        var hit = run.Step(Cast(AllyId, Swing.Id, PlayerId)).Single(e => e.Type == "damage");
        Assert.Equal(1300, Int(hit, "modifiedAmount"));
        run.Step(Cast(AllyId, Probe.Id, PlayerId));
        Assert.Equal(56, run.Ally.CastEndsTick - run.Ally.CastStartTick);

        run.RunTo(stokedAt + 161);
        Assert.DoesNotContain(run.Ally.Effects, e => e.EffectId == "stoked");
        var plain = run.Step(Cast(CasterId, Swing.Id, PlayerId)).Single(e => e.Type == "damage");
        Assert.Equal(1000, Int(plain, "modifiedAmount"));
        run.Step(Cast(CasterId, Probe.Id, PlayerId));
        Assert.Equal(70, run.Caster.CastEndsTick - run.Caster.CastStartTick);
    }

    [Fact]
    public void Searing_Hex_burns_25_percent_of_max_health_over_120_ticks()
    {
        var run = Arrange(playerX: 2000);
        run.CastAndFinish(CasterId, Hex.Id, PlayerId);
        run.RunTo(run.State.Tick + 130);
        Assert.Equal(10000 - 2500, run.Player.Health);
        Assert.DoesNotContain(run.Player.Effects, e => e.EffectId == "searingHex");
    }

    [Theory]
    [InlineData(CinderId, "cinder.purge")]
    [InlineData(ChanterId, "chanter.cleanse")]
    public void Searing_Hex_is_removed_by_Purge_and_by_Cleanse(int dispellerId, string dispel)
    {
        var run = Arrange(playerX: 2000);
        run.CastAndFinish(CasterId, Hex.Id, PlayerId);
        Assert.Contains(run.Player.Effects, e => e.EffectId == "searingHex");
        run.Step(Cast(dispellerId, dispel, PlayerId));
        Assert.DoesNotContain(run.Player.Effects, e => e.EffectId == "searingHex");
        var health = run.Player.Health;
        run.RunTo(run.State.Tick + 130);
        Assert.Equal(health, run.Player.Health);
    }

    [Fact]
    public void Drenched_slows_movement_by_3000_bp_for_60_ticks()
    {
        var run = Arrange(playerX: 2000);
        run.Step(Cast(CasterId, Soak.Id, PlayerId));
        var soakedAt = run.State.Tick;
        run.Step(new MoveCommand(0, PlayerId, new Dir2(1, 0), 0));
        Assert.Equal(2210, run.Player.Pos.X);
        run.RunTo(soakedAt + 60);
        var x = run.Player.Pos.X;
        run.Step(new MoveCommand(0, PlayerId, new Dir2(1, 0), 0));
        Assert.Equal(x + 300, run.Player.Pos.X);
    }

    [Fact]
    public void Purge_cast_on_a_Stoked_enemy_removes_Stoked()
    {
        var run = Arrange(playerX: 2000);
        run.CastAndFinish(CasterId, Stoke.Id, CasterId);
        Assert.Contains(run.Ally.Effects, e => e.EffectId == "stoked");
        run.Step(Cast(CinderId, "cinder.purge", AllyId));
        Assert.DoesNotContain(run.Ally.Effects, e => e.EffectId == "stoked");
        Assert.Contains(run.Caster.Effects, e => e.EffectId == "stoked");
    }

    private static Run Arrange(long playerX)
    {
        var player = Make(PlayerId, ActorKind.Player, "warden", ActorRole.Tank, x: playerX, maxHealth: 10000, maxResource: 100);
        var cinder = Make(CinderId, ActorKind.Player, "cinder", ActorRole.Damage, x: 9000, z: 3000, maxHealth: 10000, maxResource: 100);
        var chanter = Make(ChanterId, ActorKind.Player, "chanter", ActorRole.Healer, x: 9000, z: -3000, maxHealth: 10000, maxResource: 100);
        var caster = Make(CasterId, ActorKind.Enemy, "bellows.keeper", maxHealth: 5000);
        var ally = Make(AllyId, ActorKind.Enemy, "vault.brute", x: -1500, z: 1500, maxHealth: 5000);
        var state = At(0, player, cinder, chanter, caster, ally);
        var abilities = new Dictionary<string, AbilityDef>(Content.Abilities)
        {
            [Undertow.Id] = Undertow,
            [Stoke.Id] = Stoke,
            [Hex.Id] = Hex,
            [Soak.Id] = Soak,
            [Swing.Id] = Swing,
            [Probe.Id] = Probe,
        };
        var effects = new Dictionary<string, EffectDef>(Content.Effects)
        {
            [Rooted.Id] = Rooted,
            [Stoked.Id] = Stoked,
            [Drenched.Id] = Drenched,
            [SearingHex.Id] = SearingHex,
        };
        return new Run(state, Rules.Make(Config(abilities, effects, boundsHalf: 50000)));
    }

    private sealed class Run
    {
        private readonly StepRules _rules;

        public Run(SimState state, StepRules rules)
        {
            State = state;
            _rules = rules;
        }

        public SimState State { get; }

        public ActorState Player => State.Actors[PlayerId];

        public ActorState Caster => State.Actors[CasterId];

        public ActorState Ally => State.Actors[AllyId];

        public List<SimEvent> Step(params Command[] cmds)
        {
            var (events, emit) = Collector();
            Tick(State, _rules, cmds, emit);
            return events;
        }

        public void RunTo(int tick)
        {
            while (State.Tick < tick)
            {
                Step();
            }
        }

        // Starts the cast and steps until it completes (its cast_complete).
        public void CastAndFinish(int actorId, string abilityId, int targetId)
        {
            var events = Step(Cast(actorId, abilityId, targetId));
            for (var guard = 0; guard < 200 && !events.Any(e => e.Type == "cast_complete" && Str(e, "abilityId") == abilityId); guard++)
            {
                events = Step();
            }
            Assert.Contains(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == abilityId);
        }
    }
}
