using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Keyrun.Bots;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

public sealed class ExploreTmpTests
{
    private readonly ITestOutputHelper _out;

    public ExploreTmpTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Dump()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var created = RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), 1);
        var sb = new StringBuilder();
        while (run.State.Tick < 340)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands);
            bots.Sample(tick, run, commands);
            if (tick >= 200)
            {
                var w = run.State.Actors[1];
                sb.Append(tick).Append(" W gcdReady=").Append(w.GcdReadyTick).Append(" res=").Append(w.Resource).Append(" cast=").Append(w.CastId).Append(" cmds=")
                    .Append(string.Join("|", commands.Where(c => c.ActorId == 1).Select(c => c.GetType().Name.Replace("Command", string.Empty) + (c is CastCommand cc ? ":" + cc.AbilityId + ">" + cc.TargetId : string.Empty)))).Append('\n');
            }
            run = RunRoot.StepRun(run, commands).Run;
        }
        _out.WriteLine(sb.ToString());
    }
}
