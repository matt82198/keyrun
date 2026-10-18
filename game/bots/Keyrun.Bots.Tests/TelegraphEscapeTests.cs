using Keyrun.Bots.Shared;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Ports of tests/bots/telegraph-escape.test.ts (1 case) and tests/bots/telegraph-escape-shapes.test.ts
/// (3 cases): bot.ts's skill-dial-free danger tier walks a bot out of a circle, cone or line. The bot runs
/// with <see cref="RoleSet.None"/>: the escape tier sits above role dispatch, so no role is involved.
/// </summary>
public sealed class TelegraphEscapeTests
{
    private const long StepMm = 2000;

    private static (int Ticks, PointMm FinalPos) RunEscape(PointMm start, BotTelegraph telegraph, int maxTicks)
    {
        var bot = Bot.MakeBot();
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var self = MakeActor(id: 1, x: start.X, z: start.Z);
        var ticks = 0;
        for (; ticks < maxTicks; ticks++)
        {
            var view = View(MakeState(ticks, self), null, telegraph);
            if (!Positioning.IsInAnyDanger(PointMm.Of(self.Pos), new[] { telegraph }))
            {
                break;
            }
            var result = bot.Decide(view, memory, self.Id, prng);
            memory = result.Memory;
            prng = result.Prng;
            var move = FirstMove(result.Commands);
            Assert.NotNull(move);
            SetPos(self, ApplyMove(PointMm.Of(self.Pos), move, StepMm));
        }
        return (ticks, PointMm.Of(self.Pos));
    }

    [Fact(DisplayName = "telegraph avoidance > moves a bot out of a dangerous circle within a bounded number of ticks")]
    public void EscapesCircle()
    {
        const int maxEscapeTicks = 10;
        var telegraph = new BotTelegraph("boss-puddle", TelegraphShape.Circle, new PointMm(0, 0), 1000, true) { Radius = 5000 };
        var (ticks, final) = RunEscape(new PointMm(0, 0), telegraph, maxEscapeTicks);
        Assert.True(ticks < maxEscapeTicks);
        Assert.False(Positioning.IsInAnyDanger(final, new[] { telegraph }));
    }

    [Fact(DisplayName = "telegraph avoidance: real shapes > escapes a dangerous cone within a bounded number of ticks")]
    public void EscapesCone()
    {
        const int maxEscapeTicks = 15;
        var telegraph = new BotTelegraph("boss-cleave", TelegraphShape.Cone, new PointMm(0, 0), 1000, true) { Radius = 8000, Angle1024 = 200, Yaw = 0 };
        var (ticks, final) = RunEscape(new PointMm(3000, 0), telegraph, maxEscapeTicks);
        Assert.True(ticks < maxEscapeTicks);
        Assert.False(Positioning.IsInAnyDanger(final, new[] { telegraph }));
    }

    [Fact(DisplayName = "telegraph avoidance: real shapes > escapes a dangerous line within a bounded number of ticks")]
    public void EscapesLine()
    {
        const int maxEscapeTicks = 15;
        var telegraph = new BotTelegraph("boss-beam", TelegraphShape.Line, new PointMm(0, 0), 1000, true) { Yaw = 0, Length = 20000, Width = 6000 };
        var (ticks, final) = RunEscape(new PointMm(5000, 0), telegraph, maxEscapeTicks);
        Assert.True(ticks < maxEscapeTicks);
        Assert.False(Positioning.IsInAnyDanger(final, new[] { telegraph }));
    }

    [Fact(DisplayName = "telegraph avoidance: real shapes > a bot that never enters the danger zone issues no escape move")]
    public void NoEscapeOutsideDanger()
    {
        var telegraph = new BotTelegraph("far-puddle", TelegraphShape.Circle, new PointMm(100000, 100000), 1000, true) { Radius = 5000 };
        var (ticks, _) = RunEscape(new PointMm(0, 0), telegraph, 15);
        Assert.Equal(0, ticks);
    }
}
