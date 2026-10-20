using System.Collections.Generic;
using System.Linq;
using System.Text;
using Keyrun.Bots;
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
        var source = new BotCommandSource(setup.Party.Select(p => p.Id), KitRegistry.From(registry), seed);
        var log = new StringBuilder();
        while (run.State.Tick < maxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            source.Sample(tick, run, commands);
            if (Follow.Leader(run.State) is { } leader)
            {
                if (!Follow.AnyLivingEnemy(run.State))
                {
                    commands.RemoveAll(c => c.ActorId == leader.Id);
                    if (Follow.LeadRoute(tick, leader, run) is { } walk)
                    {
                        commands.Add(walk);
                    }
                }
                else
                {
                    commands.RemoveAll(c => c.ActorId == leader.Id && c is MoveCommand or DashCommand);
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
        if (played.Log != reference)
        {
            var a = played.Log.Split('\n');
            var b = reference.Split('\n');
            var first = Enumerable.Range(0, System.Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i]);
            Assert.Fail($"logs differ at line {first}: playtest \"{(first < a.Length ? a[first] : "<end>")}\" vs host \"{(first < b.Length ? b[first] : "<end>")}\"");
        }
    }
}
