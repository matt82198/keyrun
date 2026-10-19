using System.Runtime.Serialization;

namespace Keyrun.Content;

/// <summary>
/// The dungeon's walkable layout (game/content/data/layout.json, logical path <c>layout.json</c>; C#-only like
/// run.json, so the frozen TS oracle root content/ never changes). STATE.md D18, design/ART-PLAN.md section 3: the
/// Hollow Vault is an ordered chain of rooms and corridors, each an XZ box in mm, that overlap at their doorways. Each
/// pull lives in its own room, spawns when a party member first stands in that room's trigger box, and the corridor
/// out of a room opens when the room's pull clears (forward-only, BIBLE s9). The route runs along +x.
/// </summary>
public sealed record LayoutDef
{
    /// <summary>The pull id the host gives the boss pull (HostRunSetup appends the content boss as pull "boss").</summary>
    public const string BossPullId = "boss";

    /// <summary>The dungeon this layout belongs to.</summary>
    public required string Dungeon { get; init; }

    /// <summary>Where the party's tank slot stands at a run that starts at the first pull (inside the first box).</summary>
    public required LayoutPoint Start { get; init; }

    /// <summary>Rooms and corridors in route order; box k overlaps box k+1 at their doorway.</summary>
    public required ValueList<LayoutBox> Boxes { get; init; }

    /// <summary>
    /// One point per box: for box k before the last, the doorway point into box k+1 (inside both boxes); for the last
    /// box, its goal point. A walker inside box k walks straight at point k (every box is convex).
    /// </summary>
    public required ValueList<LayoutPoint> Route { get; init; }

    /// <summary>Where each bot class stands behind the leader while the party travels.</summary>
    public required ValueList<FormationSlot> Formation { get; init; }
}

/// <summary>A box's kind: a room (a place, possibly with a pull) or a corridor between rooms.</summary>
public enum LayoutBoxKind
{
    /// <summary>A room.</summary>
    [EnumMember(Value = "room")] Room,

    /// <summary>A corridor.</summary>
    [EnumMember(Value = "corridor")] Corridor,
}

/// <summary>One walkable box of the layout, mm.</summary>
public sealed record LayoutBox
{
    /// <summary>Box id (unique), e.g. "choir-room"; the sim's room_enter event names rooms by it.</summary>
    public required string Id { get; init; }

    /// <summary>Room or corridor.</summary>
    public required LayoutBoxKind Kind { get; init; }

    /// <summary>Lowest x.</summary>
    public required int MinX { get; init; }

    /// <summary>Highest x.</summary>
    public required int MaxX { get; init; }

    /// <summary>Lowest z.</summary>
    public required int MinZ { get; init; }

    /// <summary>Highest z.</summary>
    public required int MaxZ { get; init; }

    /// <summary>The pull this room holds (a dungeon pull id or <see cref="LayoutDef.BossPullId"/>), or null.</summary>
    public string? Pull { get; init; }

    /// <summary>The pull whose clear opens this box (a corridor out of that pull's room), or null = always open.</summary>
    public string? Gate { get; init; }

    /// <summary>A pull room's trigger: the room box shrunk 2000 mm from its entry door. Pull rooms only.</summary>
    public LayoutRect? Trigger { get; init; }

    /// <summary>
    /// A pull room's spawn origin: each enemy group spawns at origin plus its content spawn offset (the boss at the
    /// origin), so formation shapes never change. Pull rooms only.
    /// </summary>
    public LayoutPoint? Origin { get; init; }

    /// <summary>Where the tank slot stands when a run starts at this room's pull (<c>--pull N</c>; inside the trigger). Pull rooms only.</summary>
    public LayoutPoint? Start { get; init; }
}

/// <summary>An XZ rectangle, mm.</summary>
public sealed record LayoutRect
{
    /// <summary>Lowest x.</summary>
    public required int MinX { get; init; }

    /// <summary>Highest x.</summary>
    public required int MaxX { get; init; }

    /// <summary>Lowest z.</summary>
    public required int MinZ { get; init; }

    /// <summary>Highest z.</summary>
    public required int MaxZ { get; init; }
}

/// <summary>An XZ point, mm.</summary>
public sealed record LayoutPoint
{
    /// <summary>X, mm.</summary>
    public required int X { get; init; }

    /// <summary>Z, mm.</summary>
    public required int Z { get; init; }
}

/// <summary>A bot class's travel slot relative to the leader: <see cref="BehindMm"/> back along the route (-x), <see cref="SideMm"/> in z.</summary>
public sealed record FormationSlot
{
    /// <summary>The bot's class id.</summary>
    public required string ClassId { get; init; }

    /// <summary>Distance behind the leader along -x, mm (positive).</summary>
    public required int BehindMm { get; init; }

    /// <summary>Sideways offset in z, mm.</summary>
    public required int SideMm { get; init; }
}
