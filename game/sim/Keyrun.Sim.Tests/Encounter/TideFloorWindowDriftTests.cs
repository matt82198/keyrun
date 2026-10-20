using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// BIBLE.md:156 "*Tide* (floor patches drift)" and content/data/clauses.json tide "Floor patches (Windows and hazards)
/// drift slowly across the arena.": with Tide the run's active Clause, a player's floor Window (BIBLE.md:57/84 a floor
/// patch is a floor Window) must move across ticks, not only the enemy telegraphs.
/// </summary>
public sealed class TideFloorWindowDriftTests
{
    private const long Seed = 7;
    private const int Ticks = 20;

    private static Run TideRunWithPatch(string? clauseId)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var request = clauseId is null ? null : new QueueRequest("hollow-vault", 3, clauseId);
        var run = RunRoot.CreateRun(Seed, setup.Party, setup.Dungeon, setup.Content, request?.ToRunOptions());
        var player = run.State.Actors.Values.First(a => a.Kind == ActorKind.Player);
        var state = run.State.Clone();
        state.FloorWindows["warden.bulwark"] = WindowState.MakeFloorWindow(
            "warden.bulwark", "warden", player.Id, new Vec3(1000, 0, 1000), 100000, null);
        return run with { State = state };
    }

    private static long PatchXAfter(Run run, int ticks)
    {
        for (var t = 0; t < ticks; t++)
        {
            run = RunRoot.StepRun(run, System.Array.Empty<Command>()).Run;
        }
        Assert.True(run.State.FloorWindows.ContainsKey("warden.bulwark"), "the patch expired inside the window");
        return run.State.FloorWindows["warden.bulwark"].Pos.X;
    }

    [Fact(DisplayName = "Tide: a player's floor Window drifts +x by driftMmPerTick each tick")]
    public void Tide_drifts_a_player_floor_window()
    {
        var run = TideRunWithPatch("tide");
        var drift = run.Encounter.Rules.DriftMmPerTick;
        Assert.True(drift > 0, "the tide clause carries no drift");
        Assert.Equal(1000 + (drift * Ticks), PatchXAfter(run, Ticks));
    }

    [Fact(DisplayName = "Tide floor drift is pure: the input state's floor Window is never moved")]
    public void Drift_never_mutates_its_input()
    {
        var run = TideRunWithPatch("tide");
        var drifted = Clauses.DriftFloorWindows(run.State, run.Encounter.Rules);
        Assert.Equal(1000, run.State.FloorWindows["warden.bulwark"].Pos.X);
        Assert.Equal(1000 + run.Encounter.Rules.DriftMmPerTick, drifted.FloorWindows["warden.bulwark"].Pos.X);
        Assert.Same(run.State, Clauses.DriftFloorWindows(run.State, Clauses.DefaultEncounterRules(1)));
    }

    [Fact(DisplayName = "No Clause: a floor Window stays where it was planted")]
    public void No_clause_floor_window_stays_put()
    {
        Assert.Equal(1000, PatchXAfter(TideRunWithPatch(null), Ticks));
    }
}
