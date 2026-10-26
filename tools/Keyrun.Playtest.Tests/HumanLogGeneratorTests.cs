using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Host;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-GCD-1: the human chanter replay log must be GENERATED from intent (a seeded generator that places the Q kicks
/// against the Cantor's choir.healCast windows), not hand-placed at fixed ticks, so its kicks survive GCD changes.
/// This file pins <c>HumanInputLogGenerator.Generate(classId, key, seed)</c> before it exists; it is expected to be RED
/// at compile until the generator lands.
/// </summary>
public sealed class HumanLogGeneratorTests(ITestOutputHelper output)
{
    private const int FirstMinuteTicks = 1200;
    private const string CantorClass = "choir.lieutenant";
    private const string HealCast = "choir.healCast";

    [Fact(DisplayName = "A generated chanter log (key 2, seed 1) replays to at least one seat-1 interrupt on the Cantor")]
    public void Generated_chanter_log_interrupts_the_cantor()
    {
        var log = HumanInputLogGenerator.Generate("chanter", 2, 1);
        var r = HumanReplay.Play(log, FirstMinuteTicks);
        var cantors = r.Setup.Dungeon.Pulls[0].Spawns.Where(s => s.ClassId == CantorClass).Select(s => s.ActorId).ToHashSet();
        var kicks = r.Events.Where(e => e.Type == "interrupt" && e.Get("sourceId") is JsInt { Value: HostRunSetup.HumanId } &&
            e.Get("targetId") is JsInt t && cantors.Contains((int)t.Value)).Select(e => e.Tick).ToList();
        output.WriteLine($"generated chanter k2 s1: queued on frame {r.QueueFrame}, kicks on the Cantor at ticks {string.Join(",", kicks)}");

        Assert.Equal("chanter", r.ClassId);
        Assert.NotEmpty(kicks);
    }

    [Fact(DisplayName = "Every generated Q press falls inside a Cantor choir.healCast window of the same run")]
    public void Every_generated_Q_press_lands_inside_a_cantor_heal_cast_window()
    {
        var log = HumanInputLogGenerator.Generate("chanter", 2, 1);
        var windows = new HashSet<(int Actor, int Start, int End)>();
        var r = HumanReplay.Play(log, FirstMinuteTicks, null, run =>
        {
            foreach (var a in run.State.Actors.Values)
            {
                if (a.CastId == HealCast && a.CastStartTick is { } s && a.CastEndsTick is { } e)
                {
                    windows.Add((a.Id, (int)s, (int)e));
                }
            }
        });
        var cantors = r.Setup.Dungeon.Pulls[0].Spawns.Where(s => s.ClassId == CantorClass).Select(s => s.ActorId).ToHashSet();
        var qPresses = log.Events.Where(e => e.Key == "Q" && e.Pressed).Select(e => e.Tick).ToList();
        output.WriteLine($"generated chanter k2 s1: queue frame {r.QueueFrame}, Q presses at clock {string.Join(",", qPresses)}, cantor heal windows {windows.Count}");

        Assert.NotEmpty(qPresses);
        foreach (var clock in qPresses)
        {
            var t = clock - r.QueueFrame;
            Assert.Contains(windows, w => cantors.Contains(w.Actor) && w.Start <= t && t <= w.End);
        }
    }

    [Fact(DisplayName = "The generated log round-trips through the keyrun.input-log/1 serializer and parser")]
    public void Generated_log_round_trips_through_the_input_log_1_serializer()
    {
        var log = HumanInputLogGenerator.Generate("chanter", 2, 1);
        Assert.Equal("keyrun.input-log/1", log.Schema);
        Assert.Empty(log.Camera);

        var json = log.ToJson();
        var back = InputLogDocument.Parse(json, "generated-round-trip");

        Assert.Equal(log.Schema, back.Schema);
        Assert.Equal(log.Seed, back.Seed);
        Assert.Equal(log.KeyLevel, back.KeyLevel);
        Assert.Equal(log.RotationIndex, back.RotationIndex);
        Assert.Equal(log.StartHeading, back.StartHeading);
        Assert.Equal(log.ReadyKeys, back.ReadyKeys);
        Assert.Equal(
            log.Events.Select(e => (e.Tick, e.Key, e.Pressed)),
            back.Events.Select(e => (e.Tick, e.Key, e.Pressed)));
    }
}
