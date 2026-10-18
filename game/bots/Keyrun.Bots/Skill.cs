using System;

namespace Keyrun.Bots;

/// <summary>
/// The bot-side xorshift128 state (bots/src/skill.ts PrngState, four uint32 words). Separate from the sim's
/// own <c>Keyrun.Sim.Core.PrngState</c>: bots draw only from this one, threaded through runBots.
/// </summary>
/// <param name="S0">x.</param>
/// <param name="S1">y.</param>
/// <param name="S2">z.</param>
/// <param name="S3">w.</param>
public readonly record struct BotPrngState(uint S0, uint S1, uint S2, uint S3);

/// <summary>One stepPrng draw: a double in [0, 1] and the advanced state.</summary>
/// <param name="Value">w / 0xffffffff as an IEEE double.</param>
/// <param name="Next">Advanced state.</param>
public readonly record struct PrngStep(double Value, BotPrngState Next);

/// <summary>One rollMistake: whether a mistake happens and the advanced state.</summary>
/// <param name="Mistake">True when the draw fell under <see cref="Skill.MistakeChance"/>.</param>
/// <param name="Next">Advanced state.</param>
public readonly record struct MistakeRoll(bool Mistake, BotPrngState Next);

/// <summary>
/// Port of bots/src/skill.ts: the 0-100 skill dial. It maps to a deterministic reaction delay (a pure function
/// of the dial) and a mistake chance sampled from a local xorshift128 step. The float operations are kept as
/// IEEE doubles in the TS order, because the mistake decision compares a double draw against a double chance.
/// </summary>
public static class Skill
{
    private const double MinDelayTicks = 1;
    private const double MaxDelayTicks = 10;
    private const double MaxMistakeChance = 0.35;

    private static double ClampDial(double dial) => Math.Max(0, Math.Min(100, dial));

    /// <summary>JS Math.round: the nearest integer, ties toward +infinity.</summary>
    private static double JsRound(double x)
    {
        var floor = Math.Floor(x);
        return x - floor >= 0.5 ? floor + 1 : floor;
    }

    /// <summary>skill.ts reactionDelayTicks: dial 100 reacts in 1 tick, dial 0 in 10. Pure.</summary>
    /// <param name="dial">Skill dial; clamped to 0..100.</param>
    /// <returns>Delay in ticks.</returns>
    public static int ReactionDelayTicks(double dial)
    {
        var d = ClampDial(dial);
        var span = MaxDelayTicks - MinDelayTicks;
        return (int)JsRound(MaxDelayTicks - ((span * d) / 100));
    }

    /// <summary>skill.ts mistakeChance: <c>0.35 * (1 - d / 100)</c>, so 0 at dial 100.</summary>
    /// <param name="dial">Skill dial; clamped to 0..100.</param>
    /// <returns>Probability in [0, 0.35].</returns>
    public static double MistakeChance(double dial)
    {
        var d = ClampDial(dial);
        return MaxMistakeChance * (1 - (d / 100));
    }

    /// <summary>
    /// skill.ts stepPrng: one xorshift128 step. The TS mixes int32 <c>&lt;&lt;</c>/<c>^</c> with uint32 <c>&gt;&gt;&gt;</c>;
    /// every result is reduced with <c>&gt;&gt;&gt; 0</c>, so the bits equal plain uint32 arithmetic.
    /// </summary>
    /// <param name="state">Current state.</param>
    /// <returns>The draw <c>w / 0xffffffff</c> and the next state.</returns>
    public static PrngStep StepPrng(BotPrngState state)
    {
        var x = state.S0;
        var t = x ^ (x << 11);
        var w = state.S3;
        w = (w ^ (w >> 19)) ^ (t ^ (t >> 8));
        var next = new BotPrngState(state.S1, state.S2, state.S3, w);
        return new PrngStep(w / 4294967295.0, next);
    }

    /// <summary>
    /// skill.ts seedPrng: <c>[s, (s * 2654435761) &gt;&gt;&gt; 0 || 1, (s ^ 0x9e3779b9) &gt;&gt;&gt; 0 || 1, (s * 40503) &gt;&gt;&gt; 0 || 1]</c>
    /// with <c>s = seed &gt;&gt;&gt; 0 || 1</c>. The second word is multiplied in DOUBLES, as the TS does: above 2^53 the
    /// product rounds before the uint32 reduction (game/sim/ORACLE.md section 8, botprng.vectors.json).
    /// </summary>
    /// <param name="seed">The seed as a JS number.</param>
    /// <returns>The initial state.</returns>
    public static BotPrngState SeedPrng(double seed)
    {
        var s = ToUint32(seed);
        if (s == 0)
        {
            s = 1;
        }
        var s1 = ToUint32((double)s * 2654435761.0);
        var s2 = s ^ 0x9e3779b9u;
        var s3 = ToUint32((double)s * 40503.0);
        return new BotPrngState(s, s1 == 0 ? 1 : s1, s2 == 0 ? 1 : s2, s3 == 0 ? 1 : s3);
    }

    /// <summary>skill.ts rollMistake: one draw, a mistake when it falls under <see cref="MistakeChance"/>.</summary>
    /// <param name="dial">Skill dial.</param>
    /// <param name="prng">Current state.</param>
    /// <returns>The roll and the next state.</returns>
    public static MistakeRoll RollMistake(double dial, BotPrngState prng)
    {
        var step = StepPrng(prng);
        return new MistakeRoll(step.Value < MistakeChance(dial), step.Next);
    }

    /// <summary>ECMAScript ToUint32: NaN and infinities give 0; otherwise truncate toward zero, then modulo 2^32.</summary>
    private static uint ToUint32(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n))
        {
            return 0;
        }
        var t = Math.Truncate(n);
        var m = t - (Math.Floor(t / 4294967296.0) * 4294967296.0);
        return (uint)m;
    }
}
