using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.BossTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// PURPOSE: this artifact exists to make a break-bar stun actually stop an enemy's cast and actions in keyrun, closing
/// finding f-8b3fff18 (BIBLE s9.3 "Stops as well as kicks"); status: real.
/// BIBLE s9 (the Sundered Twins: "at empty both are stunned for a burst window") and s9.3 ("Stops as well as kicks"):
/// when the shared Break bar empties, a Twin mid-slam has that slam ended before it resolves, starts no new cast, swing
/// or chase move while the stun holds, and resumes its script once the stun expires. Driven through
/// <see cref="RunRoot.StepRun"/> on the armed Hollow Warden only.
/// </summary>
public sealed class StaggerStopsTwinsTests
{
    private const string Slam = "twins.slam";

    private static string? CastOf(Run run, int actorId) =>
        run.State.Actors.TryGetValue(actorId, out var actor) ? actor.CastId : null;

    private static Run StepOne(Run run, List<SimEvent> events, params Command[] commands)
    {
        var stepped = RunRoot.StepRun(run, commands);
        events.AddRange(stepped.Events);
        return stepped.Run;
    }

    private static bool By(SimEvent e, string key, int actorId) => e.Get(key) is JsInt { Value: var id } && id == actorId;

    private static bool IsTwinSlamComplete(SimEvent e) =>
        e.Type == "cast_complete" && e.Get("abilityId") is JsString { Value: Slam } && (By(e, "actorId", TwinA) || By(e, "actorId", TwinB));

    private static bool IsTwinDamage(SimEvent e) => e.Type == "damage" && (By(e, "sourceId", TwinA) || By(e, "sourceId", TwinB));

    [Fact(DisplayName = "Stagger: a Twin mid-slam is stopped, starts nothing while stunned, then resumes its script")]
    public void Stagger_stops_a_twin_mid_slam_and_holds_it_until_the_stun_expires()
    {
        var run = BuildRun();
        var events = new List<SimEvent>();

        // 1. Step until both Twins are mid-slam (their script loops every 60 ticks; the slam is a 10-tick cast).
        while (CastOf(run, TwinA) != Slam)
        {
            Assert.True(run.State.Tick < 300, "TwinA never started a slam");
            run = StepOne(run, events);
        }
        var castEnds = run.State.Actors[TwinA].CastEndsTick!.Value;
        var t = run.State.Tick;
        Assert.Equal(Slam, CastOf(run, TwinB));
        Assert.True(castEnds - t >= 4, "the slam must still be pending three ticks from now");

        // 2. Empty the shared bar off both Twins (34 + 34 + 34 >= 100): it staggers on tick t + 2.
        var fromStagger = events.Count;
        run = StepOne(run, events, new CastCommand(t, Warden, "warden.shieldBash", TwinA));
        run = StepOne(run, events, new CastCommand(t + 1, Chanter, "chanter.rebuke", TwinB));
        run = StepOne(run, events, new CastCommand(t + 2, Cinder, "cinder.quench", TwinA));
        Assert.Single(events.Skip(fromStagger), e => e.Type == "stagger");
        Assert.True(run.State.Tick < castEnds, "the stun must land before the slam was due");
        var stunEnds = run.State.Actors[TwinA].Effects.Single(e => e.EffectId == "stunned").EndsTick;
        Assert.True(stunEnds > castEnds + 60, "the 100-tick stun must outlast the slam and the next script loop");

        // 3. The stun ends the in-progress slam on both Twins, at once, with a cast_stopped each.
        Assert.Null(CastOf(run, TwinA));
        Assert.Null(CastOf(run, TwinB));
        var stopped = events.Skip(fromStagger).Where(e => e.Type == "cast_stopped" && e.Get("abilityId") is JsString { Value: Slam }).ToList();
        Assert.Contains(stopped, e => By(e, "actorId", TwinA));
        Assert.Contains(stopped, e => By(e, "actorId", TwinB));

        // 4. While stunned neither Twin starts a cast, swing or move, and no slam resolves or deals damage.
        var posA = run.State.Actors[TwinA].Pos;
        var posB = run.State.Actors[TwinB].Pos;
        while (run.State.Tick < stunEnds)
        {
            run = StepOne(run, events);
            Assert.Null(CastOf(run, TwinA));
            Assert.Null(CastOf(run, TwinB));
        }
        Assert.Equal(posA, run.State.Actors[TwinA].Pos);
        Assert.Equal(posB, run.State.Actors[TwinB].Pos);
        var duringStun = events.Skip(fromStagger).ToList();
        Assert.DoesNotContain(duringStun, IsTwinSlamComplete);
        Assert.DoesNotContain(duringStun, IsTwinDamage);

        // 5. Once the stun expires the script resumes: the next loop's slam resolves and lands.
        var afterStun = events.Count;
        while (run.State.Tick < stunEnds + 80)
        {
            run = StepOne(run, events);
        }
        var resumed = events.Skip(afterStun).Where(IsTwinSlamComplete).ToList();
        Assert.Contains(resumed, e => By(e, "actorId", TwinA));
        Assert.Contains(resumed, e => By(e, "actorId", TwinB));
        Assert.All(resumed, e => Assert.True(e.Tick >= stunEnds));
    }

    [Fact(DisplayName = "Stagger: with no stagger the same Twins slam on schedule (control)")]
    public void Without_a_stagger_the_twins_slam_on_schedule()
    {
        var run = BuildRun();
        var events = new List<SimEvent>();
        while (run.State.Tick < 200)
        {
            run = StepOne(run, events);
        }

        Assert.DoesNotContain(events, e => e.Type == "cast_stopped");
        Assert.Contains(events, e => IsTwinSlamComplete(e) && By(e, "actorId", TwinA));
        Assert.Contains(events, IsTwinDamage);
    }
}
