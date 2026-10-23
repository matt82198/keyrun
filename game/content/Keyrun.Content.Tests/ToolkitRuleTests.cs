using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// design/TOOLKIT-CONTRACT.md section 5 lane L1 (R-TOOLKIT-1, R-TOOLKIT-2): every class kit has a mobility button that
/// moves the caster, a defensive and a utility button; every pull and boss carries a threat that needs mobility and one
/// that needs a defensive. <see cref="ToolkitRule"/> is a REPORT, not a gate (it is not in ContentRules.All): the real
/// content has known gaps, committed as design/toolkit-gaps/*.json and pinned here. A lane that closes a gap must
/// regenerate the files (set KEYRUN_REGEN_TOOLKIT_GAPS=1 and run this class once).
/// </summary>
public sealed class ToolkitRuleTests
{
    private const string RegenEnv = "KEYRUN_REGEN_TOOLKIT_GAPS";

    [Fact]
    public void Real_classes_report_each_missing_toolkit_button_with_class_and_code()
    {
        var gaps = ToolkitRule.ClassGaps(ContentLoader.Load());

        Assert.NotEmpty(gaps);
        Assert.All(gaps, g => Assert.Equal("class", g.Subject));
        // Exact set (R-TOOLKIT-3): derived by running ToolkitRule.ClassGaps on the real content after the S2 ability and
        // ToolkitRule changes (design/toolkit-gaps/class-gaps.json is regenerated from the same report). Only
        // warden.charge still removes no slow or root.
        Assert.Equal(
            new[] { "warden/mobility-frees-binds" },
            gaps.Select(g => g.Id + "/" + g.Code).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(gaps, g => g.Code is "defensive-missing" or "utility-missing");
    }

    [Fact]
    public void Real_encounters_report_each_missing_threat_with_encounter_and_code()
    {
        var gaps = ToolkitRule.EncounterGaps(ContentLoader.Load());

        Assert.All(gaps, g => Assert.Equal("encounter", g.Subject));
        foreach (string id in new[] { "choir", "pair", "seed", "hollowWarden.boss", "bellfoundry.ommet", "bellfoundry.brineMother", "bellfoundry.greatBell" })
        {
            Assert.Contains(gaps, g => g.Id == id && g.Code == "threat-mobility");
            Assert.Contains(gaps, g => g.Id == id && g.Code == "threat-defensive");
        }
    }

    [Fact]
    public void A_fixture_kit_with_the_full_toolkit_passes()
    {
        var reg = ContentLoader.Load();
        reg = WithAbility(reg, reg.Abilities["cinder.blink"] with
        {
            Effects = new ValueList<EffectOp>(new EffectOp[]
            {
                new ApplyEffectOp { EffectId = "gliding", DurationTicks = 60, Stacks = 1 },
                new RemoveEffectOp { EffectId = "slowed" },
            }),
        });

        Assert.DoesNotContain(ToolkitRule.ClassGaps(reg), g => g.Id == "cinder");
    }

    [Fact]
    public void A_fixture_kit_whose_movement_button_changes_nothing_is_rejected()
    {
        var reg = ContentLoader.Load();
        reg = WithAbility(reg, reg.Abilities["chanter.glide"] with
        {
            Effects = new ValueList<EffectOp>(new EffectOp[] { new ResourceOp { Delta = 0 } }),
        });

        Assert.Contains(ToolkitRule.ClassGaps(reg), g => g.Id == "chanter" && g.Code == "mobility-moves-caster");
    }

    [Fact]
    public void A_fixture_kit_without_a_defensive_effect_is_rejected()
    {
        var reg = ContentLoader.Load();
        reg = WithAbility(reg, reg.Abilities["warden.brace"] with
        {
            Effects = new ValueList<EffectOp>(new EffectOp[] { new ResourceOp { Delta = 0 } }),
        });

        Assert.Contains(ToolkitRule.ClassGaps(reg), g => g.Id == "warden" && g.Code == "defensive-missing");
    }

    [Fact]
    public void A_fixture_pull_with_both_threats_passes()
    {
        var reg = FixtureRegistry(new[]
        {
            Step("fixture.tollRing", 300, interruptible: false, castTicks: 0, new TelegraphSpec { Shape = TelegraphShape.Circle, Radius = 9000, LeadTicks = 10 }),
            Step("fixture.lastVerse", 300, interruptible: false, castTicks: 60, new TelegraphSpec { Shape = TelegraphShape.Circle, Radius = 500, LeadTicks = 10, SpansCast = true }),
        });

        Assert.DoesNotContain(ToolkitRule.EncounterGaps(reg), g => g.Id == "fixture-pull");
    }

    [Fact]
    public void A_fixture_pull_with_only_walkable_or_survivable_casts_reports_both_gaps()
    {
        var reg = FixtureRegistry(new[]
        {
            Step("fixture.walkable", 200, interruptible: false, castTicks: 0, new TelegraphSpec { Shape = TelegraphShape.Circle, Radius = 1000, LeadTicks = 10 }),
            Step("fixture.small", 100, interruptible: false, castTicks: 0, new TelegraphSpec { Shape = TelegraphShape.Circle, Radius = 9000, LeadTicks = 10 }),
            Step("fixture.kickable", 400, interruptible: true, castTicks: 40, new TelegraphSpec { Shape = TelegraphShape.Circle, Radius = 500, LeadTicks = 10 }),
        });

        var gaps = ToolkitRule.EncounterGaps(reg).Where(g => g.Id == "fixture-pull").Select(g => g.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "threat-defensive", "threat-mobility" }, gaps);
    }

