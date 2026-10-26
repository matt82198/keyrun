using System.Collections.Generic;
using System.Linq;
using System.Text;
using Keyrun.Bots;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// No policy = today's bots, unchanged: <c>playtest run</c> without a policy file must produce the byte-identical
/// command and event log of the host's own bot driver. The reference here is the host's
/// game/src/Host/BotCommandSource.cs (compiled in by link, unedited) sampling every seat at the host's dial 80 and the
/// run seed, on a run built by HostRunSetup.Build and armed with BossRules.Arm as SimDriver.StartRun does, with the
/// lowest living seat walking the route between fights (Follow.LeadRoute) and keeping only its casts in a fight.
/// </summary>
public sealed class NoPolicyParityTests
{
    private static string ReferenceLog(int key, long seed, int maxTicks)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", key, "warden", withBots: true);
        var created = RunRoot.CreateRun(seed, setup.Party, setup.Dungeon, setup.Content, null);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var kits = KitRegistry.From(registry);
        var source = new BotCommandSource(setup.Party.Select(p => p.Id), kits, seed);
        var log = new StringBuilder();
        // BOT-RESPAWN-RETURN (R-PATH-5 (m)): the same seat-down and returning tracking as PlaytestRunner, run before each sample.
        var seatsDown = new HashSet<int>();
        var returning = new HashSet<int>();
        // f-3dcc702c: the host's autorun leader counts the forces bar to 100% (PlayerInput hands ScriptedWarden this plan).
        var forces = ForcesPlan.Of(registry);
        // The host (SimDriver) sets the forces table on its bot source, so every view carries the run's PullPlan.
        source.Forces = forces;
        while (run.State.Tick < maxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            foreach (var seat in run.State.Actors.Values)
            {
                if (seat.Kind != ActorKind.Player)
                {
                    continue;
                }
                if (!seat.Alive)
                {
                    seatsDown.Add(seat.Id);
                    continue;
                }
                if (seatsDown.Remove(seat.Id))
                {
                    returning.Add(seat.Id);
                }
                if (returning.Contains(seat.Id) && Follow.ReturnSeat(seat, run.State) is null)
                {
                    returning.Remove(seat.Id);
                }
            }
            var commands = new List<Command>();
            source.Sample(tick, run, commands);
            if (Follow.Leader(run.State) is { } leader)
            {
                if (!Follow.AnyLivingEnemy(run.State))
                {
                    commands.RemoveAll(c => c.ActorId == leader.Id);
                    if (Follow.LeadRoute(tick, leader, run, null, forces) is { } walk)
                    {
                        commands.Add(walk);
                    }
                }
                else
                {
                    // R-SEED-1a (f-244d102b): BotCommandSource.cs:162-166 passes every batch command, the leader's TankEngage
                    // approach move included, so the reference keeps exactly that move (TankEngage.ApproachMove) and strips
                    // the rest of the leader's moves in a fight, as before.
                    var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits.ForRun(run.Content.Abilities)) with { Layout = run.Content.Layout, PullPlan = PullPlan.Of(run, forces) };
                    var approach = leader.Role == ActorRole.Tank ? TankEngage.ApproachMove(view, leader, view.Kits.ResolveKit(leader.ClassId, view.ClassKit)) : null;
                    // KICK-LEDGER-COOLDOWN-GAP (f-13b5c33a): BotCommandSource passes the leader's kick walk too (the step toward a caster
                    // its Shield Bash is named to kick but cannot reach yet, RoleShared.LedgerKickApproachCommand), so the reference keeps
                    // exactly that move beside the TankEngage approach; the byte-identical assertion below is unchanged.
                    var kickWalk = RoleShared.LedgerKickApproachCommand(view, leader, view.Kits.ResolveKit(leader.ClassId, view.ClassKit));
                    // R-AOE-1 (c), f-c8ed3a62: the lead-seat bot also passes ChainPull.Move (Bot.cs); while it holds it replaces the
                    // leader's other moves, exactly as PlaytestRunner does, so the byte-identical assertion below is unchanged.
                    var chain = view.PullPlan is { } chainPlan && view.Layout is { } chainLayout ? ChainPull.Move(tick, run.State, chainLayout, chainPlan, leader) : null;
                    // BOT-RESPAWN-RETURN (R-PATH-5 (l)): the respawned leader's walk back to the nearest living seat, as PlaytestRunner.
                    var back = returning.Contains(leader.Id) && run.Content.Layout is { } backLayout && Follow.ReturnSeat(leader, run.State) is { } backSeat
                        ? Follow.WalkTo(tick, leader, backLayout, backSeat.Pos)
                        : null;
                    commands.RemoveAll(c => c.ActorId == leader.Id && c is MoveCommand or DashCommand && (chain is not null || (!Equals(c, approach) && !Equals(c, kickWalk) && !Equals(c, back))));
                    if (chain is not null)
                    {
                        commands.Add(chain);
                    }
                    else if (back is not null && !commands.Contains(back))
                    {
                        commands.Add(back);
                    }
                }
            }
            foreach (var c in commands)
            {
                log.Append("C ").Append(c).Append('\n');
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                log.Append("E ").Append(e.ToCanonicalJson()).Append('\n');
            }
        }
        return log.ToString();
    }

    private static bool IsCaptureLine(string line) =>
        line.StartsWith("TICK ", System.StringComparison.Ordinal)
        || line.StartsWith("ACTOR ", System.StringComparison.Ordinal)
        || line.StartsWith("GROUP ", System.StringComparison.Ordinal);

    [Theory(DisplayName = "No policy: the playtest log is byte-identical to the host bot driver's run")]
    [InlineData(2, 1)]
    [InlineData(5, 7)]
    public void No_policy_is_todays_bot_run(int key, long seed)
    {
        const int maxTicks = 28800;
        var played = PlaytestRunner.Play(new PlaytestOptions(key, seed, null) { MaxTicks = maxTicks });
        var reference = ReferenceLog(key, seed, maxTicks);
        Assert.True(reference.Length > 10000, "the reference run is empty");
        Assert.Contains("\"type\":\"run_end\"", reference, System.StringComparison.Ordinal);
        // R-AGGRO-1 / G7: the TICK, ACTOR and GROUP capture lines are log-writer additions the reference (a command and event
        // replica of the host driver) does not write, so parity is over the command and event lines only.
        var playedCommandsAndEvents = string.Join('\n', played.Log.Split('\n').Where(l => !IsCaptureLine(l)));
        if (playedCommandsAndEvents != reference)
        {
            var a = playedCommandsAndEvents.Split('\n');
            var b = reference.Split('\n');
            var first = Enumerable.Range(0, System.Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i]);
            Assert.Fail($"logs differ at line {first}: playtest \"{(first < a.Length ? a[first] : "<end>")}\" vs host \"{(first < b.Length ? b[first] : "<end>")}\"");
        }
    }
}
