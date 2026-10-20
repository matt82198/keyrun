using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// HUD-FORCES (ledger f-228202e1, BIBLE s9.2): the Enemy Forces bar sits directly under the Slack/timer bar, the same
/// width and left edge, like WoW's objective tracker puts Enemy Forces under the key timer. It never overlaps the
/// objective panel or the Slack bar, and on a one-row top bar it ends above the party and unit frames (top 124 px).
/// </summary>
public sealed class HudForcesLayoutTests
{
    private const int FramesTop = 124;

    [Theory(DisplayName = "HUD-FORCES: the forces bar sits under the Slack bar, same column, clear of the objective")]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(1024, 768)]
    [InlineData(2560, 1440)]
    public void Forces_bar_sits_under_the_slack_bar(int w, int h)
    {
        var bar = HudLayout.TopBar(w, h);
        var forces = HudLayout.ForcesBar(bar);
        Assert.Equal(bar.Slack.X, forces.X);
        Assert.Equal(bar.Slack.Width, forces.Width);
        Assert.Equal(bar.Slack.Bottom + HudLayout.ForcesGap, forces.Y);
        Assert.Equal(HudLayout.ForcesHeight, forces.Height);
        Assert.False(forces.Intersects(bar.Slack));
        Assert.False(forces.Intersects(bar.Objective));
        Assert.True(forces.Bottom <= FramesTop, $"forces bar bottom {forces.Bottom} runs into the frames at {FramesTop}");
        Assert.True(forces.Right <= w);
    }

    [Fact(DisplayName = "HUD-FORCES: on a narrow viewport the forces bar still follows the Slack bar")]
    public void Narrow_viewport_keeps_the_column()
    {
        var bar = HudLayout.TopBar(600, 400);
        var forces = HudLayout.ForcesBar(bar);
        Assert.Equal(bar.Slack.X, forces.X);
        Assert.Equal(bar.Slack.Bottom + HudLayout.ForcesGap, forces.Y);
        Assert.False(forces.Intersects(bar.Objective));
    }
}