    [Fact]
    public void An_untelegraphed_lethal_cast_counts_for_neither_threat()
    {
        var reg = FixtureRegistry(new[] { Step("fixture.hidden", 400, interruptible: false, castTicks: 0, telegraph: null) });

        Assert.Equal(2, ToolkitRule.EncounterGaps(reg).Count(g => g.Id == "fixture-pull"));
    }

    [Fact]
    public void The_committed_gap_files_equal_the_validator_report_on_real_content()
    {
        var reg = ContentLoader.Load();
        Assert.True(reg.Classes.Count >= 3, "vacuity guard: classes examined " + reg.Classes.Count);
        Assert.True(ToolkitRule.EncounterIds(reg).Count >= 7, "vacuity guard: encounters examined " + ToolkitRule.EncounterIds(reg).Count);

        string classText = ToolkitRule.Render(ToolkitRule.ClassGaps(reg), ClassFile);
        string encounterText = ToolkitRule.Render(ToolkitRule.EncounterGaps(reg), EnemyFileOf(reg));
        string classPath = Path.Combine(GapDir(), "class-gaps.json");
        string encounterPath = Path.Combine(GapDir(), "encounter-gaps.json");

        if (Environment.GetEnvironmentVariable(RegenEnv) == "1")
        {
            File.WriteAllText(classPath, classText);
            File.WriteAllText(encounterPath, encounterText);
        }

        Assert.Equal(classText, Lf(File.ReadAllText(classPath)));
        Assert.Equal(encounterText, Lf(File.ReadAllText(encounterPath)));
    }

    private static string Lf(string s) => s.Replace("\r\n", "\n");

    private static string ClassFile(string classId) => "content/data/classes/" + classId + ".json";

    /// <summary>Maps an encounter id (a pull id or a boss enemy id) to the enemy json file that owns its script.</summary>
    private static Func<string, string> EnemyFileOf(ContentRegistry reg)
    {
        string root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "content", "data", "enemies"), "*.json")
            .Concat(Directory.GetFiles(Path.Combine(root, "game", "content", "data", "enemies"), "*.json")).ToArray();
        return encounterId =>
        {
            string enemyId = ToolkitRule.ScriptOwners(reg, encounterId).First();
            foreach (string f in files)
                if (File.ReadAllText(f).Contains("\"id\": \"" + enemyId + "\"", StringComparison.Ordinal))
                    return Path.GetRelativePath(root, f).Replace('\\', '/');
            throw new InvalidOperationException("no enemy file declares " + enemyId);
        };
    }

    private static ContentRegistry WithAbility(ContentRegistry reg, AbilityDef ability)
    {
        var map = reg.Abilities.ToDictionary(kv => kv.Key, kv => kv.Value);
        map[ability.Id] = ability;
        return reg with { Abilities = new ValueMap<AbilityDef>(map) };
    }

    private static (AbilityDef Ability, ScriptStep Step) Step(string id, int damage, bool interruptible, int castTicks, TelegraphSpec? telegraph)
    {
        var ability = new AbilityDef
        {
            Id = id, Name = id, CastTicks = castTicks, GcdTicks = 0, CooldownTicks = 0, Charges = 1,
            Cost = new AbilityCost { Resource = "none", Amount = 0 }, Range = 0,
            Shape = new AbilityShape { Kind = ShapeKind.Self }, Interruptible = interruptible,
            Tags = new ValueList<string>(),
            Effects = new ValueList<EffectOp>(new EffectOp[] { new DamageOp { Amount = damage, School = "physical" } }),
        };
        var step = new ScriptStep { At = new ScriptAt { Kind = ScriptAtKind.Loop, Tick = 0 }, Every = 400, Cast = id, Telegraph = telegraph };
        return (ability, step);
    }

    private static ContentRegistry FixtureRegistry(IEnumerable<(AbilityDef Ability, ScriptStep Step)> casts)
    {
        var reg = ContentLoader.Load();
        var list = casts.ToList();
        var abilities = reg.Abilities.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (a, _) in list) abilities[a.Id] = a;
        var enemy = new EnemyDef
        {
            Id = "fixture.enemy", Name = "Fixture", MaxHealth = 1000, Role = EnemyRole.Melee,
            Abilities = new ValueList<string>(list.Select(c => c.Ability.Id)),
            Script = new ValueList<ScriptStep>(list.Select(c => c.Step)),
        };
        var enemies = reg.Enemies.ToDictionary(kv => kv.Key, kv => kv.Value);
        enemies[enemy.Id] = enemy;
        var dungeon = reg.Dungeons["hollow-vault"];
        var pull = new PullDef
        {
            Id = "fixture-pull", Name = "Fixture", Teaches = "fixture",
            Enemies = new ValueList<PullEnemy>(new[] { new PullEnemy { EnemyId = enemy.Id, Count = 1, Spawn = new SpawnPoint { X = 0, Z = 0 } } }),
        };
        var dungeons = reg.Dungeons.ToDictionary(kv => kv.Key, kv => kv.Value);
        dungeons["hollow-vault"] = dungeon with { Pulls = new ValueList<PullDef>(new[] { pull }) };
        return reg with
        {
            Abilities = new ValueMap<AbilityDef>(abilities),
            Enemies = new ValueMap<EnemyDef>(enemies),
            Dungeons = new ValueMap<DungeonDef>(dungeons),
        };
    }

    private static string GapDir() => Path.Combine(RepoRoot(), "design", "toolkit-gaps");

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "design")) && Directory.Exists(Path.Combine(d.FullName, "game"))) return d.FullName;
        }
        throw new DirectoryNotFoundException("repo root (design/ and game/) not found above " + AppContext.BaseDirectory);
    }
}
