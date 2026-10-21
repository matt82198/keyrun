using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>What the boss frame's Lock row says (LOCK-ROW, f-8566b4f0).</summary>
public enum LockRowState
{
    /// <summary>The boss is casting the Lock (<c>lock_cast</c> until <c>lock_open</c>).</summary>
    Casting,

    /// <summary>A Lock Window is open (<c>lock_open</c> until it is broken or fails).</summary>
    Open,

    /// <summary>The party just broke the Lock (the ring's green flash).</summary>
    Broken,

    /// <summary>The Lock just closed unbroken (the ring's red flash).</summary>
    Failed,

    /// <summary>The boss has enraged and nothing else is showing (the ring's dim red enrage ring).</summary>
    Enraged,
}

/// <summary>One frame of the boss frame's Lock row (LOCK-ROW).</summary>
/// <param name="State">What the row names.</param>
/// <param name="LockIndex">The 1-based Lock the row is about (0 for <see cref="LockRowState.Enraged"/>).</param>
/// <param name="StakeTempoBp">The Tempo on the line, basis points, from the R-LOCK-1 ladder.</param>
/// <param name="Stacks">The Tempo stack <see cref="StakeTempoBp"/> is, 1..<paramref name="MaxStacks"/>.</param>
/// <param name="MaxStacks">Stacks at which the boss is enraged (<see cref="BossConfig.FailsToEnrage"/>).</param>
/// <param name="Text">The row label.</param>
public sealed record BossLockRow(LockRowState State, int LockIndex, int StakeTempoBp, int Stacks, int MaxStacks, string Text);

/// <summary>
/// The boss frame's Lock row (LOCK-ROW, finding f-8566b4f0; BIBLE s3 "Cast bars are the enemy's language ... Shape plus
/// colour, never colour alone"; BIBLE s9 "The Lock"; ruling R-LOCK-1). Display only: fed the sim's events after each
/// step (<c>lock_cast</c>, <c>lock_open</c>, <c>lock_broken</c>, <c>lock_failed</c>, <c>boss_enrage</c>, the boss's
/// <c>death</c>), it names the Lock's state in words and the Tempo on the line, so the floor ring's colour is never the
/// only cue:
/// <list type="bullet">
/// <item>casting (<c>lock_cast</c> until <c>lock_open</c>) and open (until broken or failed): "LOCK open -- fail: Tempo
/// +16% (x1/3)", the stack a fail of this Lock adds (<see cref="BossRules.LockTempoBp"/> at fails + 1);</item>
/// <item>broken, for the ring's green flash: "LOCK broken -- Tempo +16% avoided";</item>
/// <item>failed, for the ring's red flash: "LOCK failed -- Tempo +16% (x1/3)", the stack the boss now carries;</item>
/// <item>enraged, while the dim red enrage ring is the only ring: "LOCK enraged -- Tempo +50% (x3/3)".</item>
/// </list>
/// Otherwise null: the row clears once the Lock has resolved. The priority is <c>LockRule.For</c>'s (open over a flash,
/// broken over failed, a flash over the enrage ring), and a flash lasts while the state tick is within
/// <see cref="FlashTicks"/> of the event's tick, as src/View/LockView.cs reads it. Integers only.
/// </summary>
public sealed class LockRowTracker
{
    /// <summary>How long a broken or failed row lasts, ticks (the Lock ring's flash, <c>LockRule.FlashTicks</c>).</summary>
    public const int DefaultFlashTicks = 30;

    private int? _bossId;
    private int _castIndex;
    private int _openIndex;
    private int _brokenIndex;
    private int _brokenTick = -1;
    private int _failedIndex;
    private int _failedTick = -1;
    private int _fails;
    private bool _enraged;

    /// <summary>A tracker whose broken and failed rows last <paramref name="flashTicks"/> ticks.</summary>
    /// <param name="flashTicks">The Lock ring's flash length, ticks.</param>
    public LockRowTracker(int flashTicks = DefaultFlashTicks)
    {
        FlashTicks = flashTicks;
    }

    /// <summary>How long a broken or failed row lasts, ticks.</summary>
    public int FlashTicks { get; }

