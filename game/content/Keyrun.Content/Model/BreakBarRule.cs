using System;

namespace Keyrun.Content;

/// <summary>What the boss frame's Break bar is showing (BREAK-BAR).</summary>
public enum BreakBarPhase
{
    /// <summary>The bar is live: it fills as interrupts and controls on the Twins drain the sim bar.</summary>
    Filling,

    /// <summary>The sim bar hit 0: the Twins are staggered; the bar is full until the sim refills it.</summary>
    Broken,
}

/// <summary>One frame of the boss frame's Break bar.</summary>
/// <param name="Phase">Filling or broken.</param>
/// <param name="FillPermille">Break progress, 0..1000: <c>(max - current) * 1000 / max</c>, 1000 while broken.</param>
/// <param name="Color">Fill colour, 0xRRGGBB.</param>
/// <param name="FlashAlphaPermille">Opacity of the white break flash over the bar, 0..1000 (0 = not drawn).</param>
/// <param name="LabelColor">Label text colour, 0xRRGGBB: white over the dark unfilled bar while filling, dark over the bright full bar while broken.</param>
public sealed record BreakBarLook(BreakBarPhase Phase, int FillPermille, int Color, int FlashAlphaPermille, int LabelColor);

/// <summary>
/// The view-only rule for the Twins' shared Break bar on the boss unit frame (BIBLE s9 mechanic 2; BREAK-BAR,
/// HUD-DECLUTTER: on the boss frame, never over a model). Pure and integer-only (game/CLAUDE.md purity rule):
/// src/Hud/BreakBar.cs feeds it the sim's <c>BreakBarState</c> each frame. The sim bar drains from max to 0, so the
/// frame shows the party's break progress filling up; the tick it empties the bar is full and flashes, it stays full
/// while the Twins are staggered, and it is empty again the tick the sim refills it.
/// </summary>
public static class BreakBarRule
{
    /// <summary>Filling colour: amber, the interrupt/kick colour of cast-bar frames.</summary>
    public const int FillColor = 0xF0A030;

    /// <summary>Broken colour: bright gold.</summary>
    public const int BrokenColor = 0xFFE066;

    /// <summary>Label colour while filling: white (it sits over the dark unfilled part of the bar).</summary>
    public const int FillLabelColor = 0xFFFFFF;

    /// <summary>Label colour while broken: near-black, so "BROKEN" reads over the full gold bar and the white flash.</summary>
    public const int BrokenLabelColor = 0x1A1A1A;

    /// <summary>Perceived brightness of a 0xRRGGBB colour, 0..255 (integer Rec. 601 weights 299/587/114).</summary>
    /// <param name="rgb">The colour.</param>
    /// <returns>The luma.</returns>
    public static int Luma(int rgb) => ((((rgb >> 16) & 0xff) * 299) + (((rgb >> 8) & 0xff) * 587) + ((rgb & 0xff) * 114)) / 1000;

    /// <summary>How long the break flash lasts, ticks (1 s at 20 Hz).</summary>
    public const int FlashTicks = 20;

    /// <summary>Flash opacity on the break tick, permille; it falls linearly to nothing over <see cref="FlashTicks"/>.</summary>
    public const int FlashAlphaPermille = 900;

    /// <summary>The Break bar for one frame.</summary>
    /// <param name="current">Sim <c>BreakBarState.Current</c> (remaining bar; 0 or less is staggered).</param>
    /// <param name="max">Sim <c>BreakBarState.Max</c>.</param>
    /// <param name="cooldownReadyTick">Sim <c>BreakBarState.CooldownReadyTick</c> (the refill tick while staggered).</param>
    /// <param name="tick">The current sim tick.</param>
    /// <param name="resetTicks">The sim's stagger length (<c>BreakBar.ResetCooldownTicks</c>, 200): the break tick is
    /// <paramref name="cooldownReadyTick"/> minus this.</param>
    /// <returns>The look.</returns>
    public static BreakBarLook For(long current, long max, int cooldownReadyTick, int tick, int resetTicks)
    {
        if (max <= 0)
        {
            return new BreakBarLook(BreakBarPhase.Filling, 0, FillColor, 0, FillLabelColor);
        }
        if (current <= 0)
        {
            int since = tick - (cooldownReadyTick - resetTicks);
            int flash = since >= 0 && since < FlashTicks ? FlashAlphaPermille * (FlashTicks - since) / FlashTicks : 0;
            return new BreakBarLook(BreakBarPhase.Broken, 1000, BrokenColor, flash, BrokenLabelColor);
        }
        long left = Math.Min(current, max);
        int fill = (int)((max - left) * 1000 / max);
        return new BreakBarLook(BreakBarPhase.Filling, fill, FillColor, 0, FillLabelColor);
    }
}
