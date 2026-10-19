using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// Invariants of layout.json (STATE.md D18, design/ART-PLAN.md section 3), checked fail-closed at load. A layout that
/// passes is one the sim can run: an ordered chain of overlapping boxes along +x with a route point per box, every
/// pull (and the boss) in exactly one room, each room's trigger the room shrunk 2000 mm from its entry door, every
/// spawn on the far side of its room (8 to 12 m from the door) and far enough past the trigger edge that no enemy can
/// appear within 6 m of a party member, gates that open only on a pull fought before them, and bot formation slots
/// 2 to 3 m from the leader.
/// </summary>
public static class LayoutValidator
{
    /// <summary>How far a trigger starts inside its room's entry door, mm.</summary>
    public const int TriggerInsetMm = 2000;

    /// <summary>Nearest a spawn may be to the room's entry door, mm.</summary>
    public const int MinSpawnFromDoorMm = 8000;

    /// <summary>Farthest a spawn may be from the room's entry door, mm.</summary>
    public const int MaxSpawnFromDoorMm = 12000;

    /// <summary>No enemy spawns within this distance of a party member, mm.</summary>
    public const int MinSpawnClearanceMm = 6000;

    /// <summary>
    /// How far past the trigger edge a party member can stand on the tick the pull fires: one walk tick (180 mm,
    /// run.json) plus one dash (3000 mm, Keyrun.Sim Movement.DashDistanceMm), mm.
    /// </summary>
    public const int TriggerOvershootMm = 3180;

    /// <summary>Nearest a formation slot may be to the leader, mm.</summary>
    public const int MinFormationMm = 2000;

    /// <summary>Farthest a formation slot may be from the leader, mm.</summary>
    public const int MaxFormationMm = 3000;

    /// <summary>Validates <paramref name="reg"/>'s layout against its dungeon and classes.</summary>
    /// <param name="reg">A loaded registry.</param>
    /// <exception cref="ContentLoadException">The first violated invariant.</exception>
    public static void Check(ContentRegistry reg)
    {
        System.ArgumentNullException.ThrowIfNull(reg);
        LayoutDef layout = reg.Layout;
        if (!reg.Dungeons.TryGetValue(layout.Dungeon, out DungeonDef? dungeon) || dungeon is null)
        {
            Fail("names unknown dungeon \"" + layout.Dungeon + "\"");
            return;
        }

        var boxes = layout.Boxes.ToList();
        if (boxes.Count < 2) Fail("needs at least two boxes, got " + boxes.Count);
        var ids = new HashSet<string>(System.StringComparer.Ordinal);
        for (int k = 0; k < boxes.Count; k++)
        {
            LayoutBox b = boxes[k];
            if (!ids.Add(b.Id)) Fail("duplicate box id \"" + b.Id + "\"");
            if (b.MinX >= b.MaxX || b.MinZ >= b.MaxZ) Fail("box \"" + b.Id + "\" needs minX < maxX and minZ < maxZ");
            if (k > 0)
            {
                LayoutBox prev = boxes[k - 1];
                if (b.MinX <= prev.MinX) Fail("box \"" + b.Id + "\" does not continue the route along +x after \"" + prev.Id + "\"");
                if (!(b.MinX < prev.MaxX && prev.MinX < b.MaxX && b.MinZ < prev.MaxZ && prev.MinZ < b.MaxZ))
                    Fail("box \"" + b.Id + "\" does not overlap \"" + prev.Id + "\" at a doorway");
            }
        }

        var route = layout.Route.ToList();
        if (route.Count != boxes.Count) Fail("route has " + route.Count + " points for " + boxes.Count + " boxes (one per box)");
        for (int k = 0; k < boxes.Count; k++)
        {
            if (!Inside(boxes[k], route[k])) Fail("route point " + k + " is outside box \"" + boxes[k].Id + "\"");
            if (k + 1 < boxes.Count && !Inside(boxes[k + 1], route[k]))
                Fail("route point " + k + " is not in the doorway into box \"" + boxes[k + 1].Id + "\"");
        }

        LayoutBox first = boxes[0];
        if (first.Kind != LayoutBoxKind.Room || first.Pull is not null || first.Gate is not null)
            Fail("box 0 \"" + first.Id + "\" must be a room with no pull and no gate (the party's start)");
        if (!Inside(first, layout.Start)) Fail("start is outside box 0 \"" + first.Id + "\"");

        var pullOrder = dungeon.Pulls.Select(p => p.Id).Append(LayoutDef.BossPullId).ToList();
        var roomOfPull = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (int k = 0; k < boxes.Count; k++)
        {
            LayoutBox b = boxes[k];
            if (b.Pull is null)
            {
                if (b.Trigger is not null || b.Origin is not null || b.Start is not null)
                    Fail("box \"" + b.Id + "\" has a trigger, origin or start but no pull");
                continue;
            }
            if (b.Kind != LayoutBoxKind.Room) Fail("corridor \"" + b.Id + "\" holds pull \"" + b.Pull + "\" (pulls live in rooms)");
            if (!pullOrder.Contains(b.Pull)) Fail("box \"" + b.Id + "\" names unknown pull \"" + b.Pull + "\"");
            if (!roomOfPull.TryAdd(b.Pull, k)) Fail("pull \"" + b.Pull + "\" has two rooms");
            CheckPullRoom(dungeon, b);
        }
        foreach (string pull in pullOrder)
        {
            if (!roomOfPull.ContainsKey(pull)) Fail("pull \"" + pull + "\" has no room");
        }
        for (int i = 1; i < pullOrder.Count; i++)
        {
            if (roomOfPull[pullOrder[i]] <= roomOfPull[pullOrder[i - 1]])
                Fail("pull \"" + pullOrder[i] + "\" is not after pull \"" + pullOrder[i - 1] + "\" along the route");
        }
        if (boxes.Any(b => b.Trigger is { } t && InRect(t, layout.Start)))
            Fail("start (" + layout.Start.X + ", " + layout.Start.Z + ") is inside a pull trigger (a run would start in a fight)");

        for (int k = 0; k < boxes.Count; k++)
        {
            LayoutBox b = boxes[k];
            if (b.Gate is null) continue;
            if (!roomOfPull.TryGetValue(b.Gate, out int room)) Fail("box \"" + b.Id + "\" is gated on unknown pull \"" + b.Gate + "\"");
            if (room >= k) Fail("box \"" + b.Id + "\" is gated on pull \"" + b.Gate + "\" whose room is not before it");
        }

        var formation = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (FormationSlot slot in layout.Formation)
        {
            if (!reg.Classes.ContainsKey(slot.ClassId)) Fail("formation names unknown class \"" + slot.ClassId + "\"");
            if (!formation.Add(slot.ClassId)) Fail("formation lists class \"" + slot.ClassId + "\" twice");
            long d2 = ((long)slot.BehindMm * slot.BehindMm) + ((long)slot.SideMm * slot.SideMm);
            if (slot.BehindMm <= 0 || d2 < (long)MinFormationMm * MinFormationMm || d2 > (long)MaxFormationMm * MaxFormationMm)
                Fail("formation slot \"" + slot.ClassId + "\" must be behind the leader and 2 to 3 m from it");
        }
    }

