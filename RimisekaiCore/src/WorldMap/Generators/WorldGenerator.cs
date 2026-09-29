using System;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// 世界地图生成总控器：
/// 调度 BaseLayer -> Biome -> Smoothing -> River -> Settlement -> Road -> Region 完整管线。
/// </summary>
public static class WorldGenerator
{
    public const int DefaultWidth = 128;
    public const int DefaultHeight = 128;

    public static WorldMapData Generate(int seed = 0, int width = DefaultWidth, int height = DefaultHeight)
    {
        if (seed == 0)
            seed = 42;

        var map = new WorldMapData(width, height, seed);

        // 阶段 1：连续标量场生成（海拔、湿度、温度、养分）
        BaseLayerStage.Execute(map);

        // 阶段 2：生态规则判定
        BiomeStage.Execute(map);

        // 阶段 3：多数滤波形态平滑与海岸/内陆湖修复
        SmoothingStage.Execute(map, passes: 2);

        // 阶段 4：水文模拟（高山水源、降水通量累积、下坡河道与流向）
        RiverStage.Execute(map);

        // 阶段 5：聚落规划（宜居度评估、层次化聚落放置）
        SettlementStage.Execute(map);

        // 阶段 6：道路网络（Prim 最小生成树、低阻力铺设、跨河自动架桥）
        RoadStage.Execute(map);

        // 阶段 7：地理区域聚类与全景双语地名生成
        RegionStage.Execute(map);

        return map;
    }
}
