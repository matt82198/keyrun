using System;

namespace Keyrun.Content;

/// <summary>
/// Loads game/content/data/theme.json (logical path <see cref="ContentLoader.ThemeFile"/>, embedded beside layout.json
/// by the same Keyrun.Content.csproj glob and listed in <see cref="ContentLoader.Files"/>) with the content loader's
/// strict JSON rules, then checks it fail-closed against the loaded layout and dungeon with <see cref="RoomTheme.Violations"/>.
/// The registry does not carry the theme: it is render data, read only by the view (DungeonBuilder), never by the sim.
/// </summary>
public static class ThemeLoader
{
    /// <summary>Loads and checks the theme embedded in Keyrun.Content.dll.</summary>
    /// <returns>The theme.</returns>
    public static ThemeDef Load() => Load(EmbeddedContentSource.Instance);

    /// <summary>Loads and checks the theme from <paramref name="source"/> (which must also hold the rest of the content).</summary>
    /// <param name="source">A full content source.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="ContentLoadException">Content fails to load, theme.json fails to parse, or a row breaks the placement rule.</exception>
    public static ThemeDef Load(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var registry = ContentLoader.Load(source);
        var theme = ContentLoader.ReadOne<ThemeDef>(source, ContentLoader.ThemeFile)[0];
        var errors = RoomTheme.Violations(theme, ThemeSite.Of(registry));
        if (errors.Count > 0)
        {
            throw new ContentLoadException("content: " + ContentLoader.ThemeFile + " " + string.Join("; ", errors));
        }
        return theme;
    }
}
