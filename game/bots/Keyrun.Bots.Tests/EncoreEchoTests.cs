using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using C = Keyrun.Content;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// ENCORE-ECHO (ruling R-PILE-7e, finding f-f44791bc; BIBLE section 0 "real decisions on five buttons", section 6 "Every
/// option changes function"). Chorus "Encore" measured identical to "Round Call" in every seeded Hollow Vault run once
/// the healer banks its second charge the WoW way (R-PILE-7d), so the option gets ONE function change on its own patch:
/// Mend's Chorus heal echoes once, at half strength (60 of 120), 50 ticks after the cast. It is built from existing sim
/// ops only (an <c>applyEffect</c> of a periodic heal that lasts exactly one period: <see cref="S.Effects"/> runs a due
/// periodic before <see cref="S.Effects.ExpireEffects"/> on the same tick). Every case drives the talented ability
/// through <see cref="S.Rules.Make"/> with the run content the game hands the sim (<see cref="HostRunSetup.Build"/> over
/// <see cref="C.ContentLoader.Load()"/>) and with the oracle setup the fork-parity check runs
/// (<see cref="ForkParityTests"/>), so the data, the patch and the sim rule are proven together.
/// </summary>
public sealed class EncoreEchoTests
{
    private const string Chorus = "chanter.chorus";
    private const int ChanterId = 1;
    private const int TankId = 2;
    private const int CastTick = 10;
    private const int EchoDelay = 50;

    private static readonly Lazy<C.ContentRegistry> Registry = new(() => C.ContentLoader.Load());

    /// <summary>Chorus as the talent screen patches it with <paramref name="optionId"/> (null = the base ability).</summary>
    private static S.AbilityDef Talented(string? optionId)
    {
        var reg = Registry.Value;
        var baseDef = reg.Abilities[Chorus];
        if (optionId is null)
        {
            return ForkParityTests.ToSim(baseDef);
        }
        var option = reg.Classes["chanter"].Talents.First(t => t.AbilityId == Chorus).Options.First(o => o.Id == optionId);
        return ForkParityTests.ToSim(C.TalentPicks.ApplyPatch(baseDef, option.Patch));
    }

    /// <summary>The run content the sim gets: "host" = the game's own setup, "oracle" = the fork-parity fixture.</summary>
    private static RunContent ContentOf(string source) => source switch
    {
        "host" => HostRunSetup.Build(Registry.Value, "hollow-vault", 2, "warden").Content,
        "oracle" => RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), 7).Content,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    /// <summary>
    /// A Chanter (verse <paramref name="verse"/>: 1 Mend, 0 Ward) casts <paramref name="chorus"/> on itself at
    /// <see cref="CastTick"/> with a hurt tank 1000 mm away; returns every heal the tank takes until tick 260.
    /// </summary>
    private static List<(int Tick, long Amount)> TankHeals(string source, S.AbilityDef chorus, long verse) =>
        TankHeals(source, chorus, verse, CastTick);

    /// <summary>
    /// As <see cref="TankHeals(string, S.AbilityDef, long)"/>, casting Chorus at every tick in
    /// <paramref name="castTicks"/>; the Chanter's verse is set to <paramref name="verse"/> before each cast.
    /// </summary>
    private static List<(int Tick, long Amount)> TankHeals(string source, S.AbilityDef chorus, long verse, params int[] castTicks)
    {
        var content = ContentOf(source);
        var abilities = new Dictionary<string, S.AbilityDef>(content.Abilities, StringComparer.Ordinal) { [Chorus] = chorus };
        var rules = S.Rules.Make(RunWiring.RulesConfigOf(content) with { Abilities = abilities });
        var chanter = S.Actors.Create(ChanterId, ActorKind.Player, "chanter", ActorRole.Healer, new Vec3(0, 0, 0), 1000, 1);
        chanter.Resource = verse;
        var tank = S.Actors.Create(TankId, ActorKind.Player, "warden", ActorRole.Tank, new Vec3(1000, 0, 0), 1000, 100);
        tank.Health = 100;
        var state = RunWiring.MergeActors(SimState.Create(1), new[] { chanter, tank });
        state.Phase = "combat";
        var heals = new List<(int Tick, long Amount)>();
        void Emit(SimEvent e)
        {
            if (e.Type == "heal" && e.Get("targetId") is JsInt t && t.Value == TankId && e.Get("amount") is JsInt a)
            {
                heals.Add((e.Tick, a.Value));
            }
        }
        while (state.Tick < 260)
        {
            var casting = Array.IndexOf(castTicks, state.Tick) >= 0;
            if (casting)
            {
                state.Actors[ChanterId].Resource = verse;
            }
            var cmds = casting ? new Command[] { new CastCommand(state.Tick, ChanterId, Chorus, ChanterId) } : Array.Empty<Command>();
            rules(state, cmds, Emit);
            state.Tick += 1;
        }
        return heals;
    }

    [Theory]
    [InlineData("host")]
    [InlineData("oracle")]
    public void Encore_Chorus_in_Mend_heals_on_the_cast_and_echoes_once_at_half_strength_50_ticks_later(string source)
    {
        var heals = TankHeals(source, Talented("encore"), 1);

        Assert.Equal(new[] { (CastTick, 120L), (CastTick + EchoDelay, 60L) }, heals);
    }

    /// <summary>
    /// Finding f-0fb803dc: both Encore charges spent inside the 50-tick echo window. Each cast is its own delayed echo
    /// (as a WoW delayed heal echo is): two half-strength echoes land, each 50 ticks after its own cast. Before the fix
    /// the second application refreshed the first instance's end tick but kept its periodic timer, so the second echo
    /// was lost. The second cast is one GCD (30 ticks) after the first, inside the window.
    /// </summary>
    [Theory]
    [InlineData("host")]
    [InlineData("oracle")]
    public void Encore_both_charges_inside_the_echo_window_echo_twice_each_50_ticks_after_its_own_cast(string source)
    {
        const int second = CastTick + 30;

        var heals = TankHeals(source, Talented("encore"), 1, CastTick, second);

        Assert.Equal(
            new[] { (CastTick, 120L), (second, 120L), (CastTick + EchoDelay, 60L), (second + EchoDelay, 60L) },
            heals);
    }

    [Theory]
    [InlineData("host", null)]
    [InlineData("host", "roundCall")]
    [InlineData("oracle", null)]
    public void Base_Chorus_and_Round_Call_heal_once_and_never_echo(string source, string? optionId)
    {
        var heals = TankHeals(source, Talented(optionId), 1);

        Assert.Equal(new[] { (CastTick, 120L) }, heals);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("oracle")]
    public void Encore_Chorus_in_Ward_heals_nothing_now_or_later(string source)
    {
        var heals = TankHeals(source, Talented("encore"), 0);

        Assert.Empty(heals);
    }

    [Fact]
    public void Encore_keeps_its_two_charges_and_its_100_tick_cooldown()
    {
        var encore = Talented("encore");

        Assert.Equal(2, encore.Charges);
        Assert.Equal(100, encore.CooldownTicks);
    }
}
