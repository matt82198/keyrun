using System;
using System.Linq;
using Keyrun.Content;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-c5fb7b31 (P0, wrong data on the HUD): while the party fights a generated trash pack in the Sunken Bellfoundry, the
/// objective line must describe what is fought, not the next boss pull. The header may keep naming the next boss; the
/// remain count counts the engaged pack's living enemies, or is hidden while trash is alive and no boss pull is engaged.
/// Driven by a real run (key 2, seed 1), observed at the first tick a trash pack is engaged while the next boss is ahead.
/// </summary>
public sealed class ObjectiveTrashTests
{
    private const string Dungeon = "sunken-bellfoundry";

    [Fact(DisplayName = "Objective during an engaged Bellfoundry trash pack counts the pack's living enemies, never \"1 of 1 remain\" of the next boss")]
    public void Objective_during_engaged_trash_counts_the_pack_not_the_next_boss()
    {
        var reg = ContentLoader.Load();
        var captured = false;
        var task = string.Empty;
        var remaining = -1;
        var living = -1;
        var options = new PlaytestOptions(2, 1, null)
        {
            DungeonId = Dungeon,
            Observe = (tick, run, _) =>
            {
                if (captured || run.Encounter.PullIndex < 0 || run.Encounter.PullIndex >= run.Dungeon.Pulls.Count)
                {
                    return;
                }
                var pull = run.Dungeon.Pulls[run.Encounter.PullIndex];
                if (!LayoutDef.IsBossPull(pull.Id) || run.Encounter.Engaged.Contains(pull.Id, StringComparer.Ordinal))
                {
                    return;
                }
                var packs = Aggro.GroupsOf(run.Dungeon, tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols)
                    .Where(g => g.PullIndex < 0 && run.Encounter.Engaged.Contains(g.Id, StringComparer.Ordinal))
                    .ToList();
                living = packs.SelectMany(g => g.ActorIds)
                    .Count(id => run.State.Actors.TryGetValue(id, out var a) && a.Alive && a.Kind == ActorKind.Enemy);
                if (living == 0)
                {
                    return;
                }
                var line = ObjectiveModel.Build(run, reg);
                task = line.Task;
                remaining = line.Remaining;
                captured = true;
            },
        };

        PlaytestRunner.Play(options, reg);

        Assert.True(captured, "no trash pack was engaged while the next boss pull was still ahead");
        Assert.DoesNotContain("1 of 1 remain", task, StringComparison.Ordinal);
        Assert.Equal(living, remaining);
    }

    /// <summary>Runs one playtest and returns the objective line at the first observed tick that satisfies <paramref name="when"/>.</summary>
    private static ObjectiveLine? FirstLineWhen(string dungeonId, Func<int, Run, bool> when, ContentRegistry reg)
    {
        ObjectiveLine? found = null;
        var options = new PlaytestOptions(2, 1, null)
        {
            DungeonId = dungeonId,
            Observe = (tick, run, _) =>
            {
                if (found is null && when(tick, run))
                {
                    found = ObjectiveModel.Build(run, reg);
                }
            },
        };
        PlaytestRunner.Play(options, reg);
        return found;
    }

    [Fact(DisplayName = "Bellfoundry segment 1 trash header reads \"Segment 1/3: toward <segment 1 boss>\", never \"Pull 1/3\" of the boss")]
    public void Header_during_segment_one_trash_names_the_segment_and_its_boss()
    {
        var reg = ContentLoader.Load();
        var line = FirstLineWhen(Dungeon, (tick, run) =>
        {
            if (run.Encounter.PullIndex < 0 || run.Encounter.PullIndex >= run.Dungeon.Pulls.Count)
            {
                return false;
            }
            var pull = run.Dungeon.Pulls[run.Encounter.PullIndex];
            if (!LayoutDef.IsBossPull(pull.Id) || run.Encounter.Engaged.Contains(pull.Id, StringComparer.Ordinal))
            {
                return false;
            }
            return Aggro.GroupsOf(run.Dungeon, tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols)
                .Any(g => g.PullIndex < 0 && run.Encounter.Engaged.Contains(g.Id, StringComparer.Ordinal));
        }, reg);

        Assert.NotNull(line);
        Assert.Equal(ObjectiveFocus.Pack, line!.Focus);
        Assert.Equal("Segment 1/3: toward " + reg.Enemies["bellfoundry.ommet"].Name, line.Title);
    }

    [Fact(DisplayName = "Bellfoundry segment 1 boss header reads \"Boss 1/3: <segment 1 boss>\" while that boss is the pull")]
    public void Header_during_segment_one_boss_names_the_boss()
    {
        var reg = ContentLoader.Load();
        var line = FirstLineWhen(Dungeon, (tick, run) =>
            run.Encounter.PullIndex >= 0
            && run.Encounter.PullIndex < run.Dungeon.Pulls.Count
            && run.Dungeon.Pulls[run.Encounter.PullIndex].Id == "boss-s1"
            && run.Encounter.Engaged.Contains("boss-s1", StringComparer.Ordinal), reg);

        Assert.NotNull(line);
        Assert.Equal(ObjectiveFocus.Pull, line!.Focus);
        Assert.Equal("Boss 1/3: " + reg.Enemies["bellfoundry.ommet"].Name, line.Title);
    }

    [Fact(DisplayName = "Hollow Vault header is unchanged: \"Pull 1/4  <first pull name>\" on the single-boss tutorial")]
    public void Header_on_hollow_vault_keeps_the_pull_wording()
    {
        var reg = ContentLoader.Load();
        var vault = reg.Dungeons["hollow-vault"];
        var line = FirstLineWhen("hollow-vault", (tick, run) => run.Encounter.PullIndex == 0, reg);

        Assert.NotNull(line);
        Assert.Equal("Pull 1/" + (vault.Pulls.Count + 1) + "  " + vault.Pulls[0].Name, line!.Title);
    }
}
