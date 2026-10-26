using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-AGGRO-1 / G7 spec (S1, keyrun view): a capture log must be able to answer whether an enemy was inside its aggro
/// ring. Findings 1, 6, 8, 9 of the S5 first-pack capture: the TICK line carried only the Warden's x,z, so no enemy or
/// bot position, engaged flag, holder or ring radius was in the log.
/// <para>
/// The contract under test: a short headless run's log carries, for every tick, an <c>ACTOR</c> line per living enemy and
/// per seat with <c>tick=T id=N class=C x=X z=Z</c> (integers, mm) and either <c>engaged=0|1</c> or
/// <c>holder=H|none</c>; a <c>TICK</c> line carries the sim tick so frame labels map to ticks; and each group's
/// <c>GROUP</c> line carries its pack ring radius as <c>ring_mm=R</c>.
/// </para>
/// <para>
/// Red reason today: <see cref="PlaytestRunner.Play"/> is the only headless run path in this project, and its log holds
/// only <c>C</c> and <c>E</c> lines. The TICK/GROUP writer is <c>game/src/Host/RunLog.cs</c>, which imports Godot and is
/// not linked into any headless test project, so no headless test can reach it yet.
/// </para>
/// </summary>
public sealed class CaptureActorPosTests
{
    private static readonly Regex TickLine = new(@"^TICK (\d+) ", RegexOptions.CultureInvariant);

    private static readonly Regex ActorLine = new(
        @"^ACTOR tick=(\d+) id=(\d+) class=(\S+) x=(-?\d+) z=(-?\d+) (engaged=[01]|holder=(\d+|none))\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex GroupRingLine = new(@"^GROUP .* ring_mm=(\d+)\b", RegexOptions.CultureInvariant);

    [Fact(DisplayName = "capture log: TICK carries the sim tick; ACTOR lines name every living enemy and seat with class, integer x,z, and engaged or holder; GROUP carries ring_mm")]
    public void Capture_log_names_every_living_enemy_and_seat_with_position_and_ring()
    {
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null) { MaxTicks = 300 });
        var lines = result.Log.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // The sim tick on every TICK line, so a frame label (seconds x 20) maps to a tick.
        var ticks = lines.Where(l => l.StartsWith("TICK ", StringComparison.Ordinal)).ToList();
        Assert.True(ticks.Count > 0, "no TICK lines in the headless run log; the capture writer emits none (RunLog.cs is Godot-bound)");
        Assert.All(ticks, l => Assert.True(TickLine.IsMatch(l), $"TICK line has no sim tick: {l}"));

        // Every living enemy and every seat has an ACTOR line on the final tick, with the full field set.
        var lastTick = ticks.Select(l => int.Parse(TickLine.Match(l).Groups[1].Value, CultureInfo.InvariantCulture)).Max();
        var actorLines = lines.Select(l => ActorLine.Match(l)).Where(m => m.Success).ToList();
        Assert.NotEmpty(actorLines);

        var living = result.Run.State.Actors.Values.Where(a => a.Alive).ToList();
        var enemies = living.Where(a => a.Kind == ActorKind.Enemy).ToList();
        var seats = living.Where(a => a.Kind != ActorKind.Enemy).ToList();
        Assert.NotEmpty(enemies);
        Assert.NotEmpty(seats);

        foreach (var a in enemies.Concat(seats))
        {
            var id = a.Id.ToString(CultureInfo.InvariantCulture);
            var row = actorLines.SingleOrDefault(m =>
                m.Groups[1].Value == lastTick.ToString(CultureInfo.InvariantCulture) && m.Groups[2].Value == id);
            Assert.True(row is not null && row.Success, $"no ACTOR tick={lastTick} id={id} line for living {a.Kind} {a.ClassId}");
            Assert.Equal(a.ClassId, row!.Groups[3].Value);
            Assert.Equal(a.Pos.X.ToString(CultureInfo.InvariantCulture), row.Groups[4].Value);
            Assert.Equal(a.Pos.Z.ToString(CultureInfo.InvariantCulture), row.Groups[5].Value);
        }

        // Each pack's ring radius in mm on its GROUP line.
        var groupLines = lines.Where(l => l.StartsWith("GROUP ", StringComparison.Ordinal)).ToList();
        Assert.True(groupLines.Count > 0, "no GROUP lines in the headless run log");
        Assert.All(groupLines, l => Assert.True(GroupRingLine.IsMatch(l), $"GROUP line carries no ring_mm: {l}"));
    }
}
