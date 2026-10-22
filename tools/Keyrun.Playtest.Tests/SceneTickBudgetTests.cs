using System;
using System.IO;
using System.Text.RegularExpressions;
using Keyrun.Content;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// PR #599 (f-3dcc702c) lengthened the scripted autorun: the leader pulls to 100% forces before the boss, so the boss engages
/// at tick 9746 and the run ends at 12294 at key 2 seed 1 (it was 3112 and 5845). Four game-e2e scenes run the plain autorun
/// to the boss or to run_end on a fixed tick budget (HudRunC, RunEndBonus run C, BossCastRow, BreakBar) and failed on the old
/// 7000 and 9000 budgets with "never reached the boss" and "no run_end". This pins each budget above the measured run end,
/// read from the .ps1 defaults, so a route that grows again fails here and not after thirty minutes of CI.
/// </summary>
public sealed class SceneTickBudgetTests
{
    private static readonly Lazy<int> RunEndTick = new(() =>
    {
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null) { PullToForces = true }, ContentLoader.Load());
        return result.Run.RunEnd.ResultTick ?? throw new InvalidOperationException("the pull-to-100 autorun never ended");
    });

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "src", "Host", "scenes", "HudSceneCommon.ps1")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The default of <c>[int]$name = N</c> in a scene script's param block.</summary>
    private static int Budget(string relativePath, string name)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var m = Regex.Match(text, @"\[int\]\$" + name + @"\s*=\s*(\d+)");
        Assert.True(m.Success, $"{relativePath}: no [int]${name} default");
        return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Theory(DisplayName = "Scene budget: the plain-autorun scenes keep their tick budget past the pull-to-100 run end")]
    [InlineData("game/src/Host/scenes/HudRunC-SceneTest.ps1", "PartyTicks")]
    [InlineData("game/src/Hud/RunEndBonusSceneTest.ps1", "PartyTicks")]
    [InlineData("game/src/Hud/BossCastRowSceneTest.ps1", "Ticks")]
    [InlineData("game/src/Hud/BreakBarSceneTest.ps1", "Ticks")]
    public void The_budget_covers_the_run_end(string script, string parameter)
    {
        var budget = Budget(script, parameter);
        Assert.True(budget > RunEndTick.Value, $"{script} ${parameter}={budget} ends before the autorun's run_end at tick {RunEndTick.Value}");
    }
}
