using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// CHOIR-PACK-HEAL: the Choir Cantor's Choral Mending heals its pack (design/BIBLE.md section 9.3, "Heals its pack.
/// Left uncovered, the pack outheals the timer"), and its pack is the enemies in the fight: under PRESPAWN-AGGRO an idle
/// group waiting in a later room is not part of the fight, so a friendly area effect from an enemy never reaches an
/// idle actor (the same rule ScriptTarget already applies to the Cantor's ally pick). The ability mirrors
/// content/data/abilities/enemies.json choir.healCast verbatim: circle radius 25000 (its 25000 mm reach), castTicks 80,
/// cooldown 200, interruptible, heal 3000. Driven through the real Rules step.
/// </summary>
public sealed class PackHealTests
{
    private const int Cantor = 1;
    private const int Acolyte = 2;
    private const int IdleAcolyte = 3;
    private const int Player = 4;

    private static readonly AbilityDef ChoralMending = Def("choir.healCast", new AbilityShape(ShapeKind.Circle, Radius: 25000),
        new EffectOp[] { new HealOp(3000) }, castTicks: 80, gcdTicks: 0, cooldownTicks: 200, charges: 1, range: 25000,
        interruptible: true, tags: new[] { "healer" });

    private static (SimState State, List<SimEvent> Events) CastMending(bool idleAcolyte)
    {
        var cantor = Make(Cantor, ActorKind.Enemy, "choir.lieutenant", ActorRole.None, x: 0, maxHealth: 6000);
        var acolyte = Make(Acolyte, ActorKind.Enemy, "choir.melee", ActorRole.None, x: 2000, maxHealth: 6000);
        var waiting = Make(IdleAcolyte, ActorKind.Enemy, "choir.melee", ActorRole.None, x: 20000, maxHealth: 6000);
        var player = Make(Player, ActorKind.Player, "warden", ActorRole.Tank, x: 3000, maxHealth: 400);
        cantor.Health = 2000;
        acolyte.Health = 1000;
        waiting.Health = 1000;
        waiting.Idle = idleAcolyte;
        var state = At(0, cantor, acolyte, waiting, player);
        var rules = Rules.Make(Config(AbilityMap(ChoralMending), boundsHalf: 100000));
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(Cantor, "choir.healCast")), events.Add);
        while (state.Tick <= 81)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        return (state, events);
    }

    [Fact(DisplayName = "Choral Mending heals every engaged member of its pack, the Cantor included, and never the players")]
    public void Mending_heals_the_whole_engaged_pack()
    {
        var (state, events) = CastMending(idleAcolyte: false);

        Assert.Contains(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == "choir.healCast");
        Assert.Equal(5000, state.Actors[Cantor].Health);
        Assert.Equal(4000, state.Actors[Acolyte].Health);
        Assert.Equal(4000, state.Actors[IdleAcolyte].Health);
        Assert.Equal(400, state.Actors[Player].Health);
        var healed = events.Where(e => e.Type == "heal").Select(e => Int(e, "targetId")).OrderBy(x => x).ToArray();
        Assert.Equal(new long[] { Cantor, Acolyte, IdleAcolyte }, healed);
    }

    [Fact(DisplayName = "Choral Mending never reaches an idle enemy: a group still waiting in its room is not the Cantor's pack")]
    public void Mending_skips_an_idle_enemy()
    {
        var (state, events) = CastMending(idleAcolyte: true);

        Assert.Equal(5000, state.Actors[Cantor].Health);
        Assert.Equal(4000, state.Actors[Acolyte].Health);
        Assert.Equal(1000, state.Actors[IdleAcolyte].Health);
        Assert.DoesNotContain(events, e => e.Type == "heal" && Int(e, "targetId") == IdleAcolyte);
    }
}
