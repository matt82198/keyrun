using System.IO;
using Godot;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// The host side of BIBLE s7 persistence: keeps the banked <see cref="Progression"/> (Ash and equipped gear) in
/// <see cref="ProgressionSave.FileName"/> under the Godot user data dir (<c>user://</c>), loaded at boot and saved at
/// run end. All format and file work is the pure <see cref="ProgressionSave"/>; this class only resolves the
/// directory and reports problems. It never throws: a missing save is a fresh character, a corrupt or unwritable one
/// is a fresh character (or an unsaved run) plus one warning per store, so persistence can never block or crash a run.
/// </summary>
public sealed class ProgressionStore
{
    private bool _warned;

    /// <summary>A store whose save file lives in <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory (created on first save).</param>
    public ProgressionStore(string directory)
    {
        FilePath = Path.Combine(directory, ProgressionSave.FileName);
    }

    /// <summary>The save file's absolute OS path.</summary>
    public string FilePath { get; }

    /// <summary>The store in the Godot user data dir (<c>user://</c>, globalized to an OS path).</summary>
    /// <returns>The store.</returns>
    public static ProgressionStore UserData() => new(ProjectSettings.GlobalizePath("user://"));

    /// <summary>Loads the banked progression, or a fresh character seeded from <paramref name="seed"/>.</summary>
    /// <param name="seed">The fresh character's crafting seed.</param>
    /// <returns>The progression.</returns>
    public Progression Load(long seed)
    {
        var loaded = ProgressionSave.LoadFile(FilePath, seed);
        if (loaded.Error is { } error)
        {
            WarnOnce(error);
        }
        Talents = loaded.Talents;
        return loaded.Progression;
    }

    /// <summary>The talent picks the last <see cref="Load"/> read (BIBLE s6), or none before a load.</summary>
    public SavedTalents Talents { get; private set; } = SavedTalents.None;

    /// <summary>Saves <paramref name="progression"/> and the talent picks; a failure is warned once and the run carries on.</summary>
    /// <param name="progression">The progression to bank.</param>
    /// <param name="talents">The talent picks per class to keep with it, or null for none.</param>
    /// <returns>True when it was written.</returns>
    public bool Save(Progression progression, SavedTalents? talents = null)
    {
        var error = ProgressionSave.SaveFile(FilePath, progression, talents);
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
        GD.PushWarning($"PROGRESSION {message} ({FilePath})");
    }
}
