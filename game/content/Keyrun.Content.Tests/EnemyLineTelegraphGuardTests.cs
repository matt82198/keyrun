using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Guard (lane NO-ENEMY-LINE-TELEGRAPH, drawability form): an enemy script telegraph may use only a shape the floor
/// view can draw, the set <see cref="TelegraphDrawability.ViewDrawable"/>. A line telegraph carries a length and a
/// width, and a shape outside the set would show nothing on the floor, so this fails the moment a content step uses
/// one the view does not draw. Adding a shape to the set is the same change that makes TelegraphView draw it.
/// </summary>
public sealed class EnemyLineTelegraphGuardTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    [Fact]
    public void No_enemy_script_step_uses_a_telegraph_shape_the_view_cannot_draw()
    {
        var steps = (
            from enemy in Reg.Enemies.Values
            from step in enemy.Script
            where step.Telegraph is not null
            select (enemy.Id, step.Cast, step.Telegraph!.Shape)).ToList();

        var undrawable = steps
            .Where(s => !TelegraphDrawability.CanDraw(s.Shape))
            .Select(s => s.Id + " -> " + s.Cast + " (" + s.Shape + ")")
            .ToList();

        Assert.True(
            undrawable.Count == 0,
            "TelegraphView cannot draw these telegraph shapes (not in TelegraphDrawability.ViewDrawable, so they would "
            + "show nothing on the floor). Teach src/View/TelegraphView.cs the shape, then add it to the set. Offending steps: "
            + string.Join(", ", undrawable));
        // Vacuity guard: the scan must see the shipped telegraphs (hollow-warden, pair and seed scripts).
        Assert.True(steps.Count > 0, "no enemy telegraphs were loaded; the guard scanned nothing");
    }
}
