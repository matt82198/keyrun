using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

public sealed partial record HostRunSetup
{
    /// <summary>
    /// The armed boss rules from the boss's content entry (content/data/enemies/hollow-warden.json for hollow-vault):
    /// the boss's one Break bar becomes the Twins' shared bar, the two enemies whose <c>breakBarId</c> names it are the
    /// Twins (ids from <see cref="TwinActorIdStart"/> in content id order, Tempo-scaled health and scripts, flanking the
    /// boss at +/-<see cref="TwinOffsetMm"/> x), and the boss ability tagged <see cref="LockTag"/> gives the Lock its
    /// Window length (its window op), shield effect (its applyEffect op) and the script cast the armed rules suppress.
    /// The Lock is cast (<see cref="BossConfig.CastLock"/>, LOCK-TELEGRAPH): its castTicks and its script step's
    /// telegraph give it a cast bar and a floor circle before the Window opens. The 50% gate, 30 s repeat, three-fail enrage and its Tempo spike are BIBLE s9 structure (the
    /// <see cref="BossConfig"/> defaults; content has no field for them). Fails closed on any missing or extra piece.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="boss">The dungeon's boss enemy.</param>
    /// <param name="keyLevel">The key level (Tempo scaling of the Twins, as the boss pull is scaled).</param>
    /// <returns>The config for <see cref="BossRules.Arm"/>.</returns>
    /// <exception cref="InvalidOperationException">The boss has not exactly one Break bar, the bar has not exactly two
    /// Twins, an onEmpty op is not applyEffect, or the boss has not exactly one Lock ability with one window and one
    /// applyEffect op.</exception>
    /// <param name="twinHealthBp">Tyrannical's health on the Twins, bp (plan M5: "the boss and its adds"); 10000 for none.</param>
    public static BossConfig BuildBossConfig(C.ContentRegistry registry, C.EnemyDef boss, int keyLevel, int twinHealthBp = 10000)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(boss);
        var where = $"boss \"{boss.Id}\"";
        if (boss.BreakBars is not { Count: 1 } bars)
        {
            throw new InvalidOperationException($"run setup: {where} must declare exactly one Break bar (the Twins' shared bar)");
        }
        var bar = bars[0];
        var twinDefs = registry.Enemies.Values
            .Where(e => string.Equals(e.BreakBarId, bar.Id, StringComparison.Ordinal))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        if (twinDefs.Count != 2)
        {
            throw new InvalidOperationException($"run setup: {where} bar \"{bar.Id}\" is shared by {twinDefs.Count} enemies, expected the two Twins");
        }
        var twins = twinDefs.Select((def, i) => new BossTwinSpec(
            TwinActorIdStart + i,
            def.Id,
            Affixes.ScaleHealth(Tempo.ScaleHealth(def.MaxHealth, keyLevel), twinHealthBp),
            new Vec3(i == 0 ? TwinOffsetMm : -TwinOffsetMm, 0, 0),
            Tempo.ScaleScript(ToSim(def.Script), keyLevel, CastFloors(registry, keyLevel)))).ToList();
        var onEmpty = bar.OnEmpty.Select(op => op is C.ApplyEffectOp e
            ? new BreakBarOnEmptyEffect(e.EffectId, e.DurationTicks, e.Stacks)
            : throw new InvalidOperationException($"run setup: {where} bar \"{bar.Id}\" onEmpty has a {op.GetType().Name}, only applyEffect is run")).ToList();
        var twinsBar = new BreakBarSource(bar.Id, twins.Select(t => t.ActorId).ToList(), bar.Max, bar.DrainPerInterrupt, bar.DrainPerControl, onEmpty);

        var locks = boss.Abilities
            .Select(id => registry.Abilities.TryGetValue(id, out var a) ? a : throw new InvalidOperationException($"run setup: {where} lists unknown ability \"{id}\""))
            .Where(a => a.Tags.Contains(LockTag))
            .ToList();
        if (locks.Count != 1)
        {
            throw new InvalidOperationException($"run setup: {where} has {locks.Count} abilities tagged \"{LockTag}\", expected one Lock");
        }
        var lockAbility = locks[0];
        var window = lockAbility.Effects.OfType<C.WindowOp>().ToList();
        var shield = lockAbility.Effects.OfType<C.ApplyEffectOp>().ToList();
        if (window.Count != 1 || shield.Count != 1)
        {
            throw new InvalidOperationException($"run setup: Lock \"{lockAbility.Id}\" needs one window op and one applyEffect op (has {window.Count} and {shield.Count})");
        }
        return new BossConfig(BossActorId, twins, twinsBar)
        {
            LockWindowTicks = window[0].DurationTicks,
            ShieldEffectId = shield[0].EffectId,
            SuppressedBossCasts = new[] { lockAbility.Id },
            CastLock = true,
        };
    }
}
