using System;
using System.IO;

namespace Keyrun.Playtest;

/// <summary>
/// The how-to-play guide a playtester reads first (GUIDE.md beside this file, embedded in the assembly and printed by
/// `playtest guide`). It teaches the controls, the plan file, how to read a run, the key rule and the learn loop. It never
/// names an enemy or an enemy ability (GuideTests enforces it): what to kick, dodge or focus is learned by playing.
/// </summary>
public static class Guide
{
    /// <summary>The embedded resource name of GUIDE.md.</summary>
    public const string ResourceName = "Keyrun.Playtest.GUIDE.md";

    private static readonly Lazy<string> Cached = new(Read);

    /// <summary>The guide text (LF line ends).</summary>
    public static string Text => Cached.Value;

    private static string Read()
    {
        using var stream = typeof(Guide).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the guide resource {ResourceName} is missing from the assembly");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
