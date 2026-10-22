using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Dungeon;

/// <summary>A room's cell rectangle on the grid, inclusive on both ends.</summary>
/// <param name="RoomId">The graph room it draws.</param>
/// <param name="MinCx">Lowest cell column.</param>
/// <param name="MinCz">Lowest cell row.</param>
/// <param name="MaxCx">Highest cell column.</param>
/// <param name="MaxCz">Highest cell row.</param>
public readonly record struct RoomRect(int RoomId, int MinCx, int MinCz, int MaxCx, int MaxCz)
{
    /// <summary>The centre cell column.</summary>
    public int CenterCx => (MinCx + MaxCx) / 2;

    /// <summary>The centre cell row.</summary>
    public int CenterCz => (MinCz + MaxCz) / 2;
}

/// <summary>
/// The walkable integer grid a generated dungeon emits (stage 2; R-PATH-1). This is the type the pathing lane
/// (feat/sim-pathing) reads: a row-major cell array, one byte per cell (1 walkable, 0 wall), with a fixed cell size in
/// mm and the mm position of cell (0, 0)'s low corner. Immutable once built.
/// </summary>
public sealed class WalkGrid
{
    private readonly byte[] _cells;

    /// <summary>Builds a grid over a copy of <paramref name="cells"/> (row-major, z rows of x columns).</summary>
    /// <exception cref="ArgumentException">Bad size or a cell array of another length.</exception>
    public WalkGrid(int width, int height, int cellMm, long originX, long originZ, IReadOnlyList<byte> cells, IReadOnlyList<RoomRect> rooms)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(rooms);
        if (width <= 0 || height <= 0 || cellMm <= 0 || cells.Count != width * height)
        {
            throw new ArgumentException("walk grid: bad size");
        }
        Width = width;
        Height = height;
        CellMm = cellMm;
        OriginX = originX;
        OriginZ = originZ;
        _cells = cells.ToArray();
        Rooms = rooms.ToList();
    }

    /// <summary>Columns (x).</summary>
    public int Width { get; }

    /// <summary>Rows (z).</summary>
    public int Height { get; }

    /// <summary>One cell's side, mm.</summary>
    public int CellMm { get; }

    /// <summary>mm x of cell column 0's low edge.</summary>
    public long OriginX { get; }

    /// <summary>mm z of cell row 0's low edge.</summary>
    public long OriginZ { get; }

    /// <summary>The cells, row-major (index = cz * Width + cx), 1 = walkable.</summary>
    public IReadOnlyList<byte> Cells => _cells;

    /// <summary>Every room's rectangle, in room id order.</summary>
    public IReadOnlyList<RoomRect> Rooms { get; }

    /// <summary>Walkable cells.</summary>
    public int WalkableCount => _cells.Count(c => c != 0);

    /// <summary>True when cell (cx, cz) is on the grid and walkable; off-grid is a wall.</summary>
    public bool IsWalkable(int cx, int cz) =>
        cx >= 0 && cz >= 0 && cx < Width && cz < Height && _cells[(cz * Width) + cx] != 0;

    /// <summary>True when the mm point <paramref name="p"/> (XZ) stands on a walkable cell.</summary>
    public bool Contains(Vec3 p)
    {
        var cx = IntMath.FloorDiv(p.X - OriginX, CellMm);
        var cz = IntMath.FloorDiv(p.Z - OriginZ, CellMm);
        return cx >= 0 && cz >= 0 && cx < Width && cz < Height && IsWalkable((int)cx, (int)cz);
    }

    /// <summary>The mm centre of cell (cx, cz).</summary>
    public Vec3 CellCenter(int cx, int cz) =>
        new(OriginX + ((long)cx * CellMm) + (CellMm / 2), 0, OriginZ + ((long)cz * CellMm) + (CellMm / 2));

    /// <summary>FNV-1a over the size and cells: a cheap identity for logs, never a test oracle.</summary>
    public uint Digest()
    {
        var header = new byte[8];
        BitConverterLe(header, 0, Width);
        BitConverterLe(header, 4, Height);
        return Fnv1a.Append(Fnv1a.Hash(header), _cells);
    }

    private static void BitConverterLe(byte[] into, int at, int value)
    {
        into[at] = (byte)value;
        into[at + 1] = (byte)(value >> 8);
        into[at + 2] = (byte)(value >> 16);
        into[at + 3] = (byte)(value >> 24);
    }
}
