using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// SEEDBLOOM-NO-STAKES (finding f-ff4ce040; design/BIBLE.md section 9.6 item 3: the Seed "on death drops a Window on the
/// floor; a Combo landed in it grants bonus loot at run end. Teaches the boss's core rule with no stakes"). A floor
/// Window's field Combo fires onto the Payoff actor itself (<see cref="Payoff.TryClosePayoffOnFloor"/>), and the frozen
/// content/data/combos.json row "seedbloom-field" is 120 physical damage, so the player who closed the Seed's Death
/// Bloom was hit for 120: the teaching pull punished the lesson. The ComboTables here are built from the real content
/// files the way game/src/Host/HostRunSetup.cs ToComboTables builds them (a combo-riders.json row replaces the combo's
/// effects; a <c>payoffClass "*"</c> row goes to the field table once per other class), so the test sees exactly what
/// the game plays.
/// Controls: the boss's own version (the Hollow Warden's Lock, section 9 "Only a Combo closes it") still lands its
/// damage on the Lock carrier, and a player floor Window's field Combo (Hymn) still applies to its closer, so the fix
/// is the Seed's data, not a sim rule that stops field Combos applying to the closer.
/// </summary>
public sealed class SeedBloomNoStakesTests
{
    private const long Seed = 4242;
    private const int WardenId = 1;
    private const int CinderId = 2;
    private const int ChanterId = 3;
    private const int SeedActorId = 108;
    private const int DummyId = 200;
    private const long PartyHealth = 1000;

    // content/data/classes/*.json: the registry's class ids, which HostRunSetup fans a "*" row out over.
    private static readonly string[] ClassIds = { "chanter", "cinder", "warden" };

    private static string FindUp(params string[] relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException(string.Join("/", relative) + " not found above " + AppContext.BaseDirectory);
    }

