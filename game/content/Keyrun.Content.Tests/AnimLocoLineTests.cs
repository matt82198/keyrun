using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// R-IDLEANIM-1: the capture log must prove whether idle and move clips transition on an actor. The combat proof line
/// <see cref="AnimRule.ViewLine"/> only prints for a combat clip, so idle and move never reach the log. These cases pin
/// <see cref="AnimRule.LocoLine"/>, the locomotion sibling of that line, with the same <c>VIEW anim actor=</c> prefix so
/// one grep catches both.
/// </summary>
public sealed class AnimLocoLineTests
{
    [Fact]
    public void Idle_not_moving_formats_the_loco_proof_line()
    {
        Assert.Equal(
            "VIEW anim actor=3 clip=Idle tick=176 kind=Loco moving=0",
            AnimRule.LocoLine(3, AnimClip.Idle, 176, moving: false));
    }

    [Fact]
    public void Move_while_moving_formats_the_loco_proof_line()
    {
        Assert.Equal(
            "VIEW anim actor=5 clip=Move tick=177 kind=Loco moving=1",
            AnimRule.LocoLine(5, AnimClip.Move, 177, moving: true));
    }

    [Fact]
    public void Loco_line_shares_the_view_line_prefix_so_one_grep_catches_both()
    {
        var loco = AnimRule.LocoLine(7, AnimClip.Idle, 42, moving: false);
        var combat = AnimRule.ViewLine(7, "Spellcast_Shoot", 42);
        var sharedPrefix = combat.Split(" clip=")[0];

        Assert.Equal("VIEW anim actor=7", sharedPrefix);
        Assert.StartsWith(sharedPrefix + " clip=", loco);
    }
}
