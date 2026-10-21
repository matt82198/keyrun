using System.Linq;

namespace Keyrun.Content;

/// <summary>One enemy's forces value (BIBLE section 9.2): integer points, 1000 = 100%.</summary>
public sealed record ForcesValue
{
    /// <summary>Enemy id.</summary>
    public required string EnemyId { get; init; }

    /// <summary>Points credited on the enemy's death tick; never negative.</summary>
    public required int Points { get; init; }
}

/// <summary>One branch of the route (BIBLE section 9.1): the rooms it owns between the fork and the rejoin.</summary>
public sealed record BranchDef
{
    /// <summary>Branch id, e.g. <c>west</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Room ids in walking order. A room belongs to exactly one branch.</summary>
    public required ValueList<string> Rooms { get; init; }
}

/// <summary>
/// The route shape (BIBLE section 9.1): the teaching pulls stay the critical path; after
/// <see cref="ForkAfterPull"/> the party takes one <see cref="Branches"/> entry, and every branch rejoins at
/// <see cref="RejoinPull"/>, so every route plays every teaching pull.
/// </summary>
public sealed record RouteDef
{
    /// <summary>The dungeon whose pulls are the critical path.</summary>
    public required string DungeonId { get; init; }

    /// <summary>The teaching pull after which the path forks.</summary>
    public required string ForkAfterPull { get; init; }

    /// <summary>The teaching pull where every branch rejoins (the rejoin point).</summary>
    public required string RejoinPull { get; init; }

    /// <summary>The branches; empty until PACK-CONTENT ships the packs that make each one reach the bar.</summary>
    public required ValueList<BranchDef> Branches { get; init; }
}

/// <summary>
/// forces.json (C#-only, game/content/data): the Forces bar's size, every enemy's point value and the route.
/// An enemy with no entry is worth 0 (the boss, the Twins, Echo adds).
/// </summary>
public sealed record ForcesTable
{
    /// <summary>Points that fill the bar (100%); the Hollow Door needs at least this many.</summary>
    public required int Required { get; init; }

    /// <summary>
    /// BOSS-DOOR (BIBLE s9.5, ruling R3): the share of the bar the Hollow Door needs, in basis points (10000 = 100%). An
    /// absent field is 10000, the BIBLE's full bar, so the door fails closed to 100%. The shipped value is the teaching
    /// pulls' share; optional packs raise it as they land.
    /// </summary>
    public int BossDoorRequiredBp { get; init; } = 10000;

    /// <summary>Per-enemy values.</summary>
    public required ValueList<ForcesValue> Values { get; init; }

    /// <summary>Fork, rejoin and branches.</summary>
    public required RouteDef Route { get; init; }

    /// <summary>True when <paramref name="enemyId"/> has a value entry.</summary>
    /// <param name="enemyId">Enemy id.</param>
    public bool Has(string enemyId) => Values.Any(v => v.EnemyId == enemyId);

    /// <summary>The enemy's points, or 0 when it has no entry.</summary>
    /// <param name="enemyId">Enemy id.</param>
    public int PointsOf(string enemyId) => Values.FirstOrDefault(v => v.EnemyId == enemyId)?.Points ?? 0;
}

/// <summary>Forces arithmetic over loaded content (BIBLE section 9.2). Pure integer sums; no sim state.</summary>
public static class RouteForces
{
    /// <summary>A teaching pull's points: sum of count x value over its groups.</summary>
    /// <param name="forces">The forces table.</param>
    /// <param name="pull">The pull.</param>
    public static int PullPoints(ForcesTable forces, PullDef pull)
    {
        System.ArgumentNullException.ThrowIfNull(forces);
        System.ArgumentNullException.ThrowIfNull(pull);
        return pull.Enemies.Sum(e => e.Count * forces.PointsOf(e.EnemyId));
    }

    /// <summary>An optional pack's points.</summary>
    /// <param name="forces">The forces table.</param>
    /// <param name="pack">The pack.</param>
    public static int PackPoints(ForcesTable forces, PackDef pack)
    {
        System.ArgumentNullException.ThrowIfNull(forces);
        System.ArgumentNullException.ThrowIfNull(pack);
        return pack.Enemies.Sum(e => e.Count * forces.PointsOf(e.EnemyId));
    }

    /// <summary>A layout pack's points (SKIPPABLE-PACKS): sum of count x value over its groups.</summary>
    /// <param name="forces">The forces table.</param>
    /// <param name="pack">The pack.</param>
    public static int LayoutPackPoints(ForcesTable forces, LayoutPack pack)
    {
        System.ArgumentNullException.ThrowIfNull(forces);
        System.ArgumentNullException.ThrowIfNull(pack);
        return pack.Enemies.Sum(e => e.Count * forces.PointsOf(e.EnemyId));
    }

    /// <summary>Every teaching pull's points (the critical path every route plays).</summary>
    /// <param name="forces">The forces table.</param>
    /// <param name="dungeon">The dungeon.</param>
    public static int CriticalPathPoints(ForcesTable forces, DungeonDef dungeon)
    {
        System.ArgumentNullException.ThrowIfNull(dungeon);
        return dungeon.Pulls.Sum(p => PullPoints(forces, p));
    }

    /// <summary>The most a route through <paramref name="branch"/> can bank: the critical path plus every pack in its rooms.</summary>
    /// <param name="reg">Loaded content.</param>
    /// <param name="branch">A branch of <c>reg.Forces.Route</c>.</param>
    public static int BranchPoints(ContentRegistry reg, BranchDef branch)
    {
        System.ArgumentNullException.ThrowIfNull(reg);
        System.ArgumentNullException.ThrowIfNull(branch);
        int packs = reg.Packs.Values.Where(p => branch.Rooms.Contains(p.Room)).Sum(p => PackPoints(reg.Forces, p));
        return CriticalPathPoints(reg.Forces, reg.Dungeons[reg.Forces.Route.DungeonId]) + packs;
    }
}
