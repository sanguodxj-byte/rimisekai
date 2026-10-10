using System;
using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.PoiMap;

/// <summary>
/// 跨 5x5 块的边界通道连通线。
/// </summary>
public readonly record struct BoundaryLink(int RoomAId, int RoomBId, int BlockAIndex, int BlockBIndex);

/// <summary>
/// POI 场景大地图聚合根模型（由单个或多个 5x5 单元块拼合而成）。
/// </summary>
public sealed class PoiMapData
{
    public int Seed { get; init; }

    /// <summary>拼合地图包含的所有 5x5 块</summary>
    public List<PoiBlock> Blocks { get; } = new();

    /// <summary>所有房间的扁平列表</summary>
    public List<PoiRoom> AllRooms { get; } = new();

    /// <summary>跨 5x5 边界打通的连接通道列表</summary>
    public List<BoundaryLink> BoundaryLinks { get; } = new();

    private readonly Dictionary<int, PoiRoom> _roomsById = new();
    private readonly Dictionary<int, int> _idToCellIndex = new();
    private readonly Dictionary<int, PoiBlock> _roomToBlock = new();

    public PoiMapData(int seed)
    {
        Seed = seed;
    }

    /// <summary>
    /// 注册房间，建立“根据生成ID取索引”与快速查找的映射索引表。
    /// </summary>
    public void RegisterRoom(PoiBlock block, PoiRoom room)
    {
        _roomsById[room.Id] = room;
        _idToCellIndex[room.Id] = room.CellIndex;
        _roomToBlock[room.Id] = block;

        if (!AllRooms.Contains(room))
            AllRooms.Add(room);
    }

    /// <summary>根据生成 ID 快速查找房间实体</summary>
    public PoiRoom? GetRoom(int id) => _roomsById.GetValueOrDefault(id);

    /// <summary>根据生成 ID 获取该房间在其所属 5x5 块内的局部索引 [0..24]</summary>
    public int GetCellIndex(int id) =>
        _idToCellIndex.TryGetValue(id, out var idx) ? idx : -1;

    /// <summary>根据生成 ID 获取该房间所属的 5x5 块</summary>
    public PoiBlock? GetBlockByRoomId(int id) => _roomToBlock.GetValueOrDefault(id);

    /// <summary>
    /// 将 POI 房间列表转换为与现存据点领地系统完全兼容的 Room 列表。
    /// 坐标取**块内局部坐标**（0..4）：左上角网格一次画一块，多块拼合的兴趣点靠区号区分块。
    /// </summary>
    public List<Room> ExportToHousingRooms()
    {
        var result = new List<Room>();
        foreach (var r in AllRooms)
        {
            var room = new Room
            {
                Id = r.Id,
                Name = r.Name,
                RegionId = r.RegionId,
                X = r.LocalX,
                Y = r.LocalY,
                Open = r.Open,
                OpenCost = r.OpenCost,
            };
            room.Links.AddRange(r.Links);
            if (r.Shop)
            {
                room.AddTag(Territory.CityShopTag);
                room.AddTag(Territory.IndoorTag);
            }
            result.Add(room);
        }
        return result;
    }
}
