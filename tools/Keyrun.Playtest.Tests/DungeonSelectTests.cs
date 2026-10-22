using System;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// D65 / f-61f01ca3: a run can select its dungeon. The Sunken Bellfoundry is in the registry (three segments, three
/// bosses) but nothing chose it: the host played the constant "hollow-vault". These tests pin the selection seam the
/// arena's <c>--dungeon</c> flag, play.ps1's <c>-Dungeon</c> and the pre-run screen's pick all go through
/// (<see cref="DungeonPick"/>), and that a Playtest run at the selected id plays that dungeon.
/// </summary>
public sealed class DungeonSelectTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private const string Bellfoundry = "sunken-bellfoundry";

    [Fact(DisplayName = "D65: a key 2 seed 1 run selected on the Sunken Bellfoundry plays its dungeon and names all three of its bosses")]
    public void A_bellfoundry_run_names_all_three_bosses()
    {
        var bosses = Registry.Dungeons[Bellfoundry].Segments.Select(s => s.Boss.EnemyId).ToList();
        Assert.Equal(3, bosses.Count);

        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null) { DungeonId = Bellfoundry }, Registry);

        Assert.Equal(Bellfoundry, result.Setup.Dungeon.Id);
        var seen = string.Join('\n', result.Setup.Dungeon.Pulls.Select(p => p.Id)) + "\n" + result.Log
            + "\n" + string.Join('\n', result.Run.State.Actors.Select(a => a.Value.ClassId));
        foreach (var boss in bosses)
        {
            Assert.True(seen.Contains(boss, StringComparison.Ordinal), $"the Bellfoundry run never names boss '{boss}'");
        }
    }

    [Fact(DisplayName = "D65: the default run still selects the Hollow Vault")]
    public void The_default_run_is_the_hollow_vault()
    {
        Assert.Equal("hollow-vault", DungeonPick.DefaultId);
        Assert.Equal("hollow-vault", new PlaytestOptions(2, 1, null).DungeonId);
        Assert.Equal("hollow-vault", DungeonPick.Resolve(Registry, null));
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null), Registry);
        Assert.Equal("hollow-vault", result.Setup.Dungeon.Id);
    }

    [Fact(DisplayName = "D65: --dungeon sunken-bellfoundry resolves to that id")]
    public void The_flag_value_resolves_a_registry_dungeon()
    {
        Assert.Equal(Bellfoundry, DungeonPick.Resolve(Registry, Bellfoundry));
    }

    [Fact(DisplayName = "D65: an unknown --dungeon id fails loudly, naming the id and listing the registry's dungeons, never falling back")]
    public void An_unknown_dungeon_id_names_itself_and_the_known_ids()
    {
        var ex = Assert.Throws<ArgumentException>(() => DungeonPick.Resolve(Registry, "no-such-dungeon"));
        Assert.Contains("no-such-dungeon", ex.Message, StringComparison.Ordinal);
        foreach (var id in Registry.Dungeons.Keys)
        {
            Assert.Contains(id, ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "D65: only a dungeon the content lays out a map for can be queued; the Hollow Vault is that dungeon today")]
    public void Only_the_laid_out_dungeon_has_a_map()
    {
        Assert.True(DungeonPick.HasMap(Registry, "hollow-vault"));
        Assert.Equal(Registry.Layout.Dungeon == Bellfoundry, DungeonPick.HasMap(Registry, Bellfoundry));
    }

    [Fact(DisplayName = "D65: the pre-run pick cycles the registry's dungeon ids, default first, wrapping both ways")]
    public void The_pick_cycles_the_registry_ids()
    {
        var ids = DungeonPick.Ids(Registry);
        Assert.Equal("hollow-vault", ids[0]);
        Assert.Equal(Registry.Dungeons.Count, ids.Count);
        Assert.Equal(Bellfoundry, DungeonPick.Cycle(Registry, "hollow-vault", 1));
        Assert.Equal("hollow-vault", DungeonPick.Cycle(Registry, Bellfoundry, 1));
        Assert.Equal(Bellfoundry, DungeonPick.Cycle(Registry, "hollow-vault", -1));
        var (name, bosses) = DungeonPick.Describe(Registry, Bellfoundry);
        Assert.Equal(Registry.Dungeons[Bellfoundry].Name, name);
        Assert.Equal(3, bosses);
    }
}
