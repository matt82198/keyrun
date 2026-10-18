using System;
using System.Globalization;

namespace Keyrun.Sim.Core;

/// <summary>
/// FNV-1a 32 (ORACLE.md section 2): offset basis 0x811c9dc5, prime 0x01000193, <c>h = (h ^ b) * prime mod 2^32</c>
/// per byte, rendered as 8 lowercase hex digits. Text input must be ASCII (the oracle format is ASCII-only).
/// </summary>
public static class Fnv1a
{
    /// <summary>The offset basis.</summary>
    public const uint OffsetBasis = 0x811c9dc5u;

    /// <summary>The FNV prime.</summary>
    public const uint Prime = 0x01000193u;

    /// <summary>Continues a hash over ASCII text.</summary>
    /// <exception cref="ArgumentException">A character above 0x7f.</exception>
    public static uint Append(uint hash, string ascii)
    {
        unchecked
        {
            for (var i = 0; i < ascii.Length; i++)
            {
                var c = ascii[i];
                if (c > 0x7f)
                {
                    throw new ArgumentException($"fnv1a32: non-ASCII code unit 0x{(int)c:x} at {i}", nameof(ascii));
                }
                hash = (hash ^ c) * Prime;
            }
            return hash;
        }
    }

    /// <summary>Continues a hash over raw bytes.</summary>
    public static uint Append(uint hash, ReadOnlySpan<byte> bytes)
    {
        unchecked
        {
            foreach (var b in bytes)
            {
                hash = (hash ^ b) * Prime;
            }
            return hash;
        }
    }

    /// <summary>FNV-1a 32 of ASCII text.</summary>
    public static uint Hash(string ascii) => Append(OffsetBasis, ascii);

    /// <summary>FNV-1a 32 of raw bytes.</summary>
    public static uint Hash(ReadOnlySpan<byte> bytes) => Append(OffsetBasis, bytes);

    /// <summary>8 lowercase hex digits, zero-padded.</summary>
    public static string Hex(uint hash) => hash.ToString("x8", CultureInfo.InvariantCulture);
}
