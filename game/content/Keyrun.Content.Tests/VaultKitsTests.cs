using System;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// PURPOSE: this artifact exists to add BIBLE section 9.2/9.3's five vault enemies and their casts as content data
/// (slice S1 VAULT-KITS of game/design/FORCES-ROUTE-PLAN.md), so the bible's packs can be placed on the path (finding
/// f-82d9390e); status: real.
/// The five enemies (Hollow Hound, Ash Hexer, Vault Brute, Bellows Keeper, Drowned Chorister) live in the C#-only
/// game/content/data/enemies/vault.json, their casts in abilities/vault.json and the auras those casts apply in
/// effects/vault.json. The numbers are design/BIBLE.md section 9.3 (the kickable-caster table, the Vault Brute line
/// and the Ash Hexer's Cinders) and the effect shapes the CASTER-OPS-SIM tests (#302,
/// game/sim/Keyrun.Sim.Tests/CasterOpsTests.cs) drive through the sim. No pack places these enemies yet (slice S7).
/// </summary>
public sealed class VaultKitsTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static ScriptStep StepCasting(string enemyId, string abilityId) =>
        Assert.Single(Reg.Enemies[enemyId].Script, s => s.Cast == abilityId && s.At.Kind != ScriptAtKind.OnDeath);

    [Theory(DisplayName = "Every BIBLE 9.3 vault cast loads with its bible cast time, cooldown, kickability and floor telegraph")]
    // enemy, ability, name, castTicks, cooldownTicks, interruptible, telegraph shape, radius mm, cone angle1024 (0 = circle)
    [InlineData("ash.hexer", "ash.searingHex", "Searing Hex", 50, 200, true, "circle", 1200, 0)]
    [InlineData("bellows.keeper", "bellows.stoke", "Stoke", 70, 240, true, "circle", 4000, 0)]
    [InlineData("drowned.chorister", "drowned.undertow", "Undertow", 60, 180, true, "cone", 25000, 85)]
    [InlineData("vault.brute", "vault.crushingSlam", "Crushing Slam", 40, 160, false, "cone", 3500, 256)]
    public void Every_9_3_cast_loads_with_its_bible_cast_time_cooldown_and_telegraph(
        string enemyId, string abilityId, string name, int castTicks, int cooldownTicks, bool interruptible,
        string shape, int radius, int angle1024)
    {
        AbilityDef ability = Reg.Abilities[abilityId];
        Assert.Equal(name, ability.Name);
        Assert.Equal(castTicks, ability.CastTicks);
        Assert.Equal(cooldownTicks, ability.CooldownTicks);
        Assert.Equal(interruptible, ability.Interruptible);

        // BIBLE section 3: a dangerous cast has a visible bar and a floor telegraph drawn ahead of resolution.
        ScriptStep step = StepCasting(enemyId, abilityId);
        Assert.Equal(ScriptAtKind.Loop, step.At.Kind);
        Assert.Equal(cooldownTicks, step.Every);
        Assert.NotNull(step.Telegraph);
        TelegraphSpec telegraph = step.Telegraph!;
        Assert.Equal(shape == "cone" ? TelegraphShape.Cone : TelegraphShape.Circle, telegraph.Shape);
        Assert.Equal(radius, telegraph.Radius);
        Assert.Equal(angle1024 == 0 ? (int?)null : angle1024, telegraph.Angle1024);
        Assert.True(TelegraphDrawability.CanDraw(telegraph.Shape), abilityId + ": the view cannot draw " + telegraph.Shape);
        // The floor shape stays down for the whole bar (SLAM-TELEGRAPH), so it leads by at least the cast time.
        Assert.True(telegraph.SpansCast == true, abilityId + ": telegraph does not span its cast");
        Assert.True(telegraph.LeadTicks >= ability.CastTicks, abilityId + ": lead " + telegraph.LeadTicks + " < cast " + ability.CastTicks);
    }

    [Theory(DisplayName = "Each BIBLE 9.3 vault enemy loads with its bible name, role and casts")]
    // enemy, name, role, kit (comma list), walk speed mm/tick (0 = the run default)
    [InlineData("vault.hound", "Hollow Hound", "melee", "choir.meleeSwing", 260)]
    [InlineData("ash.hexer", "Ash Hexer", "caster", "ash.searingHex,ash.cinders", 0)]
    [InlineData("vault.brute", "Vault Brute", "melee", "vault.crushingSlam,choir.meleeSwing", 207)]
    [InlineData("bellows.keeper", "Bellows Keeper", "caster", "bellows.stoke", 0)]
    [InlineData("drowned.chorister", "Drowned Chorister", "caster", "drowned.undertow", 0)]
    public void Each_9_3_enemy_loads_with_its_bible_role_and_casts(string enemyId, string name, string role, string kit, int speed)
    {
        EnemyDef enemy = Reg.Enemies[enemyId];
        Assert.Equal(name, enemy.Name);
        Assert.Equal(role == "caster" ? EnemyRole.Caster : EnemyRole.Melee, enemy.Role);
        string[] casts = kit.Split(',');
        Assert.Equal(casts, enemy.Abilities.ToArray());
        // Every kit ability is cast by the script, and the script casts nothing outside the kit.
        Assert.Equal(casts.OrderBy(c => c, StringComparer.Ordinal), enemy.Script.Select(s => s.Cast).Distinct().OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(speed == 0 ? (int?)null : speed, enemy.SpeedMmPerTick);
        Assert.True(enemy.MaxHealth > 0, enemyId + ": maxHealth " + enemy.MaxHealth);
    }

    [Fact(DisplayName = "The Ash Hexer's death leaves Cinders: a 2000 mm circle led by 40 ticks that burns for 120 ticks")]
    public void Ash_Hexer_death_leaves_Cinders_2000_mm_led_by_40_ticks()
    {
        ScriptStep death = Assert.Single(Reg.Enemies["ash.hexer"].Script, s => s.At.Kind == ScriptAtKind.OnDeath);
        Assert.Equal("ash.cinders", death.Cast);
        Assert.NotNull(death.Telegraph);
        Assert.Equal(TelegraphShape.Circle, death.Telegraph!.Shape);
        Assert.Equal(2000, death.Telegraph.Radius);
        Assert.Equal(40, death.Telegraph.LeadTicks);

        AbilityDef cinders = Reg.Abilities["ash.cinders"];
        Assert.Equal("Cinders", cinders.Name);
        Assert.False(cinders.Interruptible);
        Assert.Equal(ShapeKind.Circle, cinders.Shape.Kind);
        Assert.Equal(2000, cinders.Shape.Radius);
        ApplyEffectOp burn = Assert.IsType<ApplyEffectOp>(Assert.Single(cinders.Effects));
        Assert.Equal(120, burn.DurationTicks);
        EffectDef effect = Reg.Effects[burn.EffectId];
        Assert.NotNull(effect.Periodic);
        Assert.IsType<DamagePctOp>(effect.Periodic!.Op);
    }

    [Fact(DisplayName = "Searing Hex's telegraph is anchored on the Hexer's target (BIBLE 9.3 'circle under the target')")]
    public void Searing_hex_telegraph_is_anchored_on_the_target()
    {
        // TELEGRAPH-ANCHOR (finding f-ebb50027): the first shipped content using an anchor. The other vault casts keep
        // the caster default (Stoke centres on the Keeper; Undertow and Crushing Slam are cones from the caster).
        Assert.Equal(TelegraphAnchor.Target, StepCasting("ash.hexer", "ash.searingHex").Telegraph!.Anchor);
        Assert.Null(StepCasting("bellows.keeper", "bellows.stoke").Telegraph!.Anchor);
        Assert.Null(StepCasting("drowned.chorister", "drowned.undertow").Telegraph!.Anchor);
        Assert.Null(StepCasting("vault.brute", "vault.crushingSlam").Telegraph!.Anchor);
    }

    [Fact(DisplayName = "Each vault cast lands its BIBLE 9.3 effect through the #302 caster ops")]
    public void Each_vault_cast_lands_its_bible_effect()
    {
        // Searing Hex: a magic burn of 25% of max health over 6 s (120 ticks), removable by Purge and Cleanse.
        ApplyEffectOp hex = Assert.IsType<ApplyEffectOp>(Assert.Single(Reg.Abilities["ash.searingHex"].Effects));
        Assert.Equal(120, hex.DurationTicks);
        EffectDef hexBurn = Reg.Effects[hex.EffectId];
        Assert.Equal("magic", hexBurn.DispellableTag);
        DamagePctOp tickBurn = Assert.IsType<DamagePctOp>(hexBurn.Periodic!.Op);
        Assert.Equal(2500, tickBurn.MaxHealthBp * (hex.DurationTicks / hexBurn.Periodic.EveryTicks));

        // Stoke: enemies in 4000 mm gain 30% damage and 20% cast speed for 8 s (160 ticks); an enrage Purge removes.
        AbilityDef stoke = Reg.Abilities["bellows.stoke"];
        Assert.Equal(ShapeKind.Circle, stoke.Shape.Kind);
        Assert.Equal(4000, stoke.Shape.Radius);
        Assert.Equal(ScriptTarget.Self, StepCasting("bellows.keeper", "bellows.stoke").Target);
        ApplyEffectOp stoked = Assert.IsType<ApplyEffectOp>(Assert.Single(stoke.Effects));
        Assert.Equal(160, stoked.DurationTicks);
        EffectDef stokedDef = Reg.Effects[stoked.EffectId];
        Assert.Equal("Stoked", stokedDef.Name);
        Assert.Equal("enrage", stokedDef.DispellableTag);
        Assert.Equal(3000, stokedDef.Modifiers.DamageDoneBp);
        Assert.Equal(2500, stokedDef.Modifiers.HasteBp); // 70-tick cast -> 56 ticks, 20% faster (CasterOpsTests)

        // Undertow: pulls the target 5000 mm toward the Chorister and roots it for 1 s (20 ticks); small damage.
        var undertow = Reg.Abilities["drowned.undertow"].Effects;
        Assert.Equal(5000, Assert.Single(undertow.OfType<PullOp>()).DistanceMm);
        ApplyEffectOp root = Assert.Single(undertow.OfType<ApplyEffectOp>());
        Assert.Equal(20, root.DurationTicks);
        Assert.Equal(-10000, Reg.Effects[root.EffectId].Modifiers.MovementBp);
        Assert.Single(undertow.OfType<DamageOp>());

        // Crushing Slam: 40% of max health in a 90-degree 3500 mm frontal cone.
        AbilityDef slam = Reg.Abilities["vault.crushingSlam"];
        Assert.Equal(ShapeKind.Cone, slam.Shape.Kind);
        Assert.Equal(3500, slam.Shape.Radius);
        Assert.Equal(256, slam.Shape.Angle1024);
        Assert.Equal(4000, Assert.IsType<DamagePctOp>(Assert.Single(slam.Effects)).MaxHealthBp);
    }

    [Fact(DisplayName = "Crushing Slam is in the melee set, so the Brute's slam plays the strike cue the Warden's Strike plays")]
    public void Crushing_Slam_plays_the_melee_strike_cue()
    {
        // KF1 strike cue (melee-abilities.json): one cue rule (StrikeCueRule.DurationMs and Pose) for every listed melee
        // ability, so membership gives the slam the Warden's Strike's exact cue parameters.
        Assert.Contains("warden.strike", Reg.MeleeAbilities);
        Assert.Contains("vault.crushingSlam", Reg.MeleeAbilities);
        var cues = StrikeCueRule.Cues(
            new[]
            {
                new StrikeEvent(StrikeCueRule.ResolveEvent, 42, 7, "vault.crushingSlam"),
                new StrikeEvent(StrikeCueRule.DamageEvent, 42, 7, TargetId: 9),
            },
            StrikeCueRule.MeleeIds(Reg.MeleeAbilities),
            _ => 0);
        StrikeCue cue = Assert.Single(cues);
        Assert.Equal("vault.crushingSlam", cue.AbilityId);
        Assert.Equal(StrikeCueRule.DurationMs, cue.DurationMs);
    }

    [Fact(DisplayName = "The dungeon has exactly the four BIBLE 9.3 kickable casts")]
    public void The_dungeon_has_exactly_the_four_bible_kickable_casts()
    {
        var kickable = (
            from enemy in Reg.Enemies.Values
            from step in enemy.Script
            let ability = Reg.Abilities[step.Cast]
            where ability.Interruptible
            select ability.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Choral Mending", "Searing Hex", "Stoke", "Undertow" }, kickable);
    }
}
