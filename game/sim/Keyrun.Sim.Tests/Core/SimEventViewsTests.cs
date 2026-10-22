using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

// Namespace Keyrun.Sim.Tests (not .Core): a Keyrun.Sim.Tests.Core namespace would shadow the Core.Tick that other
// files in this project write against Keyrun.Sim.Core.
namespace Keyrun.Sim.Tests;

/// <summary>
/// Typed views over the sim's string-keyed events (<see cref="SimEventViews"/>). Each view is round-tripped from a
/// hand-built <see cref="SimEvent"/> in the exact field shape its emit site writes, and refused (false, never a throw)
/// for any other type tag. The end-to-end case replays the bots-hv-k2-s7 oracle run's recorded bot commands through
/// <see cref="RunRoot.StepRun"/> for 800 ticks and finds the Choral Mending kick with <see cref="SimEventViews.TryInterrupt"/>,
/// so the views read the events the sim really emits, not only the ones built here.
/// </summary>
public sealed class SimEventViewsTests
{
    private static JsValue I(long v) => JsValue.Int(v);

    private static JsValue S(string v) => JsValue.Str(v);

    private static JsValue B(bool v) => JsValue.Bool(v);

    private static JsValue Ids(params long[] ids) => JsValue.Arr(ids.Select(JsValue.Int));

    private static SimEvent Ev(int tick, string type, params (string, JsValue)[] fields) => new(tick, type, fields);

    /// <summary>The same event under another type tag: every view must refuse it.</summary>
    private static SimEvent Retyped(SimEvent e) => new(e.Tick, "not_" + e.Type, e.Fields.Members);

    // ---- overheat (Combat/Casting.cs:279)

    private static SimEvent Overheat() => Ev(12, "overheat", ("actorId", I(3)), ("abilityId", S("cinder.flare")));

    [Fact]
    public void Overheat_round_trips()
    {
        Assert.True(SimEventViews.TryOverheat(Overheat(), out var v));
        Assert.Equal(new OverheatView(12, 3, "cinder.flare"), v);
    }

    [Fact]
    public void Overheat_refuses_other_type() => Assert.False(SimEventViews.TryOverheat(Retyped(Overheat()), out _));

    // ---- cast_stopped (Combat/Casting.cs:300)

    private static SimEvent CastStopped() => Ev(40, "cast_stopped", ("actorId", I(102)), ("abilityId", S("choir.healCast")));

    [Fact]
    public void CastStopped_round_trips()
    {
        Assert.True(SimEventViews.TryCastStopped(CastStopped(), out var v));
        Assert.Equal(new CastStoppedView(40, 102, "choir.healCast"), v);
    }

    [Fact]
    public void CastStopped_refuses_other_type() => Assert.False(SimEventViews.TryCastStopped(Retyped(CastStopped()), out _));

    // ---- cast_complete (Combat/Casting.cs:413)

    private static SimEvent CastComplete() => Ev(41, "cast_complete", ("actorId", I(2)), ("abilityId", S("chanter.dissonance")));

    [Fact]
    public void CastComplete_round_trips()
    {
        Assert.True(SimEventViews.TryCastComplete(CastComplete(), out var v));
        Assert.Equal(new CastCompleteView(41, 2, "chanter.dissonance"), v);
    }

    [Fact]
    public void CastComplete_refuses_other_type() => Assert.False(SimEventViews.TryCastComplete(Retyped(CastComplete()), out _));

    // ---- interrupt (Combat/Casting.cs:508 kick; Run/ControlStops.cs:94 stop, with "stop")

    private static SimEvent Interrupt() => Ev(197, "interrupt",
        ("sourceId", I(2)), ("targetId", I(102)), ("abilityId", S("choir.healCast")), ("school", S("physical")));

    [Fact]
    public void Interrupt_round_trips()
    {
        Assert.True(SimEventViews.TryInterrupt(Interrupt(), out var v));
        Assert.Equal(new InterruptView(197, 2, 102, "choir.healCast", "physical", null), v);
    }

