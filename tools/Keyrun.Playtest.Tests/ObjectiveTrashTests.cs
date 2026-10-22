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
}