    /// <summary>Reads one step's events.</summary>
    /// <param name="events">The step's events.</param>
    public void Observe(IEnumerable<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            switch (e.Type)
            {
                case "lock_cast":
                    _bossId = IntOf(e, "actorId") ?? _bossId;
                    _castIndex = IntOf(e, "lockIndex") ?? _castIndex + 1;
                    break;
                case "lock_open":
                    _bossId = IntOf(e, "actorId") ?? _bossId;
                    _openIndex = IntOf(e, "lockIndex") ?? Math.Max(_castIndex, _openIndex + 1);
                    _castIndex = 0;
                    break;
                case "lock_broken":
                    _brokenIndex = IntOf(e, "lockIndex") ?? _openIndex;
                    _brokenTick = e.Tick;
                    _openIndex = 0;
                    break;
                case "lock_failed":
                    _failedIndex = IntOf(e, "lockIndex") ?? _openIndex;
                    _failedTick = e.Tick;
                    _fails = IntOf(e, "fails") ?? _fails + 1;
                    _openIndex = 0;
                    break;
                case "boss_enrage":
                    _enraged = true;
                    break;
                case "death" when _bossId is { } boss && IntOf(e, "actorId") == boss:
                    // The boss's death ends the encounter: no cast lands and no Window stays open.
                    _castIndex = 0;
                    _openIndex = 0;
                    break;
            }
        }
    }

    /// <summary>Forgets every Lock (a new run).</summary>
    public void Reset()
    {
        _bossId = null;
        _castIndex = 0;
        _openIndex = 0;
        _brokenIndex = 0;
        _brokenTick = -1;
        _failedIndex = 0;
        _failedTick = -1;
        _fails = 0;
        _enraged = false;
    }

    /// <summary>The row on <paramref name="tick"/>, or null when nothing is showing.</summary>
    /// <param name="config">The armed boss rules (the ladder).</param>
    /// <param name="tick">The state tick.</param>
    /// <returns>The row, or null.</returns>
    public BossLockRow? Row(BossConfig config, int tick)
    {
        ArgumentNullException.ThrowIfNull(config);
        var max = config.FailsToEnrage;
        var next = Math.Min(_fails + 1, max);
        var nextBp = BossRules.LockTempoBp(config, _fails + 1);
        if (_openIndex > 0)
        {
            return new BossLockRow(LockRowState.Open, _openIndex, nextBp, next, max,
                FormattableString.Invariant($"LOCK open -- fail: Tempo +{Percent(nextBp)}% (x{next}/{max})"));
        }
        if (_castIndex > 0)
        {
            return new BossLockRow(LockRowState.Casting, _castIndex, nextBp, next, max,
                FormattableString.Invariant($"LOCK casting -- fail: Tempo +{Percent(nextBp)}% (x{next}/{max})"));
        }
        if (InFlash(tick, _brokenTick))
        {
            return new BossLockRow(LockRowState.Broken, _brokenIndex, nextBp, next, max,
                FormattableString.Invariant($"LOCK broken -- Tempo +{Percent(nextBp)}% avoided"));
        }
        var held = Math.Min(_fails, max);
        if (held > 0 && InFlash(tick, _failedTick))
        {
            var heldBp = BossRules.LockTempoBp(config, _fails);
            return new BossLockRow(LockRowState.Failed, _failedIndex, heldBp, held, max,
                FormattableString.Invariant($"LOCK failed -- Tempo +{Percent(heldBp)}% (x{held}/{max})"));
        }
        if (_enraged)
        {
            return new BossLockRow(LockRowState.Enraged, 0, config.EnrageTempoBp, max, max,
                FormattableString.Invariant($"LOCK enraged -- Tempo +{Percent(config.EnrageTempoBp)}% (x{max}/{max})"));
        }
        return null;
    }

    /// <summary>The Tempo bonus as a whole percent: trunc((bp - 10000) / 100), so 11666 reads 16, 15000 reads 50.</summary>
    private static int Percent(int tempoBp) => (int)IntMath.TruncDiv(tempoBp - Tempo.BpDenom, 100);

    private bool InFlash(int tick, int at) => at >= 0 && tick - at >= 0 && tick - at < FlashTicks;

    private static int? IntOf(SimEvent e, string key) =>
        e.Get(key) is JsInt v && v.Value >= int.MinValue && v.Value <= int.MaxValue ? (int)v.Value : null;
}
