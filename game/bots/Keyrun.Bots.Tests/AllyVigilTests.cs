using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// ALLY-VIGIL (ruling R-FORKPARITY-UNPLAYED, 2026-10-21): the Warden's Vigil "ally" (Guardian) talent turns Vigil into
/// a target-shaped "ally-shield". A Warden bot carrying it must cast Vigil ON a pressured ally (the cast names a target
/// other than itself), never as its old self cast (which the sim refuses: "target_required") and never at an enemy.
/// The bots read the talent from the run's own ability map (<see cref="KitRegistry.ForRun"/>, as the host's
/// BotCommandSource does).
/// </summary>
public sealed class AllyVigilTests
{
    private const int Tick = 10;
    private const int TankId = 2;
    private const int AllyId = 3;

    private static S.AbilityDef Talented(string optionId)
    {
        var registry = ContentLoader.Load();
        var fork = registry.Classes["warden"].Talents.Single(f => f.AbilityId == "warden.vigil");
        var option = fork.Options.Single(o => o.Id == optionId);
        return ForkParityTests.ToSim(TalentPicks.ApplyPatch(registry.Abilities["warden.vigil"], option.Patch));
    }

    private static KitRegistry KitsWith(string optionId) =>
        KitRegistry.From(ContentLoader.Load()).ForRun(new Dictionary<string, S.AbilityDef>(StringComparer.Ordinal) { ["warden.vigil"] = Talented(optionId) });

    private static ActorState Warden(long resource = 100, IEnumerable<EffectInstance>? effects = null) =>
        Actor(id: TankId, classId: "warden", role: ActorRole.Tank, health: 400, maxHealth: 400, resource: resource, maxResource: 100,
            targetId: 100, cooldowns: new[] { ("warden.brace", 999), ("warden.taunt", 999) }, effects: effects);

    private static ActorState Ally(long z = 3000, long health = 100, IEnumerable<EffectInstance>? effects = null) =>
        Actor(id: AllyId, classId: "cinder", role: ActorRole.Damage, z: z, health: health, maxHealth: 250, maxResource: 100, effects: effects);

    private static ActorState Enemy(int id, int targetId) =>
        Actor(id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, z: 1500, yaw: 512, health: 6720, maxHealth: 6720,
            targetId: targetId, threat: new[] { (TankId, 100L) });

    private static List<CastCommand> Vigils(KitRegistry kits, params ActorState[] actors) =>
        TankRole.TankDecide(ViewOf(Tick, kits, null, actors), actors[0]).OfType<CastCommand>().Where(c => c.AbilityId == "warden.vigil").ToList();

    [Fact(DisplayName = "ally Vigil > a Warden with the ally talent casts Vigil on a pressured ally (the shield reaches a target other than itself)")]
    public void Casts_vigil_on_a_pressured_ally()
    {
        var vigils = Vigils(KitsWith("ally"), Warden(), Ally(), Enemy(100, AllyId));
        var cast = Assert.Single(vigils);
        Assert.Equal(AllyId, cast.TargetId);
    }

    [Fact(DisplayName = "ally Vigil > a low-health ally with nothing on it is pressured too")]
    public void Casts_vigil_on_a_low_health_ally() =>
        Assert.Equal(AllyId, Assert.Single(Vigils(KitsWith("ally"), Warden(), Ally(health: 100), Enemy(100, TankId))).TargetId);

    [Fact(DisplayName = "ally Vigil > no cast on a healthy, unattacked ally, and never a self or enemy-targeted Vigil")]
    public void No_cast_when_no_ally_is_pressured() =>
        Assert.Empty(Vigils(KitsWith("ally"), Warden(), Ally(health: 250), Enemy(100, TankId)));

    [Fact(DisplayName = "ally Vigil > no cast on an ally beyond Vigil's range")]
    public void No_cast_out_of_range() =>
        Assert.Empty(Vigils(KitsWith("ally"), Warden(), Ally(z: 9000), Enemy(100, AllyId)));

