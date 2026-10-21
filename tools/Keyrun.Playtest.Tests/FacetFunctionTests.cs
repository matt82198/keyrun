using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using AbilityDef = Keyrun.Sim.Combat.AbilityDef;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// design/BIBLE.md section 7 "Facets change function": every shipped Facet must change what its ability does in the
/// sim. Each case equips one real Facet from content/data/facets the way the game does (Progression ->
/// <see cref="HostRunSetup.StartFor"/>, i.e. <see cref="HostRunSetup.GearPatches"/> + <see cref="RunStart.Of"/>), builds
/// the run's rules from the real content (<see cref="RunWiring.RulesConfigOf"/>), casts the ability and asserts a sim
/// outcome (damage, a heal effect, a combo, the verse, a hit on an off-line target). The same cast without the Facet is
/// asserted too, so each case proves the Facet is what changed the outcome.
/// Findings f-d0142701 (Chanter verse Facets appended to Effects, which a verse ability never plays) and f-2fcf69c1
/// (line-width Facets carried but never applied).
/// </summary>
public sealed class FacetFunctionTests
{
    private const int Me = 1;
    private const int Ally = 2;
    private const int Foe = 3;
    private const int OffLineFoe = 4;
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    [Fact]
    public void HymnalMending_puts_the_Mending_HoT_on_an_ally_in_the_Hymn()
    {
        static bool AllyMended(string? facet)
        {
            var a = Arena("chanter", facet, Chanter(0), Actor(Ally, ActorKind.Player, "warden", 1000, 0));
            a.Run(Me, "chanter.hymn", null, 40);
            return a.State.Actors[Ally].Effects.Any(e => e.EffectId == "mendingHot");
        }

        Assert.False(AllyMended(null));
        Assert.True(AllyMended("chanter.armor.hymnalMending"));
    }

    [Fact]
    public void RefrainsEdge_makes_Refrain_a_Payoff_that_closes_an_open_floor_Window()
    {
        Assert.Empty(ChanterPaysOffBulwark(null, "chanter.refrain"));
        Assert.Equal("bulwark-field", Assert.Single(ChanterPaysOffBulwark("chanter.charm.refrainsEdge", "chanter.refrain")));
    }

    [Fact]
    public void EternalHymn_makes_Hymn_a_Payoff_that_closes_an_open_floor_Window()
    {
        Assert.Empty(ChanterPaysOffBulwark(null, "chanter.hymn"));
        Assert.Equal("bulwark-field", Assert.Single(ChanterPaysOffBulwark("chanter.keystone.eternalHymn", "chanter.hymn")));
    }

    [Fact]
    public void OvertureOfPower_lets_the_next_ability_follow_Refrain_without_waiting_out_the_GCD()
    {
        static long FoeHealthAfterRefrainThenDissonance(string? facet)
        {
            var a = Arena("chanter", facet, Chanter(1), Actor(Foe, ActorKind.Enemy, "x", 5000, 0, maxHealth: 10000));
            a.Run(Me, "chanter.refrain", null, 1);
            a.Run(Me, "chanter.dissonance", Foe, 2);
            return a.State.Actors[Foe].Health;
        }

        Assert.Equal(10000, FoeHealthAfterRefrainThenDissonance(null));
        Assert.Equal(10000 - 130, FoeHealthAfterRefrainThenDissonance("chanter.ring.overtureOfPower"));
    }

    [Fact]
    public void OvertureOfPower_keeps_Refrain_a_verse_swap()
    {
        var a = Arena("chanter", "chanter.ring.overtureOfPower", Chanter(1));
        a.Run(Me, "chanter.refrain", null, 1);
        Assert.Equal(Abilities.WardVerse, Actors.Verse(a.State.Actors[Me]));
        Assert.Contains(a.State.Actors[Me].Effects, e => e.EffectId == "wardShield");
    }

    [Fact]
    public void DissonantChoir_refunds_Verse_in_Ward_and_Dissonance_still_plays_its_Ward_verse()
    {
        static (long Verse, bool Silenced, bool Slowed) DissonanceFromWard(string? facet)
        {
            var a = Arena("chanter", facet, Chanter(0), Actor(Foe, ActorKind.Enemy, "x", 5000, 0, maxHealth: 10000));
            a.Run(Me, "chanter.dissonance", Foe, 2);
            var foe = a.State.Actors[Foe];
            return (a.State.Actors[Me].Resource, foe.Effects.Any(e => e.EffectId == "silenced"), foe.Effects.Any(e => e.EffectId == "slowed"));
        }

        Assert.Equal((0L, true, false), DissonanceFromWard(null));
        Assert.Equal((1L, true, false), DissonanceFromWard("chanter.keystone.dissonantChoir"));
    }

    [Fact]
    public void TwinStrike_makes_Reckoning_a_line_that_hits_an_enemy_off_the_target()
    {
        static long OffLineHealth(string? facet)
        {
            var warden = Actor(Me, ActorKind.Player, "warden", 0, 0, maxResource: 100);
            var a = Arena("warden", facet, warden, Actor(Foe, ActorKind.Enemy, "x", 2000, 0, maxHealth: 10000), Actor(OffLineFoe, ActorKind.Enemy, "x", 4500, 500, maxHealth: 10000));
            a.Run(Me, "warden.reckoning", Foe, 2);
            Assert.True(a.State.Actors[Foe].Health < 10000, "Reckoning must hit its target");
            return a.State.Actors[OffLineFoe].Health;
        }

        Assert.Equal(10000, OffLineHealth(null));
        Assert.True(OffLineHealth("warden.weapon.twinStrike") < 10000, "the 1200 mm wide line must hit the enemy 500 mm off the target line");
    }

