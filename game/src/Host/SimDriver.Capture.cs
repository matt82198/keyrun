using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.View;

namespace Keyrun.Host;

public partial class SimDriver
{
    private void MaybeCapture(SimState before, SimState after, IReadOnlyList<SimEvent> events)
    {
        if (Options.MovieDir is not { } dir || _movie is null)
        {
            return;
        }
        foreach (var shot in _movie.Observe(before, after, events))
        {
            _pendingCaptures++;
            CaptureAfterDraw(Path.Combine(dir, shot.Name), after.Tick, shot);
        }
    }

    private async void CaptureAfterDraw(string path, int tick, MovieShot shot)
    {
        // Wait for the frame that shows this tick to finish drawing, then save it. The FRAME line names the trace
        // event the frame was taken for and that event's tick (f-50d8099e).
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        _log?.Line(FormattableString.Invariant(
            $"FRAME {path} tick={tick} event=\"{shot.Event}\" event_tick={shot.EventTick} {(err == Error.Ok ? "ok" : "error " + err)}"));
        _pendingCaptures--;
    }
}
