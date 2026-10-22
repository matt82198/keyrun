using System.Collections.Generic;
using System.Linq;
using C = Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using SC = Keyrun.Sim.Combat;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// D66: each of the Sunken Bellfoundry's five new trash kinds does the one thing new to the game it is built around,
/// in the real sim on the shipped content at key 2 (the host's own ability, effect and script conversion:
/// <see cref="HostRunSetup.Build"/> and <see cref="HostRunSetup.EnemyScript"/>). The party stands still and takes no
/// action unless a test says so, so every effect seen is the enemy's.
/// <list type="bullet">
/// <item>Slag Ram: Molten Charge is a line through its target and on; the player behind the tank is hit, the one beside
/// is not.</item>
/// <item>Drowned Bell: Resonant Toll stacks Resonance on the enemies in its ring, and on nobody outside it.</item>
/// <item>Furnace Thrall: a Thrall that finished Stoke Core bursts in Slag Burn on death; one whose Stoke Core was kicked
/// dies quietly.</item>
/// <item>Scald Hound: below 30% health it boils over (Boiling Blood, an enrage); above, it never does.</item>
/// <item>Brine Mender: Bronze Ward halves the damage its pack takes; it is a kick.</item>
/// </list>
/// </summary>
public sealed class SunkenBellfoundryMobTests
{
    private const int Key = 2;
    private const int Tank = 1;
    private const int Healer = 2;
    private const int Damage = 3;
    private static readonly C.ContentRegistry Reg = C.ContentLoader.Load();
    private static readonly HostRunSetup Setup = HostRunSetup.Build(Reg, "hollow-vault", Key, "warden");

    private sealed record Mob(int Id, string EnemyId, long X, long Z, long? HealthBp = null);

    private static Run Arena(IReadOnlyList<Mob> mobs, (long X, long Z) tank, (long X, long Z) healer, (long X, long Z) damage)
    {
        var scripts = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        var spawns = new List<PullSpawn>();
        foreach (var m in mobs)
        {
            var max = Tempo.ScaleHealth(Reg.Enemies[m.EnemyId].MaxHealth, Key);
            spawns.Add(new PullSpawn(m.Id, m.EnemyId, new Vec3(m.X, 0, m.Z), 0, max) { SpeedMmPerTick = Reg.Enemies[m.EnemyId].SpeedMmPerTick });
            scripts[m.Id] = HostRunSetup.EnemyScript(Reg, m.EnemyId, Key);
        }
        var content = Setup.Content with
        {
            Abilities = Tempo.ScaleEnemyCooldowns(Setup.Content.Abilities, scripts.Values, Key),
            EnemyScripts = scripts,
            Bounds = new SC.Bounds(-40000, 40000, -40000, 40000),
            Layout = null,
            Packs = null,
            Patrols = null,
            ParTicks = null,
            Clauses = null,
        };
        var party = new[]
        {
            new PartyMemberSpec(Tank, "warden", ActorRole.Tank, new Vec3(tank.X, 0, tank.Z), 100_000, 100),
            new PartyMemberSpec(Healer, "chanter", ActorRole.Healer, new Vec3(healer.X, 0, healer.Z), 100_000, 1),
            new PartyMemberSpec(Damage, "cinder", ActorRole.Damage, new Vec3(damage.X, 0, damage.Z), 100_000, 100),
        };
        var run = RunRoot.CreateRun(7, party, new DungeonDef("arena", new[] { new PullDef("p", spawns) }), content);
        foreach (var m in mobs.Where(m => m.HealthBp is not null))
        {
            var a = run.State.Actors[m.Id];
            a.Health = a.MaxHealth * m.HealthBp!.Value / 10000;
        }
        return run;
    }

