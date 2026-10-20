using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// CINDER-OVERHEAT-HOST (finding f-180719b5): design/BIBLE.md section 5 (Cinder) "At 100 the next cast is a free
/// Overheat that dumps Heat to 0 and applies Chill (slower casts for 3 s)" must hold in the run the game actually plays,
/// not only in sim tests. Every case builds the REAL host setup (game/src/Host/HostRunSetup.cs Build, linked into this
/// project, over <see cref="ContentLoader.Load()"/>) and drives what the host hands the sim: the run content
/// (<see cref="RunRoot.CreateRun"/>) and the combat rules the run steps with (<see cref="RunWiring.RulesConfigOf"/>).
/// </summary>
public sealed class HostRunOverheatTests
{
    private const int CinderId = 1;
    private const int EnemyId = 2;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    private static HostRunSetup Setup() => HostRunSetup.Build(Registry.Value, "hollow-vault", 2, "warden");

    [Fact]
    public void The_host_run_carries_an_overheat_rule_for_every_heat_overheat_class()
    {
        var registry = Registry.Value;
        var overheat = Setup().Content.Overheat;

        Assert.NotNull(overheat);
        var heatClasses = registry.Classes.Values.Where(c => c.Loop.Kind == LoopKind.HeatOverheat).ToList();
        Assert.Contains(heatClasses, c => c.Id == "cinder");
        Assert.Equal(heatClasses.Select(c => c.Id).OrderBy(id => id, StringComparer.Ordinal), overheat!.Keys.OrderBy(id => id, StringComparer.Ordinal));
        foreach (var c in heatClasses)
        {
            Assert.Equal(new OverheatRule(c.Loop.Max, registry.Run.HeatOverheat.ChillEffectId, registry.Run.HeatOverheat.ChillTicks), overheat[c.Id]);
        }
    }

    [Fact]
    public void The_host_run_opens_the_cinder_seat_at_zero_heat_and_the_others_at_full()
    {
        var setup = Setup();
        var run = RunRoot.CreateRun(1, setup.Party, setup.Dungeon, setup.Content);

        var cinder = Assert.Single(setup.Party, p => p.ClassId == "cinder");
        Assert.Equal(0, run.State.Actors[cinder.Id].Resource);
        foreach (var member in setup.Party.Where(p => p.ClassId != "cinder"))
        {
            Assert.Equal(member.MaxResource, run.State.Actors[member.Id].Resource);
        }
    }

    [Fact]
    public void A_cinder_at_full_heat_overheats_under_the_host_run_rules()
    {
        var registry = Registry.Value;
        var rules = Rules.Make(RunWiring.RulesConfigOf(Setup().Content));
        var cinder = Actors.Create(CinderId, ActorKind.Player, "cinder", ActorRole.Damage, new Vec3(0, 0, 0), 1000, registry.Classes["cinder"].Loop.Max);
        cinder.Resource = registry.Classes["cinder"].Loop.Max;
        var enemy = Actors.Create(EnemyId, ActorKind.Enemy, "e", ActorRole.None, new Vec3(0, 0, 1000), 100000, 0);
        var state = RunWiring.MergeActors(SimState.Create(1), new[] { cinder, enemy });
        var events = new List<SimEvent>();

        // Detonate costs 30 Heat; as the Overheat it costs nothing and dumps Heat to 0.
        rules(state, new Command[] { new CastCommand(0, CinderId, "cinder.detonate", EnemyId) }, events.Add);

        var after = state.Actors[CinderId];
        Assert.Equal(0, after.Resource);
        Assert.Single(events, e => e.Type == "overheat");
        var chill = Assert.Single(after.Effects, e => e.EffectId == registry.Run.HeatOverheat.ChillEffectId);
        Assert.Equal(registry.Run.HeatOverheat.ChillTicks, chill.EndsTick);
    }
}
