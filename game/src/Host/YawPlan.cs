using System;
using System.Globalization;

namespace Keyrun.Host;

/// <summary>A scripted camera turn (<c>--press-yaw T:Y</c>, lane 3E-c): at run tick <paramref name="Tick"/> the CameraRig
/// turns to <paramref name="Yaw"/> radians.</summary>
/// <param name="Tick">The run tick (pre-step) at which the camera turns, 0 or more.</param>
/// <param name="Yaw">The heading to turn to, radians (finite; the rig wraps it into [-Pi, Pi]).</param>
public sealed record YawPlan(int Tick, float Yaw)
{
    /// <summary>Parses <c>T:Y</c> (T a tick of 0 or more, Y a finite invariant-culture number of radians).</summary>
    /// <param name="raw">The flag value.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">Not of the form T:Y.</exception>
    public static YawPlan Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var parts = raw.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tick) ||
            !float.TryParse(parts[1], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var yaw) ||
            !float.IsFinite(yaw))
        {
            throw new ArgumentException($"--press-yaw needs tick:radians, got '{raw}'");
        }
        return new YawPlan(tick, yaw);
    }
}
