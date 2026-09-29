using System.Collections.Generic;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 区域聚类与双语命名阶段：
/// 1. 泛洪同宏观生态的陆地连通分量，提取主要自然地理区域；
/// 2. 计算各区域质心与环境均值；
/// 3. 为所有区域、水系、道路及聚落生成中英双语名称。
/// </summary>
public static class RegionStage
{
    private const int MinRegionSize = 16;

    public static void Execute(WorldMapData map)
    {
        var w = map.Width;
        var h = map.Height;
        var visited = new bool[w, h];
        var regionId = 1;

        // 1. 泛洪划分宏观地貌区域
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var tile = map.Tiles[x, y];
                // 水体跳过
                if (tile.Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater)
                    continue;

                if (!visited[x, y])
                {
                    var zoneTiles = FloodZone(map, visited, x, y);
                    if (zoneTiles.Count >= MinRegionSize)
                    {
                        var region = CreateRegion(map, regionId++, zoneTiles);
                        map.Regions.Add(region);
                        foreach (var (zx, zy) in zoneTiles)
                        {
                            map.Tiles[zx, zy].RegionId = region.Id;
                        }
                    }
                }
            }
        }

        // 2. 运行时程序化为每个 POI 动态派生纯正日式西幻轻小说名称并执行全局去重
        var usedPoiNames = new HashSet<string>();
        for (var i = 0; i < map.Pois.Count; i++)
        {
            var poi = map.Pois[i];
            var localTerrain = map.Tiles[poi.X, poi.Y].Terrain;
            var attempt = 0;
            string zh;
            do
            {
                var hIndex = poi.Id + attempt * 7919;
                zh = MapCatalog.Default.GenerateSettlementName(poi.Type, map.Seed, hIndex, localTerrain);
                attempt++;
            } while (usedPoiNames.Contains(zh) && attempt < 30);

            usedPoiNames.Add(zh);
            poi.NameZh = zh;
            poi.NameEn = $"{poi.Type} {poi.Id}";
            poi.RegionId = map.Tiles[poi.X, poi.Y].RegionId;
        }

        // 3. 为河流命名
        for (var i = 0; i < map.Rivers.Count; i++)
        {
            var river = map.Rivers[i];
            var (en, zh) = NameGenerator.GenerateRiverName(map.Seed, river.Id);
            river.NameEn = en;
            river.NameZh = zh;
        }

        // 4. 为道路命名
        for (var i = 0; i < map.Roads.Count; i++)
        {
            var road = map.Roads[i];
            var (en, zh) = NameGenerator.GenerateRoadName(map.Seed, road.Id);
            road.NameEn = en;
            road.NameZh = zh;
        }
    }

    private static List<(int x, int y)> FloodZone(WorldMapData map, bool[,] visited, int startX, int startY)
    {
        var list = new List<(int x, int y)>();
        var queue = new Queue<(int x, int y)>();
        var targetBiome = map.Tiles[startX, startY].Biome;

        visited[startX, startY] = true;
        queue.Enqueue((startX, startY));

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            list.Add((cx, cy));

            for (var i = 0; i < 4; i++)
            {
                var (dx, dy) = Direction4Extensions.Offsets[i];
                var nx = cx + dx;
                var ny = cy + dy;
                if (!map.InBounds(nx, ny) || visited[nx, ny])
                    continue;

                var nt = map.Tiles[nx, ny];
                if (nt.Biome == targetBiome && nt.Terrain is not (WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater))
                {
                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return list;
    }

    private static WorldRegion CreateRegion(WorldMapData map, int id, List<(int x, int y)> tiles)
    {
        var sumX = 0L;
        var sumY = 0L;
        var sumElev = 0.0f;
        var sumMoist = 0.0f;
        var sumTemp = 0.0f;
        var terrainCounts = new Dictionary<WorldTerrainType, int>();

        foreach (var (x, y) in tiles)
        {
            sumX += x;
            sumY += y;
            var t = map.Tiles[x, y];
            sumElev += t.Elevation;
            sumMoist += t.Moisture;
            sumTemp += t.Temperature;
            terrainCounts[t.Terrain] = terrainCounts.GetValueOrDefault(t.Terrain) + 1;
        }

        var count = tiles.Count;
        WorldTerrainType dominantTerrain = WorldTerrainType.Plains;
        var maxCount = 0;
        foreach (var (tt, c) in terrainCounts)
        {
            if (c > maxCount)
            {
                maxCount = c;
                dominantTerrain = tt;
            }
        }

        var (en, zh) = NameGenerator.GenerateRegionName(dominantTerrain, map.Seed, id);

        return new WorldRegion
        {
            Id = id,
            NameEn = en,
            NameZh = zh,
            Biome = map.Tiles[tiles[0].x, tiles[0].y].Biome,
            DominantTerrain = dominantTerrain,
            CentroidX = (int)(sumX / count),
            CentroidY = (int)(sumY / count),
            TileCount = count,
            AverageElevation = sumElev / count,
            AverageMoisture = sumMoist / count,
            AverageTemperature = sumTemp / count,
        };
    }
}
