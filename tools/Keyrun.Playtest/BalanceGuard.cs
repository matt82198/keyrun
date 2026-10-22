using System;
using Keyrun.Bots.Shared;

namespace Keyrun.Playtest;

/// <summary>
/// The one guard every balance line passes before it is printed (design/PLAYTESTER-GUIDE.md section 1, "The rule,
/// mechanical"; G2.4, K3a). Balance, pacing, par and boss-length verdicts read only guided rows; a blind row is refused
/// with the rule and the file named, so a blind learner's cheese can never become a balance number. Fails closed.
/// </summary>
public static class BalanceGuard
{
    /// <summary>The rule a refused balance line cites.</summary>
    public const string Rule = "design/PLAYTESTER-GUIDE.md section 1 \"The rule, mechanical\"";

    /// <summary>Throws unless <paramref name="mode"/> is guided: the only mode whose rows may print a balance line.</summary>
    /// <param name="mode">The row's playtester mode.</param>
    /// <param name="line">The balance line the caller wants to print, named in the refusal.</param>
    /// <exception cref="InvalidOperationException">The row is blind: a blind row prints no balance line.</exception>
    public static void RequireGuided(PlaytesterMode mode, string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (mode != PlaytesterMode.Guided)
        {
            throw new InvalidOperationException(
                $"refused: a {mode.Wire()} row cannot print a balance line ({line}). Only guided rows judge balance, pacing and feel; see {Rule}, and the playtest report script refuses balance lines from blind rows.");
        }
    }
}
