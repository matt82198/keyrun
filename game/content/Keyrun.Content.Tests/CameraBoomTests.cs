using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The follow camera's pure rules (design/ART-PLAN.md section 8 lane 10, CAMERA-FEEL; <see cref="CameraBoom"/>): the
/// start heading comes from the plan (the Entrance's way out toward the first corridor), the boom is pulled back inside
/// the walls with <see cref="CameraBoom.MarginMm"/> to spare, and a wall standing between the camera and a body is
/// reported so the view can cut it away. The wall boxes are <see cref="DungeonPlan.WallBoxes"/>, checked against the
/// real layout (game/content/data/layout.json).
/// </summary>
public sealed class CameraBoomTests
{
    private static LayoutDef Real => ContentLoader.Load().Layout;

    private static IReadOnlyList<PlanBox> RealBoxes => DungeonPlan.BoxesOf(Real);

    private static IReadOnlyList<(int X, int Z)> RealRoute => Real.Route.Select(p => (p.X, p.Z)).ToList();

    private static IReadOnlyList<WallBox> RealWalls => DungeonPlan.WallBoxes(RealBoxes);

    [Fact]
    public void Start_heading_from_the_plan_faces_plus_x_at_the_entrance()
    {
        var (dx, dz) = CameraBoom.StartHeading(RealBoxes, RealRoute);

        Assert.True(dx > 0, $"start heading ({dx}, {dz}) should point along +x, down the first corridor");
        Assert.Equal(0, dz);
    }

    [Fact]
    public void Start_heading_follows_a_relaid_dungeon()
    {
        // The same rule on a dungeon laid along -z: the heading turns with it (no magic +x constant).
        var boxes = new List<PlanBox>
        {
            new("entrance", true, -6000, 6000, -6000, 6000, false),
            new("corridor-1", false, -1500, 1500, -11000, -5000, false),
        };
        var route = new List<(int X, int Z)> { (0, -5500), (0, -10500) };

        var (dx, dz) = CameraBoom.StartHeading(boxes, route);

        Assert.Equal(0, dx);
        Assert.True(dz < 0, $"start heading ({dx}, {dz}) should point along -z");
    }

    [Fact]
    public void Camera_beyond_the_entrance_west_wall_is_pulled_inside_with_the_margin()
    {
        var pivot = (X: -3000, Z: 0);
        var desired = (X: -9000, Z: 0); // 3 m past the west wall's inner face at x = -6000

        var fit = CameraBoom.Clamp(pivot, desired, RealWalls);

        Assert.Equal(-6000 + CameraBoom.MarginMm, fit.X);
        Assert.Equal(0, fit.Z);
        Assert.True(fit.Ppm < CameraBoom.Whole, $"the boom should be shortened, got {fit.Ppm} ppm");
        Assert.Equal((-6000 + CameraBoom.MarginMm - pivot.X) * (long)CameraBoom.Whole / (desired.X - pivot.X), fit.Ppm);
    }

    [Fact]
    public void Camera_already_inside_is_returned_unchanged()
    {
        var pivot = (X: 4000, Z: 0);
        var desired = (X: -2850, Z: 0); // the run-start boom: 6.85 m behind the Warden, still in the Entrance

        var fit = CameraBoom.Clamp(pivot, desired, RealWalls);

        Assert.Equal(desired.X, fit.X);
        Assert.Equal(desired.Z, fit.Z);
        Assert.Equal(CameraBoom.Whole, fit.Ppm);
    }

    [Fact]
    public void Camera_in_a_corridor_stays_between_its_walls()
    {
        // The Warden walks corridor-1 (z -1500..1500); a boom swung to -z must stop short of the corridor's south wall.
        var pivot = (X: 8000, Z: 0);
        var desired = (X: 8000, Z: -6850);

        var fit = CameraBoom.Clamp(pivot, desired, RealWalls);

        Assert.Equal(-1500 + CameraBoom.MarginMm, fit.Z);
        Assert.Equal(8000, fit.X);
    }

    [Fact]
    public void Segment_crossing_a_wall_box_reports_that_box_as_occluding()
    {
        var walls = new List<WallBox>
        {
            new("a", 0, 500, -1000, 1000, 0, DungeonPlan.WallHeightMm),
            new("b", 5000, 5500, -1000, 1000, 0, DungeonPlan.WallHeightMm),
        };

        var hits = CameraBoom.Occluding((-2000, 1000, 0), (2000, 1000, 0), walls);

        Assert.Equal(new[] { 0 }, hits);
    }

    [Fact]
    public void Segment_passing_over_a_wall_top_is_not_occluded()
    {
        var walls = new List<WallBox> { new("a", 0, 500, -1000, 1000, 0, DungeonPlan.WallHeightMm) };

        // Camera 4.5 m up, body 1.2 m up, 4 m apart: the sight line is above 1.6 m where it crosses the wall.
        var hits = CameraBoom.Occluding((-1000, 4500, 0), (3000, 1200, 0), walls);

        Assert.Empty(hits);
    }

    [Fact]
    public void Exposed_wall_boxes_match_the_layout()
    {
        var walled = DungeonPlan.Walled(RealBoxes);
        var walls = RealWalls;

        Assert.Equal(walled.WallPieces.Count, walls.Count);
        var west = walls.Where(w => w.BoxId == "entrance" && w.MaxX == -6000).ToList();
        Assert.NotEmpty(west);
        Assert.All(west, w => Assert.Equal(-6000 - DungeonPlan.WallThickMm, w.MinX));
        Assert.Equal(-6000, west.Min(w => w.MinZ));
        Assert.Equal(6000, west.Max(w => w.MaxZ));
        Assert.Equal(12000, west.Sum(w => w.MaxZ - w.MinZ)); // one solid 12 m run, no doorway on the west side
        Assert.All(walls, w => Assert.Equal(DungeonPlan.WallHeightMm, w.MaxY));
    }
}
