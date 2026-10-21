using System.Collections.Generic;
using System.Linq;
using ContentLoader = Keyrun.Content.ContentLoader;
using ContentRegistry = Keyrun.Content.ContentRegistry;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// design/BIBLE.md section 4, Chanter (Attuned) x Warden = Anthem: "Party gains 15% haste for 6 s." The frozen
/// content/data/combos.json row puts <c>applyEffect hasted</c> on the Window's carrier, which is the enemy the Chanter
/// opened Attuned on; with HEAL-NEVER-HOSTILE a beneficial effect on an enemy is wrong, and the haste would never reach
/// the party. The game's run tables (<see cref="HostRunSetup.Build"/>, the same build PlaytestRunner and the Godot host
/// use) must close Anthem as a party haste: every living party member gets <c>hasted</c> (1500 bp, 120 ticks, sourced by
/// the Warden) and no enemy gets it. The Window is opened with the real chanter.cadence Attuned window op and closed
/// through <see cref="Payoff.TryClosePayoff"/> on the real burst table and effect definitions.
/// </summary>
public sealed class AnthemHastesPartyTests
{
    private const int Now = 400;

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private static (SimState State, ActorState Warden, ActorState Chanter, ActorState Cinder, ActorState Carrier, ActorState Bystander, HostRunSetup Setup) Scene()
    {
        var setup = HostRunSetup.Build(Registry, "hollow-vault", 2, HostRunSetup.Roster[0].ClassId, withBots: true);
        var warden = Actors.Create(1, ActorKind.Player, "warden", ActorRole.Tank, new Vec3(0, 0, 0), 400, 100);
        var chanter = Actors.Create(2, ActorKind.Player, "chanter", ActorRole.Healer, new Vec3(-6000, 0, 0), 250, 100);
        var cinder = Actors.Create(3, ActorKind.Player, "cinder", ActorRole.Damage, new Vec3(6000, 0, 6000), 250, 100);
        var carrier = Actors.Create(100, ActorKind.Enemy, "choir.acolyte", ActorRole.None, new Vec3(1500, 0, 0), 500, 0);
        var bystander = Actors.Create(101, ActorKind.Enemy, "choir.acolyte", ActorRole.None, new Vec3(2500, 0, 0), 500, 0);
        var state = SimState.Create(1);
        state.Phase = "combat";
        state.Tick = Now;
        foreach (var a in new[] { warden, chanter, cinder, carrier, bystander })
        {
            state.Actors[a.Id] = a;
        }
        var attuned = setup.Content.Abilities["chanter.cadence"].Effects.OfType<WindowOp>().First(w => w.WindowId == "attuned");
        Windows.OpenWindow(state, chanter, attuned, new FloorWindowRegistry(), _ => { }, carrier);
        Assert.NotNull(WindowState.FindOpenWindow(carrier, Now));
        return (state, warden, chanter, cinder, carrier, bystander, setup);
    }

    private static PayoffOutcome CloseAnthem(SimState state, ActorState warden, ActorState carrier, HostRunSetup setup, List<SimEvent> events) =>
        Payoff.TryClosePayoff(state, carrier, warden, setup.Content.Combos!, setup.Content.Effects, events.Add, new ClauseHooks(null));

    [Fact(DisplayName = "Anthem: the Warden closing the Chanter's Attuned hastes every living party member 15% for 6 s and no enemy")]
    public void Anthem_hastes_every_living_party_member_and_no_enemy()
    {
        var (state, warden, chanter, cinder, carrier, bystander, setup) = Scene();
        var events = new List<SimEvent>();

        Assert.Equal(PayoffOutcome.Closed, CloseAnthem(state, warden, carrier, setup, events));

        var combo = Assert.Single(events, e => e.Type == "combo");
        Assert.Equal(JsValue.Str("anthem"), combo.Get("comboId"));
        Assert.Equal(1500, setup.Content.Effects["hasted"].Modifiers.HasteBp);
        foreach (var member in new[] { warden, chanter, cinder })
        {
            var inst = Assert.Single(member.Effects, e => e.EffectId == "hasted");
            Assert.Equal(Now + 120, inst.EndsTick);
            Assert.Equal(warden.Id, inst.SourceId);
        }
        Assert.DoesNotContain(carrier.Effects, e => e.EffectId == "hasted");
        Assert.DoesNotContain(bystander.Effects, e => e.EffectId == "hasted");
    }

    [Fact(DisplayName = "Anthem: a dead party member gets no haste")]
    public void Anthem_skips_a_dead_party_member()
    {
        var (state, warden, chanter, cinder, carrier, _, setup) = Scene();
        cinder.Alive = false;

        Assert.Equal(PayoffOutcome.Closed, CloseAnthem(state, warden, carrier, setup, new List<SimEvent>()));

        Assert.Contains(warden.Effects, e => e.EffectId == "hasted");
        Assert.Contains(chanter.Effects, e => e.EffectId == "hasted");
        Assert.DoesNotContain(cinder.Effects, e => e.EffectId == "hasted");
        Assert.DoesNotContain(carrier.Effects, e => e.EffectId == "hasted");
    }
}
