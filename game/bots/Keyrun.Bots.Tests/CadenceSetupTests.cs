using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// CADENCE-SETUP (finding f-b31e8f34; design/BIBLE.md section 4 "synergy is a timing game between two people" and
/// section 5 "Cadence ... Setup: applies Attuned Window"). The Chanter bot used to cast Cadence only as a heal, so the
/// Attuned Window always landed on an ally while its Payoffs (Anthem by a Warden, Flashfire by a Cinder;
/// content/data/combos.json) are cast at enemies: no Attuned Combo ever closed in bot play. The healer now opens Attuned
/// on an enemy when nobody needs a heal and a Payoff partner is ready to close it, the way a WoW Mythic+ healer
/// contributes damage setups during downtime.
/// </summary>
public sealed class CadenceSetupTests
{
    private const long Mend = 1;
    private const long Ward = 0;

    private readonly ITestOutputHelper _output;

    public CadenceSetupTests(ITestOutputHelper output) => _output = output;

    /// <summary>The seeded Hollow Vault bot runs (the oracle setups ForkParityTests measures).</summary>
    private static readonly (string Name, string SetupFile, long Seed, int Dial)[] Scenarios =
    {
        ("hv-k2-s7", "hollow-vault-k2.setup.json", 7, 100),
        ("hv-k2-s11-d90", "hollow-vault-k2.setup.json", 11, 90),
        ("hv-k10-s7", "hollow-vault-k10.setup.json", 7, 100),
    };

    private const int MaxTicks = 24000;

    /// <summary>
    /// Runs one scenario with every party actor a real-role bot (as RolesOracleTraceTests.Drive and ForkParityTests
    /// build them) and counts the burst Combos that closed an Attuned Window, plus the Attuned Windows opened on an
    /// enemy.
    /// </summary>
    /// <summary>One run's counts.</summary>
    private sealed record Counts(int AttunedCombos, int AttunedOnEnemy, int Combos, int Clear, int Deaths, long EnemyHealedByParty)
    {
        /// <summary>Combos by id, "id=count" in id order.</summary>
        public string ByCombo { get; init; } = "";

        public override string ToString() =>
            $"attuned combos {AttunedCombos}, attuned windows on enemies {AttunedOnEnemy}, all combos {Combos} ({ByCombo}), clear {Clear}, deaths {Deaths}, enemy healed by party {EnemyHealedByParty}";
    }

