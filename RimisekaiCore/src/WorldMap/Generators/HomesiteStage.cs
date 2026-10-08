using System.Collections.Generic;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 领地选址阶段（路网之后）：在大世界上为玩家领地挑一块地并接入路网。
/// 选址规则：可住的陆地（平原 / 草原 / 稀树草地 / 森林 / 丘陵），不在聚落、道路、河道上，
/// 离任何聚落至少 <see cref="MinPoiDistance"/> 格（边境开荒，而非住进城里），离最近的道路不超过 <see cref="MaxRoadDistance"/> 格；
/// 按宜居度（地形底分＋临河＋临路）排序，从前段里按种子挑一块。
/// 选定后从领地铺一条野径（RoadClass 0）连到最近的道路，领地格本身也记入路网。
/// 全由世界种子决定：同种子同址，存档只需存种子。
/// </summary>
public static class HomesiteStage
{
    public const int MinPoiDistance = 6;
    public const int MaxRoadDistance = 10;

    public static void Execute(WorldMapData map)
    {
        var rng = new SeededRng(map.Seed + 4242);
        var roadDist = DistanceField(map, t => t.IsRoad);
        var poiDist = DistanceField(map, t => t.PoiId > 0);
        var candidates = new List<(int x, int y, float score)>();
        var margin = 6;
        for (var x = margin; x < map.Width - margin; x++)
        {
            for (var y = margin; y < map.Height - margin; y++)
            {
                var tile = map.Tiles[x, y];
                if (tile.IsRoad || tile.IsRiver || tile.PoiId > 0)
                    continue;
                var baseScore = tile.Terrain switch
                {
                    WorldTerrainType.Plains => 5f,
                    WorldTerrainType.Grassland => 4.5f,
                    WorldTerrainType.Savanna => 2.5f,
                    WorldTerrainType.Forest => 3f,
                    WorldTerrainType.Hills => 2f,
                    _ => 0f,
                };
                if (baseScore <= 0f)
                    continue;
                var pd = poiDist[x, y];
                var rd = roadDist[x, y];
                if (pd < MinPoiDistance || rd > MaxRoadDistance)
                    continue;
                var score = baseScore;
                if (NearRiver(map, x, y, 3))
                    score += 2f;
                score += 2f * (1f - rd / (float)MaxRoadDistance);
                // 别离聚落太远：边境但仍在一两日路程内。
                if (pd > 24)
                    score -= 2f;
                candidates.Add((x, y, score));
            }
        }

        if (candidates.Count == 0)
            FallbackCandidates(map, candidates);
        if (candidates.Count == 0)
            return;

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        var top = System.Math.Max(1, System.Math.Min(candidates.Count, 24));
        var pick = candidates[rng.NextRange(0, top)];
        map.SetHome(pick.x, pick.y);
        ConnectToRoads(map, pick.x, pick.y);
    }

    /// <summary>严格条件一块都选不出来（如路网缺失）：退到任意可住陆地。</summary>
    private static void FallbackCandidates(WorldMapData map, List<(int x, int y, float score)> candidates)
    {
        for (var x = 1; x < map.Width - 1; x++)
            for (var y = 1; y < map.Height - 1; y++)
            {
                var t = map.Tiles[x, y];
                if (t.PoiId > 0 || t.IsRiver)
                    continue;
                if (t.Terrain is WorldTerrainType.Plains or WorldTerrainType.Grassland or WorldTerrainType.Forest
                    or WorldTerrainType.Savanna or WorldTerrainType.Hills)
                    candidates.Add((x, y, t.Terrain == WorldTerrainType.Plains ? 2f : 1f));
            }
    }

    /// <summary>从领地铺一条野径到最近的道路格。</summary>
    private static void ConnectToRoads(WorldMapData map, int hx, int hy)
    {
        var home = map.Tiles[hx, hy];
        (int x, int y)? nearest = null;
        var best = int.MaxValue;
        for (var x = 0; x < map.Width; x++)
            for (var y = 0; y < map.Height; y++)
            {
                if (!map.Tiles[x, y].IsRoad)
                    continue;
                var d = System.Math.Abs(x - hx) + System.Math.Abs(y - hy);
                if (d < best)
                {
                    best = d;
                    nearest = (x, y);
                }
            }
        home.IsRoad = true;
        if (nearest == null)
            return;
        var path = RoadStage.FindLowCostPath(map, hx, hy, nearest.Value.x, nearest.Value.y);
        if (path.Count < 2)
            return;
        for (var i = 0; i < path.Count; i++)
        {
            var (px, py) = path[i];
            var tile = map.Tiles[px, py];
            var wasRoad = tile.IsRoad && i > 0;
            tile.IsRoad = true;
            if (i > 0)
                RoadStage.SetDirectionBit(tile, path[i - 1].x - px, path[i - 1].y - py);
            if (i < path.Count - 1)
                RoadStage.SetDirectionBit(tile, path[i + 1].x - px, path[i + 1].y - py);
            if (wasRoad)
                break;
        }
        var road = new WorldRoad { Id = map.Roads.Count + 1, FromPoiId = 0, ToPoiId = map.Tiles[nearest.Value.x, nearest.Value.y].PoiId, RoadClass = 0 };
        road.Path.AddRange(path);
        map.Roads.Add(road);
    }

    private static bool NearRiver(WorldMapData map, int x, int y, int r)
    {
        for (var dx = -r; dx <= r; dx++)
            for (var dy = -r; dy <= r; dy++)
            {
                var t = map.GetTile(x + dx, y + dy);
                if (t != null && (t.IsRiver || t.Terrain is WorldTerrainType.River or WorldTerrainType.Lake))
                    return true;
            }
        return false;
    }

    /// <summary>四邻 BFS 距离场：每格到最近一个满足条件的格的步数（无则 int.MaxValue）。</summary>
    private static int[,] DistanceField(WorldMapData map, System.Func<WorldTile, bool> seed)
    {
        var dist = new int[map.Width, map.Height];
        var queue = new Queue<(int x, int y)>();
        for (var x = 0; x < map.Width; x++)
            for (var y = 0; y < map.Height; y++)
            {
                if (seed(map.Tiles[x, y]))
                {
                    dist[x, y] = 0;
                    queue.Enqueue((x, y));
                }
                else
                    dist[x, y] = int.MaxValue;
            }
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            for (var i = 0; i < 4; i++)
            {
                var (dx, dy) = Direction4Extensions.Offsets[i];
                var nx = x + dx;
                var ny = y + dy;
                if (!map.InBounds(nx, ny) || dist[nx, ny] != int.MaxValue)
                    continue;
                dist[nx, ny] = dist[x, y] + 1;
                queue.Enqueue((nx, ny));
            }
        }
        return dist;
    }
}
