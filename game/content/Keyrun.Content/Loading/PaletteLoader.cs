using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// Loads game/content/data/palette.json (logical path <see cref="ContentLoader.PaletteFile"/>, embedded beside theme.json
/// by the same Keyrun.Content.csproj glob and listed in <see cref="ContentLoader.Files"/>) with the content loader's strict JSON
/// rules, the same way <see cref="ThemeLoader"/> reads theme.json. The palette is render data: the view (TelegraphView,
/// GreyBox) reads it, the sim never does.
/// </summary>
public static class PaletteLoader
{
    /// <summary>Loads the palette embedded in Keyrun.Content.dll.</summary>
    /// <returns>The palette.</returns>
    public static PaletteDef Load() => Load(EmbeddedContentSource.Instance);

    /// <summary>Loads the palette from <paramref name="source"/>.</summary>
    /// <param name="source">A content source holding palette.json.</param>
    /// <returns>The palette.</returns>
    /// <exception cref="ContentLoadException">palette.json fails to parse against the strict content rules.</exception>
    public static PaletteDef Load(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var palette = ContentLoader.ReadOne<PaletteDef>(source, ContentLoader.PaletteFile)[0];
        var errors = new List<string>();
        Check("floors.room", palette.Floors.Room, errors);
        Check("floors.corridor", palette.Floors.Corridor, errors);
        foreach (var (name, shape) in new[]
                 {
                     ("circle", palette.Telegraph.Circle), ("cone", palette.Telegraph.Cone), ("line", palette.Telegraph.Line),
                 })
        {
            Check("telegraph." + name + ".fill", shape.Fill, errors);
            Check("telegraph." + name + ".outline", shape.Outline, errors);
            Check("telegraph." + name + ".flash", shape.Flash, errors);
        }
        if (errors.Count > 0)
        {
            throw new ContentLoadException("content: " + ContentLoader.PaletteFile + " " + string.Join("; ", errors));
        }
        return palette;
    }

    // Channels 0..255 and alpha 0..100, the integer ranges the view divides out.
    private static void Check(string at, PaletteColor c, List<string> errors)
    {
        if (c.R is < 0 or > 255 || c.G is < 0 or > 255 || c.B is < 0 or > 255 || c.A is < 0 or > 100)
        {
            errors.Add(at + " (" + c.R + "," + c.G + "," + c.B + "," + c.A + ") is out of range (rgb 0..255, alpha 0..100)");
        }
    }
}
