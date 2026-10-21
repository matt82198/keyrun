using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// The user arguments (after Godot's <c>--</c>) that the arena's ArenaOptions.Parse does not read itself, and the rule
/// for everything else: it stops the launch. Godot-free so xUnit can pin the rule.
/// </summary>
/// <remarks>
/// CARELESS-SEAT-ARGS (PR #308 run C): run C launched with <c>--careless-seat 3@300:pair</c> on a Keyrun.dll built
/// before that flag existed. Parse then ignored unknown arguments, so the run played a normal party and printed neither
/// the BOTS careless line nor an error. Every argument Parse's switch does not match comes here: a flag another reader
/// owns is stepped over with its value, and anything else (a stale build, a typo, a stray value) throws, which SimDriver
/// prints as <c>ARENA error</c> before it exits 1.
/// </remarks>
public static class ForeignUserArgs
{
    /// <summary>Main.cs: boot, print the SMOKE line, quit; no arena.</summary>
    public const string SmokeFlag = "--smoke";

    /// <summary>Main.cs: <c>--view-size WxH</c> sizes the root viewport.</summary>
    public const string ViewSizeFlag = "--view-size";

    /// <summary>The flags other readers own, and whether each takes a value.</summary>
    private static readonly IReadOnlyDictionary<string, bool> Owned = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        [SmokeFlag] = false,
        [ViewSizeFlag] = true,
        [ProgressionSavePlace.DirFlag] = true,
    };

    /// <summary>Steps over the foreign argument at <paramref name="i"/>, or throws.</summary>
    /// <param name="args">The user arguments.</param>
    /// <param name="i">The index of an argument the arena did not recognise; left on the last argument consumed. A
    /// value flag consumes the next argument unless it is missing or another flag, as its owner reads it.</param>
    /// <exception cref="ArgumentException">No reader owns the argument.</exception>
    public static void Skip(IReadOnlyList<string> args, ref int i)
    {
        ArgumentNullException.ThrowIfNull(args);
        var arg = args[i];
        if (!Owned.TryGetValue(arg, out var takesValue))
        {
            throw new ArgumentException(
                $"unknown user argument '{arg}': this build of the game does not read it (a stale build, a typo, or a split value)");
        }
        if (takesValue && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            i++;
        }
    }
}
