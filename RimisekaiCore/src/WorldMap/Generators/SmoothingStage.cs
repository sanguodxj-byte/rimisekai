using System.Collections.Generic;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 地形聚类与平滑阶段：
/// 1. 多数滤波（消除高频离散噪点，合并微碎片）；
/// 2. 海岸线过渡修复（确保深海与陆地之间有浅海过渡环带）；
/// 3. 内陆深水转换为湖泊（Lake）。
/// </summary>
public static class SmoothingStage
{
    public static void Execute(WorldMapData map, int passes = 2)
    {
        var w = map.Width;
        var h = map.Height;
        var rng = new SeededRng(map.Seed + 4321);

        // 1. 多数滤波平滑
        for (var p = 0; p < passes; p++)
        {
            var nextTerrain = new WorldTerrainType[w, h];

            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                {
                    var cur = map.Tiles[x, y].Terrain;
                    // 水系豁免
                    if (cur is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater or WorldTerrainType.Road or WorldTerrainType.River)
                    {
                        nextTerrain[x, y] = cur;
                        continue;
                    }

                    // 统计 8 邻域
                    var counts = new Dictionary<WorldTerrainType, int>();
                    var totalNeighbors = 0;

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;
                            var nx = x + dx;
                            var ny = y + dy;
                            if (!map.InBounds(nx, ny))
                                continue;

                            var nt = map.Tiles[nx, ny].Terrain;
                            counts[nt] = counts.GetValueOrDefault(nt) + 1;
                            totalNeighbors++;
                        }
                    }

                    // 如果某同类邻居占据 5 个或以上且与当前不同，有概率融合
                    WorldTerrainType dominant = cur;
                    var maxCount = 0;
                    foreach (var (t, count) in counts)
                    {
                        if (count > maxCount)
                        {
                            maxCount = count;
                            dominant = t;
                        }
                    }

                    if (dominant != cur && maxCount >= 5 && rng.NextBool(0.7f))
                        nextTerrain[x, y] = dominant;
                    else
                        nextTerrain[x, y] = cur;
                }
            }

            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                {
                    map.Tiles[x, y].Terrain = nextTerrain[x, y];
                }
            }
        }

        // 2. 海岸线过渡修复：如果 DeepWater 紧邻非水陆地，且两者之间无缓冲，将其转为 ShallowWater
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var tile = map.Tiles[x, y];
                if (tile.Terrain == WorldTerrainType.DeepWater)
                {
                    var hasLandNeighbor = false;
                    foreach (var (dx, dy) in Direction4Extensions.Offsets)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (!map.InBounds(nx, ny))
                            continue;
                        var nt = map.Tiles[nx, ny].Terrain;
                        if (nt is not WorldTerrainType.DeepWater and not WorldTerrainType.ShallowWater)
                        {
                            hasLandNeighbor = true;
                            break;
                        }
                    }

                    if (hasLandNeighbor)
                        tile.Terrain = WorldTerrainType.ShallowWater;
                }
            }
        }

        // 3. 浅海若完全被陆地包围，且没有通路抵达外海边界，标记为内陆湖泊 (Lake)
        TagInlandLakes(map);
    }

    private static void TagInlandLakes(WorldMapData map)
    {
        var w = map.Width;
        var h = map.Height;
        var visited = new bool[w, h];
        var oceanQueue = new Queue<(int x, int y)>();

        // 从地图四周边缘搜寻真正的开阔大洋
        for (var x = 0; x < w; x++)
        {
            CheckEdge(map, visited, oceanQueue, x, 0);
            CheckEdge(map, visited, oceanQueue, x, h - 1);
        }
        for (var y = 0; y < h; y++)
        {
            CheckEdge(map, visited, oceanQueue, 0, y);
            CheckEdge(map, visited, oceanQueue, w - 1, y);
        }

        // BFS 扩展所有与边缘大洋连通的水域
        while (oceanQueue.Count > 0)
        {
            var (cx, cy) = oceanQueue.Dequeue();
            foreach (var (dx, dy) in Direction4Extensions.Offsets)
            {
                var nx = cx + dx;
                var ny = cy + dy;
                if (!map.InBounds(nx, ny) || visited[nx, ny])
                    continue;

                var nt = map.Tiles[nx, ny].Terrain;
                if (nt is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater)
                {
                    visited[nx, ny] = true;
                    oceanQueue.Enqueue((nx, ny));
                }
            }
        }

        // 未被边缘大洋接触到的孤立水体转为 Lake
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var tile = map.Tiles[x, y];
                if ((tile.Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater) && !visited[x, y])
                {
                    tile.Terrain = WorldTerrainType.Lake;
                }
            }
        }
    }

    private static void CheckEdge(WorldMapData map, bool[,] visited, Queue<(int x, int y)> queue, int x, int y)
    {
        if (visited[x, y])
            return;
        var t = map.Tiles[x, y].Terrain;
        if (t is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater)
        {
            visited[x, y] = true;
            queue.Enqueue((x, y));
        }
    }
}
