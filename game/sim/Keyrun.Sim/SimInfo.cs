namespace Keyrun.Sim;

/// <summary>
/// Identity of the deterministic sim assembly. The boot scene reads it so the
/// headless smoke proves the engine host actually loads Keyrun.Sim.
/// </summary>
/// <remarks>
/// Everything in this assembly obeys the purity rules checked by
/// <c>Keyrun.Sim.Tests.PurityTests</c>: integers only, no clocks, no
/// <c>System.Random</c>, no Godot types.
/// </remarks>
public static class SimInfo
{
    /// <summary>The assembly name the host expects to load.</summary>
    public const string Name = "Keyrun.Sim";
}
