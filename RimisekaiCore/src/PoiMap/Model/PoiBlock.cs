using System.Collections.Generic;

namespace Rimisekai.PoiMap;

/// <summary>
/// 单个 5x5 POI 地图单元块。
/// </summary>
public sealed class PoiBlock
{
    public const int Size = 5;

    public int BlockX { get; init; }
    public int BlockY { get; init; }
    public int RegionId { get; init; }

    /// <summary>
    /// 5x5 内部房间网格（若某格未开辟则为 null）。
    /// </summary>
    public PoiRoom?[,] Grid { get; } = new PoiRoom?[Size, Size];

    /// <summary>
    /// 当前 5x5 块内所有已生成房间列表。
    /// </summary>
    public List<PoiRoom> Rooms { get; } = new();

    /// <summary>当前块起点房间（若有）</summary>
    public PoiRoom? StartRoom { get; set; }

    /// <summary>当前块终点房间（必须在边缘格）</summary>
    public PoiRoom? EndRoom { get; set; }

    public PoiBlock(int blockX, int blockY, int regionId)
    {
        BlockX = blockX;
        BlockY = blockY;
        RegionId = regionId;
    }

    public PoiRoom? RoomAt(int localX, int localY)
    {
        if (localX < 0 || localX >= Size || localY < 0 || localY >= Size)
            return null;
        return Grid[localX, localY];
    }

    public PoiRoom? RoomAtIndex(int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= Size * Size)
            return null;
        return Grid[cellIndex % Size, cellIndex / Size];
    }
}
