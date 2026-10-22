using System;

namespace Keyrun.Sim.Dungeon;

/// <summary>
/// The tunables of the seeded dungeon pipeline (game/design/ARCHITECTURE.md section 2). Integers only: layers, cells,
/// mm and forces points. The defaults are a first cut for "tune later" (STATE.md D44), not measured values.
/// </summary>
public sealed record DungeonGenParams
{
    /// <summary>The default parameters.</summary>
    public static readonly DungeonGenParams Default = new();

    // The graph's shape (one fork, two branches of two or three rooms) is a contract, not a tunable:
    // RoomGraphGen.BranchCount, MinBranchRooms and MaxBranchRooms (STATE.md D63).

    /// <summary>A room's side, in cells (square rooms). At least 10, so six pack slots fit.</summary>
    public int RoomCells { get; init; } = 12;

    /// <summary>Empty rows between the two branches' rooms of one layer.</summary>
    public int RowGapCells { get; init; } = 4;

    /// <summary>A corridor's width, in cells.</summary>
    public int CorridorCells { get; init; } = 2;

    /// <summary>One cell's side, mm.</summary>
    public int CellMm { get; init; } = 1000;

    /// <summary>The forces bar (BIBLE section 9.2: 1000 points = 100%).</summary>
    public int ForcesRequired { get; init; } = 1000;

    /// <summary>Throws unless the pipeline can use these values (fail closed).</summary>
    /// <exception cref="ArgumentException">A value out of range.</exception>
    public void Validate()
    {
        // Two corridors enter the boss room on opposite halves of its west wall (WalkGridGen), so each must fit in half.
        if ((2 * CorridorCells) + 3 > RoomCells)
        {
            throw new ArgumentException("dungeon params: two corridors must fit side by side in a room wall");
        }
        if (RoomCells < 10 || RowGapCells < 1 || CorridorCells < 1 || CellMm <= 0 || ForcesRequired <= 0)
        {
            throw new ArgumentException("dungeon params out of range");
        }
    }
}
