using System.Linq;
using System;
using System.Collections.Generic;
using Rimisekai.WorldMap;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.PoiMap.Generator;

/// <summary>
/// POI 多块拼合生成器：
/// 将单个或多个 5x5 单元块拼合为大型场景地图，并确保：
/// 1. 按 POI 规模（如王都 2x2、城镇 2x1、村庄 1x1）多块拼合，绝不在单块内强塞设施；
/// 2. 每个块内部自然展开道路、草坪、庭院与少量专属设施；
/// 3. 全局大门在首块边缘，全局出口在末块边缘；
/// 4. 任何在几何上相邻的 5x5 块之间，在接触边界上【至少有一条边界通道互相连通】；
/// 5. 全局建立“根据生成 ID 快速取索引与格点”的完备映射表。
/// </summary>
public static class PoiAssemblyGenerator
{
    /// <summary>
    /// 根据 POI 类型自动从数据表中匹配拼接规模（如王都 2x2，城镇 2x1，村庄 1x1），
    /// 并为各 5x5 块分配专属分区主题，自然舒展地貌。
    /// </summary>
    public static PoiMapData GenerateForPoi(WorldPoiType type, int seed = 42)
    {
        var (bw, bh, districts) = MapCatalog.Default.GetPoiScale(type);
        var blockCoords = new List<(int bx, int by)>();
        for (var by = 0; by < bh; by++)
        {
            for (var bx = 0; bx < bw; bx++)
            {
                blockCoords.Add((bx, by));
            }
        }
        return GenerateCustom(seed, blockCoords, districts: districts);
    }

    /// <summary>
    /// 生成单个 5x5 POI 地图（内部 25 格完全填满且完全联通）。
    /// </summary>
    public static PoiMapData GenerateSingle(int seed = 42, int roomCount = 25) =>
        GenerateGrid(seed, blocksW: 1, blocksH: 1, roomCountPerBlock: roomCount);

    /// <summary>
    /// 生成矩形阵列拼合的 POI 地图（如 1x2, 2x1, 2x2 等，每个块 25 格完全填满）。
    /// </summary>
    public static PoiMapData GenerateGrid(
        int seed = 42,
        int blocksW = 2,
        int blocksH = 1,
        int roomCountPerBlock = 25)
    {
        var blockCoords = new List<(int bx, int by)>();
        for (var by = 0; by < blocksH; by++)
        {
            for (var bx = 0; bx < blocksW; bx++)
            {
                blockCoords.Add((bx, by));
            }
        }
        return GenerateCustom(seed, blockCoords, roomCountPerBlock);
    }

    /// <summary>
    /// 生成任意多块拼合的 POI 地图（支持 L 形、T 形或任意拓扑邻接的 5x5 块）。
    /// </summary>
    public static PoiMapData GenerateCustom(
        int seed,
        IReadOnlyList<(int bx, int by)> blockCoords,
        int roomCountPerBlock = 25,
        IReadOnlyList<string>? districts = null)
    {
        if (blockCoords.Count == 0)
            throw new ArgumentException("至少需要一个 5x5 块配置", nameof(blockCoords));

        var map = new PoiMapData(seed);
        var rng = new SeededRng(seed);
        var nextRoomId = 1;

        // 1. 独立生成每一个 5x5 单元块
        for (var i = 0; i < blockCoords.Count; i++)
        {
            var (bx, by) = blockCoords[i];
            var blockSeed = seed + i * 10007;

            // 首块起点置于外边缘 (如西门 0,2)，其他块由内部自动排布
            (int x, int y)? preferredStart = i == 0 ? (0, 2) : null;
            var district = districts != null && i < districts.Count ? districts[i] : "";

            var block = PoiBlockGenerator.GenerateBlock(
                blockX: bx,
                blockY: by,
                regionId: i,
                nextRoomId: ref nextRoomId,
                seed: blockSeed,
                targetRoomCount: roomCountPerBlock,
                preferredStart: preferredStart,
                district: district);

            map.Blocks.Add(block);

            // 注册房间并建立 ID-索引字典
            foreach (var r in block.Rooms)
            {
                map.RegisterRoom(block, r);
            }
        }

        // 2. 跨块边界通道连接：检查所有相邻的 5x5 块对
        ConnectAdjacentBlocks(map, rng, ref nextRoomId);

        // 3. 全局起点与最终终点规范化（只有全局第一个块有入口大门，只有最后一个块有最终出口）
        if (map.Blocks.Count > 0)
        {
            for (var i = 0; i < map.Blocks.Count; i++)
            {
                var b = map.Blocks[i];
                if (i > 0 && b.StartRoom != null)
                {
                    b.StartRoom.IsStart = false;
                    b.StartRoom.RoomType = PoiRoomType.Normal;
                    b.StartRoom.Name = "街道";
                }
                if (i < map.Blocks.Count - 1 && b.EndRoom != null)
                {
                    b.EndRoom.IsEnd = false;
                    b.EndRoom.RoomType = PoiRoomType.Normal;
                    b.EndRoom.Name = "道路";
                }
            }

            // 第一个块的起点为全局正门
            if (map.Blocks[0].StartRoom != null)
            {
                map.Blocks[0].StartRoom!.IsStart = true;
                map.Blocks[0].StartRoom!.RoomType = PoiRoomType.Start;
                map.Blocks[0].StartRoom!.Name = "大门";
            }

            // 最后一个块的终点为全局出口
            var lastBlock = map.Blocks[^1];
            if (lastBlock.EndRoom != null)
            {
                lastBlock.EndRoom.IsEnd = true;
                lastBlock.EndRoom.RoomType = PoiRoomType.End;
                lastBlock.EndRoom.Name = "出口";
            }
        }

        DistinguishNames(map);
        return map;
    }

