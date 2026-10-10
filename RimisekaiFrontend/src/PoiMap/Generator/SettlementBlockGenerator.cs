using System;
using System.Collections.Generic;
using Rimisekai.WorldMap;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.PoiMap.Generator;

/// <summary>
/// 开放式聚落（村庄、城镇、王都、要塞）生成器：
/// 遵循真实生活与日式轻小说世界观，构建开阔连贯的主干大道、中央广场与田园绿地，
/// 绝非封闭式地下迷宫。设施沿街坐落，四通八达。
/// </summary>
public static class SettlementBlockGenerator
{
    private const int Size = 5;

    public static PoiBlock Generate(
        int blockX,
        int blockY,
        int regionId,
        ref int nextRoomId,
        int seed,
        (int x, int y)? preferredStart = null,
        string district = "")
    {
        var block = new PoiBlock(blockX, blockY, regionId);
        var rng = new SeededRng(seed ^ (blockX * 73856 + blockY * 19349));
        var catalog = MapCatalog.Default;

        // 1. 初始化 5x5 的 25 个房间格点
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var room = new PoiRoom(nextRoomId++, blockX, blockY, x, y, regionId)
                {
                    Name = "草地",
                    Terrain = WorldTerrainType.Grassland,
                    RoomType = PoiRoomType.Normal,
                };
                block.Grid[x, y] = room;
                block.Rooms.Add(room);
            }
        }

        // 2. 规划主干大道 (Main Street)
        // 默认第 2 行水平横穿 (从 (0,2) 到 (4,2))，或者十字大街
        var isCrossStreet = rng.NextBool(0.4f) || district.Contains("Market");

        // 横贯主道
        for (var x = 0; x < Size; x++)
        {
            var roadRoom = block.Grid[x, 2]!;
            roadRoom.Terrain = WorldTerrainType.Road;
            roadRoom.Name = "道路";
            if (x > 0)
                LinkRooms(block.Grid[x - 1, 2]!, roadRoom);
        }

        // 若为十字大街，增加纵贯道路
        if (isCrossStreet)
        {
            for (var y = 0; y < Size; y++)
            {
                var roadRoom = block.Grid[2, y]!;
                roadRoom.Terrain = WorldTerrainType.Road;
                roadRoom.Name = "道路";
                if (y > 0)
                    LinkRooms(block.Grid[2, y - 1]!, roadRoom);
            }
        }

        // 3. 确定大门（起点）与出口（终点），均严格坐落在道路两端的边缘格上
        int startX = 0, startY = 2;
        int endX = 4, endY = 2;

        if (preferredStart.HasValue && IsEdgeCoord(preferredStart.Value.x, preferredStart.Value.y))
        {
            startX = preferredStart.Value.x;
            startY = preferredStart.Value.y;
            // 终点自动取对称或对向边缘格
            endX = startX == 0 ? 4 : (startX == 4 ? 0 : startX);
            endY = startY == 0 ? 4 : (startY == 4 ? 0 : startY);
            if (endX == startX && endY == startY)
                endX = 4;
        }

        var startRoom = block.Grid[startX, startY]!;
        startRoom.IsStart = true;
        startRoom.RoomType = PoiRoomType.Start;
        startRoom.Terrain = WorldTerrainType.Road;
        startRoom.Name = "大门";
        block.StartRoom = startRoom;

        var endRoom = block.Grid[endX, endY]!;
        endRoom.IsEnd = true;
        endRoom.RoomType = PoiRoomType.End;
        endRoom.Terrain = WorldTerrainType.Road;
        endRoom.Name = "出口";
        block.EndRoom = endRoom;

        // 4. 设立中央核心区域（中央广场 / 庭院）
        var centerRoom = block.Grid[2, 2]!;
        if (!centerRoom.IsStart && !centerRoom.IsEnd)
        {
            centerRoom.Name = district.Contains("Village") ? "庭院" : "中央广场";
            centerRoom.Terrain = WorldTerrainType.Plains;
        }

        // 5. 有商店的分区先在临街第一格开商店（买卖只在这里做），其余沿街与外围分布设施与自然地貌
        if (catalog.ShopOf(district) is { } shop)
            PlaceShop(block, shop);
        DecorateSettlement(block, catalog, district, rng);

        // 6. 开放式连通：确保全图 25 格完全连通，且草地、农田、庭院与道路之间自由通行
        EnsureSettlementConnectivity(block);

        return block;
    }

    private static void DecorateSettlement(PoiBlock block, MapCatalog catalog, string district, SeededRng rng)
    {
        var facilityIdx = 0;

        // 沿街排布两侧的关键设施 (例如第 1 行与第 3 行紧邻主道的位置)
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var r = block.Grid[x, y]!;
                if (r.IsStart || r.IsEnd || r.Shop || r.Terrain == WorldTerrainType.Road || (x == 2 && y == 2))
                    continue;

                // 紧邻道路的格子优先作为工坊、酒馆、仓库、旅馆、教会
                var isNearRoad = (y == 1 || y == 3 || x == 1 || x == 3);

                if (isNearRoad && rng.NextBool(0.6f))
                {
                    var templateIndex = facilityIdx++;
                    var tpl = catalog.GetDistrictRoomTemplate(district, templateIndex);
                    r.RoomTemplateId = tpl.Id;
                    var facility = catalog.GetDistrictFacilityTemplate(district, tpl.Id, templateIndex);
                    if (facility != null)
                    {
                        r.FacilityIds.Add(facility.Id);
                        r.Name = facility.Name;
                    }
                    else
                    {
                        r.Name = tpl.Name;
                    }
                    r.Terrain = Enum.TryParse<WorldTerrainType>(tpl.Terrain, ignoreCase: true, out var t) ? t : WorldTerrainType.Plains;
                }
                else
                {
                    // 其余外围格子自然舒展为开阔草地、庭院或后院
                    r.Name = rng.NextBool(0.7f) ? "草地" : "庭院";
                    r.Terrain = WorldTerrainType.Grassland;
                }
            }
        }
    }

    /// <summary>商店落在扫描顺序里第一格紧挨道路的空地上（不占大门、出口、道路与中央广场）。</summary>
    private static void PlaceShop(PoiBlock block, FacilityTemplateDef shop)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var r = block.Grid[x, y]!;
                if (r.IsStart || r.IsEnd || r.Terrain == WorldTerrainType.Road || (x == 2 && y == 2))
                    continue;
                var byRoad = false;
                foreach (var (dx, dy) in Direction4Extensions.Offsets)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    byRoad |= nx >= 0 && nx < Size && ny >= 0 && ny < Size && block.Grid[nx, ny]!.Terrain == WorldTerrainType.Road;
                }
                if (!byRoad)
                    continue;
                r.Shop = true;
                r.Name = shop.Name;
                r.RoomTemplateId = shop.RoomIds[0];
                r.Terrain = WorldTerrainType.Plains;
                r.FacilityIds.Add(shop.Id);
                return;
            }
        }
    }

    private static void EnsureSettlementConnectivity(PoiBlock block)
    {
        // 聚落是开阔的：各格连接其临近的道路或广场，草坪与建筑自然通达
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var cur = block.Grid[x, y]!;

                // 向上连通
                if (y > 0)
                {
                    var up = block.Grid[x, y - 1]!;
                    if (cur.Terrain == WorldTerrainType.Road && up.Terrain == WorldTerrainType.Road)
                        LinkRooms(cur, up);
                    else if (cur.Terrain == WorldTerrainType.Road || up.Terrain == WorldTerrainType.Road)
                        LinkRooms(cur, up);
                }

                // 向左连通
                if (x > 0)
                {
                    var left = block.Grid[x - 1, y]!;
                    if (cur.Terrain == WorldTerrainType.Road && left.Terrain == WorldTerrainType.Road)
                        LinkRooms(cur, left);
                    else if (cur.Terrain == WorldTerrainType.Road || left.Terrain == WorldTerrainType.Road)
                        LinkRooms(cur, left);
                }
            }
        }

        // 确保没有任何孤立格点，若某非道路格仍未与主道连通，连向最近的道路邻居
        var mainRoad = block.StartRoom!;
        var reachable = GetReachableFrom(block, mainRoad.Id);

        foreach (var r in block.Rooms)
        {
            if (!reachable.Contains(r.Id))
            {
                // 连向四周任意合法的邻居
                foreach (var (dx, dy) in Direction4Extensions.Offsets)
                {
                    var nx = r.LocalX + dx;
                    var ny = r.LocalY + dy;
                    if (nx >= 0 && nx < Size && ny >= 0 && ny < Size)
                    {
                        var neighbor = block.Grid[nx, ny]!;
                        LinkRooms(r, neighbor);
                        break;
                    }
                }
            }
        }
    }

    private static HashSet<int> GetReachableFrom(PoiBlock block, int startId)
    {
        var visited = new HashSet<int> { startId };
        var q = new Queue<int>();
        q.Enqueue(startId);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            var r = block.Rooms.Find(x => x.Id == cur);
            if (r == null) continue;
            foreach (var n in r.Links)
            {
                if (visited.Add(n))
                    q.Enqueue(n);
            }
        }

        return visited;
    }

    private static void LinkRooms(PoiRoom a, PoiRoom b)
    {
        if (!a.Links.Contains(b.Id))
            a.Links.Add(b.Id);
        if (!b.Links.Contains(a.Id))
            b.Links.Add(a.Id);
    }

    private static bool IsEdgeCoord(int x, int y) =>
        x == 0 || x == Size - 1 || y == 0 || y == Size - 1;
}
