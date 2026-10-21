using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The tank survivability model (design/BIBLE.md section 3.1): a Warden built exactly as the host builds it
/// (HostRunSetup.Build, real content) standing in the Choir pull, with no healer and no button pressed but Brace on
/// cooldown, must live for a stated window. The window is the healer's reaction budget: the tank alone buys it, the
/// healer's throughput keeps it.
/// </summary>
public sealed class TankSurvivabilityTests
{
    /// <summary>
    /// The Warden alone in the Choir pull at <paramref name="key"/>: placed on the Acolytes' spawn, Brace pressed every
    /// tick it is ready, nothing else. Returns the tick the Warden died (null if alive at <paramref name="maxTicks"/>)
    /// and the most health one hit took.
    /// </summary>
    private static (int? DeathTick, long BiggestHit, long DamageTaken) Hold(int key, int maxTicks)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", key, "warden", withBots: false);
        var choir = setup.Dungeon.Pulls[0];
        var acolyte = choir.Spawns.First(s => s.ClassId == "choir.melee");
        var party = setup.Party.Select(p => p with { Pos = acolyte.Pos }).ToList();
        var run = RunRoot.CreateRun(1, party, setup.Dungeon, setup.Content with { Layout = null }, null);
        var warden = party.Single(p => p.ClassId == "warden").Id;
        long biggest = 0;
        long taken = 0;
        while (run.State.Tick < maxTicks)
        {
            var tick = run.State.Tick;
            var self = run.State.Actors[warden];
            var commands = new List<Command>();
            if (Readiness.AbilityReady(self, "warden.brace", tick))
            {
                commands.Add(new CastCommand(tick, warden, "warden.brace"));
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events.Where(e => e.Type == "damage" && e.Get("targetId") is JsInt t && t.Value == warden))
            {
                var hit = e.Get("healthDamage") is JsInt hd ? hd.Value : 0;
                biggest = System.Math.Max(biggest, hit);
                taken += hit;
            }
            if (!run.State.Actors[warden].Alive)
            {
                return (tick, biggest, taken);
            }
        }
        return (null, biggest, taken);
    }

    [Fact(DisplayName = "Key 1: an unhealed Warden with Brace on cooldown holds the Choir pull for 10 s")]
    public void Key1_warden_holds_the_choir_for_ten_seconds()
    {
        const int window = 200; // 10 s at 20 Hz: the healer's reaction budget on trash at the lowest key.
        var (death, biggest, taken) = Hold(1, window);
        Assert.True(taken > 0, "the Choir never hit the Warden: the test is vacuous");
        Assert.True(death is null, $"the Warden died at tick {death} (window {window}), biggest hit {biggest}, taken {taken}");
    }

    [Fact(DisplayName = "Armor multiplies with Brace, Vigil and Bulwark: the full stack still lets a Choir swing through")]
    public void Armor_multiplies_and_never_stacks_to_immunity()
    {
        var registry = ContentLoader.Load();
        var armor = registry.Run.ClassArmorBp["warden"];
        var stackBp = new[] { "braceSelf", "bracedSelf", "braced" }.Sum(id => (long)registry.Effects[id].Modifiers.DamageTakenBp!.Value);
        var afterStack = Keyrun.Sim.Combat.Damage.ApplyModifiers(70, 0, stackBp);
        var taken = Keyrun.Sim.Combat.Damage.ApplyArmor(afterStack, armor);
        Assert.True(armor > 0, "the Warden has no passive armor");
        Assert.Equal(afterStack * (10000 - armor) / 10000, taken);
        Assert.True(taken > 0, $"Brace + Vigil + Bulwark + armor {armor} made the Warden immune");
    }

    [Fact(DisplayName = "Key 10 (Fortified, tempo): no Choir hit takes a fifth of the Warden's health")]
    public void Key10_no_choir_hit_is_a_fifth_of_the_warden()
    {
        var (_, biggest, taken) = Hold(10, 200);
        Assert.True(taken > 0, "the Choir never hit the Warden: the test is vacuous");
        Assert.True(biggest * 5 < HostRunSetup.TankMaxHealth, $"one hit took {biggest} of {HostRunSetup.TankMaxHealth}");
    }
}