    [Fact]
    public void Interrupt_from_a_stop_carries_its_effect()
    {
        var e = Ev(50, "interrupt", ("sourceId", I(1)), ("targetId", I(101)), ("abilityId", S("seed.slam")),
            ("school", S("physical")), ("stop", S("stunned")));
        Assert.True(SimEventViews.TryInterrupt(e, out var v));
        Assert.Equal(new InterruptView(50, 1, 101, "seed.slam", "physical", "stunned"), v);
    }

    [Fact]
    public void Interrupt_refuses_other_type() => Assert.False(SimEventViews.TryInterrupt(Retyped(Interrupt()), out _));

    [Fact]
    public void Interrupt_refuses_a_missing_field()
    {
        var e = Ev(197, "interrupt", ("sourceId", I(2)), ("targetId", I(102)), ("abilityId", S("choir.healCast")));
        Assert.False(SimEventViews.TryInterrupt(e, out var v));
        Assert.Equal(default, v);
    }

    [Fact]
    public void Interrupt_refuses_a_field_of_the_wrong_kind()
    {
        var e = Ev(197, "interrupt", ("sourceId", S("2")), ("targetId", I(102)), ("abilityId", S("choir.healCast")), ("school", S("physical")));
        Assert.False(SimEventViews.TryInterrupt(e, out _));
    }

    [Fact]
    public void Interrupt_refuses_an_id_out_of_int_range()
    {
        var e = Ev(197, "interrupt", ("sourceId", I(long.MaxValue)), ("targetId", I(102)), ("abilityId", S("choir.healCast")), ("school", S("physical")));
        Assert.False(SimEventViews.TryInterrupt(e, out _));
    }

    [Fact]
    public void A_null_event_is_refused_not_thrown() => Assert.False(SimEventViews.TryInterrupt(null!, out _));

    // ---- damage (Combat/Damage.cs:120)

    private static SimEvent DamageEv() => Ev(60, "damage", ("sourceId", I(3)), ("targetId", I(100)), ("rawAmount", I(400)),
        ("modifiedAmount", I(380)), ("absorbed", I(30)), ("healthDamage", I(350)));

    [Fact]
    public void Damage_round_trips()
    {
        Assert.True(SimEventViews.TryDamage(DamageEv(), out var v));
        Assert.Equal(new DamageView(60, 3, 100, 400, 380, 30, 350), v);
    }

    [Fact]
    public void Damage_refuses_other_type() => Assert.False(SimEventViews.TryDamage(Retyped(DamageEv()), out _));

    // ---- death (Combat/Damage.cs:131; Encounter/Boss.cs:488 and Encounter/EchoSpawns.cs:68 add "reason")

    private static SimEvent Death() => Ev(70, "death", ("actorId", I(101)), ("sourceId", I(4)));

    [Fact]
    public void Death_round_trips()
    {
        Assert.True(SimEventViews.TryDeath(Death(), out var v));
        Assert.Equal(new DeathView(70, 101, 4, null), v);
    }

    [Fact]
    public void Death_with_boss_reason_round_trips()
    {
        var e = Ev(900, "death", ("actorId", I(901)), ("sourceId", I(900)), ("reason", S("boss_death")));
        Assert.True(SimEventViews.TryDeath(e, out var v));
        Assert.Equal(new DeathView(900, 901, 900, "boss_death"), v);
    }

    [Fact]
    public void Death_refuses_other_type() => Assert.False(SimEventViews.TryDeath(Retyped(Death()), out _));

    // ---- heal (Combat/Damage.cs:172)

    private static SimEvent Heal() => Ev(80, "heal", ("sourceId", I(2)), ("targetId", I(1)), ("amount", I(600)));

    [Fact]
    public void Heal_round_trips()
    {
        Assert.True(SimEventViews.TryHeal(Heal(), out var v));
        Assert.Equal(new HealView(80, 2, 1, 600), v);
    }

    [Fact]
    public void Heal_refuses_other_type() => Assert.False(SimEventViews.TryHeal(Retyped(Heal()), out _));

    // ---- miss (Combat/Mitigation.cs:42)

    private static SimEvent Miss() => Ev(81, "miss", ("sourceId", I(100)), ("targetId", I(1)), ("rawAmount", I(250)));

    [Fact]
    public void Miss_round_trips()
    {
        Assert.True(SimEventViews.TryMiss(Miss(), out var v));
        Assert.Equal(new MissView(81, 100, 1, 250), v);
    }

