using System.Collections.Generic;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The healer plays the Chanter's verse honestly (design/BIBLE.md section 5: "Refrain swaps verse; burst heal (to
/// Mend) or burst shield (to Ward)"). Refrain is classified as the self heal through its "mend" variant, but the sim
/// (Keyrun.Sim Abilities.ResolveVerse) plays the swap's DESTINATION variant: cast from Mend it shields and flips the
/// Chanter into Ward with no heal. So a Mend healer that only means to heal itself must not reach for Refrain; from
/// Ward, Refrain is the real burst heal and it lands the Chanter in Mend. Real content kits.
/// </summary>
public sealed class HealerVerseTests
{
    private const long Mend = 1;
    private const long Ward = 0;

    private static ActorState Healer(long resource, IEnumerable<(string, int)>? cooldowns = null) =>
        Actor(id: 3, classId: "chanter", role: ActorRole.Healer, health: 50, maxHealth: 250, resource: resource, maxResource: 1,
            cooldowns: cooldowns);

    private static BotView View(params ActorState[] actors) => ViewOf(10, RealKits, null, actors);

    [Fact(DisplayName = "Healer verse > a hurt Mend healer heals itself with Chorus, never the Refrain that would flip it into Ward")]
    public void MendHealerSelfHealStaysInMend()
    {
        var healer = Healer(Mend);
        Assert.Equal(new Command[] { new CastCommand(10, 3, "chanter.chorus") }, HealerRole.HealerDecide(View(healer), healer));
    }

    [Fact(DisplayName = "Healer verse > with Chorus down, a hurt Mend healer still does not fall back to Refrain")]
    public void MendHealerNoRefrainFallback()
    {
        var healer = Healer(Mend, cooldowns: new[] { ("chanter.chorus", 999) });
        var cmds = HealerRole.HealerDecide(View(healer), healer);
        Assert.DoesNotContain("chanter.refrain", CastIds(cmds));
        Assert.Equal(new Command[] { new CastCommand(10, 3, "chanter.hymn") }, cmds);
    }

    [Fact(DisplayName = "Healer verse > heal pick in Mend: the self-target heal is Chorus, not the verse swap")]
    public void HealPickInMendSkipsTheSwap()
    {
        var healer = Healer(Mend);
        var view = View(healer);
        var kit = RoleShared.KitOf(view, "chanter");
        Assert.Equal(new Command[] { new CastCommand(10, 3, "chanter.chorus") }, HealPick.HealCommands(view, healer, kit, healer));
    }

    [Fact(DisplayName = "Healer verse > heal pick in Ward: Refrain is the burst heal (it swaps to Mend) and is still the self heal")]
    public void HealPickInWardUsesRefrain()
    {
        var healer = Healer(Ward);
        var view = View(healer);
        var kit = RoleShared.KitOf(view, "chanter");
        Assert.Equal(new Command[] { new CastCommand(10, 3, "chanter.refrain") }, HealPick.HealCommands(view, healer, kit, healer));
    }
}
