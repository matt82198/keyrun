using System;

namespace Keyrun.Sim.Core;

/// <summary>
/// The boundary between an untrusted JS/JSON number (its exact source text) and the integer-only sim. The TS
/// sim guards inputs with <c>Number.isInteger</c> and <c>&gt;&gt;&gt; 0</c> (ToUint32); this type reproduces those
/// decisions from the decimal text itself, exactly, without ever forming a floating-point value.
/// </summary>
/// <remarks>
/// Accepted text is the JSON number grammar <c>-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?</c>.
/// Divergence from JS, fail closed: JS rounds a decimal such as <c>1.00000000000000001</c> to the double 1 and
/// then calls it an integer; this boundary sees the exact value and rejects it. Integers beyond 2^53 - 1 in
/// magnitude are rejected (JS would accept a rounded double there; no sim value is that large).
/// </remarks>
public static class JsNumber
{
    private const int MaxDigits = 18;

    /// <summary>Parses exact JSON number text into sign, significant digits and a power-of-ten exponent.</summary>
    private static bool TryDecompose(string raw, out bool negative, out string digits, out long exponent)
    {
        negative = false;
        digits = string.Empty;
        exponent = 0;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }
        var i = 0;
        if (raw[i] == '-')
        {
            negative = true;
            i++;
        }
        var intStart = i;
        while (i < raw.Length && raw[i] >= '0' && raw[i] <= '9')
        {
            i++;
        }
        var intPart = raw[intStart..i];
        if (intPart.Length == 0 || (intPart.Length > 1 && intPart[0] == '0'))
        {
            return false;
        }
        var fracPart = string.Empty;
        if (i < raw.Length && raw[i] == '.')
        {
            i++;
            var fracStart = i;
            while (i < raw.Length && raw[i] >= '0' && raw[i] <= '9')
            {
                i++;
            }
            fracPart = raw[fracStart..i];
            if (fracPart.Length == 0)
            {
                return false;
            }
        }
        long exp = 0;
        if (i < raw.Length && (raw[i] == 'e' || raw[i] == 'E'))
        {
            i++;
            var expNegative = false;
            if (i < raw.Length && (raw[i] == '+' || raw[i] == '-'))
            {
                expNegative = raw[i] == '-';
                i++;
            }
            var expStart = i;
            while (i < raw.Length && raw[i] >= '0' && raw[i] <= '9')
            {
                if (exp < 100000)
                {
                    exp = (exp * 10) + (raw[i] - '0');
                }
                i++;
            }
            if (i == expStart)
            {
                return false;
            }
            if (expNegative)
            {
                exp = -exp;
            }
        }
        if (i != raw.Length)
        {
            return false;
        }
        var all = (intPart + fracPart).TrimStart('0');
        exponent = exp - fracPart.Length;
        // Strip trailing zeros into the exponent so "digits" is the exact significand.
        var end = all.Length;
        while (end > 0 && all[end - 1] == '0')
        {
            end--;
            exponent++;
        }
        digits = all[..end];
        return true;
    }

    private static long Pow10(long e)
    {
        long r = 1;
        for (var k = 0L; k < e; k++)
        {
            r *= 10;
        }
        return r;
    }

    private static long DigitsValue(string digits)
    {
        long v = 0;
        foreach (var c in digits)
        {
            v = (v * 10) + (c - '0');
        }
        return v;
    }

    /// <summary>
    /// <c>Number.isInteger</c> plus the value: true and the integer when <paramref name="raw"/> denotes an exact
    /// integer of magnitude at most 2^53 - 1; false for fractions, malformed text, or anything larger.
    /// </summary>
    public static bool TryParseInteger(string raw, out long value)
    {
        value = 0;
        if (!TryDecompose(raw, out var negative, out var digits, out var exponent))
        {
            return false;
        }
        if (digits.Length == 0)
        {
            return true; // zero in any spelling (0, -0, 0.000, 0e9) is the integer 0
        }
        if (exponent < 0 || digits.Length + exponent > MaxDigits)
        {
            return false;
        }
        var magnitude = DigitsValue(digits) * Pow10(exponent);
        if (magnitude > IntMath.MaxSafeInteger)
        {
            return false;
        }
        value = negative ? -magnitude : magnitude;
        return true;
    }

    /// <summary>The integer <paramref name="raw"/> denotes; throws where the TS integer guards throw.</summary>
    /// <exception cref="ArgumentException">Not an exact integer within 2^53 - 1.</exception>
    public static long ToInteger(string raw)
    {
        if (!TryParseInteger(raw, out var value))
        {
            throw new ArgumentException($"expected an integer, got {raw}", nameof(raw));
        }
        return value;
    }

    /// <summary>
    /// JS ToUint32 (<c>x &gt;&gt;&gt; 0</c>): truncate toward zero, then wrap modulo 2^32 (so -1 is 4294967295,
    /// 1.5 is 1, -1.5 is 4294967295, 2^32 is 0).
    /// </summary>
    /// <exception cref="ArgumentException">Malformed text, or a magnitude beyond 2^53 - 1 (fail closed).</exception>
    public static uint ToUint32(string raw)
    {
        if (!TryDecompose(raw, out var negative, out var digits, out var exponent))
        {
            throw new ArgumentException($"not a JSON number: {raw}", nameof(raw));
        }
        long truncated;
        if (digits.Length == 0)
        {
            truncated = 0;
        }
        else if (exponent >= 0)
        {
            if (digits.Length + exponent > MaxDigits)
            {
                throw new ArgumentException($"number too large for an exact ToUint32: {raw}", nameof(raw));
            }
            truncated = DigitsValue(digits) * Pow10(exponent);
        }
        else
        {
            var keep = digits.Length + exponent; // digits left of the decimal point
            if (keep > MaxDigits)
            {
                throw new ArgumentException($"number too large for an exact ToUint32: {raw}", nameof(raw));
            }
            truncated = keep <= 0 ? 0 : DigitsValue(digits[..(int)keep]);
        }
        if (truncated > IntMath.MaxSafeInteger)
        {
            throw new ArgumentException($"number too large for an exact ToUint32: {raw}", nameof(raw));
        }
        var signed = negative ? -truncated : truncated;
        return unchecked((uint)signed);
    }
}
