using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// ADDS-DIE-WITH-BOSS (Matt 2026-10-20, BIBLE s9: "the Twins die with the Hollow Warden. The boss's death ends the
/// encounter, and any adds still alive despawn, as in WoW."). TwinsDieWithBossTests covers the Sundered Twins; this
/// covers the other adds a boss pull can hold: an Echo add (Echo Clause, BIBLE s8) standing when the boss dies falls
/// on the boss's death tick with a <c>death</c> event, an Echo spawn due on that step never realises, and nothing takes
/// damage after the boss is dead. Everything runs through <see cref="RunRoot.StepRun"/> on the armed boss run.
/// </summary>
public sealed class AddsDieWithBossTests
{
    private const int EchoAdd = EchoSpawns.EchoActorIdBase;

    private static bool IsDeathOf(SimEvent e, long actorId) =>
        e.Type == "death" && e.Get("actorId") is JsInt id && id.Value == actorId;

    /// <summary>The armed boss run past the Twins' spawn, with one Echo add standing beside the boss.</summary>
    private static Run WithEchoAdd(IReadOnlyList<EchoSpawnRequest>? pending = null)
    {
        var (live, _) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 3);
        live.State.Actors[EchoAdd] = new ActorState
        {
            Id = EchoAdd,
            Kind = ActorKind.Enemy,
            ClassId = "echo-add",
            Role = ActorRole.None,
            Pos = new Vec3(0, 0, 1000),
            Yaw = 0,
            Health = 600,
            MaxHealth = 600,
            Resource = 0,
            MaxResource = 0,
            Alive = true,
        };
        return live with
        {
            Encounter = live.Encounter with
            {
                NextEchoActorId = EchoAdd + 1,
                PendingEchoSpawns = pending ?? System.Array.Empty<EchoSpawnRequest>(),
            },
        };
    }

    /// <summary>
    /// Puts the boss on 1 hp and steps <paramref name="ticks"/> ticks with the Warden striking it every tick. Returns
    /// the run, every event, and the post-step tick of the step whose events hold the boss's death (-1 if none).
    /// </summary>
    private static (Run Run, List<SimEvent> Events, int DeathStepTick) KillBoss(Run run, int ticks)
    {
        run.State.Actors[BossTestKit.Boss].Health = 1;
        var events = new List<SimEvent>();
        var deathStepTick = -1;
        for (var i = 0; i < ticks; i++)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, new Command[] { new CastCommand(tick, BossTestKit.Warden, "warden.strike", BossTestKit.Boss) });
            run = stepped.Run;
            events.AddRange(stepped.Events);
            if (deathStepTick < 0 && stepped.Events.Any(e => IsDeathOf(e, BossTestKit.Boss)))
            {
                deathStepTick = run.State.Tick;
            }
        }
        return (run, events, deathStepTick);
    }

    [Fact(DisplayName = "An Echo add standing when the Hollow Warden dies falls on the boss's death tick with a death event")]
    public void Echo_add_dies_on_the_boss_death_tick()
    {
        var (after, events, deathStep) = KillBoss(WithEchoAdd(), 60);

        Assert.True(deathStep > 0, "the boss never died");
        var bossDeath = Assert.Single(events, e => IsDeathOf(e, BossTestKit.Boss));
        var addDeath = Assert.Single(events, e => IsDeathOf(e, EchoAdd));
        Assert.Equal(bossDeath.Tick, addDeath.Tick);
        Assert.Equal("boss_death", Assert.IsType<JsString>(addDeath.Get("reason")).Value);
        Assert.Equal(BossTestKit.Boss, Assert.IsType<JsInt>(addDeath.Get("sourceId")).Value);
        Assert.False(after.State.Actors[EchoAdd].Alive);
        Assert.Equal(0, after.State.Actors[EchoAdd].Health);
        Assert.Equal(RunResult.Timed, after.RunEnd.Result);
        Assert.DoesNotContain(after.State.Actors.Values, a => a.Kind == ActorKind.Enemy && a.Alive);
    }

    [Fact(DisplayName = "An Echo add mid-cast when the boss dies falls holding no cast (f-42df9d4f)")]
    public void Echo_add_mid_cast_when_the_boss_dies_falls_with_its_cast_ended()
    {
        var run = WithEchoAdd();
        var add = run.State.Actors[EchoAdd];
        add.CastId = "twins.slam";
        add.CastStartTick = run.State.Tick;
        add.CastEndsTick = run.State.Tick + 500;

        var (after, events, deathStep) = KillBoss(run, 60);

        Assert.True(deathStep > 0, "the boss never died");
        Assert.Single(events, e => IsDeathOf(e, EchoAdd));
        var fallen = after.State.Actors[EchoAdd];
        Assert.False(fallen.Alive);
        Assert.Null(fallen.CastId);
        Assert.Null(fallen.CastEndsTick);
    }

    [Fact(DisplayName = "An Echo spawn due on the boss's death step never realises; the queue is dropped")]
    public void Echo_spawn_due_on_the_boss_death_step_never_realises()
    {
        // Probe: the same run with the request parked far out (a pending request touches nothing until due), to find
        // the post-step tick the boss dies on. The sim is deterministic, so the real run dies on the same step.
        var far = new[] { new EchoSpawnRequest(1_000_000, BossTestKit.TwinA, 600) };
        var (_, _, deathStep) = KillBoss(WithEchoAdd(far), 60);
        Assert.True(deathStep > 0, "the boss never died in the probe");

        var due = new[] { new EchoSpawnRequest(deathStep, BossTestKit.TwinA, 600) };
        var (after, events, realDeathStep) = KillBoss(WithEchoAdd(due), 60);

        Assert.Equal(deathStep, realDeathStep);
        Assert.DoesNotContain(events, e => e.Type == "echo_spawn");
        Assert.DoesNotContain(after.State.Actors.Values, a => a.Id > EchoAdd);
        Assert.Empty(after.Encounter.PendingEchoSpawns);
        Assert.DoesNotContain(after.State.Actors.Values, a => a.Kind == ActorKind.Enemy && a.Alive);
    }

    [Fact(DisplayName = "Nothing is damaged after the Hollow Warden dies, even with the party still swinging")]
    public void Nothing_is_damaged_after_the_boss_dies()
    {
        var (dead, events, _) = KillBoss(WithEchoAdd(), 60);
        var bossDeath = Assert.Single(events, e => IsDeathOf(e, BossTestKit.Boss));
        var health = dead.State.Actors.Values.ToDictionary(a => a.Id, a => a.Health);
        var from = dead.State.Tick;
        var swings = Enumerable.Range(from, 120)
            .SelectMany(t => new Command[]
            {
                new CastCommand(t, BossTestKit.Warden, "warden.strike", EchoAdd),
                new CastCommand(t, BossTestKit.Cinder, "cinder.coldSnap", BossTestKit.TwinA),
            })
            .ToList();

        var (after, later) = BossTestKit.StepUntil(dead, from + 120, swings);

        Assert.DoesNotContain(events.Concat(later), e => e.Type == "damage" && e.Tick > bossDeath.Tick);
        Assert.DoesNotContain(later, e => e.Type == "death");
        Assert.All(after.State.Actors.Values, a => Assert.Equal(health[a.Id], a.Health));
    }

    [Fact(DisplayName = "Control: while the boss lives an Echo add stays up")]
    public void Control_echo_add_lives_while_the_boss_lives()
    {
        var (after, events) = BossTestKit.StepUntil(WithEchoAdd(), 200);

        Assert.True(after.State.Actors[BossTestKit.Boss].Alive);
        Assert.DoesNotContain(events, e => IsDeathOf(e, EchoAdd));
        Assert.True(after.State.Actors[EchoAdd].Alive);
    }
}
