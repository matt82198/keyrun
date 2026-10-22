using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// f-b24741e2: ONE resolver (<see cref="AbilityNames.Display"/>) gives the name every player-facing surface prints (cast
/// bars, run log, party and unit frames, pre-run screen, talent panel). A player kit ability reads its ability-text.json
/// display name, every other ability (enemy casts) keeps its content name, an unknown id falls back to the id, and no
/// shipped player kit ability can reach that fallback.
/// </summary>
public sealed class AbilityNamesTests
{
    private static ContentRegistry Registry { get; } = ContentLoader.Load();

    [Fact(DisplayName = "a player kit ability resolves to its themed display name, not its content name")]
    public void Player_ability_reads_the_themed_name()
    {
        Assert.Equal("Sundering Verdict", AbilityNames.Display(Registry, "warden.strike"));
        Assert.NotEqual(Registry.Abilities["warden.strike"].Name, AbilityNames.Display(Registry, "warden.strike"));
    }

    [Fact(DisplayName = "an enemy ability keeps its content name; an unknown id and a null registry fall back to the id")]
    public void Enemy_and_unknown_fall_back()
    {
        Assert.Equal("Choral Mending", AbilityNames.Display(Registry, "choir.healCast"));
        Assert.Equal("no.such.ability", AbilityNames.Display(Registry, "no.such.ability"));
        Assert.Equal("warden.strike", AbilityNames.Display(null, "warden.strike"));
    }

    [Fact(DisplayName = "every player kit ability id in content has a display name, so the id fallback never shows in shipped content")]
    public void Every_kit_id_has_a_display_name()
    {
        var checkedIds = 0;
        foreach (var cls in Registry.Classes.Values)
        {
            foreach (var id in AbilityTextLoader.KitIds(cls.Abilities))
            {
                var shown = AbilityNames.Display(Registry, id);
                Assert.NotEqual(id, shown);
                Assert.Equal(AbilityTextLoader.Load().For(id)!.DisplayName, shown);
                checkedIds++;
            }
        }
        Assert.True(checkedIds >= 9, $"only {checkedIds} kit ids checked");
        Assert.Equal(checkedIds, AbilityTextLoader.Load().Rows.Count);
    }

    [Fact(DisplayName = "no enemy ability id is renamed: enemy abilities resolve to exactly their content name")]
    public void Enemy_abilities_are_untouched()
    {
        var kit = Registry.Classes.Values.SelectMany(c => AbilityTextLoader.KitIds(c.Abilities)).ToHashSet();
        var enemyAbilities = Registry.Abilities.Where(kv => !kit.Contains(kv.Key)).ToList();
        Assert.NotEmpty(enemyAbilities);
        foreach (var (id, def) in enemyAbilities)
        {
            Assert.Equal(def.Name, AbilityNames.Display(Registry, id));
        }
    }
}
