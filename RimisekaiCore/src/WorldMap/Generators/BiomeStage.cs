using Rimisekai.WorldMap.Rules;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 生态与地形映射阶段：
/// 调用单源规则 WorldBiomeRules.Decide，完成初始地形分配。
/// </summary>
public static class BiomeStage
{
    public static void Execute(WorldMapData map)
    {
        var w = map.Width;
        var h = map.Height;

        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var tile = map.Tiles[x, y];
                var (terrain, biome) = WorldBiomeRules.Decide(tile.Elevation, tile.Moisture, tile.Temperature);
                tile.Terrain = terrain;
                tile.Biome = biome;
            }
        }
    }
}
