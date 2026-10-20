using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using C = Keyrun.Content;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// WILDFIRE-NEVER-ENDS (BIBLE section 0 tests 1 and 3: one talent choice must not brick the run). With the Cinder
/// fork option flare:wildfire, the three bot oracle runs (bots-hv-k2-s7, k2-s11, k10-s7; every party actor a real-role
/// bot, as <see cref="RolesOracleTraceTests.Drive"/>) must end within 24000 ticks. Before the fix the three Cinders
/// cast Flare forever from about 4 m: its 1.5 m circle was centred on the caster (Targeting.ResolveTargets), so it
/// landed on nothing, opened no Kindled, Heat sat at the cap, and the Choir lieutenant (6180 hp) took no damage after
/// tick ~1923 of bots-hv-k2-s7.
/// </summary>
public sealed class WildfireRunEndsTests
{
    private const int MaxTicks = 24000;

    private readonly ITestOutputHelper _output;

    public WildfireRunEndsTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<C.ContentRegistry> Registry = new(() => C.ContentLoader.Load());

    /// <summary>
    /// The shipped Wildfire option, from content (classes/cinder.json through <see cref="C.TalentPicks.ApplyPatch"/>),
    /// as the sim runs it: the run's own Flare with the patched shape and tags. The patch must change nothing else, or
    /// this shortcut would not be the shipped ability.
    /// </summary>
    private static S.AbilityDef WildfireFlare(S.AbilityDef simFlare)
    {
        var registry = Registry.Value;
        var baseFlare = registry.Abilities["cinder.flare"];
        var option = registry.Classes["cinder"].Talents.Single(f => f.AbilityId == "cinder.flare").Options.Single(o => o.Id == "wildfire");
        var patched = C.TalentPicks.ApplyPatch(baseFlare, option.Patch);
        Assert.Equal(C.ShapeKind.Circle, patched.Shape.Kind);
        Assert.Equal(baseFlare.CastTicks, patched.CastTicks);
        Assert.Equal(baseFlare.CooldownTicks, patched.CooldownTicks);
        Assert.Equal(baseFlare.Range, patched.Range);
        Assert.Equal(baseFlare.Effects.Count, patched.Effects.Count);
        return simFlare with
        {
            Shape = new S.AbilityShape(S.ShapeKind.Circle, patched.Shape.Radius),
            Tags = patched.Tags.ToList(),
        };
    }

    /// <summary>Runs <paramref name="name"/> with every Cinder on Wildfire; returns (result, end tick, Flare damage events).</summary>
    private static (string Result, int Tick, int Combos) Run(string name)
    {
        var dir = RolesOracleTraceTests.OracleDir();
        var trace = OracleTrace.Load(Path.Combine(dir, name + ".trace.jsonl"));
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed);
        var wildfire = WildfireFlare(setup.Content.Abilities["cinder.flare"]);
        var loadouts = new Dictionary<int, IReadOnlyDictionary<string, S.AbilityDef>>();
        foreach (var m in setup.Party.Where(p => p.ClassId == "cinder"))
        {
            loadouts[m.Id] = new Dictionary<string, S.AbilityDef>(StringComparer.Ordinal) { [wildfire.Id] = wildfire };
        }
        Assert.True(loadouts.Count > 0, $"{name}: no Cinder in the party");
        var options = (setup.Options ?? new RunOptions()) with { Loadouts = loadouts };
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, options);
        var kits = KitRegistry.From(Registry.Value);
        var bots = new SortedDictionary<int, IBot>();
        var initial = new SortedDictionary<int, BotMemory>();
        foreach (var p in setup.Party)
        {
            bots[p.Id] = Bot.MakeBot(RoleSet.Default);
            initial[p.Id] = BotMemory.Default(100);
        }
        IReadOnlyDictionary<int, BotMemory> memories = initial;
        var prng = Skill.SeedPrng(trace.Seed);
        var combos = 0;
        while (run.RunEnd.Result is null && run.State.Tick < MaxTicks)
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits);
            var batch = Bot.RunBots(view, bots, memories, prng);
            memories = batch.Memories;
            prng = batch.Prng;
            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            combos += stepped.Events.Count(e => e.Type == "combo");
        }
        return (run.RunEnd.Result?.ToJs() ?? "none", run.RunEnd.ResultTick ?? run.State.Tick, combos);
    }

    [Theory(DisplayName = "wildfire > the bot run with every Cinder on Wildfire ends within 24000 ticks")]
    [InlineData("bots-hv-k2-s7")]
    [InlineData("bots-hv-k2-s11")]
    [InlineData("bots-hv-k10-s7")]
    public void Wildfire_run_ends(string name)
    {
        var (result, tick, combos) = Run(name);
        _output.WriteLine($"{name}: {result}@{tick} combos {combos}");
        Assert.True(result != "none", $"{name}: the Wildfire run never ended (still running at tick {tick}, {combos} combos)");
        Assert.True(tick <= MaxTicks, $"{name}: ended at {tick}");
    }
}