    private static Counts Run((string Name, string SetupFile, long Seed, int Dial) s)
    {
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), s.SetupFile), s.Seed);
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var bots = new SortedDictionary<int, IBot>();
        var initial = new SortedDictionary<int, BotMemory>();
        foreach (var p in setup.Party)
        {
            bots[p.Id] = Bot.MakeBot(RoleSet.Default);
            initial[p.Id] = BotMemory.Default(s.Dial);
        }
        IReadOnlyDictionary<int, BotMemory> memories = initial;
        var prng = Skill.SeedPrng(s.Seed);
        var partyIds = new HashSet<int>(setup.Party.Select(p => p.Id));
        var attunedCombos = 0;
        var attunedOnEnemy = 0;
        var combos = 0;
        var deaths = 0;
        long enemyHealed = 0;
        var byCombo = new SortedDictionary<string, int>(StringComparer.Ordinal);
        while (run.RunEnd.Result is null && run.State.Tick < MaxTicks)
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, RealKits);
            var batch = Bot.RunBots(view, bots, memories, prng);
            memories = batch.Memories;
            prng = batch.Prng;
            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "combo")
                {
                    combos++;
                    var cid = e.Get("comboId") is JsString cs ? cs.Value : "?";
                    byCombo[cid] = byCombo.TryGetValue(cid, out var n) ? n + 1 : 1;
                    if (e.Get("windowId") is JsString { Value: "attuned" })
                    {
                        attunedCombos++;
                    }
                }
                else if (e.Type == "window_opened"
                    && e.Get("windowId") is JsString { Value: "attuned" }
                    && e.Get("targetId") is JsInt t
                    && !partyIds.Contains(checked((int)t.Value)))
                {
                    attunedOnEnemy++;
                }
                else if (e.Type == "death" && e.Get("actorId") is JsInt dead && partyIds.Contains(checked((int)dead.Value)))
                {
                    deaths++;
                }
                else if (e.Type == "heal"
                    && e.Get("sourceId") is JsInt src && partyIds.Contains(checked((int)src.Value))
                    && e.Get("targetId") is JsInt healed && !partyIds.Contains(checked((int)healed.Value))
                    && e.Get("amount") is JsInt amount)
                {
                    enemyHealed += amount.Value;
                }
            }
        }
        return new Counts(attunedCombos, attunedOnEnemy, combos, run.RunEnd.ResultTick ?? MaxTicks + 1, deaths, enemyHealed)
        {
            ByCombo = string.Join(" ", byCombo.Select(kv => kv.Key + "=" + kv.Value)),
        };
    }

    [Fact(DisplayName = "Cadence setup > the bot party closes Attuned Combos (Anthem or Flashfire) in the seeded Hollow Vault runs")]
    public void Bot_party_closes_attuned_combos()
    {
        var total = 0;
        foreach (var s in Scenarios)
        {
            var c = Run(s);
            _output.WriteLine($"{s.Name}: {c}");
            Assert.True(c.AttunedOnEnemy >= c.AttunedCombos, $"{s.Name}: {c.AttunedCombos} Attuned combos but only {c.AttunedOnEnemy} Attuned windows on enemies");
            total += c.AttunedCombos;
        }
        _output.WriteLine($"attuned combos total {total}");
        Assert.True(total >= 1, $"no Attuned combo closed in any seeded bot run (attuned combos total {total})");
    }

    private static ActorState Chanter(long resource, long health = 250, IEnumerable<(string, int)>? cooldowns = null) =>
        Actor(id: 2, classId: "chanter", role: ActorRole.Healer, x: 0, z: 0, health: health, maxHealth: 250, resource: resource, maxResource: 1,
            cooldowns: cooldowns);

    private static ActorState Warden(long health = 400) =>
        Actor(id: 1, classId: "warden", role: ActorRole.Tank, x: 0, z: 6000, health: health, maxHealth: 400, resource: 100, maxResource: 100, targetId: 50);

    private static ActorState Cinder(IEnumerable<(string, int)>? cooldowns = null, IEnumerable<EffectInstance>? effects = null) =>
        Actor(id: 3, classId: "cinder", role: ActorRole.Damage, x: 1000, z: 0, health: 250, maxHealth: 250, resource: 0, maxResource: 100,
            targetId: 50, cooldowns: cooldowns, effects: effects);

    private static ActorState Enemy(IEnumerable<EffectInstance>? effects = null) =>
        Actor(id: 50, kind: ActorKind.Enemy, classId: "enemy", role: ActorRole.Damage, x: 0, z: 8000, health: 1000, maxHealth: 1000, effects: effects);

    private static BotView View(params ActorState[] actors) => ViewOf(10, RealKits, null, actors);

    /// <summary>Every payoff ability of the Cinder on cooldown far past the decision tick.</summary>
    private static IEnumerable<(string, int)> CinderPayoffsDown() =>
        RoleShared.KitOf(View(Cinder()), "cinder").PayoffAbilityIds.Select(id => (id, 9999));

    /// <summary>Every setup ability of the Cinder on cooldown far past the decision tick (its own Window is not ready).</summary>
    private static IEnumerable<(string, int)> CinderSetupsDown() =>
        RoleShared.KitOf(View(Cinder()), "cinder").SetupAbilityIds.Select(id => (id, 9999));

    [Fact(DisplayName = "Cadence setup > nobody hurt, a Cinder payoff ready and its own Setup down: the Mend healer opens Attuned on the party's enemy")]
    public void Setup_on_enemy_when_partner_ready()
    {
        var chanter = Chanter(Mend);
        var cmds = HealerRole.HealerDecide(View(Warden(), chanter, Cinder(cooldowns: CinderSetupsDown()), Enemy()), chanter);
        Assert.Equal(new Command[] { new CastCommand(10, 2, "chanter.cadence", 50) }, cmds);
    }

    [Fact(DisplayName = "Cadence setup > the partner's own Setup is ready: the healer leaves the Window slot to it (fills gaps only)")]
    public void No_setup_when_partner_can_open_its_own()
    {
        var chanter = Chanter(Mend);
        var cmds = HealerRole.HealerDecide(View(Warden(), chanter, Cinder(), Enemy()), chanter);
        Assert.Empty(cmds);
    }

    [Fact(DisplayName = "Cadence setup > an ally under the heal threshold still gets the heal, not the setup")]
    public void Heal_beats_setup()
    {
        var chanter = Chanter(Mend);
        var cmds = HealerRole.HealerDecide(View(Warden(health: 100), chanter, Cinder(), Enemy()), chanter);
        Assert.DoesNotContain(cmds.OfType<CastCommand>(), c => c.TargetId == 50);
        Assert.NotEmpty(cmds);
    }

    [Fact(DisplayName = "Cadence setup > no partner payoff ready: no setup")]
    public void No_setup_without_ready_partner()
    {
        var chanter = Chanter(Mend);
        var warden = Warden();
        var wardenPayoffsDown = RoleShared.KitOf(View(warden), "warden").PayoffAbilityIds.Select(id => (id, 9999)).ToList();
        warden.Cooldowns = new JsRecord<int>();
        foreach (var (id, ready) in wardenPayoffsDown)
        {
            warden.Cooldowns[id] = ready;
        }
        var cmds = HealerRole.HealerDecide(View(warden, chanter, Cinder(cooldowns: CinderPayoffsDown()), Enemy()), chanter);
        Assert.Empty(cmds);
    }

    [Fact(DisplayName = "Cadence setup > a ready tank is not a setup partner (only the Window-hunting damage role is)")]
    public void Tank_is_not_a_partner()
    {
        var chanter = Chanter(Mend);
        var cmds = HealerRole.HealerDecide(View(Warden(), chanter, Cinder(cooldowns: CinderPayoffsDown()), Enemy()), chanter);
        Assert.Empty(cmds);
    }

    [Fact(DisplayName = "Cadence setup > a Window already on the enemy: no setup (an older Window would shadow Attuned)")]
    public void No_setup_over_an_open_window()
    {
        var chanter = Chanter(Mend);
        var kindled = new[] { Effect("window:kindled:cinder", 3, 200) };
        var cmds = HealerRole.HealerDecide(View(Warden(), chanter, Cinder(cooldowns: CinderSetupsDown()), Enemy(effects: kindled)), chanter);
        Assert.Empty(cmds);
    }

    [Fact(DisplayName = "Cadence setup > in Ward the healer sets up too (the Ward shield never lands on an enemy, HEAL-NEVER-HOSTILE)")]
    public void Setup_in_ward_too()
    {
        var chanter = Chanter(Ward);
        var cmds = HealerRole.HealerDecide(View(Warden(), chanter, Cinder(cooldowns: CinderSetupsDown()), Enemy()), chanter);
        Assert.Equal(new Command[] { new CastCommand(10, 2, "chanter.cadence", 50) }, cmds);
    }
}
