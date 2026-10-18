using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using Keyrun.Sim.Core;

namespace Keyrun.Host;

/// <summary>
/// The arena's run log: one <c>TICK n</c> line per step plus one <c>EVENT {json}</c> line per sim event
/// (canonical JSON, the trace format ORACLE.md uses), and free-form lines (RUN, FRAME, AUTORUN).
/// Lines go to the Godot console and, with <c>--log FILE</c>, to that file (UTF-8, no BOM, LF).
/// TICK lines reach the console only in autorun, so interactive play is not flooded at 20 lines a second.
/// </summary>
public sealed class RunLog : IDisposable
{
    private readonly StreamWriter? _file;
    private readonly bool _ticksToConsole;
    private bool _disposed;

    /// <summary>Opens the log.</summary>
    /// <param name="path">File to write, or null for console only. Its directory is created if missing.</param>
    /// <param name="ticksToConsole">Echo TICK lines to the console too.</param>
    public RunLog(string? path, bool ticksToConsole)
    {
        _ticksToConsole = ticksToConsole;
        if (!string.IsNullOrEmpty(path))
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _file = new StreamWriter(full, append: false, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }
    }

    /// <summary>Writes one free-form line to the console and the file.</summary>
    public void Line(string text)
    {
        GD.Print(text);
        Write(text);
    }

    /// <summary>Logs one step: its pre-step tick, the Warden after it, and every event it emitted.</summary>
    /// <param name="tick">The pre-step tick (the tick the commands and events carry).</param>
    /// <param name="after">The state after the step.</param>
    /// <param name="events">The step's events, in emission order.</param>
    public void Tick(int tick, SimState after, IReadOnlyList<SimEvent> events)
    {
        var line = after.Actors.TryGetValue(SimDriver.WardenId, out var w)
            ? $"TICK {tick} warden=({w.Pos.X},{w.Pos.Z}) yaw={w.Yaw} events={events.Count}"
            : $"TICK {tick} events={events.Count}";
        if (_ticksToConsole)
        {
            GD.Print(line);
        }
        Write(line);
        foreach (var e in events)
        {
            Line("EVENT " + e.ToCanonicalJson());
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _file?.Dispose();
    }

    private void Write(string text)
    {
        if (!_disposed)
        {
            _file?.WriteLine(text);
        }
    }
}
