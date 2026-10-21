using System.IO;
using Godot;
using Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// The host side of the private M+ rating (Matt 2026-10-20, f-a3230c59: "shown only on the player's own screen, never to
/// others and never used to filter groups"): keeps the <see cref="PrivateRecord"/> in <see cref="PrivateRecord.FileName"/>
/// in the local profile directory, beside <see cref="ProgressionStore"/>'s save. All format and file work is the pure
/// <see cref="PrivateRecord"/>; this class only resolves the path and reports problems, once per store, without the
/// rating's value. It never throws: a missing or bad file is no rating, an unwritable one is an unsaved rating.
/// </summary>
public sealed class PrivateRatingStore
{
    private bool _warned;

    /// <summary>A store whose file lives in <paramref name="directory"/>.</summary>
    /// <param name="directory">The local profile directory (created on first save).</param>
    public PrivateRatingStore(string directory)
    {
        FilePath = Path.Combine(directory, PrivateRecord.FileName);
    }

    /// <summary>The file's absolute OS path.</summary>
    public string FilePath { get; }

    /// <summary>Loads the record, or <see cref="PrivateRecord.Empty"/>.</summary>
    /// <returns>The record.</returns>
    public PrivateRecord Load()
    {
        var (record, error) = PrivateRecord.LoadFile(FilePath);
        if (error is not null)
        {
            WarnOnce(error);
        }
        return record;
    }

    /// <summary>Saves <paramref name="record"/>; a failure is warned once and the run carries on.</summary>
    /// <param name="record">The record.</param>
    /// <returns>True when it was written.</returns>
    public bool Save(PrivateRecord record)
    {
        var error = PrivateRecord.SaveFile(FilePath, record);
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
        GD.PushWarning($"PRIVATE-RATING {message} ({FilePath})");
    }
}
