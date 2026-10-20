using System;
using System.IO;
using Godot;
using Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// Keeps the character's private run record (<see cref="PrivateRecord"/>, BIBLE s10 "No exported score") in
/// <see cref="PrivateRecord.FileName"/> beside the progression save, in the same directory the between-runs screen uses
/// for character state (<see cref="ProgressionSavePlace.Directory"/>: user:// in the interactive game, --progression-dir
/// when given, nowhere for a headless run without it). The file is local and is never sent anywhere. Like
/// <see cref="ProgressionStore"/> it never throws: a missing save is an empty record, a corrupt or unwritable one is an
/// empty record (or an unsaved run) plus one warning per store.
/// </summary>
public sealed class PrivateRatingStore
{
    private bool _warned;

    /// <summary>A store whose file lives in <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory (created on first save).</param>
    public PrivateRatingStore(string directory)
    {
        FilePath = Path.Combine(directory, PrivateRecord.FileName);
    }

    /// <summary>The save file's absolute OS path.</summary>
    public string FilePath { get; }

    /// <summary>Loads the record, or an empty one.</summary>
    /// <returns>The record.</returns>
    public PrivateRecord Load()
    {
        string? text = null;
        try
        {
            if (File.Exists(FilePath))
            {
                text = File.ReadAllText(FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WarnOnce("unreadable: " + ex.Message);
            return PrivateRecord.Empty;
        }
        var (record, error) = PrivateRecord.Parse(text);
        if (error is not null)
        {
            WarnOnce(error);
        }
        return record;
    }

    /// <summary>Saves <paramref name="record"/> (a sibling .tmp, then replace); a failure is warned once.</summary>
    /// <param name="record">The record.</param>
    /// <returns>True when it was written.</returns>
    public bool Save(PrivateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, record.ToJson());
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WarnOnce("unwritable: " + ex.Message);
            return false;
        }
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