    [Fact]
    public void Miss_refuses_other_type() => Assert.False(SimEventViews.TryMiss(Retyped(Miss()), out _));

    // ---- mark_applied (Combat/PairMarks.cs:144)

    private static SimEvent MarkApplied() => Ev(90, "mark_applied", ("cast", S("pair.spreadCall")), ("kind", S("spread")),
        ("sourceId", I(106)), ("targetIds", Ids(1, 3)), ("radius", I(5000)), ("detonatesTick", I(150)));

    [Fact]
    public void MarkApplied_round_trips()
    {
        Assert.True(SimEventViews.TryMarkApplied(MarkApplied(), out var v));
        Assert.Equal(90, v.Tick);
        Assert.Equal("pair.spreadCall", v.Cast);
        Assert.Equal("spread", v.Kind);
        Assert.Equal(106, v.SourceId);
        Assert.Equal(new[] { 1, 3 }, v.TargetIds);
        Assert.Equal(5000, v.Radius);
        Assert.Equal(150, v.DetonatesTick);
    }

    [Fact]
    public void MarkApplied_refuses_other_type() => Assert.False(SimEventViews.TryMarkApplied(Retyped(MarkApplied()), out _));

    // ---- mark_detonated (Combat/PairMarks.cs:185)

    private static SimEvent MarkDetonated() => Ev(150, "mark_detonated", ("kind", S("spread")), ("markedId", I(1)),
        ("hitIds", Ids(1, 2)), ("damage", JsValue.Arr(
            JsValue.Obj(("actorId", I(1)), ("amount", I(900))),
            JsValue.Obj(("actorId", I(2)), ("amount", I(450))))));

    [Fact]
    public void MarkDetonated_round_trips()
    {
        Assert.True(SimEventViews.TryMarkDetonated(MarkDetonated(), out var v));
        Assert.Equal(150, v.Tick);
        Assert.Equal("spread", v.Kind);
        Assert.Equal(1, v.MarkedId);
        Assert.Equal(new[] { 1, 2 }, v.HitIds);
        Assert.Equal(new[] { new MarkHit(1, 900), new MarkHit(2, 450) }, v.Damage);
    }

    [Fact]
    public void MarkDetonated_refuses_other_type() => Assert.False(SimEventViews.TryMarkDetonated(Retyped(MarkDetonated()), out _));

    // ---- combo_denied (Combat/Payoff.cs:67 on a target; Combat/Payoff.cs:141 on the floor, no targetId)

    private static SimEvent ComboDenied() => Ev(100, "combo_denied", ("windowId", S("w.chill")), ("targetId", I(100)),
        ("setupActorId", I(3)), ("payoffActorId", I(4)), ("reason", S("self_combo")));

    [Fact]
    public void ComboDenied_round_trips()
    {
        Assert.True(SimEventViews.TryComboDenied(ComboDenied(), out var v));
        Assert.Equal(new ComboDeniedView(100, "w.chill", 100, 3, 4, "self_combo"), v);
    }

    [Fact]
    public void ComboDenied_on_the_floor_has_no_target()
    {
        var e = Ev(100, "combo_denied", ("windowId", S("w.floor")), ("setupActorId", I(3)), ("payoffActorId", I(4)), ("reason", S("self_combo")));
        Assert.True(SimEventViews.TryComboDenied(e, out var v));
        Assert.Equal(new ComboDeniedView(100, "w.floor", null, 3, 4, "self_combo"), v);
    }

    [Fact]
    public void ComboDenied_refuses_other_type() => Assert.False(SimEventViews.TryComboDenied(Retyped(ComboDenied()), out _));

    // ---- combo (Combat/Payoff.cs:89 on a target; Combat/Payoff.cs:157 on the floor, "field": true and no targetId)

    private static SimEvent Combo() => Ev(101, "combo", ("comboId", S("shatter")), ("windowId", S("w.chill")), ("setupActorId", I(3)),
        ("setupClass", S("cinder")), ("payoffActorId", I(1)), ("payoffClass", S("warden")), ("targetId", I(100)));

