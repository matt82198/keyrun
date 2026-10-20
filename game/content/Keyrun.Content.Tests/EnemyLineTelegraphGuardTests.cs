using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Guard (lane NO-ENEMY-LINE-TELEGRAPH): src/View/TelegraphView.cs draws every floor telegraph as a fan
/// (apex at the caster, radius by angle), and a line telegraph carries a length and width but no radius or
/// angle, so it would draw nothing on the floor. No enemy script uses a line today (every
/// content/data/enemies/*.json telegraph is a circle or a cone). This test fails the moment one does, so the
/// view gets its rectangle mesh before the content ships an invisible attack.
/// </summary>
public sealed class EnemyLineTelegraphGuardTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    [Fact]
    public void No_enemy_script_step_uses_a_line_telegraph_until_the_view_can_draw_one()
    {
        var steps = (
            from enemy in Reg.Enemies.Values
            from step in enemy.Script
            where step.Telegraph is not null
            select (enemy.Id, step.Cast, step.Telegraph!.Shape)).ToList();

        var lines = steps
            .Where(s => s.Shape == TelegraphShape.Line)
            .Select(s => s.Id + " -> " + s.Cast)
            .ToList();

        Assert.True(
            lines.Count == 0,
            "TelegraphView cannot draw line telegraphs yet (it draws a fan; a line would show nothing on the floor). "
            + "Fix src/View/TelegraphView.cs to draw the line's rectangle first, then remove this guard. Offending steps: "
            + string.Join(", ", lines));
        // Vacuity guard: the scan must see the shipped telegraphs (hollow-warden, pair and seed scripts).
        Assert.True(steps.Count > 0, "no enemy telegraphs were loaded; the guard scanned nothing");
    }
}
