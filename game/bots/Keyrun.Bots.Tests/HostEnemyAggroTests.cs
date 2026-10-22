using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// AGGRO-CHASE host wiring (finding f-f0c886eb; HostRunSetup.WithEnemyAggro): the run SimDriver builds at key 2 carries
/// run.json enemyAggro onto every enemy it places. The Choir and the Pair (rooms that author no ring) aggro on each
/// member's own radius; packs, the patrol and the boss keep the ring the layout authors and carry no radius of their own;
/// every trash enemy assists; the patrol never leashes and the boss has every rule off (R-BOSS-2 owns its reset).
/// </summary>
public sealed class HostEnemyAggroTests
{
    private static readonly HostRunSetup Setup = HostRunSetup.Build(ContentLoader.Load(), "hollow-vault", 2, "warden", withBots: true);

    [Fact(DisplayName = "Host enemy aggro: the Choir and Pair members aggro on their own 8000 mm radius, assist at 3000 and leash at 30000")]
    public void Choir_and_pair_members_carry_their_own_radius()
    {
        var members = Setup.Dungeon.Pulls.Where(p => p.Id is "choir" or "pair").SelectMany(p => p.Spawns).ToList();
        Assert.Equal(8, members.Count);
        Assert.All(members, s =>
        {
            Assert.Equal(8000, s.AggroMm);
            Assert.Equal(3000, s.AssistMm);
            Assert.Equal(30000, s.LeashMm);
        });
    }

    [Fact(DisplayName = "Host enemy aggro: pack and patrol members keep the layout's ring (no own radius) but assist; patrols never leash")]
    public void Packs_and_patrols_keep_the_authored_ring()
    {
        var packMembers = Setup.Content.Packs!.SelectMany(k => k.Spawns).ToList();
        Assert.NotEmpty(packMembers);
        Assert.All(packMembers, s =>
        {
            Assert.Null(s.AggroMm);
            Assert.Equal(3000, s.AssistMm);
            Assert.Equal(30000, s.LeashMm);
        });
        var patrolMembers = Setup.Content.Patrols!.SelectMany(k => k.Spawns).ToList();
        Assert.NotEmpty(patrolMembers);
        Assert.All(patrolMembers, s =>
        {
            Assert.Null(s.AggroMm);
            Assert.Equal(3000, s.AssistMm);
            Assert.Null(s.LeashMm);
        });
    }

    [Fact(DisplayName = "Host enemy aggro: the boss keeps its arena ring with every per-enemy rule off")]
    public void Boss_has_every_rule_off()
    {
        var boss = Assert.Single(Setup.Dungeon.Pulls.Single(p => p.Id == "boss").Spawns);
        Assert.Null(boss.AggroMm);
        Assert.Null(boss.AssistMm);
        Assert.Null(boss.LeashMm);
    }

    [Fact(DisplayName = "Host enemy aggro: the created run's actors carry the spawn values, and every idle enemy has a home")]
    public void Created_run_carries_the_values()
    {
        var run = RunRoot.CreateRun(1, Setup.PartyFor(Setup.Dungeon), Setup.Dungeon, Setup.Content, null);
        var choir = Setup.Dungeon.Pulls.Single(p => p.Id == "choir").Spawns.Select(s => run.State.Actors[s.ActorId]).ToList();
        Assert.All(choir, a => Assert.Equal(8000, a.AggroMm));
        Assert.All(run.State.Actors.Values.Where(a => a.Kind == Keyrun.Sim.Core.ActorKind.Enemy && a.Idle && !a.Dormant), a => Assert.Equal(a.Pos, a.Home));
    }
}