    [Fact]
    public void Combo_round_trips()
    {
        Assert.True(SimEventViews.TryCombo(Combo(), out var v));
        Assert.Equal(new ComboView(101, "shatter", "w.chill", 3, "cinder", 1, "warden", 100, false), v);
    }

    [Fact]
    public void Combo_on_the_floor_is_a_field_combo()
    {
        var e = Ev(101, "combo", ("comboId", S("consecrate")), ("windowId", S("w.floor")), ("setupActorId", I(2)),
            ("setupClass", S("chanter")), ("payoffActorId", I(1)), ("payoffClass", S("warden")), ("field", B(true)));
        Assert.True(SimEventViews.TryCombo(e, out var v));
        Assert.Equal(new ComboView(101, "consecrate", "w.floor", 2, "chanter", 1, "warden", null, true), v);
    }

    [Fact]
    public void Combo_refuses_other_type() => Assert.False(SimEventViews.TryCombo(Retyped(Combo()), out _));

    // ---- cast_refused (Combat/Rules.cs:200)

    private static SimEvent CastRefused() => Ev(5, "cast_refused", ("actorId", I(1)), ("abilityId", S("warden.strike")), ("reason", S("out_of_range")));

    [Fact]
    public void CastRefused_round_trips()
    {
        Assert.True(SimEventViews.TryCastRefused(CastRefused(), out var v));
        Assert.Equal(new CastRefusedView(5, 1, "warden.strike", "out_of_range"), v);
    }

    [Fact]
    public void CastRefused_refuses_other_type() => Assert.False(SimEventViews.TryCastRefused(Retyped(CastRefused()), out _));

    // ---- window_opened (Combat/Windows.cs:95 on a target; Combat/Windows.cs:108 on the floor, with "telegraph")

    private static SimEvent WindowOpened() => Ev(110, "window_opened", ("windowId", S("w.chill")), ("onTarget", B(true)),
        ("setupActorId", I(3)), ("setupClass", S("cinder")), ("targetId", I(100)), ("endsTick", I(170)));

    [Fact]
    public void WindowOpened_round_trips()
    {
        Assert.True(SimEventViews.TryWindowOpened(WindowOpened(), out var v));
        Assert.Equal(new WindowOpenedView(110, "w.chill", true, 3, "cinder", 100, 170, null), v);
    }

    [Fact]
    public void WindowOpened_on_the_floor_keeps_its_telegraph()
    {
        var tg = JsValue.Obj(("shape", S("circle")), ("radius", I(3000)));
        var e = Ev(110, "window_opened", ("windowId", S("w.floor")), ("onTarget", B(false)), ("setupActorId", I(2)),
            ("setupClass", S("chanter")), ("endsTick", I(170)), ("telegraph", tg));
        Assert.True(SimEventViews.TryWindowOpened(e, out var v));
        Assert.Equal("w.floor", v.WindowId);
        Assert.False(v.OnTarget);
        Assert.Null(v.TargetId);
        Assert.Equal(170, v.EndsTick);
        Assert.Equal(tg, v.Telegraph);
    }

    [Fact]
    public void WindowOpened_refuses_other_type() => Assert.False(SimEventViews.TryWindowOpened(Retyped(WindowOpened()), out _));

    // ---- window_expired (Combat/Windows.cs:145 on a target; Combat/Windows.cs:182 on the floor)

    private static SimEvent WindowExpired() => Ev(170, "window_expired", ("windowId", S("w.lock")), ("onTarget", B(true)),
        ("setupActorId", I(900)), ("setupClass", S("hollowWarden")), ("targetId", I(900)), ("lock", B(true)));

    [Fact]
    public void WindowExpired_round_trips()
    {
        Assert.True(SimEventViews.TryWindowExpired(WindowExpired(), out var v));
        Assert.Equal(new WindowExpiredView(170, "w.lock", true, 900, "hollowWarden", 900, true), v);
    }

    [Fact]
    public void WindowExpired_on_the_floor_has_no_target_and_no_lock()
    {
        var e = Ev(170, "window_expired", ("windowId", S("w.floor")), ("onTarget", B(false)), ("setupActorId", I(2)), ("setupClass", S("chanter")));
        Assert.True(SimEventViews.TryWindowExpired(e, out var v));
        Assert.Equal(new WindowExpiredView(170, "w.floor", false, 2, "chanter", null, false), v);
    }

