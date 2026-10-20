using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane 3I, the pure half of the host wiring (game/src/Host/SimDriver.cs StartRun): a run is created from a
/// <see cref="QueueRequest"/> with <see cref="QueueRequest.ToRunOptions"/> and then armed with
/// <see cref="BossRules.Arm"/>, in that order. These tests pin the order's contract on the hollow-vault k2 oracle setup
/// (the real content's clauses and boss script): the queued Clause survives the arming, no request means no Clause,
/// and arming is inert on every tick before the boss pull goes live (so an armed host run plays pulls 1-3 exactly as
/// an unarmed one). The engine-level proof is HudSceneTest runs D and E.
/// </summary>
public sealed class HostQueuedRunTests
{
    private const long Seed = 7;

    private static RunSetup Setup() => RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);

    // SimDriver.StartRun: CreateRun with the request's options, then the 3F hand-off.
    private static Run CreateArmed(RunSetup setup, QueueRequest? request) =>
        Arm(RunRoot.CreateRun(Seed, setup.Party, setup.Dungeon, setup.Content, request?.ToRunOptions()));

    private static Run Arm(Run run) => run with { Encounter = BossRules.Arm(run.Encounter, BossTestKit.Config()) };

    [Theory(DisplayName = "Host queued run: the queued Clause is the armed run's one active Clause")]
    [InlineData("echo")]
    [InlineData("tide")]
    [InlineData("hush")]
    [InlineData("pact")]
    public void Queued_clause_survives_arming(string clauseId)
    {
        var setup = Setup();
        var run = CreateArmed(setup, new QueueRequest("hollow-vault", 3, clauseId));

        Assert.Equal(clauseId, run.Encounter.Rules.ActiveClauseId);
        Assert.NotNull(run.Encounter.Boss);
        Assert.Equal("twinsBreak", run.Encounter.Boss!.Config.TwinsBar.Id);
        Assert.Equal(2, run.Encounter.Boss.Config.Twins.Count);
    }

    [Fact(DisplayName = "Host queued run: no request means no Clause (the CLI run and Enter restart)")]
    public void No_request_no_clause()
    {
        var run = CreateArmed(Setup(), null);

        Assert.Null(run.Encounter.Rules.ActiveClauseId);
        Assert.NotNull(run.Encounter.Boss);
    }

    [Fact(DisplayName = "Host queued run: an unknown queued Clause fails closed at creation")]
    public void Unknown_clause_fails_closed()
    {
        var setup = Setup();
        Assert.Throws<UnknownClauseException>(() => CreateArmed(setup, new QueueRequest("hollow-vault", 3, "nope")));
    }

    [Fact(DisplayName = "Host queued run: arming is inert before the boss pull (pulls 1-3 step identically)")]
    public void Arming_is_inert_before_the_boss_pull()
    {
        var setup = Setup();
        var plain = RunRoot.CreateRun(Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var armed = Arm(RunRoot.CreateRun(Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options));
        var bossPull = setup.Dungeon.Pulls.Count - 1;
        for (var t = 0; t < 400; t++)
        {
            Assert.True(plain.Encounter.PullIndex < bossPull, "the k2 run reached the boss pull inside the window");
            var a = RunRoot.StepRun(plain, System.Array.Empty<Command>());
            var b = RunRoot.StepRun(armed, System.Array.Empty<Command>());
            plain = a.Run;
            armed = b.Run;
            Assert.Equal(
                Projection.Line(plain.State, plain.Encounter.PullIndex, plain.Encounter.PenaltyTicks),
                Projection.Line(armed.State, armed.Encounter.PullIndex, armed.Encounter.PenaltyTicks));
            Assert.Equal(a.Events.Select(e => e.ToCanonicalJson()), b.Events.Select(e => e.ToCanonicalJson()));
        }
        Assert.NotNull(armed.Encounter.Boss);
        Assert.False(armed.Encounter.Boss!.TwinsSpawned);
    }
}