    private static void CheckPullRoom(DungeonDef dungeon, LayoutBox room)
    {
        string where = "room \"" + room.Id + "\" (pull \"" + room.Pull + "\")";
        LayoutRect trigger = room.Trigger ?? throw Error(where + " has no trigger box");
        LayoutPoint origin = room.Origin ?? throw Error(where + " has no spawn origin");
        LayoutPoint start = room.Start ?? throw Error(where + " has no start");
        if (trigger.MinX != room.MinX + TriggerInsetMm || trigger.MaxX != room.MaxX || trigger.MinZ != room.MinZ || trigger.MaxZ != room.MaxZ)
            Fail(where + " trigger must be its room shrunk " + TriggerInsetMm + " mm from the entry door (x " + (room.MinX + TriggerInsetMm) + ".." + room.MaxX + ", z " + room.MinZ + ".." + room.MaxZ + ")");
        if (!InRect(trigger, start)) Fail(where + " start is outside its trigger");

        IEnumerable<LayoutPoint> spawns;
        if (room.Pull == LayoutDef.BossPullId)
        {
            spawns = new[] { origin };
        }
        else
        {
            PullDef pull = dungeon.Pulls.First(p => p.Id == room.Pull);
            spawns = pull.Enemies.Select(e => new LayoutPoint { X = origin.X + e.Spawn.X, Z = origin.Z + e.Spawn.Z }).ToList();
        }
        foreach (LayoutPoint s in spawns)
        {
            string at = where + " spawn (" + s.X + ", " + s.Z + ")";
            if (!InRect(trigger, s)) Fail(at + " is outside its trigger");
            long depth = s.X - room.MinX;
            if (depth < MinSpawnFromDoorMm || depth > MaxSpawnFromDoorMm)
                Fail(at + " is " + depth + " mm from the door (want " + MinSpawnFromDoorMm + ".." + MaxSpawnFromDoorMm + ")");
            if (s.X - trigger.MinX < MinSpawnClearanceMm + TriggerOvershootMm)
                Fail(at + " is within " + MinSpawnClearanceMm + " mm of where a party member can stand when the pull fires");
            if (Distance2(s, start) < (long)MinSpawnClearanceMm * MinSpawnClearanceMm)
                Fail(at + " is within " + MinSpawnClearanceMm + " mm of the room's start");
        }
    }

    private static bool Inside(LayoutBox b, LayoutPoint p) => p.X >= b.MinX && p.X <= b.MaxX && p.Z >= b.MinZ && p.Z <= b.MaxZ;

    private static bool InRect(LayoutRect r, LayoutPoint p) => p.X >= r.MinX && p.X <= r.MaxX && p.Z >= r.MinZ && p.Z <= r.MaxZ;

    private static long Distance2(LayoutPoint a, LayoutPoint b)
    {
        long dx = a.X - b.X;
        long dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }

    private static ContentLoadException Error(string message) => new("content: layout " + message);

    private static void Fail(string message) => throw Error(message);
}
