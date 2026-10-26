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
/// CINDERFIELD-CLOSER-COST (finding f-17c18460; keyrun STATE.md D-row for Matt's 2026-10-20 ruling "probably, but as
/// damage scales this will turn into a one shot, so abilities and damage reduction stuff needs to be considered lol").
/// A floor Window's field Combo fires onto the Payoff actor itself (<see cref="Payoff.TryClosePayoffOnFloor"/>), so the
/// frozen content/data/combos.json row "cinderfield-field" (80 fire) burns whoever closes a Cinderfield. The burn keeps
/// happening, but it must be a survivable cost at every key: relative to the closer's max health, hard-capped, and
/// still reduced by the closer's damage reduction (Brace, Vigil, any damageTakenBp).
/// <para>The ComboTables are built from the real content files the way game/src/Host/HostRunSetup.cs ToComboTables
/// builds them (a game/content/data/combo-riders.json row replaces the combo's effects; a <c>payoffClass "*"</c> row is
/// fanned out to the field table once per other class), the Pact Clause and the Brace effect are read from
/// content/data, so the test sees what the game plays.</para>
/// <para>What can grow the burn (the closer is both the source and the target of a field Combo): the Pact Clause's
/// comboDamageBp (key-set Clauses from run.json clauseStartKey up to <see cref="ScreenState.MaxKey"/>), the closer's
/// own damageDoneBp, and any damageTakenBp on the closer. Gear Tier is banked power with no combat stat path today
/// (<see cref="Item.Tier"/> is read by rewards, progression and the HUD only), so "Tier 10" is modelled as the
/// channel future gear power would ride: a doubling damageDoneBp on the closer, plus an incoming vulnerability.</para>
/// </summary>
public sealed class CinderfieldCloserCostTests
{
    private const long Seed = 1717;
    private const int CinderId = 1;
    private const int ChanterId = 2;
    private const int WardenId = 3;
    private const int DummyId = 200;

    // game/src/Host/HostRunSetup.cs SupportMaxHealth / TankMaxHealth: the roster's max health per seat.
    private const long SupportMaxHealth = 250;
    private const long TankMaxHealth = 400;

    /// <summary>The conservative cap: the burn never takes more than a fifth of the closer's max health.</summary>
    private const long CapBp = 2000;

    /// <summary>The highest key the game offers (<see cref="ScreenState.MaxKey"/>).</summary>
    private static readonly int HighestKey = ScreenState.MaxKey;

    // content/data/classes/*.json: the registry's class ids, which HostRunSetup fans a "*" row out over.
    private static readonly string[] ClassIds = { "chanter", "cinder", "warden" };

    /// <summary>Gear Tier 10 as an amplifier on the closer: +100% damage done (the burn's source is the closer).</summary>
    private static readonly EffectDef Tier10Power = new("test.tier10Power", 1, null, new EffectModifiers(DamageDoneBp: 10000));

    /// <summary>An incoming vulnerability on the closer: +50% damage taken.</summary>
    private static readonly EffectDef Vulnerable = new("test.vulnerable", 1, null, new EffectModifiers(DamageTakenBp: 5000));

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
        using var riderDoc = JsonDocument.Parse(File.ReadAllText(FindUp("game", "content", "data", "combo-riders.json")));
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

