namespace Keyrun.Sim.Core;

/// <summary>
/// The ONE sim facing convention (sim/combat/targeting.ts faceDir and its tables): yaw 0 faces +x, yaw 256
/// faces +z, as a Q12 (x4096) direction vector from a 128-entry cos/sin table indexed by
/// <c>((Math.round(angle / 8) % 128) + 128) % 128</c>. Shared by encounter telegraphs (this lane) and combat
/// targeting (lane 2A), so both read facing identically. The tables are the TS literal data, verbatim.
/// </summary>
public static class Facing
{
    /// <summary>Q12 scale of the facing vector (targeting.ts FACING_Q).</summary>
    public const long Q = 4096;

    /// <summary>Table entries per full turn.</summary>
    public const int TableN = 128;

    private static readonly int[] CosTable =
    {
        4096, 4091, 4076, 4052, 4017, 3973, 3920, 3857, 3784, 3703, 3612, 3513, 3406, 3290, 3166, 3035,
        2896, 2751, 2598, 2440, 2276, 2106, 1931, 1751, 1567, 1380, 1189, 995, 799, 601, 401, 201,
        0, -201, -401, -601, -799, -995, -1189, -1380, -1567, -1751, -1931, -2106, -2276, -2440, -2598, -2751,
        -2896, -3035, -3166, -3290, -3406, -3513, -3612, -3703, -3784, -3857, -3920, -3973, -4017, -4052, -4076, -4091,
        -4096, -4091, -4076, -4052, -4017, -3973, -3920, -3857, -3784, -3703, -3612, -3513, -3406, -3290, -3166, -3035,
        -2896, -2751, -2598, -2440, -2276, -2106, -1931, -1751, -1567, -1380, -1189, -995, -799, -601, -401, -201,
        0, 201, 401, 601, 799, 995, 1189, 1380, 1567, 1751, 1931, 2106, 2276, 2440, 2598, 2751,
        2896, 3035, 3166, 3290, 3406, 3513, 3612, 3703, 3784, 3857, 3920, 3973, 4017, 4052, 4076, 4091,
    };

    private static readonly int[] SinTable =
    {
        0, 201, 401, 601, 799, 995, 1189, 1380, 1567, 1751, 1931, 2106, 2276, 2440, 2598, 2751,
        2896, 3035, 3166, 3290, 3406, 3513, 3612, 3703, 3784, 3857, 3920, 3973, 4017, 4052, 4076, 4091,
        4096, 4091, 4076, 4052, 4017, 3973, 3920, 3857, 3784, 3703, 3612, 3513, 3406, 3290, 3166, 3035,
        2896, 2751, 2598, 2440, 2276, 2106, 1931, 1751, 1567, 1380, 1189, 995, 799, 601, 401, 201,
        0, -201, -401, -601, -799, -995, -1189, -1380, -1567, -1751, -1931, -2106, -2276, -2440, -2598, -2751,
        -2896, -3035, -3166, -3290, -3406, -3513, -3612, -3703, -3784, -3857, -3920, -3973, -4017, -4052, -4076, -4091,
        -4096, -4091, -4076, -4052, -4017, -3973, -3920, -3857, -3784, -3703, -3612, -3513, -3406, -3290, -3166, -3035,
        -2896, -2751, -2598, -2440, -2276, -2106, -1931, -1751, -1567, -1380, -1189, -995, -799, -601, -401, -201,
    };

    private const int Step = 1024 / TableN;

    /// <summary>
    /// targeting.ts tableIndex for an angle given as a numerator over 2 (<paramref name="twiceAngle"/> = 2 *
    /// angle), so the TS half-integer argument <c>angle1024 / 2</c> stays exact:
    /// <c>Math.round(angle / 8)</c> = RoundHalfUp(twiceAngle, 16), then the double JS <c>%</c> wrap.
    /// </summary>
    public static int TableIndexOfTwice(long twiceAngle)
    {
        var r = IntMath.RoundHalfUp(twiceAngle, 2 * Step);
        return (int)IntMath.JsRem(IntMath.JsRem(r, TableN) + TableN, TableN);
    }

    /// <summary>tableIndex(angle) for an integer angle.</summary>
    public static int TableIndex(long angle) => TableIndexOfTwice(2 * angle);

    /// <summary>tableIndex(angle1024 / 2), the half-angle index targeting.ts inCone uses (exact for odd angles).</summary>
    public static int TableIndexOfHalf(long angle1024) => TableIndexOfTwice(angle1024);

    /// <summary>cos table entry (Q12).</summary>
    public static long Cos(int index) => CosTable[index];

    /// <summary>sin table entry (Q12).</summary>
    public static long Sin(int index) => SinTable[index];

    /// <summary>targeting.ts faceDir: the Q12 facing vector of <paramref name="yaw1024"/> in the XZ plane.</summary>
    public static Dir2 FaceDir(long yaw1024)
    {
        var idx = TableIndex(yaw1024);
        return new Dir2(CosTable[idx], SinTable[idx]);
    }
}
