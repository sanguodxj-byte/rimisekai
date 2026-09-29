using System;
using System.Collections.Generic;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 水文模拟阶段（河流生成）：
/// 1. Priority-Flood 洼地填充计算单调排水高度 (Drainage Height)；
/// 2. 降水通量 (Flux) 自高向低向下游累积；
/// 3. 高山水源地筛选与非极大值抑制 (NMS)；
/// 4. 沿最陡下坡向海流动，支持干流与支流汇聚 (Confluence)；
/// 5. 记录河流流向掩码与流经进度。
/// </summary>
public static class RiverStage
{
    private const float MinFluxToFormRiver = 160f;
    private const int MinRiverLength = 8;
    private const float DrainageEpsilon = 0.0008f;

    public static void Execute(WorldMapData map)
    {
        var w = map.Width;
        var h = map.Height;

        // 1. 构建排水高度 (Priority-Flood 消除局部死水洼地)
        BuildDrainageHeights(map);

        // 2. 降水与通量累积 (Flux Accumulation)
        CalculatePrecipitationFlux(map);

        // 3. 寻找潜在源头候选点并执行 NMS (非极大值抑制)
        var sources = SelectRiverSources(map);

        // 4. 追踪生成河流
        TraceAndStampRivers(map, sources);
    }

    private static void BuildDrainageHeights(WorldMapData map)
    {
        var w = map.Width;
        var h = map.Height;
        var pq = new PriorityQueue<(int x, int y), float>();
        var visited = new bool[w, h];

        // 将所有水体（海洋、湖泊）及边缘格点作为基准流入池
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var t = map.Tiles[x, y];
                var isWater = t.Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater or WorldTerrainType.Lake;
                var isEdge = x == 0 || x == w - 1 || y == 0 || y == h - 1;

                if (isWater || isEdge)
                {
                    var baseHeight = isWater ? -1.0f : t.Elevation;
                    t.DrainageHeight = baseHeight;
                    visited[x, y] = true;
                    pq.Enqueue((x, y), baseHeight);
                }
            }
        }

        // Priority-Flood 向上淹没陆地，确保从任何陆地都有单调向下的流水通道
        while (pq.Count > 0)
        {
            var (cx, cy) = pq.Dequeue();
            var curHeight = map.Tiles[cx, cy].DrainageHeight;

            for (var i = 0; i < 4; i++)
            {
                var (dx, dy) = Direction4Extensions.Offsets[i];
                var nx = cx + dx;
                var ny = cy + dy;
                if (!map.InBounds(nx, ny) || visited[nx, ny])
                    continue;

                visited[nx, ny] = true;
                var nTile = map.Tiles[nx, ny];

                // 单调递增扰动（防止平原出现完全等高的环路）
                var jitter = (VariantHasher.Pick(nx, ny, 1000, 0x1234) / 1000.0f) * 0.0005f;
                var step = DrainageEpsilon + jitter;
                var fillHeight = System.Math.Max(nTile.Elevation, curHeight + step);

                nTile.DrainageHeight = fillHeight;
                pq.Enqueue((nx, ny), fillHeight);
            }
        }
    }

    private static void CalculatePrecipitationFlux(WorldMapData map)
    {
        var w = map.Width;
        var h = map.Height;
        var landTiles = new List<WorldTile>();

        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var t = map.Tiles[x, y];
                if (t.Terrain is not (WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater or WorldTerrainType.Lake))
                {
                    // 局部降水量 = 基础 + 湿度*18 + 高度迎风增量 + 森林地被增量
                    var rain = 4.0f + t.Moisture * 20.0f;
                    if (t.Elevation > 0.55f)
                        rain += (t.Elevation - 0.55f) * 15.0f;
                    if (t.Terrain is WorldTerrainType.Forest or WorldTerrainType.DenseForest or WorldTerrainType.Jungle)
                        rain += 3.0f;

                    t.WaterFlux = rain;
                    landTiles.Add(t);
                }
            }
        }

        // 按照排水高度从高到低排序，将通量向最低下游传递
        landTiles.Sort((a, b) => b.DrainageHeight.CompareTo(a.DrainageHeight));

        foreach (var tile in landTiles)
        {
            var downstream = FindSteepestDownstream(map, tile.X, tile.Y);
            if (downstream != null)
            {
                downstream.WaterFlux += tile.WaterFlux;
            }
        }
    }

    private static WorldTile? FindSteepestDownstream(WorldMapData map, int x, int y)
    {
        var cur = map.Tiles[x, y];
        WorldTile? best = null;
        var lowest = cur.DrainageHeight;

        for (var i = 0; i < 4; i++)
        {
            var (dx, dy) = Direction4Extensions.Offsets[i];
            var nx = x + dx;
            var ny = y + dy;
            if (!map.InBounds(nx, ny))
                continue;

            var neighbor = map.Tiles[nx, ny];
            if (neighbor.DrainageHeight < lowest)
            {
                lowest = neighbor.DrainageHeight;
                best = neighbor;
            }
        }

        return best;
    }

    private static List<(int x, int y)> SelectRiverSources(WorldMapData map)
    {
        var w = map.Width;
        var h = map.Height;
        var candidates = new List<(WorldTile tile, float score)>();

        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var t = map.Tiles[x, y];
                // 发源地需为高山、雪山或丘陵，并且有足够集水通量
                if (t.Terrain is WorldTerrainType.Mountain or WorldTerrainType.MountainSnow or WorldTerrainType.Hills)
                {
                    if (t.WaterFlux >= MinFluxToFormRiver)
                    {
                        var score = t.WaterFlux * (t.Elevation > 0.7f ? 1.5f : 1.0f);
                        candidates.Add((t, score));
                    }
                }
            }
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));

        // NMS：相互之间距离过近（例如 10 格内）的源头予以抑制
        var selected = new List<(int x, int y)>();
        const int nmsRadius = 10;
        const int nmsRadiusSq = nmsRadius * nmsRadius;

        foreach (var (cand, _) in candidates)
        {
            var tooClose = false;
            foreach (var (sx, sy) in selected)
            {
                var dx = cand.X - sx;
                var dy = cand.Y - sy;
                if (dx * dx + dy * dy < nmsRadiusSq)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                selected.Add((cand.X, cand.Y));
                if (selected.Count >= 24) // 限制最大主干河流数
                    break;
            }
        }

        return selected;
    }

    private static void TraceAndStampRivers(WorldMapData map, List<(int x, int y)> sources)
    {
        var riverId = 1;

        foreach (var (sx, sy) in sources)
        {
            var path = new List<(int x, int y)>();
            var visited = new HashSet<(int x, int y)>();
            var curX = sx;
            var curY = sy;
            var flowsIntoWater = false;
            var isConfluence = false;

            while (map.InBounds(curX, curY))
            {
                path.Add((curX, curY));
                visited.Add((curX, curY));

                var curTile = map.Tiles[curX, curY];

                // 遇到现有河流 -> 汇流
                if (curTile.IsRiver && path.Count > 1)
                {
                    isConfluence = true;
                    break;
                }

                // 寻找下游格点（带轻微惯性权重）
                var bestNeighbor = -1;
                var lowestHeight = curTile.DrainageHeight;

                for (var i = 0; i < 4; i++)
                {
                    var (dx, dy) = Direction4Extensions.Offsets[i];
                    var nx = curX + dx;
                    var ny = curY + dy;
                    if (!map.InBounds(nx, ny) || visited.Contains((nx, ny)))
                        continue;

                    var nt = map.Tiles[nx, ny];
                    if (nt.DrainageHeight < lowestHeight)
                    {
                        lowestHeight = nt.DrainageHeight;
                        bestNeighbor = i;
                    }
                }

                if (bestNeighbor < 0)
                    break; // 无更低下游，终止

                var (stepDx, stepDy) = Direction4Extensions.Offsets[bestNeighbor];
                curX += stepDx;
                curY += stepDy;

                var nextTile = map.Tiles[curX, curY];
                if (nextTile.Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater or WorldTerrainType.Lake)
                {
                    path.Add((curX, curY));
                    flowsIntoWater = true;
                    break;
                }
            }

            // 过滤过短无意义溪流
            if (path.Count < MinRiverLength && !isConfluence)
                continue;

            // 盖印河流到地图格点
            var maxFlux = 0.0f;
            for (var i = 0; i < path.Count; i++)
            {
                var (px, py) = path[i];
                var t = map.Tiles[px, py];
                maxFlux = System.Math.Max(maxFlux, t.WaterFlux);
                var progress = (float)i / System.Math.Max(1, path.Count - 1);

                t.RiverFlowProgress = t.RiverFlowProgress < 0 ? progress : System.Math.Max(t.RiverFlowProgress, progress);

                // 如果该格不是海洋/湖泊，且不是已有道路桥梁，则将地形标记为 River
                if (t.Terrain is not (WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater or WorldTerrainType.Lake))
                {
                    t.IsRiver = true;
                    t.Terrain = WorldTerrainType.River;
                }

                // 设置流向位掩码
                if (i > 0)
                {
                    var (prevX, prevY) = path[i - 1];
                    SetDirectionBit(t, prevX - px, prevY - py);
                }
                if (i < path.Count - 1)
                {
                    var (nextX, nextY) = path[i + 1];
                    SetDirectionBit(t, nextX - px, nextY - py);
                }
            }

            var river = new WorldRiver
            {
                Id = riverId++,
                Source = (sx, sy),
                Mouth = path[^1],
                MaxFlux = maxFlux,
                FlowsIntoWater = flowsIntoWater,
                IsConfluence = isConfluence,
            };
            river.Path.AddRange(path);
            map.Rivers.Add(river);
        }
    }

    private static void SetDirectionBit(WorldTile tile, int dx, int dy)
    {
        for (var dir = 0; dir < 4; dir++)
        {
            var (odx, ody) = Direction4Extensions.Offsets[dir];
            if (odx == dx && ody == dy)
            {
                tile.RiverDirections |= (byte)(1 << dir);
                break;
            }
        }
    }
}
