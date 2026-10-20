using System;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The floor telegraph fan (<see cref="TelegraphFan"/>, lane TELEGRAPH-CIRCLE). src/View/TelegraphView.cs draws every
/// telegraph as a flat fan around the caster's facing; before this lane it took the fan's angle from
/// <c>Angle1024 ?? 0</c>, so a circle (which carries a radius and no angle, e.g. the hollowWarden.slam radius-3000
/// circle at hollow-warden-boss.trace.jsonl:286) built 32 zero-area triangles and drew nothing. These tests drive the
/// span the view uses through the same fan the view builds (32 segments, apex at the origin) and assert the outline:
/// a circle is a closed full turn with the area of its disc, and a cone keeps exactly its own angle.
/// </summary>
public sealed class TelegraphFanTests
{
    private const int Segments = 32;

    /// <summary>The fan TelegraphView.BuildCone builds for a span, as (x, z) arc points at the given radius.</summary>
    private static (double X, double Z)[] Arc(int span1024, double radius)
    {
        double half = span1024 / 2.0 * Math.Tau / 1024.0;
        var pts = new (double X, double Z)[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            double a = -half + (2 * half * i / Segments);
            pts[i] = (radius * Math.Cos(a), radius * Math.Sin(a));
        }
        return pts;
    }

    /// <summary>Total area of the fan's triangles (apex at the origin), by the cross product of each arc edge.</summary>
    private static double FanArea((double X, double Z)[] arc)
    {
        double area = 0;
        for (int i = 0; i < arc.Length - 1; i++)
        {
            area += Math.Abs((arc[i].X * arc[i + 1].Z) - (arc[i + 1].X * arc[i].Z)) / 2.0;
        }
        return area;
    }

    [Fact]
    public void A_circle_with_no_angle_yields_a_closed_full_turn_with_the_area_of_its_disc()
    {
        const double radius = 3000; // hollowWarden.slam, hollow-warden-boss.trace.jsonl:286
        int span = TelegraphFan.Span1024("circle", angle1024: null);
        Assert.Equal(TelegraphFan.FullTurn, span);

        var arc = Arc(span, radius);
        double area = FanArea(arc);
        Assert.True(area > 0, "circle telegraph fan is degenerate: area " + area);
        // A 32-gon inscribed in the disc: within 1% of pi r^2.
        double disc = Math.PI * radius * radius;
        Assert.True(Math.Abs(area - disc) / disc < 0.01, "circle fan area " + area + " vs disc " + disc);
        // Spans 360 degrees: the outline closes on itself and every arc point sits on the radius.
        Assert.Equal(arc[0].X, arc[Segments].X, 6);
        Assert.Equal(arc[0].Z, arc[Segments].Z, 6);
        foreach (var (x, z) in arc)
        {
            Assert.Equal(radius, Math.Sqrt((x * x) + (z * z)), 6);
        }
    }

    [Fact]
    public void A_circle_ignores_any_stray_angle()
    {
        Assert.Equal(TelegraphFan.FullTurn, TelegraphFan.Span1024("circle", angle1024: 170));
    }

    [Theory]
    [InlineData(170)]
    [InlineData(256)]
    [InlineData(341)]
    public void A_cone_keeps_its_own_angle(int angle1024)
    {
        int span = TelegraphFan.Span1024("cone", angle1024);
        Assert.Equal(angle1024, span);
        var arc = Arc(span, 2000);
        double swept = Math.Atan2(arc[Segments].Z, arc[Segments].X) - Math.Atan2(arc[0].Z, arc[0].X);
        Assert.Equal(angle1024 * Math.Tau / 1024.0, swept, 9);
        Assert.True(FanArea(arc) > 0);
    }

    [Fact]
    public void A_cone_with_no_angle_stays_empty_as_before()
    {
        Assert.Equal(0, TelegraphFan.Span1024("cone", angle1024: null));
    }
}