    private static IReadOnlyList<EffectOp> Ops(JsonElement arr, string where) => arr.EnumerateArray().Select(o => (EffectOp)(o.GetProperty("op").GetString() switch
    {
        "damage" => new DamageOp(o.GetProperty("amount").GetInt64(), o.GetProperty("school").GetString()!),
        "heal" => new HealOp(o.GetProperty("amount").GetInt64()),
        "applyEffect" => new ApplyEffectOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("stacks").GetInt64()),
        "removeEffect" => new RemoveEffectOp(o.GetProperty("effectId").GetString()!),
        "resource" => new ResourceOp(o.GetProperty("delta").GetInt64()),
        "threat" => new ThreatOp(
            o.TryGetProperty("amount", out var a) ? a.GetInt64() : null,
            o.TryGetProperty("multiplierBp", out var m) ? m.GetInt64() : null),
        "partyEffect" => new PartyEffectOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("stacks").GetInt64()),
        "cloud" => new CloudOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("radiusMm").GetInt64()),
        "closerBurn" => new CloserBurnOp(o.GetProperty("maxHealthBp").GetInt64(), o.GetProperty("capBp").GetInt64(), o.GetProperty("school").GetString()!),
        var k => throw new InvalidOperationException($"{where}: op {k} not mapped by this test"),
    })).ToList();

    /// <summary>HostRunSetup.ToComboTables over content/data/combos.json and game/content/data/combo-riders.json.</summary>
    private static (ComboTable Combos, ComboTable Field) GameComboTables()
    {
        using var riderDoc = JsonDocument.Parse(File.ReadAllText(FindUp("content", "data", "combo-riders.json")));
        var riders = riderDoc.RootElement.EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, r => Ops(r.GetProperty("effects"), "rider"), StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(File.ReadAllText(FindUp("content", "data", "combos.json")));
        var combos = new ComboTable();
        var field = new ComboTable();
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var id = c.GetProperty("id").GetString()!;
            var setup = c.GetProperty("setupClass").GetString()!;
            var payoff = c.GetProperty("payoffClass").GetString()!;
            var def = new ComboDef(id, riders.TryGetValue(id, out var r) ? r : Ops(c.GetProperty("effects"), id));
            if (payoff == "*")
            {
                foreach (var cls in ClassIds.Where(k => k != setup))
                {
                    field.Set(setup, cls, def);
                }
            }
            else
            {
                combos.Set(setup, payoff, def);
            }
        }
        return (combos, field);
    }

    private static Run CreateRun()
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            ["test.kill"] = RunTestKit.Ability("test.kill", new EffectOp[] { new DamageOp(100000, "physical") }),
            ["test.strike"] = RunTestKit.Ability("test.strike", new EffectOp[] { new PayoffOp() }, new AbilityShape(ShapeKind.Self)),
            ["test.hit"] = RunTestKit.Ability("test.hit", new EffectOp[] { new PayoffOp() }),
            // The Lock as content/data/abilities/enemies.json opens it (window "lock", onTarget, setupClass "boss",
            // alternate), here put on the dummy so a party Payoff can close it on that carrier.
            ["test.lock"] = RunTestKit.Ability("test.lock", new EffectOp[] { new WindowOp("lock", 60, true, "boss", Alternate: true) }),
            ["test.hymn"] = RunTestKit.Ability("test.hymn", new EffectOp[] { new WindowOp("hymn", 100, false, "chanter") }, new AbilityShape(ShapeKind.Self)),
            // seed.deathBloom as content/data/abilities/enemies.json has it.
            ["seed.deathBloom"] = RunTestKit.Ability(
                "seed.deathBloom",
                new EffectOp[] { new WindowOp("seedbloom", 100, false, "boss") },
                new AbilityShape(ShapeKind.Circle, 2500),
                range: 0),
        };
        var (combos, field) = GameComboTables();
        var content = RunTestKit.Content(abilities) with
        {
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [SeedActorId] = new[] { ScriptStep.OnDeath("seed.deathBloom") } },
            Combos = combos,
            FieldCombos = field,
        };
        return RunRoot.CreateRun(
            Seed,
            new[]
            {
                RunTestKit.Member(WardenId, "warden", ActorRole.Tank, 0, 0, PartyHealth, 100),
                RunTestKit.Member(CinderId, "cinder", ActorRole.Damage, 0, 0, PartyHealth, 100),
                RunTestKit.Member(ChanterId, "chanter", ActorRole.Healer, 0, 0, PartyHealth, 100),
            },
            RunTestKit.OnePull(
                "seedbloom-stakes-fixture",
                "seed",
                RunTestKit.Spawn(SeedActorId, "seed", 1000, 0, 100),
                RunTestKit.Spawn(DummyId, "dummy", 3000, 0, 100000)),
            content);
    }

    private static long I(SimEvent e, string key) => ((JsInt)e.Get(key)!).Value;

    private static string? S(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    [Fact(DisplayName = "SEEDBLOOM-NO-STAKES: closing the Seed's Death Bloom costs the closer nothing, and the Combo still earns the bonus")]
    public void Closing_the_seedbloom_window_deals_no_damage_to_the_closer()
    {
        var commands = new List<Command>
        {
            new CastCommand(0, WardenId, "test.kill", SeedActorId),
            new CastCommand(10, CinderId, "test.strike"),
        };
        var (run, events) = RunTestKit.StepUntil(CreateRun(), 20, commands);

        var combo = Assert.Single(events, e => e.Type == "combo" && S(e, "windowId") == "seedbloom");
        Assert.Equal("seedbloom-field", S(combo, "comboId"));
        Assert.Equal(CinderId, (int)I(combo, "payoffActorId"));

        // No stakes: nothing in the run damaged the closer, and its health is whole.
        Assert.DoesNotContain(events, e => e.Type == "damage" && I(e, "targetId") == CinderId);
        Assert.Equal(PartyHealth, run.State.Actors[CinderId]!.Health);

        // The lesson still pays: the Combo is the Seed bonus.
        var entry = Assert.Single(run.Rewards.BonusLoot);
        Assert.Equal(new BonusLootEntry("seed", "seedbloom", "seedbloom-field", combo.Tick, SeedActorId, CinderId), entry);
    }

    [Fact(DisplayName = "SEEDBLOOM-NO-STAKES control: the boss's Lock Combo still lands its damage on the Lock carrier")]
    public void The_boss_lock_combo_still_deals_its_damage()
    {
        var commands = new List<Command>
        {
            new CastCommand(0, WardenId, "test.lock", DummyId),
            new CastCommand(5, CinderId, "test.hit", DummyId),
        };
        var (_, events) = RunTestKit.StepUntil(CreateRun(), 10, commands);

        var combo = Assert.Single(events, e => e.Type == "combo" && S(e, "windowId") == "lock");
        Assert.Equal("lock-cinder", S(combo, "comboId"));
        var hit = Assert.Single(events, e => e.Type == "damage" && I(e, "targetId") == DummyId && e.Tick == combo.Tick);
        Assert.Equal(200, I(hit, "rawAmount"));
    }

    [Fact(DisplayName = "SEEDBLOOM-NO-STAKES control: a player floor Window's field Combo still applies to its closer")]
    public void A_player_field_combo_still_applies_to_the_closer()
    {
        var commands = new List<Command>
        {
            new CastCommand(0, ChanterId, "test.hymn"),
            new CastCommand(5, WardenId, "test.strike"),
        };
        var (_, events) = RunTestKit.StepUntil(CreateRun(), 10, commands);

        var combo = Assert.Single(events, e => e.Type == "combo" && S(e, "windowId") == "hymn");
        Assert.Equal("hymn-field", S(combo, "comboId"));
        Assert.Contains(events, e => e.Type == "heal" && I(e, "targetId") == WardenId && e.Tick == combo.Tick);
    }
}
