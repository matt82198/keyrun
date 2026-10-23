using Godot;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// The Godot host side of BIBLE s7 persistence: resolves the Godot user data dir (<c>user://</c>) and delegates every
/// load and save to the Godot-free <see cref="ProgressionStoreCore"/>, warning through <see cref="GD.PushWarning(string)"/>.
/// It never throws: a missing save is a fresh character, a corrupt or unwritable one is a fresh character (or an
/// unsaved run) plus one warning per store, so persistence can never block or crash a run.
/// </summary>
public sealed class ProgressionStore
{
    private readonly ProgressionStoreCore _core;

    /// <summary>A store whose save file lives in <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory (created on first save).</param>
    public ProgressionStore(string directory)
    {
        _core = new ProgressionStoreCore(directory, message => GD.PushWarning(message));
    }

    /// <summary>The save file's absolute OS path.</summary>
    public string FilePath => _core.FilePath;

    /// <summary>The store in the Godot user data dir (<c>user://</c>, globalized to an OS path).</summary>
    /// <returns>The store.</returns>
    public static ProgressionStore UserData() => new(ProjectSettings.GlobalizePath("user://"));

    /// <summary>Loads the banked progression, or a fresh character seeded from <paramref name="seed"/>.</summary>
    /// <param name="seed">The fresh character's crafting seed.</param>
    /// <returns>The progression.</returns>
    public Progression Load(long seed) => _core.Load(seed);

    /// <summary>
    /// Loads the banked progression, migrating a v1 save's history to <paramref name="lastQueuedClassId"/> (f-6ee6bccf,
    /// G2.2 R-TREES-3 (b)), or a fresh character seeded from <paramref name="seed"/>.
    /// </summary>
    /// <param name="seed">The fresh character's crafting seed.</param>
    /// <param name="lastQueuedClassId">The class id the screen queues (null keeps the 2-arg behaviour).</param>
    /// <returns>The progression.</returns>
    public Progression Load(long seed, string? lastQueuedClassId) => _core.Load(seed, lastQueuedClassId);

    /// <summary>Saves <paramref name="progression"/>; a failure is warned once and the run carries on.</summary>
    /// <param name="progression">The progression to bank.</param>
    /// <returns>True when it was written.</returns>
    public bool Save(Progression progression) => _core.Save(progression);
}
