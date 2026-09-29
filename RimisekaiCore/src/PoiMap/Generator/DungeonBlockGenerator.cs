using System;
using System.Collections.Generic;
using Rimisekai.WorldMap;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.PoiMap.Generator;

/// <summary>
/// 封闭式地牢/迷宫（遗迹、洞窟、地下城）生成器：
/// 专用于探索型地下城，保留幽深走廊、封闭石室、分支死胡同藏宝库与边缘关口出口。
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
        (int x, int y)? preferredStart = null,
        string district = "RuinDistrict")
    {
        var block = new PoiBlock(blockX, blockY, regionId);
        var rng = new SeededRng(seed ^ (blockX * 73856 + blockY * 19349));
        var catalog = MapCatalog.Default;

        // 1. 初始化 5x5 地牢石室格点
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

        // 3. 确定地牢入口大门（必须在边缘格）
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

        // 4. 确定地牢深处出口（在边缘格，且离入口图距离最远）
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

        // 5. 装饰地牢房间（分支死胡同设为宝库/神龛，主通道设为石廊）
        DecorateDungeon(block, catalog, district, rng);

        return block;
    }

    private static void DecorateDungeon(PoiBlock block, MapCatalog catalog, string district, SeededRng rng)
    {
        var facilityIdx = 0;

        foreach (var r in block.Rooms)
        {
            if (r.IsStart || r.IsEnd)
                continue;

            // 死胡同末端设为藏宝库或神龛
            if (r.Links.Count == 1)
            {
                var isTreasure = rng.NextBool(0.6f);
                var tpl = isTreasure ? catalog.GetTreasureTemplate() : catalog.GetShrineTemplate();
                r.RoomType = isTreasure ? PoiRoomType.Treasure : PoiRoomType.Shrine;
                r.Terrain = WorldTerrainType.Plains;
                r.Name = tpl.Name;
            }
            else if (r.Links.Count >= 3)
            {
                // 枢纽大厅
                r.Name = "石柱大厅";
                r.Terrain = WorldTerrainType.Plains;
            }
            else
            {
                // 普通通道或古遗迹设施
                var tpl = catalog.GetDistrictRoomTemplate(district, facilityIdx++);
                r.Name = tpl.Name;
                r.Terrain = Enum.TryParse<WorldTerrainType>(tpl.Terrain, ignoreCase: true, out var t) ? t : WorldTerrainType.Plains;
            }
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
