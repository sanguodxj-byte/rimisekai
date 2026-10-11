using System;
using System.Collections.Generic;
using Rimisekai.WorldMap;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.PoiMap.Generator;

/// <summary>
/// Roguelike 副本区块生成器（遗迹、森林、古战场等）：
/// 保留纵深探索、分支回路与边缘出口；具体房间池由副本原型 ID 决定。
/// </summary>
public static class DungeonBlockGenerator
{
    private const int Size = 5;

    public static PoiBlock Generate(
        int blockX,
        int blockY,
        int regionId,
        ref int nextRoomId,
        int seed,
        string dungeonId,
        (int x, int y)? preferredStart = null)
    {
        var block = new PoiBlock(blockX, blockY, regionId);
        var rng = new SeededRng(seed ^ (blockX * 73856 + blockY * 19349));
        var catalog = MapCatalog.Default;

        // 1. 初始化 5x5 副本房间格点
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var room = new PoiRoom(nextRoomId++, blockX, blockY, x, y, regionId)
                {
                    Name = "石室",
                    Terrain = WorldTerrainType.Plains,
                    RoomType = PoiRoomType.Normal,
                };
                block.Grid[x, y] = room;
                block.Rooms.Add(room);
            }
        }

        // 2. 收集相邻边并使用 Kruskal 生成树构建迷宫走廊
        var allEdges = new List<(PoiRoom a, PoiRoom b)>();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var cur = block.Grid[x, y]!;
                if (x + 1 < Size)
                    allEdges.Add((cur, block.Grid[x + 1, y]!));
                if (y + 1 < Size)
                    allEdges.Add((cur, block.Grid[x, y + 1]!));
            }
        }

        for (var i = allEdges.Count - 1; i > 0; i--)
        {
            var p = rng.NextRange(0, i + 1);
            (allEdges[i], allEdges[p]) = (allEdges[p], allEdges[i]);
        }

        var parent = new Dictionary<int, int>();
        foreach (var r in block.Rooms)
            parent[r.Id] = r.Id;

        int Find(int i) => parent[i] == i ? i : (parent[i] = Find(parent[i]));
        void Union(int i, int j) => parent[Find(i)] = Find(j);

        var remainingEdges = new List<(PoiRoom a, PoiRoom b)>();

        foreach (var (a, b) in allEdges)
        {
            if (Find(a.Id) != Find(b.Id))
            {
                Union(a.Id, b.Id);
                LinkRooms(a, b);
            }
            else
            {
                remainingEdges.Add((a, b));
            }
        }

        // 适度增加 2~3 条环路回路
        for (var i = 0; i < System.Math.Min(3, remainingEdges.Count); i++)
        {
            LinkRooms(remainingEdges[i].a, remainingEdges[i].b);
        }

        // 3. 确定副本入口（必须在边缘格）
        int startX = 0, startY = 2;
        if (preferredStart.HasValue && IsEdgeCoord(preferredStart.Value.x, preferredStart.Value.y))
        {
            startX = preferredStart.Value.x;
            startY = preferredStart.Value.y;
        }

        var startRoom = block.Grid[startX, startY]!;
        startRoom.IsStart = true;
        startRoom.RoomType = PoiRoomType.Start;
        startRoom.Terrain = WorldTerrainType.Road;
        startRoom.Name = "大门";
        block.StartRoom = startRoom;

        // 4. 确定副本出口（在边缘格，且离入口图距离最远）
        var edgeRooms = new List<PoiRoom>();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (IsEdgeCoord(x, y))
                {
                    var r = block.Grid[x, y]!;
                    if (!r.IsStart)
                        edgeRooms.Add(r);
                }
            }
        }

        var distMap = ComputeGraphDistances(block, startRoom.Id);
        edgeRooms.Sort((a, b) => distMap.GetValueOrDefault(b.Id, 0).CompareTo(distMap.GetValueOrDefault(a.Id, 0)));

        var chosenEnd = edgeRooms[0];
        chosenEnd.IsEnd = true;
        chosenEnd.RoomType = PoiRoomType.End;
        chosenEnd.Terrain = WorldTerrainType.Road;
        chosenEnd.Name = "出口";
        block.EndRoom = chosenEnd;

        // 5. 装饰副本房间：房间只表达空间类型，所有非战斗探索内容由附属设施承载。
        DecorateDungeon(block, catalog, dungeonId, rng);

        return block;
    }

    private static void DecorateDungeon(PoiBlock block, MapCatalog catalog, string dungeonId, SeededRng rng)
    {
        var facilityIdx = 0;
        var templates = catalog.GetDungeonRoomTemplates(dungeonId);
        var templateUseCounts = new Dictionary<int, int>();

        foreach (var r in block.Rooms)
        {
            if (r.IsStart || r.IsEnd)
                continue;

            var adjacentTemplateIds = new HashSet<int>();
            foreach (var (x, y) in new[]
            {
                (r.LocalX - 1, r.LocalY),
                (r.LocalX + 1, r.LocalY),
                (r.LocalX, r.LocalY - 1),
                (r.LocalX, r.LocalY + 1),
            })
            {
                var adjacentRoom = block.RoomAt(x, y);
                if (adjacentRoom?.RoomTemplateId is int adjacentTemplateId)
                    adjacentTemplateIds.Add(adjacentTemplateId);
            }

            var leastUsedCount = int.MaxValue;
            var candidates = new List<RoomTemplateDef>();
            foreach (var candidate in templates)
            {
                if (adjacentTemplateIds.Contains(candidate.Id))
                    continue;

                var usedCount = templateUseCounts.GetValueOrDefault(candidate.Id);
                if (usedCount < leastUsedCount)
                {
                    candidates.Clear();
                    leastUsedCount = usedCount;
                }
                if (usedCount == leastUsedCount)
                    candidates.Add(candidate);
            }

            var templateIndex = facilityIdx++;
            var tpl = candidates[rng.NextRange(0, candidates.Count)];
            templateUseCounts[tpl.Id] = templateUseCounts.GetValueOrDefault(tpl.Id) + 1;
            r.RoomTemplateId = tpl.Id;
            // 分支末梢优先提供可探索设施；普通房间只偶尔出现设施，避免每间屋子都弹事件。
            // 储藏间始终附带箱柜，让地城稳定具备一处可由战利品生成器结算的宝箱。
            var hasFacility = tpl.Id == 1052 || r.Links.Count == 1 || rng.NextBool(0.25f);
            var facility = hasFacility ? catalog.GetDungeonFacilityTemplate(dungeonId, tpl.Id, templateIndex) : null;
            if (facility != null)
                r.FacilityIds.Add(facility.Id);
            r.Name = tpl.Name;
            r.RoomType = r.Links.Count >= 3 ? PoiRoomType.Hall
                : r.Links.Count == 2 ? PoiRoomType.Corridor
                : PoiRoomType.Normal;
            r.Terrain = Enum.TryParse<WorldTerrainType>(tpl.Terrain, ignoreCase: true, out var t) ? t : WorldTerrainType.Plains;
        }
    }

    private static void LinkRooms(PoiRoom a, PoiRoom b)
    {
        if (!a.Links.Contains(b.Id))
            a.Links.Add(b.Id);
        if (!b.Links.Contains(a.Id))
            b.Links.Add(a.Id);
    }

    private static Dictionary<int, int> ComputeGraphDistances(PoiBlock block, int startId)
    {
        var dist = new Dictionary<int, int>();
        var queue = new Queue<int>();
        dist[startId] = 0;
        queue.Enqueue(startId);

        while (queue.Count > 0)
        {
            var curId = queue.Dequeue();
            var d = dist[curId];
            var room = block.Rooms.Find(r => r.Id == curId);
            if (room == null) continue;

            foreach (var nId in room.Links)
            {
                if (!dist.ContainsKey(nId))
                {
                    dist[nId] = d + 1;
                    queue.Enqueue(nId);
                }
            }
        }

        return dist;
    }

    private static bool IsEdgeCoord(int x, int y) =>
        x == 0 || x == Size - 1 || y == 0 || y == Size - 1;
}
