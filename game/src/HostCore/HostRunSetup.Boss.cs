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
    /// The most adds one boss Break bar may bring (D66): spawn points <see cref="AddOffsets"/> ring the boss.
    /// </summary>
    public const int MaxBossAdds = 4;

    /// <summary>
    /// Add spawn offsets from the boss, in content id order: the Hollow Warden's Twins at +x and -x
    /// (<see cref="TwinOffsetMm"/>, unchanged), then +z and -z for a third and fourth add.
    /// </summary>
    private static readonly Vec3[] AddOffsets =
    {
        new(TwinOffsetMm, 0, 0), new(-TwinOffsetMm, 0, 0), new(0, 0, TwinOffsetMm), new(0, 0, -TwinOffsetMm),
    };

    /// <summary>The id of the empty bar a boss with no adds is armed with (the encounter never creates it).</summary>
    public const string NoAddsBarId = "none";

    /// <summary>
    /// The armed boss rules from the boss's content entry (content/data/enemies/hollow-warden.json for hollow-vault):
    /// the boss's one Break bar becomes the adds' shared bar, the enemies whose <c>breakBarId</c> names it are the adds
    /// (the Hollow Warden's two Twins; ids from <see cref="TwinActorIdStart"/> in content id order, Tempo-scaled health
    /// and scripts, at <see cref="AddOffsets"/> around the boss), and the boss ability tagged <see cref="LockTag"/> gives
    /// the Lock its Window length (its window op), shield effect (its applyEffect op) and the script cast the armed rules
    /// suppress. The Lock is cast (<see cref="BossConfig.CastLock"/>, LOCK-TELEGRAPH): its castTicks and its script
    /// step's telegraph give it a cast bar and a floor circle before the Window opens. The 50% gate, 30 s repeat,
    /// three-fail enrage and its Tempo spike are BIBLE s9 structure (the <see cref="BossConfig"/> defaults; content has no
    /// field for them).
    /// D66 (the Sunken Bellfoundry's three bosses): a boss declares at most one Break bar, shared by 1 to
    /// <see cref="MaxBossAdds"/> adds, and at most one Lock. No bar arms an empty <see cref="NoAddsBarId"/> bar (no adds);
    /// no Lock arms the rules with the Lock off (threshold 0, nothing suppressed, nothing cast), so a boss without a Lock
    /// never opens one. Fails closed on anything else.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="boss">The dungeon's boss enemy.</param>
    /// <param name="keyLevel">The key level (Tempo scaling of the adds, as the boss pull is scaled).</param>
    /// <returns>The config for <see cref="BossRules.Arm"/>.</returns>
    /// <exception cref="InvalidOperationException">The boss has more than one Break bar, a bar has no adds or more than
    /// <see cref="MaxBossAdds"/>, an onEmpty op is not applyEffect, or the boss has more than one Lock ability or a Lock
    /// without exactly one window and one applyEffect op.</exception>
    /// <param name="twinHealthBp">Tyrannical's health on the adds, bp (plan M5: "the boss and its adds"); 10000 for none.</param>
    public static BossConfig BuildBossConfig(C.ContentRegistry registry, C.EnemyDef boss, int keyLevel, int twinHealthBp = 10000)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(boss);
        var where = $"boss \"{boss.Id}\"";
        var bars = boss.BreakBars ?? new C.ValueList<C.BreakBarDef>();
        if (bars.Count > 1)
        {
            throw new InvalidOperationException($"run setup: {where} declares {bars.Count} Break bars, at most one (the adds' shared bar)");
        }
        var twins = new List<BossTwinSpec>();
        var twinsBar = new BreakBarSource(NoAddsBarId, Array.Empty<int>(), 1, 0, 0, Array.Empty<BreakBarOnEmptyEffect>());
        if (bars.Count == 1)
        {
            var bar = bars[0];
            var twinDefs = registry.Enemies.Values
                .Where(e => string.Equals(e.BreakBarId, bar.Id, StringComparison.Ordinal))
                .OrderBy(e => e.Id, StringComparer.Ordinal)
                .ToList();
            if (twinDefs.Count < 1 || twinDefs.Count > MaxBossAdds)
            {
                throw new InvalidOperationException($"run setup: {where} bar \"{bar.Id}\" is shared by {twinDefs.Count} enemies, expected 1 to {MaxBossAdds} adds");
            }
            twins = twinDefs.Select((def, i) => new BossTwinSpec(
                TwinActorIdStart + i,
                def.Id,
                Affixes.ScaleHealth(Tempo.ScaleHealth(def.MaxHealth, keyLevel), twinHealthBp),
                AddOffsets[i],
                Tempo.ScaleScript(ToSim(def.Script), keyLevel, CastFloors(registry, keyLevel)))).ToList();
            var onEmpty = bar.OnEmpty.Select(op => op is C.ApplyEffectOp e
                ? new BreakBarOnEmptyEffect(e.EffectId, e.DurationTicks, e.Stacks)
                : throw new InvalidOperationException($"run setup: {where} bar \"{bar.Id}\" onEmpty has a {op.GetType().Name}, only applyEffect is run")).ToList();
            twinsBar = new BreakBarSource(bar.Id, twins.Select(t => t.ActorId).ToList(), bar.Max, bar.DrainPerInterrupt, bar.DrainPerControl, onEmpty);
        }

        var locks = boss.Abilities
            .Select(id => registry.Abilities.TryGetValue(id, out var a) ? a : throw new InvalidOperationException($"run setup: {where} lists unknown ability \"{id}\""))
            .Where(a => a.Tags.Contains(LockTag))
            .ToList();
        if (locks.Count > 1)
        {
            throw new InvalidOperationException($"run setup: {where} has {locks.Count} abilities tagged \"{LockTag}\", at most one Lock");
        }
        if (locks.Count == 0)
        {
            return new BossConfig(BossActorId, twins, twinsBar)
            {
                LockThresholdBp = 0,
                SuppressedBossCasts = Array.Empty<string>(),
                CastLock = false,
            };
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
