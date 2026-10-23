using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// G2.4 guide-as-data (finding f-40098929): the Warden playtester guide data must carry the rotation rows of
/// design/PLAYTESTER-GUIDE.md s3.2, the kicks (s3.3), telegraphs (s3.4) and never (s3.5) sections, and every
/// when-key the s4 Warden rows use must be in the guide loader's vocabulary. Red on main: warden.json carries
/// 6 rotation rows, no shieldBash row and no second guardBreak filler row, and none of the three sections.
/// </summary>
public sealed class GuideDataWardenTests
{
    private const string GuideResource = "content/data/guides/warden.json";

    // PLAYTESTER-GUIDE.md s3.2, rows 1-8 (row 0 is "(wait)": it has no press, so the guide schema has no slot for it).
    private static readonly string[] DocRotationPresses =
    {
        "warden.shieldBash",  // row 1: kick window, s3.2 row 1
        "warden.brace",       // row 2
        "warden.vigil",       // row 3
        "warden.bulwark",     // row 4
        "warden.reckoning",   // row 5
        "warden.guardBreak",  // row 6: pack cleave
        "warden.strike",      // row 7: setup
        "warden.guardBreak",  // row 8: filler, "Otherwise"
    };

    private static readonly string[] DocSections = { "kicks", "telegraphs", "never" };

    // when-keys the s4 Warden excerpt uses (PLAYTESTER-GUIDE.md lines 202-227), with the line each one appears on.
    private static readonly (string Key, int DocLine)[] DocWhenKeys =
    {
        ("kickDuty", 205),
        ("castTicksLeftAtMost", 205),
        ("resourceAtLeast", 211),
        ("effectNotUp", 212),
        ("attackersOnSelfAtLeast", 214),
        ("effectTicksLeftAtMost", 214),
        ("healthBelowPct", 215),
        ("targetIsKillTarget", 222),
        ("targetWindowNot", 222),
        ("insideTelegraph", 227),
    };

    private static JsonElement LoadWarden()
    {
        // Manifest names carry the platform separator (backslash on Windows); normalize as ContentSources does.
        System.Reflection.Assembly assembly = typeof(ContentLoader).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.Replace('\\', '/') == GuideResource)
            ?? throw new FileNotFoundException("embedded resource " + GuideResource + " not found");
        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException("embedded resource " + resource + " cannot be opened");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relative + " not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Warden_guide_has_one_rotation_row_per_playtester_guide_section_3_2_press_row()
    {
        JsonElement root = LoadWarden();
        var remaining = root.GetProperty("rotation").EnumerateArray()
            .Select(r => r.GetProperty("press").GetString() ?? string.Empty)
            .ToList();
        int actual = remaining.Count;

        var missing = new List<string>();
        for (int i = 0; i < DocRotationPresses.Length; i++)
        {
            if (!remaining.Remove(DocRotationPresses[i]))
            {
                missing.Add("doc row " + (i + 1) + " press " + DocRotationPresses[i]);
            }
        }

        Assert.True(
            actual == DocRotationPresses.Length && missing.Count == 0,
            "warden.json has " + actual + " rotation rows; PLAYTESTER-GUIDE.md s3.2 has " + DocRotationPresses.Length
            + " press rows (rows 1-8; row 0 '(wait)' has no press and no schema slot). Missing: "
            + (missing.Count == 0 ? "(none)" : string.Join("; ", missing)));
    }

    [Fact]
    public void Warden_guide_has_non_empty_kicks_telegraphs_and_never_sections()
    {
        JsonElement root = LoadWarden();
        var problems = new List<string>();
        foreach (string section in DocSections)
        {
            if (!root.TryGetProperty(section, out JsonElement el))
            {
                problems.Add(section + " is absent");
            }
            else if (el.ValueKind != JsonValueKind.Array)
            {
                problems.Add(section + " is not an array");
            }
            else if (el.GetArrayLength() == 0)
            {
                problems.Add(section + " is empty");
            }
        }

        Assert.True(
            problems.Count == 0,
            "warden.json sections missing (PLAYTESTER-GUIDE.md s3.3 kicks, s3.4 telegraphs, s3.5 never): "
            + string.Join("; ", problems));
    }

    [Fact]
    public void Warden_guide_when_keys_used_by_the_section_4_rows_are_all_accepted_by_the_guide_loader()
    {
        // The loader is Keyrun.Bots.Guides.GuideSet (ParseWhen), which this test project does not reference;
        // its case labels are read from source so the vocabulary cannot drift from the parser unnoticed.
        string source = File.ReadAllText(FindRepoFile("game/bots/Keyrun.Bots/Guides/GuideSet.cs"));
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(source, "case \"([A-Za-z]+)\":"))
        {
            accepted.Add(m.Groups[1].Value);
        }
        Assert.True(accepted.Count >= 8, "vacuity guard: only " + accepted.Count + " when-keys parsed from GuideSet.ParseWhen");

        var unsupported = DocWhenKeys
            .Where(k => !accepted.Contains(k.Key))
            .Select(k => k.Key + " (PLAYTESTER-GUIDE.md:" + k.DocLine + ")")
            .ToList();

        Assert.True(
            unsupported.Count == 0,
            "guide loader (game/bots/Keyrun.Bots/Guides/GuideSet.cs ParseWhen) rejects " + unsupported.Count
            + " when-key(s) the s4 Warden rows use: " + string.Join(", ", unsupported));
    }
}
