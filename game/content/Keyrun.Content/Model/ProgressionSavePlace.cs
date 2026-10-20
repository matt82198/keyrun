using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// Where the between-runs screen keeps the banked progression (lane PROGRESSION-SAVE, BIBLE s7: Ash and gear persist
/// across launches). Godot-free so xUnit can pin the decision; src/Hud/Screen.cs hands the result to
/// ProgressionStore. The interactive game saves in the Godot user data dir. A headless run (every scene test, CI and
/// the exported-exe e2e) persists nothing unless it names its own directory with <see cref="DirFlag"/>, so a test can
/// never load or overwrite the player's real save.
/// </summary>
public static class ProgressionSavePlace
{
    /// <summary>The user argument naming the save directory (<c>--progression-dir DIR</c>).</summary>
    public const string DirFlag = "--progression-dir";

    /// <summary>The directory to keep the save in, or null to persist nothing this launch.</summary>
    /// <param name="userArgs">The user arguments (after Godot's <c>--</c>).</param>
    /// <param name="headless">True when the display server is headless.</param>
    /// <param name="userDataDir">The Godot user data dir (<c>user://</c>, globalized).</param>
    /// <returns><see cref="DirFlag"/>'s value when given; else null when headless; else <paramref name="userDataDir"/>.
    /// A flag with no usable value (missing, empty, or another flag) is null: never a guessed location.</returns>
    public static string? Directory(IReadOnlyList<string> userArgs, bool headless, string userDataDir)
    {
        for (var i = 0; i < userArgs.Count; i++)
        {
            if (userArgs[i] != DirFlag)
            {
                continue;
            }
            var value = i + 1 < userArgs.Count ? userArgs[i + 1] : string.Empty;
            return value.Length == 0 || value.StartsWith("--", System.StringComparison.Ordinal) ? null : value;
        }
        return headless ? null : userDataDir;
    }
}
