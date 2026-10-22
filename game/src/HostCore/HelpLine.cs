using System;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// The in-run controls hint line (the Arena's <c>Hud/Help</c> label): the run's own dungeon display name, then the
/// controls. f-caa537fc (P0 wrong data): the name comes from the run's dungeon, never a literal, so a Bellfoundry run
/// cannot show the Hollow Vault's name. Godot-free.
/// </summary>
public static class HelpLine
{
    /// <summary>The controls part of the line, after the dungeon name.</summary>
    public const string Controls = " - WASD move, hold right mouse to look, 1-5 abilities, Q kick (hover a foe), E defensive, R utility, F charge, Tab target, G ping, V camera";

    /// <summary>The hint line for a run on <paramref name="dungeonId"/>.</summary>
    /// <param name="registry">The content.</param>
    /// <param name="dungeonId">The run's dungeon id (an id the registry lacks throws, see <see cref="DungeonPick.Describe"/>).</param>
    /// <returns>The display name, then <see cref="Controls"/>.</returns>
    public static string For(C.ContentRegistry registry, string dungeonId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var (name, _) = DungeonPick.Describe(registry, dungeonId);
        return name + Controls;
    }
}