    private static (Run Run, List<SimEvent> Events) StepTo(Run run, int untilTick, System.Func<Run, IReadOnlyList<Command>>? commands = null)
    {
        var events = new List<SimEvent>();
        while (run.State.Tick < untilTick)
        {
            var stepped = RunRoot.StepRun(run, commands?.Invoke(run) ?? System.Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
        }
        return (run, events);
    }

    private static bool Has(Run run, int actorId, string effectId) =>
        run.State.Actors[actorId].Effects.Any(e => e.EffectId == effectId && e.EndsTick > run.State.Tick);

    private static long Stacks(Run run, int actorId, string effectId) =>
        run.State.Actors[actorId].Effects.Where(e => e.EffectId == effectId && e.EndsTick > run.State.Tick).Sum(e => e.Stacks);

    private static long? Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : null;

    [Fact(DisplayName = "D66 Slag Ram: Molten Charge runs through the tank and hits the player behind it, never the one beside")]
    public void Slag_ram_charge_is_a_line_through_the_tank()
    {
        var run = Arena(new[] { new Mob(100, "bellfoundry.slagRam", 0, 0) }, tank: (0, 2000), healer: (1500, 4000), damage: (600, 6000));
        var (_, events) = StepTo(run, 600);
        var charges = events.Where(e => e.Type == "cast_complete" && Int(e, "actorId") == 100 && e.Get("abilityId") is JsString { Value: "slagRam.moltenCharge" }).ToList();
        Assert.True(charges.Count >= 2, $"{charges.Count} charges landed");
        foreach (var c in charges)
        {
            var hit = events.Where(d => d.Type == "damage" && d.Tick == c.Tick && Int(d, "sourceId") == 100 && Int(d, "rawAmount") == 150)
                .Select(d => Int(d, "targetId")).ToHashSet();
            Assert.Contains(Tank, hit.Select(h => (int)h!.Value));
            Assert.Contains(Damage, hit.Select(h => (int)h!.Value));
            Assert.DoesNotContain(Healer, hit.Select(h => (int)h!.Value));
        }
    }

    [Fact(DisplayName = "D66 Drowned Bell: Resonant Toll stacks Resonance on enemies in its ring and on nobody outside it")]
    public void Drowned_bell_stacks_resonance_inside_its_ring()
    {
        var run = Arena(
            new[] { new Mob(100, "bellfoundry.tollingBell", 0, 0), new Mob(101, "bellfoundry.brineMender", 3000, 0), new Mob(102, "bellfoundry.brineMender", 15000, 0) },
            tank: (0, 20000), healer: (2000, 20000), damage: (-2000, 20000));
        var (after, _) = StepTo(run, 700);
        Assert.True(Stacks(after, 101, "resonance") >= 2, $"inside the ring: {Stacks(after, 101, "resonance")} Resonance stacks");
        Assert.False(Has(after, 102, "resonance"), "outside the ring: Resonance");
        Assert.All(new[] { Tank, Healer, Damage }, p => Assert.False(Has(after, p, "resonance")));
    }

    [Fact(DisplayName = "D66 Furnace Thrall: dying Overheated it bursts in Slag Burn; dying with its Stoke Core kicked it does not")]
    public void Furnace_thrall_bursts_on_death_unless_its_core_was_kicked()
    {
        var kick = Reg.Abilities.Values.First(a => a.Id.StartsWith("cinder.", System.StringComparison.Ordinal) && a.Effects.Any(o => o is C.InterruptOp)).Id;
        Run Thrall() => Arena(new[] { new Mob(100, "bellfoundry.furnaceThrall", 0, 0) }, tank: (0, 1500), healer: (8000, 8000), damage: (1500, 0));

        // Uncovered: Stoke Core lands, the Thrall is Overheated; killed, it bursts and the player beside it burns.
        var (hot, _) = StepTo(Thrall(), 200);
        Assert.True(Has(hot, 100, "overheatedCore"), "Stoke Core never landed");
        var thrall = hot.State.Actors[100];
        thrall.Health = 0;
        thrall.Alive = false;
        var (burst, burstEvents) = StepTo(hot, 210);
        Assert.Contains(burstEvents, e => e.Type == "cast_complete" && e.Get("abilityId") is JsString { Value: "furnaceThrall.slagBurst" });
        Assert.True(Has(burst, Damage, "slagBurn"), "the player beside the burst does not burn");
        Assert.False(Has(burst, Healer, "slagBurn"), "the player far from the burst burns");

        // Covered: the Cinder kicks Stoke Core mid-cast; killed, the Thrall leaves nothing.
        var kickedRun = Thrall();
        var kicked = false;
        var (cold, coldEvents) = StepTo(kickedRun, 200, r =>
        {
            var a = r.State.Actors[100];
            if (!kicked && a.CastId == "furnaceThrall.stokeCore" && a.CastEndsTick - r.State.Tick < 30)
            {
                kicked = true;
                return new Command[] { new CastCommand(r.State.Tick, Damage, kick, 100) };
            }
            return System.Array.Empty<Command>();
        });
        Assert.Contains(coldEvents, e => e.Type == "interrupt" && Int(e, "targetId") == 100);
        Assert.False(Has(cold, 100, "overheatedCore"), "the kicked Stoke Core still overheated the Thrall");
        var coldThrall = cold.State.Actors[100];
        coldThrall.Health = 0;
        coldThrall.Alive = false;
        var (quiet, quietEvents) = StepTo(cold, 210);
        Assert.DoesNotContain(quietEvents, e => e.Type == "cast_complete" && e.Get("abilityId") is JsString { Value: "furnaceThrall.slagBurst" });
        Assert.False(Has(quiet, Damage, "slagBurn"));
    }

    [Fact(DisplayName = "D66 Scald Hound: below 30% health it boils over (Boiling Blood); at 31% it never does")]
    public void Scald_hound_enrages_below_thirty_percent()
    {
        var (low, _) = StepTo(Arena(new[] { new Mob(100, "bellfoundry.scaldHound", 0, 0, HealthBp: 2900) }, (0, 1500), (8000, 8000), (-8000, 8000)), 130);
        Assert.True(Has(low, 100, "boilingBlood"), "a 29% Hound did not boil over");
        var (high, _) = StepTo(Arena(new[] { new Mob(100, "bellfoundry.scaldHound", 0, 0, HealthBp: 3100) }, (0, 1500), (8000, 8000), (-8000, 8000)), 400);
        Assert.False(Has(high, 100, "boilingBlood"), "a 31% Hound boiled over");
    }

    [Fact(DisplayName = "D66 Brine Mender: Bronze Ward lands on its pack (half damage taken) and its cast can be kicked")]
    public void Brine_mender_wards_its_pack_and_is_a_kick()
    {
        var mender = Reg.Abilities["brineMender.bronzeWard"];
        Assert.True(mender.Interruptible, "Bronze Ward is not a kick");
        Assert.Contains("healer", mender.Tags); // the bots' kick-priority tag (Kits.PriorityInterruptTag)
        Assert.Equal(-5000, Reg.Effects["bronzeWard"].Modifiers.DamageTakenBp);
        var (after, _) = StepTo(Arena(
            new[] { new Mob(100, "bellfoundry.brineMender", 0, 0), new Mob(101, "bellfoundry.tollingBell", 3000, 0), new Mob(102, "bellfoundry.tollingBell", 15000, 0) },
            (0, 20000), (2000, 20000), (-2000, 20000)), 300);
        Assert.True(Has(after, 101, "bronzeWard"), "the Bell beside the Mender is not warded");
        Assert.False(Has(after, 102, "bronzeWard"), "the Bell outside the ward's ring is warded");
    }
}
