using System;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The drawability set (<see cref="TelegraphDrawability"/>) and the line rectangle the view draws for a line telegraph
/// (lane LASH-1). TelegraphView.Spawn builds its line mesh from <see cref="TelegraphDrawability.LineCorners"/>, so these
/// tests drive the same corners the view uses: the rectangle starts at the caster (forward 0), runs the telegraph's
/// length along its facing, and is the full width across it, the footprint <c>Telegraphs.PointInLine</c> resolves.
/// </summary>
public sealed class TelegraphDrawabilityTests
{
    [Fact]
    public void The_view_draws_circles_cones_and_lines()
    {
        Assert.True(TelegraphDrawability.CanDraw(TelegraphShape.Circle));
        Assert.True(TelegraphDrawability.CanDraw(TelegraphShape.Cone));
        Assert.True(TelegraphDrawability.CanDraw(TelegraphShape.Line));
    }

    [Fact]
    public void A_line_is_a_rectangle_from_the_caster_the_length_forward_and_the_width_across()
    {
        var corners = TelegraphDrawability.LineCorners(length: 3000, width: 1200);

        // Four corners, each (forward, lateral) in mm: back edge at the caster, front edge at the length.
        Assert.Equal(4, corners.Length);
        Assert.Contains((0L, -600L), corners);
        Assert.Contains((0L, 600L), corners);
        Assert.Contains((3000L, -600L), corners);
        Assert.Contains((3000L, 600L), corners);
        Assert.All(corners, c => Assert.True(c.Forward == 0 || c.Forward == 3000));
        Assert.All(corners, c => Assert.True(c.Lateral == -600 || c.Lateral == 600));
    }

    [Fact]
    public void A_line_rectangle_has_the_area_of_its_length_times_its_width()
    {
        var corners = TelegraphDrawability.LineCorners(length: 3000, width: 1200);

        // Shoelace over the polygon in its drawn order: the view's quad is the convex hull of these corners.
        var hull = new[] { corners[0], corners[1], corners[2], corners[3] };
        long twiceArea = 0;
        for (var i = 0; i < hull.Length; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Length];
            twiceArea += (a.Forward * b.Lateral) - (b.Forward * a.Lateral);
        }
        Assert.Equal(3000L * 1200L, Math.Abs(twiceArea) / 2);
    }
}
