using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Nav;

namespace Keyrun.Bots.Shared;

/// <summary>floor.ts FloorEscapeOptions.keepNear: prefer an escape point within <paramref name="MaxDistMm"/> of <paramref name="Anchor"/>.</summary>
/// <param name="Anchor">Point to stay near (the healer's party).</param>
/// <param name="MaxDistMm">Radius, mm.</param>
public sealed record KeepNear(PointMm Anchor, long MaxDistMm);

/// <summary>bots/src/shared/floor.ts FloorEscapeOptions.</summary>
public sealed record FloorEscapeOptions
{
    /// <summary>Margin added to each patch radius, mm (null = 500).</summary>
    public long? MarginMm { get; init; }

    /// <summary>Range-keeping preference, or null.</summary>
    public KeepNear? KeepNear { get; init; }
}

/// <summary>
/// Port of bots/src/shared/floor.ts: hostile floor-patch avoidance, melee re-approach, and the friendly floor
/// Window approach. A floor patch is a circle telegraph whose id starts with <see cref="FloorPatchPrefix"/> and
/// carries <c>danger: false</c>; a friendly floor Window encodes <c>floor:window:&lt;windowId&gt;:&lt;setupClass&gt;</c>
/// and is never fled. This is the skill-dial-gated tier below bot.ts's reflex telegraph dodge: roles call it.
/// </summary>
public static class Floor
{
    /// <summary>floor.ts FLOOR_PATCH_PREFIX.</summary>
    public const string FloorPatchPrefix = "floor:";

    private const long DefaultMarginMm = 500;

    /// <summary>floor.ts MELEE_RANGE_MM: 5 m.</summary>
    private const double MeleeRangeMm = 5 * 1000;

    /// <summary>floor.ts MELEE_CLASS_IDS: the Warden is the only melee-flavoured starter class (every tank is melee).</summary>
    private static readonly HashSet<string> MeleeClassIds = new(StringComparer.Ordinal) { "warden" };

    private static bool IsFloorPatchTelegraph(BotTelegraph tg) =>
        tg.Shape == TelegraphShape.Circle && tg.Id.StartsWith(FloorPatchPrefix, StringComparison.Ordinal);

    private static bool IsFriendlyWindowPatch(BotTelegraph tg) =>
        Tail(tg.Id, FloorPatchPrefix.Length).StartsWith(Pings.WindowEffectPrefix, StringComparison.Ordinal);

    /// <summary>JS <c>s.slice(n)</c> for n &gt;= 0: empty when n is past the end.</summary>
    private static string Tail(string s, int n) => n >= s.Length ? string.Empty : s.Substring(n);

    private static List<BotTelegraph> HostileFloorPatches(IEnumerable<BotTelegraph> telegraphs, int tick) =>
        telegraphs.Where(tg => tg.ResolvesTick > tick && IsFloorPatchTelegraph(tg) && !IsFriendlyWindowPatch(tg)).ToList();

    private static List<BotTelegraph> Inflated(IEnumerable<BotTelegraph> telegraphs, long marginMm) =>
        telegraphs.Select(tg => tg with { Danger = true, Radius = (tg.Radius ?? 0) + marginMm }).ToList();

    private static bool OrdinaryTelegraphDangerActive(BotView view, ActorState self)
    {
        var tick = view.State.Tick;
        var ordinary = view.Telegraphs.Where(t => t.Danger && t.ResolvesTick > tick && !IsFloorPatchTelegraph(t) && !Positioning.IsSpent(t, view.State)).ToList();
        return Positioning.IsInAnyDanger(PointMm.Of(self.Pos), ordinary);
    }

    /// <summary>
    /// floor.ts floorEscapeCommand: a move clearing the hostile floor patch <paramref name="self"/> stands in
    /// (radius plus margin), or null. Defers (null) while an ordinary danger telegraph is active, since bot.ts's
    /// reflex tier already moves the bot. With <see cref="FloorEscapeOptions.KeepNear"/>, prefers a point inside
    /// the anchor radius and falls back to the plain nearest safe point.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="opts">Options.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? FloorEscapeCommand(BotView view, ActorState self, FloorEscapeOptions? opts = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var options = opts ?? new FloorEscapeOptions();
        if (OrdinaryTelegraphDangerActive(view, self))
        {
            return null;
        }
        var tick = view.State.Tick;
        var patches = HostileFloorPatches(view.Telegraphs, tick);
        if (patches.Count == 0)
        {
            return null;
        }
        var selfMm = PointMm.Of(self.Pos);
        var danger = Inflated(patches, options.MarginMm ?? DefaultMarginMm);
        if (!Positioning.IsInAnyDanger(selfMm, danger))
        {
            return null;
        }