    [Fact(DisplayName = "ally Vigil > no recast while the ally still carries Vigil's shield")]
    public void No_recast_while_shielded() =>
        Assert.Empty(Vigils(KitsWith("ally"), Warden(), Ally(effects: new[] { Effect("bracedSelf", TankId, Tick + 50) }), Enemy(100, AllyId)));

    [Fact(DisplayName = "ally Vigil > the reserve talent keeps Vigil a self shield: never cast on an ally")]
    public void Reserve_never_targets_an_ally() =>
        Assert.DoesNotContain(Vigils(KitsWith("reserve"), Warden(), Ally(), Enemy(100, AllyId)), c => c.TargetId == AllyId);

    /// <summary>
    /// In a real seeded Hollow Vault run (the fork-parity hv-k2-s7 run, host-built view over the run's ability map),
    /// the ally-talented Warden bot completes Vigil casts and an ally other than the Warden carries Vigil's bracedSelf
    /// from it: the talent is played, not just loaded.
    /// </summary>
    [Fact(DisplayName = "ally Vigil > in the seeded Hollow Vault run the bots cast ally Vigil and an ally carries its shield")]
    public void The_bots_cast_ally_vigil_in_a_real_run()
    {
        var (casts, allyShieldTicks) = RunWithVigil(Talented("ally"));
        _output.WriteLine($"hv-k2-s7 ally: warden.vigil cast_complete {casts}, ally-shielded ticks {allyShieldTicks}");
        Assert.True(casts > 0, "the ally-talented Warden never completed a Vigil cast");
        Assert.True(allyShieldTicks > 0, "no ally other than the Warden ever carried Vigil's bracedSelf");
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    /// <summary>xUnit output.</summary>
    /// <param name="output">Test output.</param>
    public AllyVigilTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static (int Casts, int AllyShieldTicks) RunWithVigil(S.AbilityDef vigil)
    {
        var setup = Keyrun.Tools.Replay.RunSetup.Load(System.IO.Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), 7);
        var wardens = setup.Party.Where(p => p.ClassId == "warden").Select(p => p.Id).ToHashSet();
        Assert.NotEmpty(wardens);
        var loadouts = wardens.ToDictionary(id => id, _ => (IReadOnlyDictionary<string, S.AbilityDef>)new Dictionary<string, S.AbilityDef>(StringComparer.Ordinal) { [vigil.Id] = vigil });
        var options = (setup.Options ?? new Keyrun.Sim.Runs.RunOptions()) with { Loadouts = loadouts };
        var run = Keyrun.Sim.Runs.RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, options);
        var kits = KitRegistry.From(ContentLoader.Load());
        var bots = new SortedDictionary<int, IBot>();
        var initial = new SortedDictionary<int, BotMemory>();
        foreach (var p in setup.Party)
        {
            bots[p.Id] = Bot.MakeBot(RoleSet.Default);
            initial[p.Id] = BotMemory.Default(100);
        }
        IReadOnlyDictionary<int, BotMemory> memories = initial;
        var prng = Skill.SeedPrng(7);
        var casts = 0;
        var shielded = 0;
        while (run.RunEnd.Result is null && run.State.Tick < 24000)
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits.ForRun(run.Content.Abilities));
            var batch = Bot.RunBots(view, bots, memories, prng);
            memories = batch.Memories;
            prng = batch.Prng;
            var stepped = Keyrun.Sim.Runs.RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            casts += stepped.Events.Count(e => e.Type == "cast_complete" && e.Get("abilityId") is JsString { Value: "warden.vigil" });
            if (run.State.Actors.Values.Any(a => a.Kind == ActorKind.Player && !wardens.Contains(a.Id)
                && a.Effects.Any(x => x.EffectId == "bracedSelf" && wardens.Contains(x.SourceId))))
            {
                shielded++;
            }
        }
        return (casts, shielded);
    }
}
