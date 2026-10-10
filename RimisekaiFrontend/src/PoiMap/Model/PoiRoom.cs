using System.Collections.Generic;
using Rimisekai.WorldMap;

namespace Rimisekai.PoiMap;

/// <summary>
/// POI 房间实体。
/// 包含全局唯一生成 Id、5x5 块内局部坐标 (LocalX, LocalY) 与局部索引 CellIndex。
/// </summary>
public sealed class PoiRoom
{
    /// <summary>生成分配的全局唯一 ID</summary>
    public int Id { get; init; }

    /// <summary>房间名称</summary>
    public string Name { get; set; } = "";

    /// <summary>来源房间模板 ID；设施通过该模板 ID 绑定到房间。</summary>
    public int? RoomTemplateId { get; set; }

    /// <summary>绑定到此房间实例的设施模板 ID。</summary>
    public List<int> FacilityIds { get; } = new();

    /// <summary>物理地形形态（道路、草坪、林地、建筑等）</summary>
    public WorldTerrainType Terrain { get; set; } = WorldTerrainType.Plains;

    /// <summary>所属 5x5 块的块坐标 (BlockX, BlockY)</summary>
    public int BlockX { get; init; }
    public int BlockY { get; init; }

    /// <summary>所属区域/分区 ID (通常对应 5x5 块的索引)</summary>
    public int RegionId { get; set; }

    /// <summary>5x5 内部局部网格坐标 [0..4]</summary>
    public int LocalX { get; init; }
    public int LocalY { get; init; }

    /// <summary>
    /// 5x5 网格局部线性索引 [0..24]：LocalY * 5 + LocalX。
    /// 方便按生成 ID 极速获取在 5x5 内的物理槽位。
    /// </summary>
    public int CellIndex => LocalY * 5 + LocalX;

    /// <summary>大地图拼合下的绝对全局坐标</summary>
    public int GlobalX => BlockX * 5 + LocalX;
    public int GlobalY => BlockY * 5 + LocalY;

    /// <summary>房间类型</summary>
    public PoiRoomType RoomType { get; set; } = PoiRoomType.Normal;

    /// <summary>是否为起点房间</summary>
    public bool IsStart { get; set; }

    /// <summary>是否为终点房间（必须位于边缘格）</summary>
    public bool IsEnd { get; set; }

    /// <summary>是否已开放/开拓</summary>
    public bool Open { get; set; } = true;

    /// <summary>开拓花费</summary>
    public int OpenCost { get; set; } = 0;

    /// <summary>双向通路连通的目标房间 ID 列表</summary>
    public List<int> Links { get; } = new();

    /// <summary>
    /// 判断该房间在自身 5x5 网格中是否位于边缘格。
    /// </summary>
    public bool IsAtBlockEdge => LocalX == 0 || LocalX == 4 || LocalY == 0 || LocalY == 4;

    public PoiRoom(int id, int blockX, int blockY, int localX, int localY, int regionId = 0)
    {
        Id = id;
        BlockX = blockX;
        BlockY = blockY;
        LocalX = localX;
        LocalY = localY;
        RegionId = regionId;
    }
}
