using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Ruling R-SORROW-1 (event store seq 132): the Sunken Bellfoundry obeys design/BIBLE.md section 3 ("every dangerous enemy
/// action is a cast with a visible bar and a floor telegraph"), D66 (themed unique mobs) and D67 (three distinct bosses).
/// Every rule here is read from the loaded content, never from source text.
/// </summary>
public sealed class BellfoundryBibleTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    /// <summary>The auto-attack filler every Bellfoundry melee kind may carry: an instant swing, exempt by name (BIBLE s3).</summary>
    private const string AutoAttack = "choir.meleeSwing";

    /// <summary>The boss arena, mm (x -4000..18000, z -6000..9000): its shorter side bounds any ring a telegraph may draw.</summary>
    private const int ArenaWidthMm = 22000;

    private const int ArenaDepthMm = 15000;

    /// <summary>The five Hollow Vault kinds (enemies/vault.json) the Bellfoundry packs used to field.</summary>
    private static readonly string[] ReusedVaultKinds = { "ash.hexer", "vault.brute", "vault.hound", "bellows.keeper", "drowned.chorister" };

    /// <summary>Bellfoundry-owned replacements (D66): one per reused kind, with the forces points of the kind it replaces (R-SORROW-1: hexer 45, brute 55, hound 20, keeper 50, chorister 50; the loader fails closed on a forces value for a kind no pack fields, so the retired kinds' value files are gone).</summary>
    private static readonly (string Replaces, string Id, int Points)[] Owned =
    {
        ("ash.hexer", "bellfoundry.slagHexer", 45),
        ("vault.brute", "bellfoundry.ingotBrute", 55),
        ("vault.hound", "bellfoundry.slagWhelp", 20),
        ("bellows.keeper", "bellfoundry.pitTender", 50),
        ("drowned.chorister", "bellfoundry.knellSinger", 50),
    };

    private static IEnumerable<EnemyDef> BellfoundryEnemies() =>
        Reg.Enemies.Values.Where(e => e.Id.StartsWith("bellfoundry.", StringComparison.Ordinal));

    /// <summary>(enemy, ability) for every non-auto-attack ability in a Bellfoundry kit.</summary>
    private static IEnumerable<(EnemyDef Enemy, AbilityDef Ability)> Casts() =>
        BellfoundryEnemies()
            .SelectMany(e => e.Abilities.Concat(e.Script.Select(s => s.Cast)).Distinct().Select(a => (Enemy: e, Ability: Reg.Abilities[a])))
            .Where(c => c.Ability.Id != AutoAttack);

    private static bool Acts(AbilityDef a) =>
        a.Effects.Any(op => op is DamageOp or DamagePctOp or HealOp or ApplyEffectOp or PullOp);

    [Fact(DisplayName = "R-SORROW-1 / BIBLE s3: no Bellfoundry ability that damages, heals, buffs or controls is instant")]
    public void No_bellfoundry_cast_that_acts_has_castTicks_zero()
    {
        var casts = Casts().ToList();
        Assert.True(casts.Count >= 15, "vacuity guard: only " + casts.Count + " Bellfoundry casts enumerated");
        var instant = casts.Where(c => Acts(c.Ability) && c.Ability.CastTicks <= 0)
            .Select(c => c.Enemy.Id + " -> " + c.Ability.Id + " (castTicks " + c.Ability.CastTicks + ")").ToList();
        Assert.True(instant.Count == 0, "Bellfoundry instant casts (no readable bar):" + Environment.NewLine + string.Join(Environment.NewLine, instant));
    }

    [Fact(DisplayName = "R-SORROW-1 / BIBLE s3: every Bellfoundry cast is scripted with a floor telegraph that has a shape, a lead and spans the cast")]
    public void Every_bellfoundry_cast_has_a_shaped_floor_telegraph()
    {
        var gaps = new List<string>();
        foreach (var (enemy, ability) in Casts())
        {
            var steps = enemy.Script.Where(s => s.Cast == ability.Id).ToList();
            if (steps.Count == 0)
            {
                gaps.Add(enemy.Id + " -> " + ability.Id + ": in the kit, never scripted");
                continue;
            }
            foreach (var step in steps)
            {
                var t = step.Telegraph;
                if (t is null) { gaps.Add(enemy.Id + " -> " + ability.Id + ": no telegraph"); continue; }
                bool shaped = t.Shape switch
                {
                    TelegraphShape.Circle => t.Radius is > 0,
                    TelegraphShape.Cone => t.Radius is > 0 && t.Angle1024 is > 0,
                    TelegraphShape.Line => t.Length is > 0 && t.Width is > 0,
                    _ => false,
                };
                if (!shaped) gaps.Add(enemy.Id + " -> " + ability.Id + ": telegraph " + t.Shape + " has no size");
                if (t.LeadTicks < 10) gaps.Add(enemy.Id + " -> " + ability.Id + ": leadTicks " + t.LeadTicks);
                if (ability.CastTicks > 0 && (t.SpansCast != true || t.LeadTicks < ability.CastTicks))
                    gaps.Add(enemy.Id + " -> " + ability.Id + ": telegraph does not span its " + ability.CastTicks + "-tick cast");
            }
        }
        Assert.True(gaps.Count == 0, string.Join(Environment.NewLine, gaps));
    }

    [Fact(DisplayName = "R-SORROW-1: every Bellfoundry telegraph and cast shape fits inside the boss arena")]
    public void Every_bellfoundry_telegraph_fits_the_arena()
    {
        var bad = new List<string>();
        foreach (var enemy in BellfoundryEnemies())
        {
            // R-UNDERTOW-1: Undertow is arena-wide by ruling, bounded by the 24000 mm room's diagonal instead.
            foreach (var step in enemy.Script.Where(s => s.Telegraph is not null && s.Cast != "brineMother.undertow"))
            {
                var t = step.Telegraph!;
                int ring = t.Shape == TelegraphShape.Line ? t.Length ?? 0 : (t.Radius ?? 0) * 2;
                if (ring > ArenaDepthMm) bad.Add(enemy.Id + " -> " + step.Cast + ": telegraph spans " + ring + " mm, the arena is " + ArenaDepthMm + " deep");
                if (ring > ArenaWidthMm) bad.Add(enemy.Id + " -> " + step.Cast + ": telegraph spans " + ring + " mm, the arena is " + ArenaWidthMm + " wide");
            }
        }
        foreach (var (_, ability) in Casts().Where(c => c.Ability.Id != "brineMother.undertow"))
        {
            int reach = ability.Shape.Kind == ShapeKind.Line ? ability.Shape.Length ?? 0 : (ability.Shape.Radius ?? 0) * 2;
            if (reach > ArenaDepthMm) bad.Add(ability.Id + ": shape spans " + reach + " mm, the arena is " + ArenaDepthMm + " deep");
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact(DisplayName = "R-SORROW-1 item 2: Mother's Undertow and Drowning Hymn are at most 6000 mm and read as different shapes")]
    public void Brine_mother_casts_are_small_and_distinct()
    {
        var undertow = Reg.Abilities["brineMother.undertow"];
        var hymn = Reg.Abilities["brineMother.drowningHymn"];
        var script = Reg.Enemies["bellfoundry.brineMother"].Script;
        // R-UNDERTOW-1 (seq 146): Undertow is arena-wide, not 6000 mm. The boss-3 room is 24000 x 24000 mm and the boss stands
        // 11500 mm in from the west door on the door row, so the farthest standable point is at most sqrt(12500^2 + 24000^2) =
        // 27058 mm away (17327 mm on the centre row); the radius is that rounded up to the next 500 mm.
        Assert.Equal(27500, undertow.Shape.Radius);
        Assert.Equal(27500, undertow.Range);
        Assert.Equal(27500, script.Single(s => s.Cast == undertow.Id).Telegraph!.Radius);
        foreach (var ability in new[] { hymn })
        {
            Assert.InRange(ability.Shape.Radius ?? ability.Shape.Length ?? 0, 1, 6000);
            Assert.InRange(ability.Range, 0, 6000);
            var telegraph = script.Single(s => s.Cast == ability.Id).Telegraph!;
            Assert.InRange(telegraph.Radius ?? telegraph.Length ?? 0, 1, 6000);
            // The floor shape is the shape that hits.
            Assert.Equal(ability.Shape.Kind.ToString(), telegraph.Shape.ToString(), ignoreCase: true);
        }
        Assert.NotEqual(undertow.Shape.Kind, hymn.Shape.Kind);
        Assert.Contains(ShapeKind.Circle, new[] { undertow.Shape.Kind, hymn.Shape.Kind });
        Assert.True(undertow.Shape.Kind is ShapeKind.Cone or ShapeKind.Line || hymn.Shape.Kind is ShapeKind.Cone or ShapeKind.Line);
    }

    [Fact(DisplayName = "R-SORROW-1 item 3: Slag Burst and Boiling Blood are casts with a bar of 30+ ticks and a floor telegraph")]
    public void Slag_burst_and_boiling_blood_are_casts()
    {
        foreach (var (enemyId, abilityId) in new[] { ("bellfoundry.furnaceThrall", "furnaceThrall.slagBurst"), ("bellfoundry.scaldHound", "scaldHound.boilingBlood") })
        {
            Assert.True(Reg.Abilities[abilityId].CastTicks >= 30, abilityId + " castTicks " + Reg.Abilities[abilityId].CastTicks);
            var step = Reg.Enemies[enemyId].Script.Single(s => s.Cast == abilityId);
            Assert.NotNull(step.Telegraph);
            Assert.True(step.Telegraph!.SpansCast == true && step.Telegraph.LeadTicks >= Reg.Abilities[abilityId].CastTicks, abilityId + " telegraph does not span its bar");
        }
        Assert.True(Reg.Abilities["scaldHound.boilingBlood"].Interruptible, "the party must be able to kick the enrage");
    }

    [Fact(DisplayName = "R-SORROW-1 item 4: Stoke Core and Ommet's Temper carry a floor telegraph")]
    public void Stoke_core_and_temper_are_telegraphed()
    {
        foreach (var (enemyId, abilityId) in new[] { ("bellfoundry.furnaceThrall", "furnaceThrall.stokeCore"), ("bellfoundry.ommet", "ommet.temper") })
        {
            var step = Reg.Enemies[enemyId].Script.Single(s => s.Cast == abilityId);
            Assert.NotNull(step.Telegraph);
            Assert.True(step.Telegraph!.Radius is > 0, abilityId + " telegraph has no radius");
            Assert.True(step.Telegraph.SpansCast == true && step.Telegraph.LeadTicks >= Reg.Abilities[abilityId].CastTicks);
        }
    }

    [Fact(DisplayName = "R-SORROW-1 item 1: Still the Bell is replaced by Resonance, a 60-tick kick with a 3500 mm ring, 125 damage and a 40-tick stun")]
    public void Old_sorrow_resonates()
    {
        Assert.False(Reg.Abilities.ContainsKey("greatBell.stillTheBell"), "Still the Bell survives");
        var resonance = Reg.Abilities["greatBell.resonance"];
        Assert.Equal("Resonance", resonance.Name);
        Assert.Equal(60, resonance.CastTicks);
        Assert.True(resonance.Interruptible);
        Assert.DoesNotContain("setup:lock", resonance.Tags);
        Assert.Equal(ShapeKind.Circle, resonance.Shape.Kind);
        Assert.InRange(resonance.Shape.Radius ?? 0, 3000, 4000);
        Assert.Equal(125, Assert.Single(resonance.Effects.OfType<DamageOp>()).Amount);
        var stun = Assert.Single(resonance.Effects.OfType<ApplyEffectOp>());
        Assert.Equal("stunned", stun.EffectId);
        Assert.Equal(40, stun.DurationTicks);
        Assert.DoesNotContain(resonance.Effects, op => op is WindowOp);
        var boss = Reg.Enemies["bellfoundry.greatBell"];
        var step = boss.Script.Single(s => s.Cast == "greatBell.resonance");
        Assert.Equal(TelegraphShape.Circle, step.Telegraph!.Shape);
        Assert.Equal(resonance.Shape.Radius, step.Telegraph.Radius);
        Assert.True(step.Telegraph.SpansCast == true && step.Telegraph.LeadTicks >= 60);
        var dungeon = Reg.Dungeons["sunken-bellfoundry"];
        Assert.DoesNotContain(dungeon.Segments.SelectMany(s => s.Boss.Mechanics).Concat(dungeon.Boss.Mechanics), m => m == "still-the-bell");
        Assert.Contains(dungeon.Segments.Last().Boss.Mechanics, m => m == "resonance");
    }

    [Fact(DisplayName = "D67: three Bellfoundry bosses, each with at least two cast mechanics, and no two bosses in the game share an ability body")]
    public void Bosses_are_distinct()
    {
        var bosses = Reg.Enemies.Values.Where(e => e.Role == EnemyRole.Boss).ToList();
        Assert.Equal(4, bosses.Count); // the Hollow Warden and the three Bellfoundry bosses
        foreach (var boss in bosses.Where(b => b.Id.StartsWith("bellfoundry.", StringComparison.Ordinal)))
        {
            Assert.True(boss.Script.Select(s => s.Cast).Distinct().Count() >= 2, boss.Id + " has fewer than two cast mechanics");
        }
        var bodies = bosses.SelectMany(b => b.Abilities.Select(a => (Boss: b.Id, Ability: Reg.Abilities[a]))).ToList();
        for (int i = 0; i < bodies.Count; i++)
        {
            for (int j = i + 1; j < bodies.Count; j++)
            {
                var a = bodies[i].Ability;
                var b = bodies[j].Ability;
                bool same = a.CastTicks == b.CastTicks && a.Shape == b.Shape && a.Interruptible == b.Interruptible
                    && a.Tags.SequenceEqual(b.Tags) && a.Effects.SequenceEqual(b.Effects);
                Assert.False(same, bodies[i].Boss + " " + a.Id + " is field-for-field " + bodies[j].Boss + " " + b.Id);
            }
        }
    }

    [Fact(DisplayName = "R-SORROW-1 item 5: no Bellfoundry pack fields an enemy defined in enemies/vault.json")]
    public void No_pack_references_a_vault_enemy()
    {
        var vault = JsonNode.Parse(EmbeddedContentSource.Instance.Read("enemies/vault.json"))!.AsArray()
            .Select(n => n!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        // The five reused kinds plus the corridor Patroller (R-PATROL-4), which only the Hollow Vault's layout patrols field,
        // plus the Warrior Remnant's own Cantor (R-REMNANT-3), which only that Hollow Vault pack fields.
        Assert.Equal(ReusedVaultKinds.Append("vault.patroller").Append("remnant.cantor").OrderBy(i => i, StringComparer.Ordinal), vault.OrderBy(i => i, StringComparer.Ordinal));
        var offenders = Reg.Roster.Values.Where(p => p.Dungeon == "sunken-bellfoundry")
            .SelectMany(p => p.Enemies.Where(g => vault.Contains(g.EnemyId)).Select(g => p.Id + " -> " + g.EnemyId)).ToList();
        Assert.True(offenders.Count == 0, string.Join(", ", offenders));
    }

    [Fact(DisplayName = "R-SORROW-1 item 5: each replaced kind has a Bellfoundry-owned id, a signature cast of its own, and the forces of the kind it replaces")]
    public void Owned_kinds_replace_the_vault_kinds()
    {
        var used = Reg.Roster.Values.Where(p => p.Dungeon == "sunken-bellfoundry").SelectMany(p => p.Enemies).Select(g => g.EnemyId).ToHashSet();
        foreach (var (_, id, points) in Owned)
        {
            Assert.True(Reg.Enemies.ContainsKey(id), id + " is not defined in enemies/bellfoundry.json");
            Assert.Contains(id, used);
            Assert.Equal(points, Reg.Forces.PointsOf(id));
            var def = Reg.Enemies[id];
            var signature = def.Script.Where(s => s.Cast != AutoAttack && Reg.Abilities[s.Cast].CastTicks >= 30).ToList();
            Assert.NotEmpty(signature);
            Assert.All(signature, s => Assert.NotNull(s.Telegraph));
            Assert.All(def.Abilities.Where(a => a != AutoAttack), a => Assert.StartsWith(id.Substring("bellfoundry.".Length) + ".", a, StringComparison.Ordinal));
        }
        // Distinct from one another: no two owned kinds share a signature ability.
        var abilities = Owned.SelectMany(o => Reg.Enemies[o.Id].Abilities.Where(a => a != AutoAttack)).ToList();
        Assert.Equal(abilities.Count, abilities.Distinct().Count());
    }
}
