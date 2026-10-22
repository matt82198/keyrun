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
    // --record-input / --replay-input (lane 3E-b). A replay is refused (ARENA error, exit 1) when the log's seed or key
    // differs from this launch's: the same keys would not make the same run.
    private void OpenInputLogs()
    {
        if (Options.RecordInput is not null)
        {
            _recorder = new InputRecorder();
            _inputClock ??= new ReplayClock();
        }
        if (Options.ReplayInput is { } path)
        {
            var log = _replayLog ?? InputLog.Load(path);
            if (log.Seed != Options.Seed || log.KeyLevel != Options.KeyLevel)
            {
                throw new ArgumentException(FormattableString.Invariant(
                    $"--replay-input {path} was recorded at seed={log.Seed} key={log.KeyLevel}, this launch is seed={Options.Seed} key={Options.KeyLevel}"));
            }
            if ((log.Camera.Count > 0 || log.StartHeading is not null) && _rig is null)
            {
                throw new ArgumentException($"--replay-input {path} holds camera headings but the scene has no CameraRig at {CameraRigPath}");
            }
            _replayer = new InputReplayer(log);
            _inputClock ??= new ReplayClock();
            _replayStartHeading = log.StartHeading;
            _replayTotal = log.Events.Count;
            _replayCameraTotal = log.Camera.Count;
        }
    }

    // --replay-input: feeds every due event to the engine's Input singleton and flushes it at once, so the key state
    // PlayerInput polls (Input.IsPhysicalKeyPressed) and the viewport's event delivery (_Input, GUI, _UnhandledInput)
    // both see it before the next step, exactly as a keyboard press between frames. Waits for the screen to be hooked
    // on the first screen (the run-less launch), where no tick advances. f-6a00609e / R-CLOCK-1: the ticks are read on
    // ReplayClock: the first Screen's frames (counted in _Process) until the first run exists, those frames plus
    // TicksRun after (the value a recording stamps, f-6b922005).
    private void MaybeReplay()
    {
        if (_replayer is null || _inputClock is null || _done || (_screen is null && Run is null))
        {
            return;
        }
        var now = _inputClock.Now(Run is not null, TicksRun);
        foreach (var e in _replayer.Due(now))
        {
            _log?.Line(FormattableString.Invariant($"INPUT replay tick={e.Tick} key={e.Key} pressed={(e.Pressed ? "true" : "false")}"));
            Input.ParseInputEvent(new InputEventKey { Keycode = e.Key, PhysicalKeycode = e.Key, Pressed = e.Pressed });
            Input.FlushBufferedEvents();
        }
        // Lane 3E-c: the recorded heading goes back on the rig PlayerInput reads, before the step at its tick. A /1 log
        // has none, and its summary line is unchanged.
        foreach (var c in _replayer.DueCamera(now))
        {
            _log?.Line(FormattableString.Invariant($"INPUT replay cam tick={c.Tick} yaw={InputLog.FormatYaw(c.Yaw)}"));
            _rig?.RestoreYaw(c.Yaw);
        }
        if (_replayer.Done && !_replayDoneLogged)
        {
            _replayDoneLogged = true;
            var cam = _replayCameraTotal > 0 ? FormattableString.Invariant($" cam={_replayCameraTotal}") : string.Empty;
            _log?.Line(FormattableString.Invariant($"INPUT replay done events={_replayTotal} last={_replayer.LastTick}{cam}"));
        }
    }

    // --record-input: written at every run end (a crash keeps the finished runs) and at quit, which logs the summary.
    private void SaveRecording(bool final)
    {
        if (_recorder is null || Options.RecordInput is not { } path)
        {
            return;
        }
        try
        {
            _recorder.ToLog(Options.Seed, Options.KeyLevel, RotationIndex).Save(path);
            if (final)
            {
                var cam = _recorder.Camera.Count > 0 ? FormattableString.Invariant($" cam={_recorder.Camera.Count}") : string.Empty;
                _log?.Line(FormattableString.Invariant($"INPUT record done events={_recorder.Events.Count}{cam} path={path}"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"INPUT record error path={path}: {ex.Message}");
        }
    }
}