    [Fact]
    public void WindowExpired_refuses_other_type() => Assert.False(SimEventViews.TryWindowExpired(Retyped(WindowExpired()), out _));

    // ---- lock_enrage (Combat/Windows.cs:160)

    private static SimEvent LockEnrage() => Ev(171, "lock_enrage", ("actorId", I(900)));

    [Fact]
    public void LockEnrage_round_trips()
    {
        Assert.True(SimEventViews.TryLockEnrage(LockEnrage(), out var v));
        Assert.Equal(new LockEnrageView(171, 900), v);
    }

    [Fact]
    public void LockEnrage_refuses_other_type() => Assert.False(SimEventViews.TryLockEnrage(Retyped(LockEnrage()), out _));

    // ---- pull_start (Encounter/Pulls.cs:149; Encounter/Aggro.cs:208)

    private static SimEvent PullStart() => Ev(0, "pull_start", ("pullIndex", I(0)), ("pullId", S("entry")), ("actorIds", Ids(100, 101, 102)));

    [Fact]
    public void PullStart_round_trips()
    {
        Assert.True(SimEventViews.TryPullStart(PullStart(), out var v));
        Assert.Equal(0, v.Tick);
        Assert.Equal(0, v.PullIndex);
        Assert.Equal("entry", v.PullId);
        Assert.Equal(new[] { 100, 101, 102 }, v.ActorIds);
    }

    [Fact]
    public void PullStart_refuses_other_type() => Assert.False(SimEventViews.TryPullStart(Retyped(PullStart()), out _));

    [Fact]
    public void PullStart_refuses_a_non_integer_actor_id()
    {
        var e = Ev(0, "pull_start", ("pullIndex", I(0)), ("pullId", S("entry")), ("actorIds", JsValue.Arr(I(100), S("101"))));
        Assert.False(SimEventViews.TryPullStart(e, out _));
    }

    // ---- pack_engage (Encounter/Aggro.cs:209)

    private static SimEvent PackEngage() => Ev(300, "pack_engage", ("packId", S("pack.corridor")), ("actorIds", Ids(200, 201)));

    [Fact]
    public void PackEngage_round_trips()
    {
        Assert.True(SimEventViews.TryPackEngage(PackEngage(), out var v));
        Assert.Equal(300, v.Tick);
        Assert.Equal("pack.corridor", v.PackId);
        Assert.Equal(new[] { 200, 201 }, v.ActorIds);
    }

    [Fact]
    public void PackEngage_refuses_other_type() => Assert.False(SimEventViews.TryPackEngage(Retyped(PackEngage()), out _));

    // ---- lock_failed (Encounter/Boss.cs:279)

    private static SimEvent LockFailed() => Ev(400, "lock_failed", ("actorId", I(900)), ("lockIndex", I(1)), ("fails", I(1)), ("tempoBp", I(11000)));

    [Fact]
    public void LockFailed_round_trips()
    {
        Assert.True(SimEventViews.TryLockFailed(LockFailed(), out var v));
        Assert.Equal(new LockFailedView(400, 900, 1, 1, 11000), v);
    }

    [Fact]
    public void LockFailed_refuses_other_type() => Assert.False(SimEventViews.TryLockFailed(Retyped(LockFailed()), out _));

    // ---- boss_enrage (Encounter/Boss.cs:287)

    private static SimEvent BossEnrage() => Ev(401, "boss_enrage", ("actorId", I(900)), ("tempoBp", I(15000)));

    [Fact]
    public void BossEnrage_round_trips()
    {
        Assert.True(SimEventViews.TryBossEnrage(BossEnrage(), out var v));
        Assert.Equal(new BossEnrageView(401, 900, 15000), v);
    }

    [Fact]
    public void BossEnrage_refuses_other_type() => Assert.False(SimEventViews.TryBossEnrage(Retyped(BossEnrage()), out _));

    // ---- lock_cast (Encounter/Boss.cs:322)

    private static SimEvent LockCast() => Ev(380, "lock_cast", ("actorId", I(900)), ("lockIndex", I(1)), ("landsTick", I(400)));

