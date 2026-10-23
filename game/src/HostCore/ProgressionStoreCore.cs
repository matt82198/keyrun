using System;
using System.IO;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// The Godot-free half of the host progression store (BIBLE s7 persistence): keeps the banked
/// <see cref="Progression"/> in <see cref="ProgressionSave.FileName"/> inside a caller-supplied directory. All format
/// and file work is the pure <see cref="ProgressionSave"/>; this class only reports problems through the injected
/// <c>warn</c> callback. The Godot host (<c>ProgressionStore</c>) resolves the user data directory and supplies the
/// warning sink. It never throws: a missing save is a fresh character, a corrupt or unwritable one is a fresh character
/// (or an unsaved run) plus one warning per store, so persistence can never block or crash a run.
/// </summary>
public sealed class ProgressionStoreCore
{
    private readonly Action<string> _warn;
    private bool _warned;

    /// <summary>A store whose save file lives in <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory (created on first save).</param>
    /// <param name="warn">The sink for the single warning this store may emit.</param>
    public ProgressionStoreCore(string directory, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(warn);
        FilePath = Path.Combine(directory, ProgressionSave.FileName);
        _warn = warn;
    }

    /// <summary>The save file's absolute OS path.</summary>
    public string FilePath { get; }

    /// <summary>Loads the banked progression, or a fresh character seeded from <paramref name="seed"/>.</summary>
    /// <param name="seed">The fresh character's crafting seed.</param>
    /// <param name="lastQueuedClassId">
    /// The class the save last queued, passed to the class-aware <see cref="ProgressionSave.LoadFile(string, long, string?)"/>
    /// so a v1 save's history migrates to that class. Null keeps the two-argument behaviour exactly (no migration).
    /// </param>
    /// <returns>The progression.</returns>
    public Progression Load(long seed, string? lastQueuedClassId = null)
    {
        var loaded = ProgressionSave.LoadFile(FilePath, seed, lastQueuedClassId);
        if (loaded.Error is { } error)
        {
            WarnOnce(error);
        }
        return loaded.Progression;
    }

    /// <summary>Saves <paramref name="progression"/>; a failure is warned once and the run carries on.</summary>
    /// <param name="progression">The progression to bank.</param>
    /// <returns>True when it was written.</returns>
    public bool Save(Progression progression)
    {
        var error = ProgressionSave.SaveFile(FilePath, progression);
        if (error is null)
        {
            return true;
        }
        WarnOnce(error);
        return false;
    }

    private void WarnOnce(string message)
    {
        if (_warned)
        {
            return;
        }
        _warned = true;
        _warn($"PROGRESSION {message} ({FilePath})");
    }
}
