using System;
using System.IO;
using System.Text;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The opt-in rewrite switch for the talent patch parity snapshot. With the environment variable
/// <c>KEYRUN_REGEN_PARITY=1</c> set, this test regenerates <c>Fixtures/talent-patch-parity.json</c> from the loaded
/// content through <see cref="TalentPatchParitySnapshot.Generate"/>. Without the variable it returns at once and writes
/// nothing, so a default test run never touches the fixture. Run it only after a deliberate content change, then review
/// the fixture diff and run the suite again.
/// </summary>
public sealed class TalentPatchParityRegenTests
{
    /// <summary>The environment variable that turns the rewrite on.</summary>
    public const string RegenVariable = "KEYRUN_REGEN_PARITY";

    [Fact]
    public void Regenerate_the_fixture_only_when_the_regen_switch_is_set()
    {
        if (Environment.GetEnvironmentVariable(RegenVariable) != "1") return;

        var text = TalentPatchParitySnapshot.Generate(ContentLoader.Load());
        File.WriteAllText(SnapshotPath(), text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string SnapshotPath()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var path = Path.Combine(d.FullName, "game", "content", "Keyrun.Content.Tests", "Fixtures", "talent-patch-parity.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Fixtures/talent-patch-parity.json not found above " + AppContext.BaseDirectory);
    }
}