    [Fact]
    public void LockCast_round_trips()
    {
        Assert.True(SimEventViews.TryLockCast(LockCast(), out var v));
        Assert.Equal(new LockCastView(380, 900, 1, 400), v);
    }

    [Fact]
    public void LockCast_refuses_other_type() => Assert.False(SimEventViews.TryLockCast(Retyped(LockCast()), out _));

    // ---- lock_open (Encounter/Boss.cs:344)

    private static SimEvent LockOpen() => Ev(400, "lock_open", ("actorId", I(900)), ("lockIndex", I(1)), ("endsTick", I(500)));

    [Fact]
    public void LockOpen_round_trips()
    {
        Assert.True(SimEventViews.TryLockOpen(LockOpen(), out var v));
        Assert.Equal(new LockOpenView(400, 900, 1, 500), v);
    }

    [Fact]
    public void LockOpen_refuses_other_type() => Assert.False(SimEventViews.TryLockOpen(Retyped(LockOpen()), out _));

    // ---- boss_add_spawn (Encounter/Boss.cs:465)

    private static SimEvent BossAddSpawn() => Ev(350, "boss_add_spawn", ("actorId", I(901)), ("classId", S("hollowWarden.twinA")), ("bossActorId", I(900)));

    [Fact]
    public void BossAddSpawn_round_trips()
    {
        Assert.True(SimEventViews.TryBossAddSpawn(BossAddSpawn(), out var v));
        Assert.Equal(new BossAddSpawnView(350, 901, "hollowWarden.twinA", 900), v);
    }

    [Fact]
    public void BossAddSpawn_refuses_other_type() => Assert.False(SimEventViews.TryBossAddSpawn(Retyped(BossAddSpawn()), out _));

    // ---- lock_broken (Encounter/Boss.cs:524)

    private static SimEvent LockBroken() => Ev(420, "lock_broken", ("actorId", I(900)), ("lockIndex", I(1)), ("comboTick", I(419)),
        ("setupActorId", I(3)), ("setupClass", S("cinder")), ("payoffActorId", I(1)), ("payoffClass", S("warden")));

    [Fact]
    public void LockBroken_round_trips()
    {
        Assert.True(SimEventViews.TryLockBroken(LockBroken(), out var v));
        Assert.Equal(new LockBrokenView(420, 900, 1, 419, 3, "cinder", 1, "warden"), v);
    }

    [Fact]
    public void LockBroken_refuses_other_type() => Assert.False(SimEventViews.TryLockBroken(Retyped(LockBroken()), out _));

    // ---- stagger (Encounter/BreakBar.cs:95)

    private static SimEvent Stagger() => Ev(430, "stagger", ("barId", S("twinsBreak")), ("actorIds", Ids(901, 902)));

    [Fact]
    public void Stagger_round_trips()
    {
        Assert.True(SimEventViews.TryStagger(Stagger(), out var v));
        Assert.Equal(430, v.Tick);
        Assert.Equal("twinsBreak", v.BarId);
        Assert.Equal(new[] { 901, 902 }, v.ActorIds);
    }

    [Fact]
    public void Stagger_refuses_other_type() => Assert.False(SimEventViews.TryStagger(Retyped(Stagger()), out _));

    // ---- echo_spawn (Encounter/EchoSpawns.cs:109)

    private static SimEvent EchoSpawn() => Ev(440, "echo_spawn", ("actorId", I(1000)), ("sourceActorId", I(105)), ("maxHealth", I(800)));

    [Fact]
    public void EchoSpawn_round_trips()
    {
        Assert.True(SimEventViews.TryEchoSpawn(EchoSpawn(), out var v));
        Assert.Equal(new EchoSpawnView(440, 1000, 105, 800), v);
    }

    [Fact]
    public void EchoSpawn_refuses_other_type() => Assert.False(SimEventViews.TryEchoSpawn(Retyped(EchoSpawn()), out _));

    // ---- pull_clear (Encounter/Pulls.cs:176)

    private static SimEvent PullClear() => Ev(450, "pull_clear", ("pullIndex", I(2)), ("pullId", S("seed")));

