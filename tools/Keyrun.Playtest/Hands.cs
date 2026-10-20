using System;

namespace Keyrun.Playtest;

/// <summary>
/// A seat's hands: human-like execution of a correct plan. Every action waits a seeded reaction delay, and every
/// action has a seeded chance to be fumbled (late: tried again after another reaction). Integers only, deterministic
/// per (run seed, actor id), so the same seed and policy always play the same run.
/// </summary>
public sealed class Hands
{
    /// <summary>Shortest reaction to a new rotation decision, ticks (0.3 s at 20 Hz).</summary>
    public const int ReactBaseTicks = 6;

    /// <summary>Reaction spread above the base, ticks: a decision waits base + [0, spread).</summary>
    public const int ReactSpreadTicks = 8;

    /// <summary>Shortest reaction from seeing an enemy cast to pressing the kick, ticks (0.4 s).</summary>
    public const int KickReactBaseTicks = 8;

    /// <summary>Kick reaction spread, ticks (so 0.4 to 1.0 s after the bar appears).</summary>
    public const int KickReactSpreadTicks = 12;

    /// <summary>Chance per mille that one rotation press is fumbled.</summary>
    public const int MissPermille = 80;

    /// <summary>Chance per mille that a kick press is fumbled (late).</summary>
    public const int KickMissPermille = 150;

    private ulong _state;

    /// <summary>Hands for <paramref name="actorId"/> in a run seeded <paramref name="seed"/>.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="actorId">The seat.</param>
    public Hands(long seed, int actorId)
    {
        _state = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + ((ulong)(uint)actorId << 32) + 0xD1B54A32D192ED03UL);
    }

    /// <summary>SplitMix64: the next 64 random bits.</summary>
    /// <returns>Bits.</returns>
    public ulong NextBits()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A uniform integer in [0, <paramref name="n"/>).</summary>
    /// <param name="n">Bound, at least 1.</param>
    /// <returns>The integer.</returns>
    public int Next(int n)
    {
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "bound must be at least 1");
        }
        return (int)(NextBits() % (ulong)n);
    }

    /// <summary>True with probability <paramref name="permille"/>/1000.</summary>
    /// <param name="permille">0-1000.</param>
    /// <returns>The roll.</returns>
    public bool Roll(int permille) => Next(1000) < permille;

    /// <summary>A rotation reaction delay, ticks.</summary>
    /// <returns>Ticks.</returns>
    public int React() => ReactBaseTicks + Next(ReactSpreadTicks);

    /// <summary>A kick reaction delay, ticks.</summary>
    /// <returns>Ticks.</returns>
    public int KickReact() => KickReactBaseTicks + Next(KickReactSpreadTicks);
}
