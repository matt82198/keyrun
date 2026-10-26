using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The run-log proof line for a looped clip change (ANIM actor=N clip=X ability=- at=T). Pure: <see cref="AnimRunLog"/> formats
/// it from the previous and the new <see cref="AnimClip"/> with no Godot types. A line is written only when the clip
/// changed, so an unengaged pack that stays Idle logs nothing, and a capture can prove it played its Move clip.
/// </summary>
public sealed class AnimRunLogTests
{
    [Fact]
    public void Clip_change_to_Move_formats_the_run_line_and_unchanged_clip_formats_none()
    {
        // Idle to Move on actor 7 at sim tick 412 must print the exact proof line.
        var moved = AnimRunLog.Line(actorId: 7, previous: AnimClip.Idle, clip: AnimClip.Move, tick: 412);
        Assert.Equal("ANIM actor=7 clip=Move ability=- at=412", moved);
        Assert.Matches(@"^ANIM actor=\d+ clip=\S+ ability=\S+ at=\d+$", moved);

        // Idle to Idle is no change, so there is no line to print.
        var unchanged = AnimRunLog.Line(actorId: 7, previous: AnimClip.Idle, clip: AnimClip.Idle, tick: 413);
        Assert.Null(unchanged);
    }
}
