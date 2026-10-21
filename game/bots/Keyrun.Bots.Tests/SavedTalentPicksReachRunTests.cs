using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using C = Keyrun.Content;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// TALENT-SAVE (BIBLE s6 "Respec is free on the screen, always. The screen is a build lab"; finding f-83c5e369): a fork
/// picked on the screen is kept in the progression save, so the next launch opens with the same build, and the saved
/// pick is what the queued run plays. The chain is the screen's own: <see cref="C.TalentPicks"/> -> its Choices on
/// <see cref="Progression.Talents"/> -> <see cref="ProgressionSave"/> text -> load -> <see cref="C.TalentPicks.Restore"/>
/// -> <see cref="C.TalentPicks.Resolve"/> -> the host conversion (<see cref="ForkParityTests.ToSim"/>) ->
/// <see cref="ScreenState.Queue"/> -> <see cref="QueueRequest.ToRunOptions"/> -> <see cref="RunRoot.CreateRun"/>.
/// Before the lane the save had no talents: a relaunch started every fork unpicked and the run played the base kit.
/// </summary>
public sealed class SavedTalentPicksReachRunTests
{
    private static readonly Lazy<C.ContentRegistry> Registry = new(() => C.ContentLoader.Load());

    private static (int Fork, int Option) Find(C.ClassDef cls, string abilityId, string optionId)
    {
        var fork = cls.Talents.Select((f, i) => (f, i)).Single(x => x.f.AbilityId == abilityId).i;
        var option = cls.Talents[fork].Options.Select((o, i) => (o, i)).Single(x => x.o.Id == optionId).i;
        return (fork, option);
    }

    [Fact(DisplayName = "talent save > a fork picked on the screen survives save and load and the run plays it")]
    public void Saved_pick_reaches_the_run()
    {
        var reg = Registry.Value;
        var warden = reg.Classes["warden"];
        var (fork, option) = Find(warden, "warden.strike", "rend");
        var picked = C.TalentPicks.Start(warden).Pick(fork, option);

        // Launch 1: the pick lands in the progression and is written.
        var saved = ProgressionSave.Serialize(Progression.Start(7).WithTalents(picked.Choices));

        // Launch 2: a fresh process reads the save back and rebuilds the screen's build from it.
        var loaded = ProgressionSave.Load(saved, seed: 999);
        Assert.Null(loaded.Error);
        var restored = C.TalentPicks.Restore(warden, loaded.Progression.Talents);
        Assert.Equal(picked, restored);
        Assert.Equal("rend", restored.PickedOption(fork));

        // Queue: the restored build resolves, converts and rides the request into the run.
        var dir = RolesOracleTraceTests.OracleDir();
        var trace = OracleTrace.Load(Path.Combine(dir, "bots-hv-k2-s7.trace.jsonl"));
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed);
        var actor = setup.Party.First(p => p.ClassId == "warden").Id;
        var kit = restored.Resolve(reg.Abilities).ToDictionary(kv => kv.Key, kv => ForkParityTests.ToSim(kv.Value), StringComparer.Ordinal);
        var state = ScreenState.Open("hollow-vault", setup.Content.Clauses!.Keys.ToList(), 2);
        var (_, request) = state.Queue(new Dictionary<int, IReadOnlyDictionary<string, S.AbilityDef>> { [actor] = kit });
        Assert.NotNull(request);
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, request!.ToRunOptions());

        static int Sundered(S.AbilityDef d) => d.Effects.OfType<S.WindowOp>().Single().DurationTicks;
        Assert.Equal(100, Sundered(run.Content.Abilities["warden.strike"])); // Rend: "Sundered lasts 5s instead of 3s"
        Assert.NotEqual(100, Sundered(setup.Content.Abilities["warden.strike"]));
    }

    [Fact(DisplayName = "talent save > a free respec on the screen is saved too: the reloaded build is empty")]
    public void Saved_respec_reloads_empty()
    {
        var warden = Registry.Value.Classes["warden"];
        var (fork, option) = Find(warden, "warden.strike", "rend");
        var respecced = C.TalentPicks.Start(warden).Pick(fork, option).Respec();

        var loaded = ProgressionSave.Load(ProgressionSave.Serialize(Progression.Start(7).WithTalents(respecced.Choices)), seed: 1);

        Assert.Null(loaded.Error);
        Assert.Empty(loaded.Progression.Talents);
        Assert.Equal(0, C.TalentPicks.Restore(warden, loaded.Progression.Talents).Count);
    }
}