    [Fact]
    public void PullClear_round_trips()
    {
        Assert.True(SimEventViews.TryPullClear(PullClear(), out var v));
        Assert.Equal(new PullClearView(450, 2, "seed"), v);
    }

    [Fact]
    public void PullClear_refuses_other_type() => Assert.False(SimEventViews.TryPullClear(Retyped(PullClear()), out _));

    // ---- telegraph (Encounter/Scripts.cs:173)

    private static SimEvent Telegraph() => Ev(460, "telegraph", ("actorId", I(105)), ("cast", S("seed.slam")), ("shape", S("circle")),
        ("leadTicks", I(20)), ("resolvesTick", I(480)), ("origin", JsValue.Vec(new Vec3(100, 0, -2000))), ("yaw", I(512)), ("radius", I(4000)));

    [Fact]
    public void Telegraph_round_trips()
    {
        Assert.True(SimEventViews.TryTelegraph(Telegraph(), out var v));
        Assert.Equal(new TelegraphView(460, 105, "seed.slam", "circle", 20, 480, new Vec3(100, 0, -2000), 512, 4000, null, null, null), v);
    }

    [Fact]
    public void Telegraph_cone_and_line_fields_round_trip()
    {
        var e = Ev(461, "telegraph", ("actorId", I(900)), ("cast", S("hollowWarden.cleave")), ("shape", S("rect")),
            ("leadTicks", I(30)), ("resolvesTick", I(491)), ("origin", JsValue.Vec(new Vec3(0, 0, 0))), ("yaw", I(0)),
            ("angle1024", I(128)), ("length", I(8000)), ("width", I(2000)));
        Assert.True(SimEventViews.TryTelegraph(e, out var v));
        Assert.Equal(new TelegraphView(461, 900, "hollowWarden.cleave", "rect", 30, 491, new Vec3(0, 0, 0), 0, null, 128, 8000, 2000), v);
    }

    [Fact]
    public void Telegraph_refuses_other_type() => Assert.False(SimEventViews.TryTelegraph(Retyped(Telegraph()), out _));

    [Fact]
    public void Telegraph_refuses_a_malformed_origin()
    {
        var e = Ev(460, "telegraph", ("actorId", I(105)), ("cast", S("seed.slam")), ("shape", S("circle")),
            ("leadTicks", I(20)), ("resolvesTick", I(480)), ("origin", JsValue.Obj(("x", I(1)), ("y", I(0)))), ("yaw", I(512)));
        Assert.False(SimEventViews.TryTelegraph(e, out _));
    }

    // ---- telegraph_hit (Encounter/Telegraphs.cs:270)

    private static SimEvent TelegraphHit() => Ev(480, "telegraph_hit", ("telegraphId", I(7)), ("actorIds", Ids(1, 2)));

    [Fact]
    public void TelegraphHit_round_trips()
    {
        Assert.True(SimEventViews.TryTelegraphHit(TelegraphHit(), out var v));
        Assert.Equal(480, v.Tick);
        Assert.Equal(7, v.TelegraphId);
        Assert.Equal(new[] { 1, 2 }, v.ActorIds);
    }

    [Fact]
    public void TelegraphHit_refuses_other_type() => Assert.False(SimEventViews.TryTelegraphHit(Retyped(TelegraphHit()), out _));

    // ---- telegraph_cancel (Encounter/Telegraphs.cs:332)

    private static SimEvent TelegraphCancel() => Ev(470, "telegraph_cancel", ("telegraphId", I(7)), ("actorId", I(105)), ("cast", S("seed.slam")));

    [Fact]
    public void TelegraphCancel_round_trips()
    {
        Assert.True(SimEventViews.TryTelegraphCancel(TelegraphCancel(), out var v));
        Assert.Equal(new TelegraphCancelView(470, 7, 105, "seed.slam"), v);
    }

    [Fact]
    public void TelegraphCancel_refuses_other_type() => Assert.False(SimEventViews.TryTelegraphCancel(Retyped(TelegraphCancel()), out _));

    // ---- finish (Encounter/Timer.cs:220)

    private static SimEvent Finish() => Ev(1503, "finish", ("result", S("timed")));

