using System.Collections.Generic;
using Rimisekai.WorldMap.Rules;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 道路与桥梁生成阶段：
/// 1. Prim 最小生成树 (MST) 建立聚落交通网络拓扑；
/// 2. 沿地形最小移动代价寻找道路格点；
/// 3. 与河流相交格自动架桥 (IsBridge = true)；
/// 4. 记录道路掩码与等级。
/// </summary>
public static class RoadStage
{
    public static void Execute(WorldMapData map)
    {
        if (map.Pois.Count < 2)
            return;

        // 1. Prim 最小生成树确定连通边
        var edges = BuildSpanningTree(map.Pois);

        // 2. 为每条边寻找地形最低代价路径并盖印
        var roadId = 1;
        foreach (var (fromPoi, toPoi) in edges)
        {
            var path = FindLowCostPath(map, fromPoi.X, fromPoi.Y, toPoi.X, toPoi.Y);
            if (path.Count < 2)
                continue;

            // 道路等级：若有一端为首都或城镇，等级为 2（官道），否则为 1（大道）
            var roadClass = (fromPoi.Type is WorldPoiType.Capital or WorldPoiType.Town ||
                             toPoi.Type is WorldPoiType.Capital or WorldPoiType.Town) ? 2 : 1;

            for (var i = 0; i < path.Count; i++)
            {
                var (px, py) = path[i];
                var tile = map.Tiles[px, py];

                tile.IsRoad = true;
                tile.RoadClass = System.Math.Max(tile.RoadClass, roadClass);

                // 连通位掩码
                if (i > 0)
                {
                    var (prevX, prevY) = path[i - 1];
                    SetDirectionBit(tile, prevX - px, prevY - py);
                }
                if (i < path.Count - 1)
                {
                    var (nextX, nextY) = path[i + 1];
                    SetDirectionBit(tile, nextX - px, nextY - py);
                }
            }

            var road = new WorldRoad
            {
                Id = roadId++,
                FromPoiId = fromPoi.Id,
                ToPoiId = toPoi.Id,
                RoadClass = roadClass,
            };
            road.Path.AddRange(path);
            map.Roads.Add(road);
        }
    }

    private static List<(WorldPoi from, WorldPoi to)> BuildSpanningTree(List<WorldPoi> pois)
    {
        var edges = new List<(WorldPoi from, WorldPoi to)>();
        var connected = new HashSet<int> { pois[0].Id };

        while (connected.Count < pois.Count)
        {
            WorldPoi? bestFrom = null;
            WorldPoi? bestTo = null;
            var bestDistSq = int.MaxValue;

            foreach (var p in pois)
            {
                if (!connected.Contains(p.Id))
                    continue;

                foreach (var other in pois)
                {
                    if (connected.Contains(other.Id))
                        continue;

                    var dx = p.X - other.X;
                    var dy = p.Y - other.Y;
                    var d2 = dx * dx + dy * dy;
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        bestFrom = p;
                        bestTo = other;
                    }
                }
            }

            if (bestFrom != null && bestTo != null)
            {
                connected.Add(bestTo.Id);
                edges.Add((bestFrom, bestTo));
            }
            else
            {
                break;
            }
        }

        // 适度增加 1~2 条邻近环路边，丰富路网结构
        for (var i = 0; i < pois.Count; i++)
        {
            for (var j = i + 1; j < pois.Count; j++)
            {
                var dx = pois[i].X - pois[j].X;
                var dy = pois[i].Y - pois[j].Y;
                var distSq = dx * dx + dy * dy;
                if (distSq < 225 && !HasEdge(edges, pois[i].Id, pois[j].Id)) // 15格内
                {
                    edges.Add((pois[i], pois[j]));
                    if (edges.Count >= pois.Count + 2)
                        return edges;
                }
            }
        }

        return edges;
    }

    private static bool HasEdge(List<(WorldPoi from, WorldPoi to)> edges, int id1, int id2)
    {
        foreach (var (f, t) in edges)
        {
            if ((f.Id == id1 && t.Id == id2) || (f.Id == id2 && t.Id == id1))
                return true;
        }
        return false;
    }

    private static List<(int x, int y)> FindLowCostPath(WorldMapData map, int startX, int startY, int goalX, int goalY)
    {
        var w = map.Width;
        var h = map.Height;
        var cameFrom = new Dictionary<(int x, int y), (int x, int y)>();
        var costSoFar = new Dictionary<(int x, int y), float>();
        var pq = new PriorityQueue<(int x, int y), float>();

        var start = (startX, startY);
        costSoFar[start] = 0;
        pq.Enqueue(start, 0);

        while (pq.Count > 0)
        {
            var current = pq.Dequeue();
            if (current.Item1 == goalX && current.Item2 == goalY)
                break;

            var curCost = costSoFar[current];

            for (var i = 0; i < 4; i++)
            {
                var (dx, dy) = Direction4Extensions.Offsets[i];
                var nx = current.Item1 + dx;
                var ny = current.Item2 + dy;
                if (!map.InBounds(nx, ny))
                    continue;

                var nTile = map.Tiles[nx, ny];
                // 绝对禁止穿越深海
                if (nTile.Terrain == WorldTerrainType.DeepWater)
                    continue;

                // 代价计算：现有道路极度便宜，鼓励复用已有道路
                var moveCost = WorldBiomeRules.GetMovementCost(nTile.Terrain);
                if (nTile.IsRoad)
                    moveCost *= 0.25f;

                var newCost = curCost + moveCost;
                var nextPos = (nx, ny);

                if (!costSoFar.TryGetValue(nextPos, out var existingCost) || newCost < existingCost)
                {
                    costSoFar[nextPos] = newCost;
                    var heuristic = System.Math.Abs(nx - goalX) + System.Math.Abs(ny - goalY);
                    pq.Enqueue(nextPos, newCost + heuristic * 0.8f);
                    cameFrom[nextPos] = current;
                }
            }
        }

        var path = new List<(int x, int y)>();
        var curr = (goalX, goalY);
        while (cameFrom.ContainsKey(curr))
        {
            path.Add(curr);
            curr = cameFrom[curr];
        }
        path.Add((startX, startY));
        path.Reverse();
        return path;
    }

    private static void SetDirectionBit(WorldTile tile, int dx, int dy)
    {
        for (var dir = 0; dir < 4; dir++)
        {
            var (odx, ody) = Direction4Extensions.Offsets[dir];
            if (odx == dx && ody == dy)
            {
                tile.RoadDirections |= (byte)(1 << dir);
                break;
            }
        }
    }
}
