using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// ContentRules is the one ordered list ContentValidator.Validate runs. A rule added as a file under Loading/Rules/ but
/// never registered would silently never run, so the list must name every IContentRule in the content assembly once,
/// and its length must equal the rule files on disk (every .cs under Loading/Rules/ except the interface and the list).
/// </summary>
public sealed class ContentRulesRegistryTests
{
    [Fact]
    public void Every_rule_type_in_the_content_assembly_is_registered_exactly_once()
    {
        Type[] ruleTypes = typeof(ContentRules).Assembly.GetTypes()
            .Where(t => typeof(IContentRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        Type[] registered = ContentRules.All.Select(r => r.GetType()).OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        Assert.True(ruleTypes.Length >= 18, "vacuity guard: only " + ruleTypes.Length + " rule types found");
        Assert.Equal(ruleTypes.Select(t => t.Name), registered.Select(t => t.Name));
    }

    [Fact]
    public void Registry_count_equals_the_rule_files_under_Loading_Rules()
    {
        string[] files = Directory.GetFiles(RulesDir(), "*.cs")
            .Select(Path.GetFileName)
            .Where(n => n != "IContentRule.cs" && n != "ContentRules.cs")
            .Select(n => n!)
            .ToArray();
        Assert.Equal(files.Length, ContentRules.All.Count);
        Assert.Equal(files.Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(n => n, StringComparer.Ordinal),
            ContentRules.All.Select(r => r.GetType().Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Rules_run_in_the_old_fail_fast_order()
    {
        string[] expected =
        {
            "AbilitiesRule", "ClassesRule", "CombosRule", "ComboRidersRule", "EnemiesRule", "DungeonsRule", "FacetsRule",
            "RunConfigRule", "HeatOverheatRule", "ClassArmorRule", "PairMarksRule", "StunImmuneRule", "MeleeAbilitiesRule",
            "ForcesRule", "LayoutRule", "BranchTotalsRule", "SkipBarRule", "GearRule",
        };
        Assert.Equal(expected, ContentRules.All.Select(r => r.GetType().Name));
    }

    private static string RulesDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var path = Path.Combine(d.FullName, "game", "content", "Keyrun.Content", "Loading", "Rules");
            if (Directory.Exists(path)) return path;
        }
        throw new DirectoryNotFoundException("game/content/Keyrun.Content/Loading/Rules not found above " + AppContext.BaseDirectory);
    }
}
