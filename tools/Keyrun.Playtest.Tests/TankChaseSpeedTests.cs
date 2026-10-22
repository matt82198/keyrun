using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Host;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-2f6982e9 (BIBLE section 3, WoW): the tank bot closes on an engaged pack at full walk speed, arrives first and
/// holds aggro; the followers never creep. Measured in the human-cinder replay (key 2, seed 1): from the Choir
/// pull_start the bot Warden (seat 2) walks toward the pack, and its mean per-tick displacement over the ticks it is
/// moving before it reaches melee of its target must be at least the walk speed less a documented tolerance.
/// Walk speed is run.json movement baseSpeedMmPerTick (180 mm/tick), read from the content, not copied.
/// </summary>
public sealed class TankChaseSpeedTests(ITestOutputHelper output)
{
    private static string Fixture()
    {
        const string rel = "game/fixtures/input/human-cinder.input-log.json";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, rel)))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, $"{rel} not found above {AppContext.BaseDirectory}");
        return Path.Combine(dir!.FullName, rel);
    }

    [Fact(DisplayName = "The bot Warden closes on the engaged Choir at >= 150 mm/tick (walk speed less 30), not a creep")]
    public void Bot_warden_closes_at_walk_speed()
    {
        var reg = Keyrun.Content.ContentLoader.Load();
        var walk = reg.Run.Movement.SpeedMmPerTick;
        var samples = new List<(int Tick, long X, long Z)>();
        int pullStart = -1;
        int wardenId = -1;
        var r = HumanReplay.Play(Fixture(), 200, reg, run =>
        {
            if (wardenId < 0)
            {
                wardenId = run.State.Actors.Values.First(a => a.Kind == ActorKind.Player && a.ClassId == "warden").Id;
            }
            var w = run.State.Actors[wardenId];
            samples.Add((run.State.Tick, w.Pos.X, w.Pos.Z));
        });
        var start = r.Events.First(e => e.Type == "pull_start" && e.Get("pullId") is JsString { Value: "choir" }).Tick;
        pullStart = start;
        var line = string.Join(" ", samples.Where(s => s.Tick >= pullStart && s.Tick <= pullStart + 60).Select(s => $"{s.Tick}:{s.X}"));
        output.WriteLine($"walk={walk} pullStart={pullStart} warden x by tick: {line}");

        // Per-tick displacement while closing: from pull_start until the Warden first stands still for 3 ticks (melee reached).
        var disp = new List<long>();
        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i].Tick <= pullStart)
            {
                continue;
            }
            var dx = samples[i].X - samples[i - 1].X;
            var dz = samples[i].Z - samples[i - 1].Z;
            disp.Add((long)Math.Sqrt((double)dx * dx + (double)dz * dz));
        }
        var closing = new List<long>();
        var still = 0;
        foreach (var d in disp)
        {
            if (d == 0 && ++still >= 3)
            {
                break;
            }
            still = d == 0 ? still : 0;
            closing.Add(d);
        }
        // The ticks it stood in melee waiting are not closing ticks; interior stop-and-go zeros stay in the mean.
        while (closing.Count > 0 && closing[^1] == 0)
        {
            closing.RemoveAt(closing.Count - 1);
        }
        var mean = closing.Count == 0 ? 0 : closing.Average();
        output.WriteLine($"closing ticks={closing.Count} mean={mean:F1} mm/tick; displacements: {string.Join(",", closing.Take(80))}");
        Assert.True(closing.Count >= 5, "the Warden never started closing");
        Assert.True(mean >= walk - 30, $"mean closing displacement {mean:F1} mm/tick < {walk - 30} (walk {walk})");
    }
}
