using System;
using System.Collections.Generic;
using Rimisekai.WorldMap;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.PoiMap.Generator;

/// <summary>
/// 5x5 单块 POI 地图生成策略路由门面：
/// 区分开放式生活聚落（村庄、城镇、王都、要塞）与封闭式地下迷宫（遗迹、洞窟），
/// 确保村镇开阔舒展、生活气息浓厚，地牢幽深曲折。
/// </summary>
public static class PoiBlockGenerator
{
    public static PoiBlock GenerateBlock(
        int blockX,
        int blockY,
        int regionId,
        ref int nextRoomId,
        int seed,
        int targetRoomCount = 25,
        (int x, int y)? preferredStart = null,
        string district = "")
    {
        // 遗迹与洞窟采用封闭式地牢迷宫生成策略
        if (district.Contains("Ruin", StringComparison.OrdinalIgnoreCase) ||
            district.Contains("Dungeon", StringComparison.OrdinalIgnoreCase))
        {
            return DungeonBlockGenerator.Generate(blockX, blockY, regionId, ref nextRoomId, seed, preferredStart, district);
        }

        // 村庄、城镇、王都、要塞等聚落采用开放式聚落策略（主道贯穿、广场草坪自然开阔，绝非迷宫）
        return SettlementBlockGenerator.Generate(blockX, blockY, regionId, ref nextRoomId, seed, preferredStart, district);
    }
}