        // FLOOR-ESCAPE WALKABLE (finding f-4250969b, R-DODGE-2): on a layout run the safe point must be walkable and the
        // move walks the sim's path to it. The sim clamps each actor into its open boxes after every tick, so a straight
        // move toward a point in a wall pins the seat against the wall inside the hazard. Off a layout run the straight
        // move is unchanged.
        if (view.Layout is { } layout)
        {
            var grid = NavGrid.Of(layout);
            bool Walkable(PointMm p) => grid.Walkable(new Vec3(p.X, self.Pos.Y, p.Z));
            PointMm? walkable = null;
            if (options.KeepNear is { } walkKeep)
            {
                walkable = Positioning.NearestSafePoint(selfMm, danger, 500, 20, p =>
                {
                    var dx = p.X - walkKeep.Anchor.X;
                    var dz = p.Z - walkKeep.Anchor.Z;
                    return Walkable(p) && (dx * dx) + (dz * dz) <= walkKeep.MaxDistMm * walkKeep.MaxDistMm;
                });
            }
            walkable ??= Positioning.NearestSafePoint(selfMm, danger, extraCheck: Walkable);
            return walkable is null
                ? null
                : PathMoves.Along(tick, self, layout, new Vec3(walkable.Value.X, self.Pos.Y, walkable.Value.Z));
        }

        PointMm? safe = null;
        if (options.KeepNear is { } keep)
        {
            safe = Positioning.NearestSafePoint(selfMm, danger, 500, 20, p =>
            {
                var dx = p.X - keep.Anchor.X;
                var dz = p.Z - keep.Anchor.Z;
                return (dx * dx) + (dz * dz) <= keep.MaxDistMm * keep.MaxDistMm;
            });
        }
        safe ??= Positioning.NearestSafePoint(selfMm, danger);
        return safe is null ? null : Moves.MoveToward(tick, self.Id, selfMm, safe.Value);
    }

    private static bool IsMelee(ActorState self) => self.Role == ActorRole.Tank || MeleeClassIds.Contains(self.ClassId);

    /// <summary>
    /// floor.ts meleeReapproachCommand: a melee bot (tank or Warden) whose alive target is beyond 5 m walks back
    /// in, unless it is still clearing a hostile floor patch. Null otherwise.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? MeleeReapproachCommand(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        if (!IsMelee(self) || self.TargetId is null)
        {
            return null;
        }
        if (!view.State.Actors.TryGetValue(self.TargetId.Value, out var target) || !target.Alive)
        {
            return null;
        }
        if (FloorEscapeCommand(view, self) is not null)
        {
            return null;
        }
        double dx = target.Pos.X - self.Pos.X;
        double dz = target.Pos.Z - self.Pos.Z;
        var dist = Math.Sqrt((dx * dx) + (dz * dz));
        if (dist <= MeleeRangeMm)
        {
            return null;
        }
        return PathMoves.Toward(view, view.State.Tick, self, target.Pos);
    }

    private sealed record FriendlyWindow(PointMm Center, long RadiusMm, string SetupClass);

    private static FriendlyWindow? ParseFriendlyWindow(BotTelegraph tg)
    {
        if (!IsFloorPatchTelegraph(tg) || !IsFriendlyWindowPatch(tg))
        {
            return null;
        }
        var rest = Tail(tg.Id, FloorPatchPrefix.Length + Pings.WindowEffectPrefix.Length);
        var parts = rest.Split(':');
        var setupClass = parts[^1];
        if (setupClass.Length == 0)
        {
            return null;
        }
        return new FriendlyWindow(tg.Center, tg.Radius ?? 0, setupClass);
    }

    /// <summary>
    /// floor.ts friendlyWindowApproachCommand: walk toward the first active friendly floor Window this bot's class
    /// may close (The Lock, <see cref="BotWindows.CanClose"/>). Null once inside it, or when none qualifies.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? FriendlyWindowApproachCommand(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        var selfMm = PointMm.Of(self.Pos);
        foreach (var tg in view.Telegraphs)
        {
            if (tg.ResolvesTick <= tick)
            {
                continue;
            }
            var win = ParseFriendlyWindow(tg);
            if (win is null || !BotWindows.CanClose(win.SetupClass, self.ClassId))
            {
                continue;
            }
            var dx = win.Center.X - selfMm.X;
            var dz = win.Center.Z - selfMm.Z;
            if ((dx * dx) + (dz * dz) <= win.RadiusMm * win.RadiusMm)
            {
                return null;
            }
            return Moves.MoveToward(tick, self.Id, selfMm, win.Center);
        }
        return null;
    }
}
