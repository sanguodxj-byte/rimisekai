using System;

namespace Rimisekai.WorldMap;

/// <summary>
/// 四朝向方格方向枚举与辅助位操作。
/// 0: 北 (0, -1)
/// 1: 东 (+1, 0)
/// 2: 南 (0, +1)
/// 3: 西 (-1, 0)
/// </summary>
public enum Direction4 : byte
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}

public static class Direction4Extensions
{
    public static readonly (int dx, int dy)[] Offsets = new (int dx, int dy)[]
    {
        (0, -1), // North
        (1, 0),  // East
        (0, 1),  // South
        (-1, 0), // West
    };

    public static Direction4 Opposite(this Direction4 dir) => (Direction4)(((int)dir + 2) % 4);

    public static byte ToMask(this Direction4 dir) => (byte)(1 << (int)dir);
}

/// <summary>
/// 世界地图单格实体（四朝向方格格点）。
/// </summary>
public sealed class WorldTile
{
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>基础地形类型</summary>
    public WorldTerrainType Terrain { get; set; } = WorldTerrainType.Plains;

    /// <summary>宏观生态类型</summary>
    public WorldBiomeType Biome { get; set; } = WorldBiomeType.Plains;

    /// <summary>连续标量：海拔高程 [0.0, 1.0]</summary>
    public float Elevation { get; set; }

    /// <summary>连续标量：湿度 [0.0, 1.0]</summary>
    public float Moisture { get; set; }

    /// <summary>连续标量：温度 [0.0, 1.0]</summary>
    public float Temperature { get; set; }

    /// <summary>连续标量：养分/肥沃度 [0.0, 1.0]</summary>
    public float Nutrient { get; set; }

    /// <summary>水文通量（降水汇水面积积分）</summary>
    public float WaterFlux { get; set; }

    /// <summary>排水高度（消除局部洼地后的单调下降高度）</summary>
    public float DrainageHeight { get; set; }

    /// <summary>河流标记</summary>
    public bool IsRiver { get; set; }

    /// <summary>河道流向掩码（位0:北，位1:东，位2:南，位3:西）</summary>
    public byte RiverDirections { get; set; }

    /// <summary>河流流水进度 [0.0~1.0]，从源头到入海口</summary>
    public float RiverFlowProgress { get; set; } = -1f;

    /// <summary>道路标记</summary>
    public bool IsRoad { get; set; }

    /// <summary>道路连通掩码（位0:北，位1:东，位2:南，位3:西）</summary>
    public byte RoadDirections { get; set; }

    /// <summary>道路等级：0=野径，1=车道，2=王道官道</summary>
    public int RoadClass { get; set; }

    /// <summary>桥梁标记（道路经过河流或水域）</summary>
    public bool IsBridge => IsRoad && (IsRiver || Terrain is WorldTerrainType.River or WorldTerrainType.ShallowWater or WorldTerrainType.DeepWater);

    /// <summary>归属的地质区域 ID (-1 表示未分配/公海)</summary>
    public int RegionId { get; set; } = -1;

    /// <summary>关联的兴趣点/聚落 ID (-1 表示无)</summary>
    public int PoiId { get; set; } = -1;

    public WorldTile(int x, int y)
    {
        X = x;
        Y = y;
    }
}