    [Fact]
    public void Finish_round_trips()
    {
        Assert.True(SimEventViews.TryFinish(Finish(), out var v));
        Assert.Equal(new FinishView(1503, "timed"), v);
    }

    [Fact]
    public void Finish_refuses_other_type() => Assert.False(SimEventViews.TryFinish(Retyped(Finish()), out _));

    // ---- immune (Run/ControlStops.cs:144)

    private static SimEvent Immune() => Ev(490, "immune", ("sourceId", I(1)), ("targetId", I(900)), ("effectId", S("stunned")));

    [Fact]
    public void Immune_round_trips()
    {
        Assert.True(SimEventViews.TryImmune(Immune(), out var v));
        Assert.Equal(new ImmuneView(490, 1, 900, "stunned"), v);
    }

    [Fact]
    public void Immune_refuses_other_type() => Assert.False(SimEventViews.TryImmune(Retyped(Immune()), out _));

    // ---- respawn (Run/Release.cs:75)

    private static SimEvent Respawn() => Ev(500, "respawn", ("actorId", I(4)));

    [Fact]
    public void Respawn_round_trips()
    {
        Assert.True(SimEventViews.TryRespawn(Respawn(), out var v));
        Assert.Equal(new RespawnView(500, 4), v);
    }

    [Fact]
    public void Respawn_refuses_other_type() => Assert.False(SimEventViews.TryRespawn(Retyped(Respawn()), out _));

    // ---- run_end (Run/Run.cs:289)

    private static SimEvent RunEnd() => Ev(1503, "run_end", ("result", S("timed")));

    [Fact]
    public void RunEnd_round_trips()
    {
        Assert.True(SimEventViews.TryRunEnd(RunEnd(), out var v));
        Assert.Equal(new RunEndView(1503, "timed"), v);
    }

    [Fact]
    public void RunEnd_refuses_other_type() => Assert.False(SimEventViews.TryRunEnd(Retyped(RunEnd()), out _));

    // ---- room_enter (Run/Run.cs:371)

    private static SimEvent RoomEnter() => Ev(510, "room_enter", ("actorId", I(1)), ("room", S("seed-chamber")));

    [Fact]
    public void RoomEnter_round_trips()
    {
        Assert.True(SimEventViews.TryRoomEnter(RoomEnter(), out var v));
        Assert.Equal(new RoomEnterView(510, 1, "seed-chamber"), v);
    }

    [Fact]
    public void RoomEnter_refuses_other_type() => Assert.False(SimEventViews.TryRoomEnter(Retyped(RoomEnter()), out _));

    // ---- end to end: real events from a real run

    /// <summary>
    /// The bots-hv-k2-s7 oracle run (setup hollow-vault-k2, seed 7, five bot-driven seats) replayed from its recorded
    /// commands through <see cref="RunRoot.StepRun"/> for 800 ticks: a seat kicks Choral Mending (choir.healCast), and
    /// <see cref="SimEventViews.TryInterrupt"/> reads that kick from the sim's own interrupt event.
    /// </summary>
    [Fact]
    public void A_real_run_has_a_seat_kicking_choral_mending()
    {
        var trace = OracleTrace.Load(OracleFixtures.PathOf("bots-hv-k2-s7.trace.jsonl"));
        var setup = RunSetup.Load(OracleFixtures.PathOf(trace.SetupFile), trace.Seed).WithTsKit();
        var seats = new HashSet<int>(setup.Party.Select(p => p.Id));
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var events = new List<SimEvent>();
        foreach (var record in trace.Records.Where(r => r.T > 0 && r.T <= 800))
        {
            var stepped = RunRoot.StepRun(run, record.Commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
        }
        Assert.Equal(800, run.State.Tick);
        var kicks = events.Select(e => SimEventViews.TryInterrupt(e, out var v) ? v : (InterruptView?)null)
            .Where(v => v is not null).Select(v => v!.Value).ToList();
        Assert.Contains(kicks, k => k.AbilityId == "choir.healCast" && seats.Contains(k.SourceId) && !seats.Contains(k.TargetId));
        // Every interrupt the run emitted reads through the view (none is refused for a shape the view does not know).
        Assert.Equal(events.Count(e => e.Type == "interrupt"), kicks.Count);
    }
}
