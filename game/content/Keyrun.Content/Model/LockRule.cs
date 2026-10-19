using System;

namespace Keyrun.Content;

/// <summary>What the Lock ring on the boss is showing (LOCK-VIEW).</summary>
public enum LockPhase
{
    /// <summary>No Lock open and no flash running: the ring is invisible (an enraged boss shows <see cref="Enraged"/>).</summary>
    Idle,

    /// <summary>A Lock Window is open: violet, shrinking with the time left.</summary>
    Open,

    /// <summary>The party just broke a Lock: a short green flash.</summary>
    Broken,

    /// <summary>A Lock just closed unbroken: a short red flash.</summary>
    Failed,

    /// <summary>The boss has enraged and nothing else is showing: a persistent dim red ring.</summary>
    Enraged,
}

/// <summary>One frame of the Lock ring.</summary>
/// <param name="Phase">What the top ring shows.</param>
/// <param name="Color">Top ring colour, 0xRRGGBB.</param>
/// <param name="RadiusMm">Top ring radius around the boss's feet, mm.</param>
/// <param name="AlphaPermille">Top ring opacity, 0..1000 (0 = not drawn).</param>
/// <param name="EnrageRing">True when the dim enrage ring (<see cref="LockRule.EnrageColor"/> at
/// <see cref="LockRule.EnrageRadiusMm"/>, <see cref="LockRule.EnrageAlphaPermille"/>) is drawn under the top ring.</param>
public sealed record LockLook(LockPhase Phase, int Color, int RadiusMm, int AlphaPermille, bool EnrageRing);

/// <summary>
/// The view-only colour and size rule for the Lock, the Hollow Warden's end-of-run mechanic (BIBLE s9 "The Lock"; LOCK-VIEW).
/// Pure and integer-only (game/CLAUDE.md purity rule): src/View/LockView.cs feeds it the sim's
/// <c>EncounterState.Boss</c> each frame and multiplies <see cref="LockLook.RadiusMm"/> by 0.001 to get metres.
/// Priority: Open over a flash (a new Lock beats the last one's flash), Broken over Failed, a flash over Enraged; the
/// enrage ring stays underneath whatever is on top.
/// </summary>
public static class LockRule
{
    /// <summary>Open Lock colour: violet, the LOCK chip colour (PartyFrames) and the Lock Window ring colour (WindowRingView).</summary>
    public const int OpenColor = 0xC77DFF;

    /// <summary>Broken flash colour: green.</summary>
    public const int BrokenColor = 0x60E080;

    /// <summary>Failed flash colour: red.</summary>
    public const int FailedColor = 0xFF5050;

    /// <summary>Enrage ring colour: dim red.</summary>
    public const int EnrageColor = 0xA03030;

    /// <summary>Open ring radius the tick the Lock opens, mm.</summary>
    public const int OpenStartRadiusMm = 4000;

    /// <summary>Open ring radius when the Window has run out, mm.</summary>
    public const int OpenEndRadiusMm = 1500;

    /// <summary>Broken and failed flash radius, mm.</summary>
    public const int FlashRadiusMm = 3000;

    /// <summary>Enrage ring radius, mm.</summary>
    public const int EnrageRadiusMm = 2000;

    /// <summary>How long a broken or failed flash lasts, ticks (1.5 s at 20 Hz).</summary>
    public const int FlashTicks = 30;

    /// <summary>Open ring opacity, permille.</summary>
    public const int OpenAlphaPermille = 850;

    /// <summary>Flash opacity at its first tick, permille; it falls linearly to nothing over <see cref="FlashTicks"/>.</summary>
    public const int FlashAlphaPermille = 900;

    /// <summary>Enrage ring opacity, permille.</summary>
    public const int EnrageAlphaPermille = 450;

    /// <summary>The Lock ring for one frame.</summary>
    /// <param name="open">True while a Lock Window is open (<c>BossState.Active</c> is set).</param>
    /// <param name="ticksLeft">Ticks until the open Window ends (<c>Active.EndsTick - tick</c>); clamped to [0, window].</param>
    /// <param name="windowTicks">The Window length (<c>BossConfig.LockWindowTicks</c>, 60).</param>
    /// <param name="locksBroken">Locks broken so far (<c>BossState.LocksBroken</c>); a broken flash needs at least one.</param>
    /// <param name="lockFails">Locks failed so far (<c>BossState.LockFails</c>); a failed flash needs at least one.</param>
    /// <param name="enraged">True once the boss has enraged (<c>BossState.Enraged</c>).</param>
    /// <param name="ticksSinceBroken">Ticks since the last <c>lock_broken</c>, negative for never.</param>
    /// <param name="ticksSinceFailed">Ticks since the last <c>lock_failed</c>, negative for never.</param>
    /// <returns>The look.</returns>
    public static LockLook For(bool open, int ticksLeft, int windowTicks, int locksBroken, int lockFails, bool enraged, int ticksSinceBroken, int ticksSinceFailed)
    {
        if (open)
        {
            return new LockLook(LockPhase.Open, OpenColor, OpenRadiusMm(ticksLeft, windowTicks), OpenAlphaPermille, enraged);
        }
        if (locksBroken > 0 && InFlash(ticksSinceBroken))
        {
            return new LockLook(LockPhase.Broken, BrokenColor, FlashRadiusMm, FlashAlpha(ticksSinceBroken), enraged);
        }
        if (lockFails > 0 && InFlash(ticksSinceFailed))
        {
            return new LockLook(LockPhase.Failed, FailedColor, FlashRadiusMm, FlashAlpha(ticksSinceFailed), enraged);
        }
        return enraged
            ? new LockLook(LockPhase.Enraged, EnrageColor, EnrageRadiusMm, EnrageAlphaPermille, true)
            : new LockLook(LockPhase.Idle, OpenColor, 0, 0, false);
    }

    /// <summary>
    /// The open ring radius: <see cref="OpenEndRadiusMm"/> + (start - end) x left / window, integer, so it shrinks
    /// linearly from <see cref="OpenStartRadiusMm"/> at the open tick to <see cref="OpenEndRadiusMm"/> at the end.
    /// </summary>
    /// <param name="ticksLeft">Ticks until the Window ends.</param>
    /// <param name="windowTicks">Window length, ticks (0 or less reads as no time left).</param>
    /// <returns>The radius, mm.</returns>
    public static int OpenRadiusMm(int ticksLeft, int windowTicks)
    {
        if (windowTicks <= 0)
        {
            return OpenEndRadiusMm;
        }
        int left = Math.Clamp(ticksLeft, 0, windowTicks);
        return OpenEndRadiusMm + (int)((long)(OpenStartRadiusMm - OpenEndRadiusMm) * left / windowTicks);
    }

    private static bool InFlash(int since) => since >= 0 && since < FlashTicks;

    private static int FlashAlpha(int since) => FlashAlphaPermille * (FlashTicks - since) / FlashTicks;
}
