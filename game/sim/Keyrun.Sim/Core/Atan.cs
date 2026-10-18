using System;

namespace Keyrun.Sim.Core;

/// <summary>
/// Port of sim/core/atan.ts: integer-only atan2-style yaw. Octant-reduces (dx, dz) onto [0, 45 degrees],
/// looks up atan(i/256) in a 257-entry fixed-point table (yaw units x 4096) with integer linear
/// interpolation, then mirrors back to the true octant. The table is the TS literal data, copied verbatim.
/// </summary>
/// <remarks>Pinned bit-exactly by atan.vectors.json (yawFromComponents) and fixed.vectors.json (yawBetween).</remarks>
public static class Atan
{
    private const long SubScale = 4096;
    private const long Quarter = 256;
    private const long YawMax = 1024;

    // atan(i/256) for i = 0..256 in yaw units * SubScale (sim/core/atan.ts ATAN_TABLE, verbatim).
    private static readonly int[] Table =
    {
        0, 2608, 5215, 7822, 10430, 13036, 15643, 18249, 20854, 23459, 26063, 28666, 31268, 33870, 36470, 39069,
        41667, 44264, 46860, 49454, 52046, 54637, 57226, 59814, 62400, 64984, 67566, 70146, 72724, 75299, 77873, 80444,
        83012, 85579, 88142, 90703, 93262, 95817, 98370, 100920, 103467, 106011, 108552, 111090, 113624, 116155, 118683, 121207,
        123728, 126245, 128759, 131269, 133775, 136277, 138776, 141270, 143761, 146247, 148730, 151208, 153682, 156151, 158617, 161078,
        163534, 165986, 168433, 170876, 173314, 175748, 178176, 180600, 183019, 185433, 187842, 190246, 192645, 195039, 197427, 199811,
        202189, 204562, 206930, 209292, 211649, 214000, 216346, 218686, 221021, 223350, 225674, 227992, 230304, 232611, 234911, 237206,
        239495, 241778, 244056, 246327, 248592, 250852, 253105, 255352, 257594, 259829, 262058, 264281, 266498, 268708, 270912, 273110,
        275302, 277488, 279667, 281840, 284006, 286167, 288321, 290468, 292609, 294744, 296872, 298994, 301109, 303218, 305320, 307416,
        309505, 311588, 313664, 315734, 317797, 319854, 321904, 323948, 325985, 328015, 330039, 332056, 334066, 336070, 338068, 340058,
        342043, 344020, 345991, 347955, 349913, 351864, 353809, 355746, 357678, 359602, 361520, 363432, 365337, 367235, 369126, 371011,
        372890, 374762, 376627, 378486, 380338, 382183, 384022, 385855, 387681, 389500, 391313, 393119, 394919, 396712, 398499, 400280,
        402053, 403821, 405582, 407336, 409085, 410826, 412562, 414291, 416013, 417729, 419439, 421143, 422840, 424531, 426215, 427893,
        429565, 431231, 432891, 434544, 436191, 437832, 439466, 441095, 442717, 444334, 445944, 447548, 449145, 450737, 452323, 453903,
        455476, 457044, 458606, 460162, 461711, 463255, 464793, 466325, 467851, 469372, 470886, 472395, 473897, 475394, 476886, 478371,
        479851, 481325, 482793, 484256, 485713, 487164, 488610, 490050, 491484, 492913, 494337, 495755, 497167, 498574, 499975, 501371,
        502762, 504147, 505527, 506901, 508270, 509634, 510992, 512345, 513693, 515036, 516373, 517705, 519032, 520354, 521670, 522982,
        524288,
    };

    private static long TableAt(long i)
    {
        if (i < 0 || i >= Table.Length)
        {
            throw new InvalidOperationException($"atan: table index {i} out of range");
        }
        return Table[i];
    }

    /// <summary>Non-negative n/d rounded to nearest, ties away from zero; requires d &gt; 0 (atan.ts roundDivNonNeg).</summary>
    private static long RoundDivNonNeg(long n, long d) => (n + (d / 2)) / d;

    /// <summary>atan(numerator/denominator) in yaw units * SubScale; requires 0 &lt;= numerator &lt;= denominator, denominator &gt; 0.</summary>
    private static long LookupScaled(long numerator, long denominator)
    {
        var scaledIndex = numerator * 256;
        var idx = scaledIndex / denominator;
        if (idx >= 256)
        {
            return TableAt(256);
        }
        var rem = scaledIndex - (idx * denominator);
        var lo = TableAt(idx);
        if (rem == 0)
        {
            return lo;
        }
        var hi = TableAt(idx + 1);
        return lo + RoundDivNonNeg((hi - lo) * rem, denominator);
    }

    /// <summary>
    /// Yaw (0..1023) of the direction (dx, dz): 0 = +x, 256 = +z (atan2(dz, dx) mapped onto [0, 1024)).
    /// The zero vector returns 0.
    /// </summary>
    public static int YawFromComponents(long dx, long dz)
    {
        if (dx == 0 && dz == 0)
        {
            return 0;
        }
        var ax = Math.Abs(dx);
        var az = Math.Abs(dz);
        var unsignedScaled = az > ax
            ? (Quarter * SubScale) - LookupScaled(ax, az)
            : LookupScaled(az, ax);

        long resultScaled;
        if (dx >= 0 && dz >= 0)
        {
            resultScaled = unsignedScaled;
        }
        else if (dx < 0 && dz >= 0)
        {
            resultScaled = (2 * Quarter * SubScale) - unsignedScaled;
        }
        else if (dx < 0 && dz < 0)
        {
            resultScaled = (2 * Quarter * SubScale) + unsignedScaled;
        }
        else
        {
            resultScaled = (4 * Quarter * SubScale) - unsignedScaled;
        }
        return (int)(RoundDivNonNeg(resultScaled, SubScale) % YawMax);
    }
}
