namespace Keyrun.Sim.Core;

/// <summary>
/// An integer position or offset in millimetres (sim/core/state.ts Vec3): x and z span the flat arena floor,
/// y is up. Components are <c>long</c> so squared distances and Q12 (x4096) dot products never overflow.
/// </summary>
/// <param name="X">East-west, mm.</param>
/// <param name="Y">Up, mm.</param>
/// <param name="Z">North-south, mm.</param>
public readonly record struct Vec3(long X, long Y, long Z)
{
    /// <summary>The origin.</summary>
    public static readonly Vec3 Zero = new(0, 0, 0);
}

/// <summary>A 2D direction in the XZ plane, as carried by move and dash commands (each component -1000..1000).</summary>
/// <param name="X">X component.</param>
/// <param name="Z">Z component.</param>
public readonly record struct Dir2(long X, long Z);
