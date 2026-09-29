using System.Collections.Generic;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 聚落与兴趣点 (POI) 生成阶段：
/// 计算全土地理宜居度 (Habitability)，通过层次化泊松间距排斥采样散布城镇、城堡、村庄与遗迹。
/// </summary>
public static class SettlementStage
{
    public static void Execute(WorldMapData map)
    {
        var w = map.Width;
        var h = map.Height;
        var rng = new SeededRng(map.Seed + 8888);

        var scores = new float[w, h];
        var candidates = new List<(int x, int y, float score)>();

        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var t = map.Tiles[x, y];
                var score = EvaluateHabitability(map, t);
                scores[x, y] = score;
                if (score > 0.5f)
                    candidates.Add((x, y, score));
            }
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));

        var pois = new List<WorldPoi>();
        var poiId = 1;

        // 1. 放置首都 (1~2 个)
        PlacePoisOfType(candidates, pois, ref poiId, WorldPoiType.Capital, minDistance: 32, maxCount: 2);

        // 2. 放置大城镇 (Town)
        PlacePoisOfType(candidates, pois, ref poiId, WorldPoiType.Town, minDistance: 18, maxCount: 6);

        // 3. 放置城堡/要塞 (Castle / Fortress)
        PlacePoisOfType(candidates, pois, ref poiId, WorldPoiType.Castle, minDistance: 14, maxCount: 4);

        // 4. 放置乡村 (Village)
        PlacePoisOfType(candidates, pois, ref poiId, WorldPoiType.Village, minDistance: 10, maxCount: 12);

        // 5. 放置遗迹 (Ruin)
        PlaceRuins(map, pois, ref poiId, rng, maxCount: 5);

        // 登记到地图上
        foreach (var poi in pois)
        {
            map.Pois.Add(poi);
            map.Tiles[poi.X, poi.Y].PoiId = poi.Id;
        }
    }

    private static float EvaluateHabitability(WorldMapData map, WorldTile tile)
    {
        // 禁放水域与河流中央
        if (tile.Terrain is WorldTerrainType.DeepWater or WorldTerrainType.ShallowWater or WorldTerrainType.Lake or WorldTerrainType.River)
            return 0.0f;
        if (tile.Terrain is WorldTerrainType.MountainSnow or WorldTerrainType.Ice)
            return 0.0f;

        var baseScore = tile.Terrain switch
        {
            WorldTerrainType.Plains => 5.0f,
            WorldTerrainType.Grassland => 4.5f,
            WorldTerrainType.Savanna => 2.5f,
            WorldTerrainType.Forest => 2.0f,
            WorldTerrainType.Hills => 1.5f,
            WorldTerrainType.Sand => 0.5f,
            WorldTerrainType.DenseForest => 0.4f,
            WorldTerrainType.Taiga => 0.8f,
            WorldTerrainType.Wasteland => 0.1f,
            WorldTerrainType.Rocky => 0.3f,
            WorldTerrainType.Swamp => 0.1f,
            WorldTerrainType.Bog => 0.05f,
            WorldTerrainType.Mountain => 0.2f,
            _ => 1.0f,
        };

        // 水源邻近奖励 (2格内有淡水或海岸)
        var nearRiver = false;
        var nearCoast = false;

        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                var nx = tile.X + dx;
                var ny = tile.Y + dy;
                if (!map.InBounds(nx, ny))
                    continue;

                var nt = map.Tiles[nx, ny].Terrain;
                if (nt is WorldTerrainType.River or WorldTerrainType.Lake)
                    nearRiver = true;
                else if (nt is WorldTerrainType.ShallowWater)
                    nearCoast = true;
            }
        }

        if (nearRiver)
            baseScore *= 2.2f;
        else if (nearCoast)
            baseScore *= 1.4f;

        return baseScore;
    }

    private static void PlacePoisOfType(
        List<(int x, int y, float score)> candidates,
        List<WorldPoi> pois,
        ref int poiId,
        WorldPoiType type,
        int minDistance,
        int maxCount)
    {
        var placed = 0;
        var minDistSq = minDistance * minDistance;

        foreach (var (x, y, _) in candidates)
        {
            var tooClose = false;
            foreach (var existing in pois)
            {
                var dx = existing.X - x;
                var dy = existing.Y - y;
                if (dx * dx + dy * dy < minDistSq)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                pois.Add(new WorldPoi
                {
                    Id = poiId++,
                    Type = type,
                    X = x,
                    Y = y,
                });
                placed++;
                if (placed >= maxCount)
                    break;
            }
        }
    }

    private static void PlaceRuins(WorldMapData map, List<WorldPoi> pois, ref int poiId, SeededRng rng, int maxCount)
    {
        var w = map.Width;
        var h = map.Height;
        var placed = 0;
        var attempts = 0;

        while (placed < maxCount && attempts++ < 200)
        {
            var rx = rng.NextRange(5, w - 5);
            var ry = rng.NextRange(5, h - 5);
            var t = map.Tiles[rx, ry];

            // 遗迹倾向于在深山、荒原、丛林等偏僻处
            if (t.Terrain is WorldTerrainType.Mountain or WorldTerrainType.Hills or WorldTerrainType.DenseForest or WorldTerrainType.Jungle or WorldTerrainType.Wasteland or WorldTerrainType.Rocky)
            {
                var tooClose = false;
                foreach (var p in pois)
                {
                    var dx = p.X - rx;
                    var dy = p.Y - ry;
                    if (dx * dx + dy * dy < 144) // 至少间隔12格
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    pois.Add(new WorldPoi
                    {
                        Id = poiId++,
                        Type = WorldPoiType.Ruin,
                        X = rx,
                        Y = ry,
                    });
                    placed++;
                }
            }
        }
    }
}
