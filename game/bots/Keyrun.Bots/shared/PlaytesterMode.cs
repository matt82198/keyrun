using System;

namespace Keyrun.Bots.Shared;

/// <summary>
/// The playtester mode of a bot run (design/PLAYTESTER-GUIDE.md section 1, "The rule, mechanical"; G2.4, K3a). A guided run
/// plays a per-class guide and is the only kind that judges balance, pacing and feel. A blind run learns from its own
/// history and reports only time-to-learn. Every run Summary and every K2 receipt carries its mode.
/// </summary>
public enum PlaytesterMode
{
    /// <summary>
    /// The guided mode. The default: a plan with no <c>mode</c> field, and a host run with no plan. Every plan written
    /// before the field existed is a guide plan, so guided is the mode that reproduces today's behaviour and today's rows.
    /// </summary>
    Guided,

    /// <summary>The blind mode: the learner's own plan, judged only on time-to-learn; never a balance verdict.</summary>
    Blind,
}

/// <summary>The wire names of <see cref="PlaytesterMode"/> as they appear in plans, summaries and receipts.</summary>
public static class PlaytesterModeWire
{
    /// <summary>The wire name of <see cref="PlaytesterMode.Guided"/>.</summary>
    public const string Guided = "guided";

    /// <summary>The wire name of <see cref="PlaytesterMode.Blind"/>.</summary>
    public const string Blind = "blind";

    /// <summary>The lowercase wire name of a mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>"guided" or "blind".</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a known mode.</exception>
    public static string Wire(this PlaytesterMode mode) => mode switch
    {
        PlaytesterMode.Guided => Guided,
        PlaytesterMode.Blind => Blind,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown playtester mode"),
    };

    /// <summary>Reads a wire name exactly: "guided" or "blind", lowercase, nothing else.</summary>
    /// <param name="text">The wire name.</param>
    /// <param name="mode">The mode when the name is known; otherwise Guided.</param>
    /// <returns>True when the name is one of the two modes.</returns>
    public static bool TryParse(string? text, out PlaytesterMode mode)
    {
        switch (text)
        {
            case Guided:
                mode = PlaytesterMode.Guided;
                return true;
            case Blind:
                mode = PlaytesterMode.Blind;
                return true;
            default:
                mode = PlaytesterMode.Guided;
                return false;
        }
    }
}
