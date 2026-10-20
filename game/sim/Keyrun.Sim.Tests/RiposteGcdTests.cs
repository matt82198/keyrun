using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Warden's Riposte talent (content/data/classes/warden.json "Strike costs no GCD when it follows Brace.";
/// design/BIBLE.md section "Talents": "Riposte (Strike after a Brace is free)"). A Strike carrying the
/// "riposte:free-after-brace" tag skips its GCD only while the caster holds an unexpired "braceSelf" (the effect
/// warden.brace applies), and only once per Brace. Every other Strike pays the normal GCD. Driven through the real
/// cast pipeline (Rules.Make).
/// </summary>
public sealed class RiposteGcdTests
{
    private const int StrikeGcd = 30;

    private static readonly EffectOp[] StrikeOps =
    {
        new DamageOp(80, "physical"),
        new ResourceOp(15),
    };

    /// <summary>warden.strike with the Riposte patch applied: same GCD as base, plus the riposte tag.</summary>
    private static readonly AbilityDef RiposteStrike = Def("warden.strike", new AbilityShape(ShapeKind.Target),
        StrikeOps, gcdTicks: StrikeGcd, range: 3000, tags: new[] { "setup:sundered", "riposte:free-after-brace" });

    /// <summary>Base warden.strike (no Riposte).</summary>
    private static readonly AbilityDef BaseStrike = Def("warden.strike", new AbilityShape(ShapeKind.Target),
        StrikeOps, gcdTicks: StrikeGcd, range: 3000, tags: new[] { "setup:sundered" });

    private static (ActorState Warden, SimState State) Duel(int tick)
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        warden.TargetId = 2;
        var enemy = Make(2, ActorKind.Enemy, "trash", x: 1000, z: 0, maxHealth: 100000);
        return (warden, At(tick, warden, enemy));
    }

    private static void Braced(ActorState warden, int endsTick) => warden.Effects.Add(new EffectInstance
    {
        EffectId = "braceSelf",
        SourceId = warden.Id,
        Stacks = 1,
        EndsTick = endsTick,
    });

    private static void StrikeOnce(SimState state, AbilityDef strike) =>
        Tick(state, Rules.Make(Config(AbilityMap(strike))), Cmds(Cast(1, "warden.strike", 2)), _ => { });

    [Fact]
    public void Riposte_Strike_without_a_Brace_pays_the_full_GCD()
    {
        var (warden, state) = Duel(100);
        StrikeOnce(state, RiposteStrike);
        Assert.Equal(100 + StrikeGcd, warden.GcdReadyTick);
    }

    [Fact]
    public void Riposte_Strike_while_Braced_costs_no_GCD()
    {
        var (warden, state) = Duel(100);
        Braced(warden, 160);
        StrikeOnce(state, RiposteStrike);
        Assert.True(warden.GcdReadyTick <= 101, $"GCD should be free, ready at {warden.GcdReadyTick}");
    }

    [Fact]
    public void Riposte_frees_only_the_first_Strike_after_a_Brace()
    {
        var (warden, state) = Duel(100);
        Braced(warden, 160);
        StrikeOnce(state, RiposteStrike); // tick 100: free
        StrikeOnce(state, RiposteStrike); // tick 101: pays the GCD
        Assert.Equal(101 + StrikeGcd, warden.GcdReadyTick);
    }

    [Fact]
    public void Riposte_Strike_after_the_Brace_expired_pays_the_full_GCD()
    {
        var (warden, state) = Duel(100);
        Braced(warden, 100); // ended this tick
        StrikeOnce(state, RiposteStrike);
        Assert.Equal(100 + StrikeGcd, warden.GcdReadyTick);
    }

    [Fact]
    public void Base_Strike_while_Braced_pays_the_full_GCD()
    {
        var (warden, state) = Duel(100);
        Braced(warden, 160);
        StrikeOnce(state, BaseStrike);
        Assert.Equal(100 + StrikeGcd, warden.GcdReadyTick);
    }

    [Fact]
    public void A_fresh_Brace_grants_a_fresh_free_Strike()
    {
        var (warden, state) = Duel(100);
        Braced(warden, 160);
        StrikeOnce(state, RiposteStrike); // free, spends this Brace's Riposte
        state.Tick = 500;
        warden.GcdReadyTick = 0;
        warden.Effects = warden.Effects.Where(e => e.EndsTick > 500).ToList(); // the old Brace and its marker expire
        Braced(warden, 560);
        StrikeOnce(state, RiposteStrike);
        Assert.True(warden.GcdReadyTick <= 501, $"second Brace's Strike should be free, ready at {warden.GcdReadyTick}");
    }
}
