using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// PURPOSE: this artifact exists to make a stun stop every cast of a stunnable actor in the keyrun sim (finding
/// f-c63b43bf, ruling: defer to WoW), with the uninterruptible flag meaning kick-immune only and boss stun-immunity a
/// separate flag; status: real.
/// run.json stunImmuneEnemies (game/content/data/run.json): the boss's stun-immunity flag, separate from an ability's
/// <c>interruptible: false</c> (kick-immune). C#-only because content/ is a frozen oracle root. A missing list or an id
/// that names no enemy fails the load; there is no default.
/// </summary>
public sealed class StunImmuneEnemiesTests
{
    private const string RunFile = "run.json";
    private const string Boss = "hollowWarden.boss";

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    [Fact(DisplayName = "The bosses are the stun-immune enemies (R-BOSS-3); their adds and the trash are stunnable")]
    public void Loads_the_boss_as_the_one_stun_immune_enemy()
    {
        var reg = ContentLoader.Load();
        // R-BOSS-3 (bosses are immune to CC): every boss-role enemy, the Hollow Warden and (D66) the Sunken Bellfoundry's
        // three, and nothing else.
        Assert.Equal(new[] { Boss, "bellfoundry.ommet", "bellfoundry.brineMother", "bellfoundry.greatBell" }, reg.Run.StunImmuneEnemies.ToArray());
        Assert.Equal(
            reg.Enemies.Values.Where(e => e.Role == EnemyRole.Boss).Select(e => e.Id).OrderBy(i => i, System.StringComparer.Ordinal),
            reg.Run.StunImmuneEnemies.OrderBy(i => i, System.StringComparer.Ordinal));
        Assert.DoesNotContain("bellfoundry.clapperA", reg.Run.StunImmuneEnemies);
        Assert.DoesNotContain("bellfoundry.clapperB", reg.Run.StunImmuneEnemies);
        Assert.Equal(EnemyRole.Boss, reg.Enemies[Boss].Role);
        Assert.DoesNotContain("hollowWarden.twinA", reg.Run.StunImmuneEnemies);
        Assert.DoesNotContain("hollowWarden.twinB", reg.Run.StunImmuneEnemies);
    }

    [Fact(DisplayName = "Stun immunity is separate from uninterruptible: the boss's casts are all kick-immune, and so are stunnable enemies' casts")]
    public void Stun_immunity_is_not_the_uninterruptible_flag()
    {
        var reg = ContentLoader.Load();
        Assert.All(reg.Enemies[Boss].Abilities, id => Assert.False(reg.Abilities[id].Interruptible));
        Assert.All(reg.Enemies["hollowWarden.twinA"].Abilities, id => Assert.False(reg.Abilities[id].Interruptible));
    }

    [Fact(DisplayName = "A missing stunImmuneEnemies list fails the load")]
    public void Missing_list_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => n.AsObject().Remove("stunImmuneEnemies")));
        Assert.Contains(RunFile, ex.Message);
    }

    [Fact(DisplayName = "A stunImmuneEnemies id that names no enemy fails the load")]
    public void Unknown_enemy_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => n["stunImmuneEnemies"] = new JsonArray("hollowWarden.nobody")));
        Assert.Contains("run stunImmuneEnemies", ex.Message);
        Assert.Contains("hollowWarden.nobody", ex.Message);
    }
}