    /// <summary>The Pact Clause as content/data/clauses.json defines it.</summary>
    private static ClauseDef Pact()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindUp("content", "data", "clauses.json")));
        var pact = doc.RootElement.EnumerateArray().Single(c => c.GetProperty("id").GetString() == "pact");
        var ps = pact.GetProperty("params").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64(), StringComparer.Ordinal);
        Assert.True(ps["comboDamageBp"] > 0, "the Pact must amplify Combos for this case to be the worst case");
        return new ClauseDef("pact", pact.GetProperty("name").GetString()!, ClauseKind.Pact, ps, pact.GetProperty("description").GetString()!);
    }

    /// <summary>A player defensive as content/data/effects/player.json defines it (its damageTakenBp only).</summary>
    private static EffectDef PlayerEffect(string id)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindUp("content", "data", "effects", "player.json")));
        var e = doc.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        var taken = e.GetProperty("modifiers").GetProperty("damageTakenBp").GetInt64();
        Assert.True(taken < 0, $"{id} must reduce damage taken");
        return new EffectDef(id, 1, null, new EffectModifiers(DamageTakenBp: taken));
    }

    /// <summary>A stackable vulnerability as game/content/data/effects/vault.json defines it (maxStacks and damageTakenBp).</summary>
    private static EffectDef VaultEffect(string id)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindUp("game", "content", "data", "effects", "vault.json")));
        var e = doc.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        var taken = e.GetProperty("modifiers").GetProperty("damageTakenBp").GetInt64();
        Assert.True(taken > 0, $"{id} must increase damage taken");
        return new EffectDef(id, e.GetProperty("maxStacks").GetInt64(), null, new EffectModifiers(DamageTakenBp: taken));
    }

    private static Run CreateRun(int keyLevel, bool pact)
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            // cinder.cinderfield's window op (content/data/abilities/cinder.json): a floor patch on the caster.
            ["test.cinderfield"] = RunTestKit.Ability(
                "test.cinderfield",
                new EffectOp[] { new WindowOp("cinderfield", 160, false, "cinder", RadiusMm: 1800) },
                new AbilityShape(ShapeKind.Self)),
            ["test.strike"] = RunTestKit.Ability("test.strike", new EffectOp[] { new PayoffOp() }, new AbilityShape(ShapeKind.Self)),
        };
        var effects = new Dictionary<string, EffectDef>(StringComparer.Ordinal)
        {
            [Tier10Power.Id] = Tier10Power,
            [Vulnerable.Id] = Vulnerable,
            ["braceSelf"] = PlayerEffect("braceSelf"),
            ["bracedSelf"] = PlayerEffect("bracedSelf"),
            ["aegisShield"] = PlayerEffect("aegisShield"),
            ["cleaved"] = VaultEffect("cleaved"),
        };
        var (combos, field) = GameComboTables();
        var pactDef = Pact();
        var content = RunTestKit.Content(abilities, effects) with
        {
            Combos = combos,
            FieldCombos = field,
            KeyLevel = keyLevel,
            Clauses = new Dictionary<string, ClauseDef>(StringComparer.Ordinal) { [pactDef.Id] = pactDef },
        };
        return RunRoot.CreateRun(
            Seed,
            new[]
            {
                RunTestKit.Member(CinderId, "cinder", ActorRole.Damage, 0, 0, SupportMaxHealth, 100),
                RunTestKit.Member(ChanterId, "chanter", ActorRole.Healer, 0, 0, SupportMaxHealth, 100),
                RunTestKit.Member(WardenId, "warden", ActorRole.Tank, 0, 0, TankMaxHealth, 100),
            },
            RunTestKit.OnePull("cinderfield-cost-fixture", "dummy", RunTestKit.Spawn(DummyId, "dummy", 6000, 0, 100000)),
            content,
            pact ? new RunOptions { ClauseId = pactDef.Id } : null);
    }

    private static long I(SimEvent e, string key) => ((JsInt)e.Get(key)!).Value;

    private static string? S(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    /// <summary>
    /// The Cinder lays a Cinderfield at tick 0; <paramref name="closerId"/>, standing in it, closes it at tick 5. Each
    /// id in <paramref name="closerEffects"/> is on the closer before the close. Returns the closer's health after the
    /// close and the health the burn took.
    /// </summary>
    private static (long Health, bool Alive, long Burn) Close(int keyLevel, bool pact, int closerId, params string[] closerEffects) =>
        CloseWith(keyLevel, pact, closerId, 0, closerEffects);

    /// <summary>
    /// <see cref="Close(int, bool, int, string[])"/> with the closer's gear damage-taken bp (<c>ActorState.GearDamageTakenBp</c>,
    /// the C#-only gear stat Mitigation.DealDamage adds to the target's modifiers) set before the close.
    /// </summary>
    private static (long Health, bool Alive, long Burn) CloseWith(int keyLevel, bool pact, int closerId, long gearTakenBp, params string[] closerEffects)
    {
        var run = CreateRun(keyLevel, pact);
        var closer = run.State.Actors[closerId]!;
        closer.GearDamageTakenBp = gearTakenBp;
        foreach (var id in closerEffects)
        {
            var def = run.Content.Effects[id];
            Effects.ApplyEffectInstance(closer, def, closerId, 1000, 1, run.State.Tick);
        }
        var commands = new List<Command>
        {
            new CastCommand(0, CinderId, "test.cinderfield"),
            new CastCommand(5, closerId, "test.strike"),
        };
        var (after, events) = RunTestKit.StepUntil(run, 10, commands);

        var combo = Assert.Single(events, e => e.Type == "combo" && S(e, "windowId") == "cinderfield");
        Assert.Equal("cinderfield-field", S(combo, "comboId"));
        Assert.Equal(closerId, (int)I(combo, "payoffActorId"));
        var burn = Assert.Single(events, e => e.Type == "damage" && I(e, "targetId") == closerId);
        Assert.Equal(closerId, (int)I(burn, "sourceId"));
        Assert.Equal(combo.Tick, burn.Tick);
        var actor = after.State.Actors[closerId]!;
        return (actor.Health, actor.Alive, I(burn, "healthDamage"));
    }

    private static long Cap(long maxHealth) => maxHealth * CapBp / 10000;

    [Theory(DisplayName = "CINDERFIELD-CLOSER-COST: at the highest key with the Pact and Tier 10 power, closing a Cinderfield never takes more than the cap")]
    [InlineData(ChanterId, SupportMaxHealth)]
    [InlineData(WardenId, TankMaxHealth)]
    public void Highest_key_tier10_pact_burn_stays_under_the_cap(int closerId, long maxHealth)
    {
        var (health, alive, burn) = Close(HighestKey, pact: true, closerId, Tier10Power.Id, Vulnerable.Id);

        Assert.True(alive, $"closer {closerId} died to its own Cinderfield close (burn {burn} of {maxHealth})");
        Assert.True(burn > 0, "the closer is still burned");
        Assert.True(burn <= Cap(maxHealth), $"burn {burn} exceeds the cap {Cap(maxHealth)} ({CapBp} bp of {maxHealth})");
        Assert.True(health >= maxHealth - Cap(maxHealth), $"closer left at {health} of {maxHealth}");
    }

    [Fact(DisplayName = "CINDERFIELD-CLOSER-COST: the real content at the highest key with the Pact keeps the Chanter above the cap")]
    public void Highest_key_pact_real_content_burn_stays_under_the_cap()
    {
        var (health, alive, burn) = Close(HighestKey, pact: true, ChanterId);

        Assert.True(alive);
        Assert.True(burn > 0);
        Assert.True(burn <= Cap(SupportMaxHealth), $"burn {burn} exceeds the cap {Cap(SupportMaxHealth)}");
        Assert.True(health >= SupportMaxHealth - Cap(SupportMaxHealth));
    }

    [Theory(DisplayName = "CINDERFIELD-CLOSER-COST: Brace and Vigil still reduce the burn, so defensive play matters")]
    [InlineData("braceSelf")]
    [InlineData("bracedSelf")]
    public void Damage_reduction_reduces_the_burn(string defensive)
    {
        var (_, _, open) = Close(HighestKey, pact: true, ChanterId);
        var (_, _, braced) = Close(HighestKey, pact: true, ChanterId, defensive);

        Assert.True(braced > 0, "a braced closer is still burned");
        Assert.True(braced < open, $"{defensive}: braced burn {braced} is not below the open burn {open}");
    }

    [Fact(DisplayName = "CINDERFIELD-CLOSER-COST: Brace still reduces the burn when Tier 10 power pushes it past the cap")]
    public void Damage_reduction_still_matters_above_the_cap()
    {
        var (_, _, open) = Close(HighestKey, pact: true, ChanterId, Tier10Power.Id, Vulnerable.Id);
        var (_, _, braced) = Close(HighestKey, pact: true, ChanterId, Tier10Power.Id, Vulnerable.Id, "braceSelf");

        Assert.True(braced > 0);
        Assert.True(braced < open, $"braced burn {braced} is not below the open burn {open}");
    }

    [Fact(DisplayName = "R-DEFCAP-1 red: a combo burn on a fully stacked defensive closer still lands at least 25% of the open burn")]
    public void Full_defensive_stack_combo_burn_keeps_a_floor()
    {
        // Brace (-4000) + Vigil (-3000) + Aegis (-3500) = -10500 total damageTakenBp, past the -7500 player floor.
        // The Combos.cs closer path sums damageTakenBp itself, so the floor must hold on this path too.
        var (_, _, open) = Close(HighestKey, pact: true, ChanterId);
        var (health, alive, stacked) = Close(HighestKey, pact: true, ChanterId, "braceSelf", "bracedSelf", "aegisShield");

        Assert.True(open > 0, "precondition: the open closer is burned, so the floor comparison is not vacuous");
        Assert.True(alive, "a stacked closer survives its own burn");
        Assert.True(
            stacked >= open / 4,
            $"stacked burn {stacked} is below 25% of the open burn {open}: total damageTakenBp -10500 reached the combo path unfloored");
        Assert.True(health <= SupportMaxHealth, $"health {health} exceeds max {SupportMaxHealth}");
    }

    [Theory(DisplayName = "R-DEFCAP-1 red: a combo burn on a closer with stacked Cleaved (+2500 each) and full defensives still lands at least 25% of the open burn")]
    [InlineData(1)]
    [InlineData(2)]
    public void Cleaved_stacks_with_full_defensive_stack_combo_burn_keeps_a_floor(int cleaveStacks)
    {
        // Cleaved (vault.json, +2500 damageTakenBp per stack) on the closer, then Brace (-4000) + Vigil (-3000) + Aegis (-3500).
        // The net total is +5000 - 10500 = -5500, above the -7500 floor, so the floor never engages; the reduction stage
        // then multiplies the capped burn by (10000 - 10500) and the burn reaches 0. A defensive stack must not zero it.
        var (_, _, open) = Close(HighestKey, pact: true, ChanterId);
        var ids = Enumerable.Repeat("cleaved", cleaveStacks).Concat(new[] { "braceSelf", "bracedSelf", "aegisShield" }).ToArray();
        var (health, alive, burn) = Close(HighestKey, pact: true, ChanterId, ids);

        Assert.True(open > 0, "precondition: the open closer is burned, so the floor comparison is not vacuous");
        Assert.True(alive, "a closer with stacked Cleaved and full defensives survives its own burn");
        Assert.True(
            burn >= open / 4,
            $"{cleaveStacks} Cleaved stack(s) + full defensives: burn {burn} is below 25% of the open burn {open}");
        Assert.True(health <= SupportMaxHealth, $"health {health} exceeds max {SupportMaxHealth}");
    }

    [Fact(DisplayName = "R-DEFCAP-1 red: gear damage-taken reduction on the closer lowers the combo burn, as it does on the Mitigation path, and keeps the 25% floor")]
    public void Gear_damage_taken_reduction_applies_to_the_combo_burn()
    {
        // GearDamageTakenBp -3000 (a gear stat, Runs.GearPower) is read by Mitigation.DealDamage but not by BurnCloser.
        var (_, _, open) = Close(HighestKey, pact: true, ChanterId);
        var (_, _, geared) = CloseWith(HighestKey, pact: true, ChanterId, -3000);

        Assert.True(open > 0, "precondition: the open closer is burned, so the comparison is not vacuous");
        Assert.True(geared < open, $"gear-reduced burn {geared} is not below the open burn {open}: GearDamageTakenBp is ignored on the combo path");
        Assert.True(geared >= open / 4, $"gear-reduced burn {geared} is below 25% of the open burn {open}");
    }

    [Fact(DisplayName = "CINDERFIELD-CLOSER-COST: at key 2 closing a Cinderfield still burns the closer, 15% of its max health")]
    public void Key_two_burn_still_happens()
    {
        var (health, alive, burn) = Close(2, pact: false, ChanterId);

        Assert.True(alive);
        // The rider's base: 1500 bp of the closer's max health, 250 * 1500 / 10000 = 37 (truncated).
        Assert.Equal(SupportMaxHealth * 1500 / 10000, burn);
        Assert.Equal(SupportMaxHealth - burn, health);
    }
}
