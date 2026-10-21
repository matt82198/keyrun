using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Tools.Replay;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-ANTHEM-1 (finding f-7d5df7f9; BIBLE section 5 Anthem: "Party gains 15% haste for 6 s."). The oracle setups carry
/// the content combos.json Anthem as <c>applyEffect hasted</c>, and <see cref="Combos.ApplyComboEffects"/> used to put
/// it on the Window's carrier: in the bot oracle runs that is the enemy, so Seed 108 got hasted and its seed.slam
/// ended 3 ticks early. The rule now: a combo's applyEffect whose effect carries positive haste is party haste. It
/// goes to every living actor of the Payoff actor's faction and never to the carrier. Twin of
/// tests/sim/combat/anthem-party-haste.test.ts (sim/combat/combos.ts applyComboEffects).
/// </summary>
public sealed class AnthemPartyHasteTests
{
    private static RunSetup Setup() => RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), 7);

    private static ActorState Player(int id, string classId, ActorRole role) =>
        Make(id, ActorKind.Player, classId, role, x: id * 1000, maxHealth: 1000, maxResource: 100);

    private static ActorState Seed(int id) => Make(id, ActorKind.Enemy, "seed", x: 500, maxHealth: 1000);

    private static EffectInstance[] Hasted(ActorState a) => a.Effects.Where(e => e.EffectId == "hasted").ToArray();

    [Fact(DisplayName = "R-ANTHEM-1: the oracle setup's Anthem closed on an enemy hastes every living party member for 6 s and not the enemy")]
    public void Anthem_hastes_the_party_not_the_carrier()
    {
        var content = Setup().Content;
        var anthem = Assert.IsType<ComboDef>(Combos.LookupCombo(content.Combos!, "chanter", "warden"));
        Assert.Equal("anthem", anthem.Id);
        var warden = Player(1, "warden", ActorRole.Tank);
        var cinder = Player(2, "cinder", ActorRole.Damage);
        var chanter = Player(3, "chanter", ActorRole.Healer);
        var seed = Seed(108);
        var state = At(40, warden, cinder, chanter, seed);

        Combos.ApplyComboEffects(state, warden, seed, anthem.Effects, content.Effects, _ => { });

        Assert.Empty(Hasted(seed));
        foreach (var member in new[] { warden, cinder, chanter })
        {
            var inst = Assert.Single(Hasted(member));
            Assert.Equal(160, inst.EndsTick);
            Assert.Equal(warden.Id, inst.SourceId);
        }
    }

    [Fact(DisplayName = "R-ANTHEM-1: Anthem skips a dead party member and never reaches another enemy")]
    public void Anthem_skips_the_dead_and_every_enemy()
    {
        var content = Setup().Content;
        var anthem = Combos.LookupCombo(content.Combos!, "chanter", "warden")!;
        var warden = Player(1, "warden", ActorRole.Tank);
        var chanter = Player(3, "chanter", ActorRole.Healer);
        chanter.Alive = false;
        var seed = Seed(108);
        var other = Seed(109);
        var state = At(40, warden, chanter, seed, other);

        Combos.ApplyComboEffects(state, warden, seed, anthem.Effects, content.Effects, _ => { });

        Assert.Single(Hasted(warden));
        Assert.Empty(Hasted(chanter));
        Assert.Empty(Hasted(seed));
        Assert.Empty(Hasted(other));
    }

    [Fact(DisplayName = "R-ANTHEM-1 control: a combo debuff still lands on the carrier (Shatter's stun)")]
    public void Shatter_stun_still_lands_on_the_carrier()
    {
        var content = Setup().Content;
        var shatter = Combos.LookupCombo(content.Combos!, "warden", "cinder")!;
        var cinder = Player(2, "cinder", ActorRole.Damage);
        var seed = Seed(108);
        var state = At(40, cinder, seed);

        Combos.ApplyComboEffects(state, cinder, seed, shatter.Effects, content.Effects, _ => { });

        Assert.Contains(seed.Effects, e => e.EffectId == "stunned");
        Assert.DoesNotContain(cinder.Effects, e => e.EffectId == "stunned");
    }
}
