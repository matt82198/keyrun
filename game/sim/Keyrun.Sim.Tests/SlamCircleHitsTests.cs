using System.Linq;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests;

/// <summary>
/// MECHANICS-PUNISH G1 repro: design/BIBLE.md section 3, "Every dangerous enemy action is a cast with a visible bar
/// and a floor telegraph (circle, cone, line) drawn ahead of resolution". The Hollow Warden's slam draws a 3000 mm
/// circle; a player standing inside it at resolution must take the slam, one outside must not.
/// </summary>
public sealed class SlamCircleHitsTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public SlamCircleHitsTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void A_player_inside_the_drawn_slam_circle_is_hit_and_one_outside_is_not()
    {
        var run = BossTestKit.BuildRun(armed: false);
        // Chanter stands in the circle (2062 mm from the boss), not the boss's target; Cinder stands far outside.
        run.State.Actors[BossTestKit.Cinder].Pos = new Vec3(0, 0, 9000);
        var (_, events) = BossTestKit.StepUntil(run, 700);
        var slamTicks = events.Where(e => e.Type == "cast_complete" && e.IntField("actorId") == BossTestKit.Boss &&
            ((JsString)e.Get("abilityId")!).Value == "hollowWarden.slam").Select(e => e.Tick).ToList();
        _out.WriteLine("slam completes at ticks: " + string.Join(",", slamTicks));
        Assert.NotEmpty(slamTicks);
        var t = slamTicks[0];
        var hits = events.Where(e => e.Tick == t && e.Type == "damage" && e.IntField("sourceId") == BossTestKit.Boss)
            .Select(e => (int)e.IntField("targetId")).ToList();
        _out.WriteLine("boss damage targets at tick " + t + ": " + string.Join(",", hits));
        Assert.Contains(BossTestKit.Chanter, hits);
        Assert.DoesNotContain(BossTestKit.Cinder, hits);
    }
}