    private static readonly string[] Ordinals = { "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };

    /// <summary>
    /// 同一块里重名的房间按块内方位加前缀（以块心为准：东北草地、西庭院；正中写「中」），
    /// 同方位仍重名的再按生成顺序接序号（东北草地一、东北草地二）。不重名的房间保持原名。
    /// </summary>
    private static void DistinguishNames(PoiMapData map)
    {
        foreach (var block in map.Blocks)
        {
            foreach (var group in block.Rooms.GroupBy(r => r.Name).Where(g => g.Count() > 1).ToList())
            {
                foreach (var room in group)
                {
                    var dx = room.LocalX - PoiBlock.Size / 2;
                    var dy = room.LocalY - PoiBlock.Size / 2;
                    var dir = (dx > 0 ? "东" : dx < 0 ? "西" : "") + (dy < 0 ? "北" : dy > 0 ? "南" : "");
                    room.BaseName = room.Name;
                    room.Name = (dir.Length > 0 ? dir : "中") + room.Name;
                }
                foreach (var same in group.GroupBy(r => r.Name).Where(g => g.Count() > 1))
                {
                    var n = 0;
                    foreach (var room in same)
                        room.Name += Ordinals[n++];
                }
            }
        }
    }

    private static void ConnectAdjacentBlocks(PoiMapData map, SeededRng rng, ref int nextRoomId)
    {
        for (var i = 0; i < map.Blocks.Count; i++)
        {
            for (var j = i + 1; j < map.Blocks.Count; j++)
            {
                var a = map.Blocks[i];
                var b = map.Blocks[j];

                // 检查 A 和 B 是否在 5x5 网格上正交相邻
                var dx = b.BlockX - a.BlockX;
                var dy = b.BlockY - a.BlockY;

                if (System.Math.Abs(dx) + System.Math.Abs(dy) != 1)
                    continue; // 非直接相邻，跳过

                // 确保至少有一条边界通道连通
                EnsureBoundaryConnection(map, a, b, dx, dy, rng, ref nextRoomId);
            }
        }
    }

    private static void EnsureBoundaryConnection(
        PoiMapData map,
        PoiBlock a,
        PoiBlock b,
        int dx,
        int dy,
        SeededRng rng,
        ref int nextRoomId)
    {
        // 查找所有重叠的接触边界槽位
        // dx=1, dy=0: A 东边 (x=4) 紧邻 B 西边 (x=0)，接触线 y 从 0 到 4
        // dx=-1, dy=0: A 西边 (x=0) 紧邻 B 东边 (x=4)
        // dx=0, dy=1: A 南边 (y=4) 紧邻 B 北边 (y=0)，接触线 x 从 0 到 4
        // dx=0, dy=-1: A 北边 (y=0) 紧邻 B 南边 (y=4)

        var candidatePairs = new List<(PoiRoom roomA, PoiRoom roomB)>();

        for (var slot = 0; slot < 5; slot++)
        {
            int ax, ay, bx, by;
            if (dx == 1) // B 在 A 的东侧
            {
                ax = 4; ay = slot;
                bx = 0; by = slot;
            }
            else if (dx == -1) // B 在 A 的西侧
            {
                ax = 0; ay = slot;
                bx = 4; by = slot;
            }
            else if (dy == 1) // B 在 A 的南侧
            {
                ax = slot; ay = 4;
                bx = slot; by = 0;
            }
            else // B 在 A 的北侧
            {
                ax = slot; ay = 0;
                bx = slot; by = 4;
            }

            var roomA = a.RoomAt(ax, ay);
            var roomB = b.RoomAt(bx, by);

            if (roomA != null && roomB != null)
            {
                candidatePairs.Add((roomA, roomB));
            }
        }

        if (candidatePairs.Count > 0)
        {
            // 打通 1~2 条边界通道
            var connectsToMake = System.Math.Min(candidatePairs.Count, rng.NextRange(1, 3));
            // 洗牌
            for (var k = candidatePairs.Count - 1; k > 0; k--)
            {
                var p = rng.NextRange(0, k + 1);
                (candidatePairs[k], candidatePairs[p]) = (candidatePairs[p], candidatePairs[k]);
            }

            for (var k = 0; k < connectsToMake; k++)
            {
                var (rA, rB) = candidatePairs[k];
                LinkCrossBlock(map, a, b, rA, rB);
            }
        }
        else
        {
            // 边缘未对齐的情况下：强制在接触线上选择一个适宜槽位，补齐两端房间并打通
            ForceCreateBoundaryBridge(map, a, b, dx, dy, ref nextRoomId);
        }
    }

    private static void ForceCreateBoundaryBridge(
        PoiMapData map,
        PoiBlock a,
        PoiBlock b,
        int dx,
        int dy,
        ref int nextRoomId)
    {
        // 挑选一个中间槽位 (比如 slot=2，即 x=2 或 y=2)
        var slot = 2;
        int ax, ay, bx, by;

        if (dx == 1)
        {
            ax = 4; ay = slot;
            bx = 0; by = slot;
        }
        else if (dx == -1)
        {
            ax = 0; ay = slot;
            bx = 4; by = slot;
        }
        else if (dy == 1)
        {
            ax = slot; ay = 4;
            bx = slot; by = 0;
        }
        else
        {
            ax = slot; ay = 0;
            bx = slot; by = 4;
        }

        var rA = a.RoomAt(ax, ay) ?? CreateAndLinkInternal(map, a, ax, ay, ref nextRoomId);
        var rB = b.RoomAt(bx, by) ?? CreateAndLinkInternal(map, b, bx, by, ref nextRoomId);

        LinkCrossBlock(map, a, b, rA, rB);
    }

    private static PoiRoom CreateAndLinkInternal(PoiMapData map, PoiBlock block, int lx, int ly, ref int nextRoomId)
    {
        var room = new PoiRoom(nextRoomId++, block.BlockX, block.BlockY, lx, ly, block.RegionId)
        {
            Name = "边界过道",
            RoomType = PoiRoomType.Corridor,
        };
        block.Grid[lx, ly] = room;
        block.Rooms.Add(room);
        map.RegisterRoom(block, room);

        // 找块内部最近的已有房间连通，确保块内不断连
        PoiRoom? nearest = null;
        var minDist = int.MaxValue;
        foreach (var existing in block.Rooms)
        {
            if (existing == room)
                continue;
            var d = System.Math.Abs(existing.LocalX - lx) + System.Math.Abs(existing.LocalY - ly);
            if (d < minDist)
            {
                minDist = d;
                nearest = existing;
            }
        }

        if (nearest != null)
        {
            if (!room.Links.Contains(nearest.Id))
                room.Links.Add(nearest.Id);
            if (!nearest.Links.Contains(room.Id))
                nearest.Links.Add(room.Id);
        }

        return room;
    }

    private static void LinkCrossBlock(PoiMapData map, PoiBlock a, PoiBlock b, PoiRoom rA, PoiRoom rB)
    {
        if (!rA.Links.Contains(rB.Id))
            rA.Links.Add(rB.Id);
        if (!rB.Links.Contains(rA.Id))
            rB.Links.Add(rA.Id);

        map.BoundaryLinks.Add(new BoundaryLink(rA.Id, rB.Id, a.RegionId, b.RegionId));
    }

    private static void Apply<T>(this T obj, Action<T> action) => action(obj);
}
