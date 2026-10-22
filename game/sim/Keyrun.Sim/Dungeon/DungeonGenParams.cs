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

    /// <summary>Fewest middle layers (rooms between the entrance and the boss on any route). At least 2.</summary>
    public int MinMiddleLayers { get; init; } = 4;

    /// <summary>Most middle layers.</summary>
    public int MaxMiddleLayers { get; init; } = 6;

    /// <summary>Most rooms side by side in one layer (the fork width). At least 2.</summary>
    public int MaxWidth { get; init; } = 3;

    /// <summary>A room's side, in cells (square rooms). At least 10, so six pack slots fit.</summary>
    public int RoomCells { get; init; } = 12;

    /// <summary>Empty rows between two rooms of one layer.</summary>
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
        if (MinMiddleLayers < 2 || MaxMiddleLayers < MinMiddleLayers)
        {
            throw new ArgumentException("middle layers: need 2 <= min <= max");
        }
        if (MaxWidth < 2 || RoomCells < 10 || RowGapCells < 1 || CorridorCells < 1 || CellMm <= 0 || ForcesRequired <= 0)
        {
            throw new ArgumentException("dungeon params out of range");
        }
    }
}