    [Fact]
    public void TwinStrike_line_is_its_data_width_and_no_wider()
    {
        var warden = Actor(Me, ActorKind.Player, "warden", 0, 0, maxResource: 100);
        var a = Arena("warden", "warden.weapon.twinStrike", warden, Actor(Foe, ActorKind.Enemy, "x", 2000, 0, maxHealth: 10000), Actor(OffLineFoe, ActorKind.Enemy, "x", 4500, 700, maxHealth: 10000));
        a.Run(Me, "warden.reckoning", Foe, 2);
        Assert.Equal(10000, a.State.Actors[OffLineFoe].Health);
    }

    [Fact]
    public void InfernoBolt_makes_Ember_Bolt_a_piercing_line_that_hits_an_enemy_off_the_target()
    {
        static long OffLineHealth(string? facet)
        {
            var cinder = Actor(Me, ActorKind.Player, "cinder", 0, 0, maxResource: 100);
            cinder.Resource = 0;
            var a = Arena("cinder", facet, cinder, Actor(Foe, ActorKind.Enemy, "x", 8000, 0, maxHealth: 10000), Actor(OffLineFoe, ActorKind.Enemy, "x", 20000, 500, maxHealth: 10000));
            a.Run(Me, "cinder.emberBolt", Foe, 40);
            Assert.True(a.State.Actors[Foe].Health < 10000, "Ember Bolt must hit its target");
            return a.State.Actors[OffLineFoe].Health;
        }

        Assert.Equal(10000, OffLineHealth(null));
        Assert.True(OffLineHealth("cinder.keystone.infernoBolt") < 10000, "the 25 m line must hit the enemy behind the target");
    }

    // A Warden opens Bulwark's floor Window over the Chanter, then the Chanter casts the ability: the combo ids fired.
    private static List<string> ChanterPaysOffBulwark(string? facet, string abilityId)
    {
        var a = Arena("chanter", facet, Chanter(1), Actor(Ally, ActorKind.Player, "warden", 0, 0));
        a.Run(Ally, "warden.bulwark", null, 30);
        Assert.Contains(a.Events, e => e.Type == "window_open" || e.Type == "window_opened" || e.Type.StartsWith("window", StringComparison.Ordinal));
        a.Events.Clear();
        a.Run(Me, abilityId, null, 40);
        return a.Events.Where(e => e.Type == "combo").Select(e => ((JsString)e.Get("comboId")!).Value).ToList();
    }

    private static ActorState Chanter(long verse)
    {
        var c = Actor(Me, ActorKind.Player, "chanter", 500, 0, maxResource: 1);
        c.Resource = verse;
        c.Health = 500;
        return c;
    }

    private static ActorState Actor(int id, ActorKind kind, string classId, long x, long z, long maxHealth = 1000, long maxResource = 0) =>
        Actors.Create(id, kind, classId, kind == ActorKind.Enemy ? ActorRole.None : ActorRole.Damage, new Vec3(x, 0, z), maxHealth, maxResource);

    // The wearer (actor Me, class classId) with the one Facet equipped, the way SimDriver.StartRun builds the run.
    private static Arena Arena(string classId, string? facetId, params ActorState[] actors)
    {
        var setup = HostRunSetup.Build(Reg, "hollow-vault", 2, classId, withBots: false);
        var progression = Progression.Start(1);
        if (facetId is not null)
        {
            var def = Reg.Facets[facetId];
            var slot = GearFacets.SlotId(def.Slot);
            progression = progression.Take(new Item("reward-" + slot + "-" + facetId + "-t1-0", slot, 1, new Facet(def.Id, def.Text, def.AbilityId), Rewards.StartingCharges));
        }
        var start = setup.StartFor(null, progression, Reg, classId);
        Assert.Empty(start.Gear!.UnappliedFacetIds);
        var abilities = setup.Content.Abilities.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        if (start.Options?.Loadouts is { } loadouts && loadouts.TryGetValue(setup.HumanActorId, out var kit))
        {
            foreach (var kv in kit)
            {
                abilities[kv.Key] = kv.Value;
            }
        }
        Assert.Equal(facetId is null ? 0 : 1, start.Gear.Abilities.Count);
        var state = SimState.Create(1);
        state.Phase = "combat";
        foreach (var actor in actors)
        {
            state.Actors[actor.Id] = actor;
        }
        return new Arena(state, Rules.Make(RunWiring.RulesConfigOf(setup.Content with { Abilities = abilities })));
    }
}

/// <summary>A sim state plus the run's rules, stepped one tick at a time.</summary>
internal sealed class Arena
{
    public Arena(SimState state, StepRules rules)
    {
        State = state;
        Rules = rules;
    }

    public SimState State { get; }

    public StepRules Rules { get; }

    public List<SimEvent> Events { get; } = new();

    /// <summary>Issues the cast on this tick, then steps <paramref name="ticks"/> ticks in all.</summary>
    public void Run(int actorId, string abilityId, int? targetId, int ticks)
    {
        IReadOnlyList<Command> cmds = new Command[] { new CastCommand(State.Tick, actorId, abilityId, targetId) };
        for (var i = 0; i < ticks; i++)
        {
            Rules(State, cmds, Events.Add);
            State.Tick += 1;
            cmds = Array.Empty<Command>();
        }
    }
}
