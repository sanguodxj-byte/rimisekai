using System;
using System.Collections.Generic;
using Rimisekai.WorldMap;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.PoiMap.Generator;

/// <summary>
/// 5x5 单块 POI 地图生成策略路由门面：
/// 聚落使用开放式生活布局；带 dungeonId 的 Roguelike 探索副本使用主题房间索引生成。
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
        string district = "",
        string dungeonId = "")
    {
        // 副本主题由 dungeon_defs.json 显式索引，不依赖地貌名称或 POI 文本判断。
        if (dungeonId.Length > 0)
            return DungeonBlockGenerator.Generate(blockX, blockY, regionId, ref nextRoomId, seed, dungeonId, preferredStart);

        // 村庄、城镇、王都、要塞等聚落采用开放式聚落策略（主道贯穿、广场草坪自然开阔，绝非迷宫）
        return SettlementBlockGenerator.Generate(blockX, blockY, regionId, ref nextRoomId, seed, preferredStart, district);
    }
}
