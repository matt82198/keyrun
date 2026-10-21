using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Xunit;
using C = Keyrun.Content;

namespace Keyrun.Bots.Tests;

/// <summary>
/// PURPOSE: this artifact exists to scale enemy cooldowns for pack-only enemies too, so Tempo never leaves a pack
/// ability unscaled (finding f-c94c7cd7); status: real.
/// The REAL host setup (game/src/Host/HostRunSetup.cs) scales every enemy ability's cooldown with the key (BIBLE s8,
/// Tempo: "enemies cast more often", as a Mythic+ key level speeds trash and boss abilities alike). An enemy only a
/// layout pack fields (such as the Hall Sentry) is still an enemy of the run: an ability only it casts must get the
/// key-scaled cooldown and its loop must be floored against that cooldown, or the cast fires not_ready.
/// </summary>
public sealed class TempoPackCooldownTests
{
    private const string PackOnlyEnemy = "hall.sentry";
    private const string PackOnlyAbility = "test.packOnlySwing";
    private const int PackOnlyCooldown = 300;
    private const int KeyLevel = 10;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    /// <summary>
    /// The loaded content with one ability no pull, boss or Twin casts: a copy of the Hall Sentry's swing under a new id
    /// and a cooldown longer than the Sentry's loop, cast only by the Sentry (a pack-only enemy of the Hollow Vault).
    /// </summary>
    private static ContentRegistry WithPackOnlyAbility()
    {
        var registry = Registry.Value;
        var sentry = registry.Enemies[PackOnlyEnemy];
        var swing = registry.Abilities[sentry.Script[0].Cast];
        var ability = swing with { Id = PackOnlyAbility, CooldownTicks = PackOnlyCooldown };
        var script = sentry.Script.Select(s => s with { Cast = PackOnlyAbility }).ToList();
        var enemy = sentry with
        {
            Abilities = new ValueList<string>(new[] { PackOnlyAbility }),
            Script = new ValueList<C.ScriptStep>(script),
        };
        return registry with
        {
            Abilities = new ValueMap<C.AbilityDef>(registry.Abilities.Append(new KeyValuePair<string, C.AbilityDef>(PackOnlyAbility, ability))),
            Enemies = new ValueMap<C.EnemyDef>(registry.Enemies.Select(kv => kv.Key == PackOnlyEnemy
                ? new KeyValuePair<string, C.EnemyDef>(kv.Key, enemy)
                : kv)),
        };
    }

    [Fact(DisplayName = "Tempo: an ability only a pack-only enemy casts gets its key-scaled cooldown")]
    public void Pack_only_ability_cooldown_is_scaled()
    {
        var registry = WithPackOnlyAbility();
        var setup = HostRunSetup.Build(registry, "hollow-vault", KeyLevel, "warden");

        Assert.NotEqual(PackOnlyCooldown, Tempo.ScaleCooldownTicks(PackOnlyCooldown, KeyLevel));
        Assert.Equal(Tempo.ScaleCooldownTicks(PackOnlyCooldown, KeyLevel), setup.Content.Abilities![PackOnlyAbility].CooldownTicks);
    }

    [Fact(DisplayName = "Tempo: a pack-only enemy's loop is floored against its ability's scaled cooldown")]
    public void Pack_only_loop_is_floored_against_the_scaled_cooldown()
    {
        var registry = WithPackOnlyAbility();
        var setup = HostRunSetup.Build(registry, "hollow-vault", KeyLevel, "warden");
        var floor = Tempo.ScaledCastFloorTicks(PackOnlyCooldown, registry.Abilities[PackOnlyAbility].CastTicks, KeyLevel);

        var loops = setup.Content.EnemyScripts!.Values.SelectMany(s => s).Where(s => s.Cast == PackOnlyAbility).ToList();
        Assert.NotEmpty(loops);
        Assert.All(loops, step =>
        {
            Assert.NotNull(step.Every);
            Assert.True(step.Every >= floor, $"pack-only loop every {step.Every} is below the cast floor {floor}");
        });
    }

    [Theory(DisplayName = "Tempo: every enemy-cast cooldown is scaled exactly once; the party's are untouched")]
    [InlineData(2)]
    [InlineData(10)]
    public void Every_enemy_cast_is_scaled_exactly_once(int keyLevel)
    {
        var registry = WithPackOnlyAbility();
        var setup = HostRunSetup.Build(registry, "hollow-vault", keyLevel, "warden");
        var enemyCasts = setup.Content.EnemyScripts!.Values
            .Concat(setup.Boss.Twins.Select(t => t.Script))
            .SelectMany(s => s)
            .Select(s => s.Cast)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(PackOnlyAbility, enemyCasts);
        foreach (var (id, def) in registry.Abilities)
        {
            var expected = enemyCasts.Contains(id) ? Tempo.ScaleCooldownTicks(def.CooldownTicks, keyLevel) : def.CooldownTicks;
            Assert.True(
                expected == setup.Content.Abilities![id].CooldownTicks,
                $"{id}: cooldown {setup.Content.Abilities[id].CooldownTicks}, expected {expected} (authored {def.CooldownTicks})");
        }
    }
}
