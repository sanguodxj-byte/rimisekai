using System;

namespace Rimisekai.WorldMap.Rules;

/// <summary>
/// 单源真理（SSOT）世界生态判定规则。
/// 严格依据 (海拔高程, 湿度, 温度) 三维连续标量，决定对应格点的细分地形与宏观生态。
/// </summary>
public static class WorldBiomeRules
{
    // 高程阈值
    public const float SeaLevel = 0.22f;          // 低于此为深海
    public const float ShallowLevel = 0.28f;      // 0.22~0.28 为浅海
    public const float BeachLevel = 0.32f;        // 0.28~0.32 为过渡海岸滩涂
    public const float HillLevel = 0.58f;         // 0.58~0.70 为丘陵
    public const float MountainLevel = 0.70f;     // 0.70 以上为山脉

    // 温度阈值
    public const float ColdThreshold = 0.30f;     // 低于此为寒带
    public const float HotThreshold = 0.72f;      // 高于此为热带

    // 湿度阈值
    public const float VeryDryThreshold = 0.18f;  // 极干燥
    public const float DryThreshold = 0.32f;      // 干燥
    public const float WetThreshold = 0.62f;      // 湿润

    /// <summary>
    /// 核心判定函数：根据高程、湿度、温度输出细分地形与宏观生态。
    /// </summary>
    public static (WorldTerrainType terrain, WorldBiomeType biome) Decide(float elevation, float moisture, float temperature)
    {
        // 1. 水域
        if (elevation < SeaLevel)
            return (WorldTerrainType.DeepWater, WorldBiomeType.Ocean);
        if (elevation < ShallowLevel)
            return (WorldTerrainType.ShallowWater, WorldBiomeType.Coastal);

        // 2. 潮滩/海滨低地过渡带
        if (elevation < BeachLevel)
        {
            if (temperature < ColdThreshold)
                return (WorldTerrainType.Ice, WorldBiomeType.Tundra);
            return (WorldTerrainType.Sand, WorldBiomeType.Coastal);
        }

        // 3. 高山/极顶
        if (elevation >= MountainLevel)
        {
            if (elevation > 0.88f || temperature < ColdThreshold)
                return (WorldTerrainType.MountainSnow, WorldBiomeType.Mountain);
            return (WorldTerrainType.Mountain, WorldBiomeType.Mountain);
        }

        // 4. 丘陵台地
        if (elevation >= HillLevel)
        {
            if (temperature < 0.18f)
                return (WorldTerrainType.Snow, WorldBiomeType.Tundra);
            if (temperature > HotThreshold && moisture < DryThreshold)
                return (WorldTerrainType.Rocky, WorldBiomeType.Wasteland);
            return (WorldTerrainType.Hills, WorldBiomeType.Mountain);
        }

        // 5. 广袤平原陆地层 (BeachLevel <= elevation < HillLevel)
        // 5.1 寒带 (温度 < ColdThreshold)
        if (temperature < ColdThreshold)
        {
            if (moisture < DryThreshold)
                return (WorldTerrainType.Rocky, WorldBiomeType.Tundra);
            if (moisture > WetThreshold)
                return (WorldTerrainType.Bog, WorldBiomeType.Tundra);
            return (WorldTerrainType.Taiga, WorldBiomeType.Tundra);
        }

        // 5.2 热带 (温度 > HotThreshold)
        if (temperature > HotThreshold)
        {
            if (moisture < VeryDryThreshold)
                return (WorldTerrainType.Sand, WorldBiomeType.Wasteland);
            if (moisture < DryThreshold)
                return (WorldTerrainType.Savanna, WorldBiomeType.Plains);
            if (moisture > WetThreshold)
                return (WorldTerrainType.Jungle, WorldBiomeType.Jungle);
            return (WorldTerrainType.Savanna, WorldBiomeType.Plains);
        }

        // 5.3 温带 (ColdThreshold <= 温度 <= HotThreshold)
        if (moisture < DryThreshold)
        {
            if (moisture < VeryDryThreshold)
                return (WorldTerrainType.Wasteland, WorldBiomeType.Wasteland);
            return (WorldTerrainType.Plains, WorldBiomeType.Plains);
        }

        if (moisture > WetThreshold)
        {
            if (temperature > 0.55f)
                return (WorldTerrainType.Swamp, WorldBiomeType.Swamp);
            return (WorldTerrainType.DenseForest, WorldBiomeType.Forest);
        }

        // 适中湿度过渡：草原 -> 草甸 -> 森林
        var midMoisture = (moisture - DryThreshold) / (WetThreshold - DryThreshold);
        if (midMoisture < 0.35f)
            return (WorldTerrainType.Plains, WorldBiomeType.Plains);
        if (midMoisture < 0.70f)
            return (WorldTerrainType.Grassland, WorldBiomeType.Plains);

        return (WorldTerrainType.Forest, WorldBiomeType.Forest);
    }

    /// <summary>
    /// 地形基础通行阻力（用于生成期道路铺设与水系阻力，非运行时实体寻路）。
    /// </summary>
    public static float GetMovementCost(WorldTerrainType terrain) => terrain switch
    {
        WorldTerrainType.Plains => 1.0f,
        WorldTerrainType.Grassland => 1.0f,
        WorldTerrainType.Savanna => 1.2f,
        WorldTerrainType.Forest => 1.5f,
        WorldTerrainType.Sand => 1.8f,
        WorldTerrainType.Taiga => 1.6f,
        WorldTerrainType.Rocky => 2.0f,
        WorldTerrainType.Hills => 2.2f,
        WorldTerrainType.DenseForest => 2.8f,
        WorldTerrainType.Jungle => 3.0f,
        WorldTerrainType.Bog => 3.5f,
        WorldTerrainType.Swamp => 3.5f,
        WorldTerrainType.Wasteland => 1.8f,
        WorldTerrainType.Snow => 2.5f,
        WorldTerrainType.Ice => 2.5f,
        WorldTerrainType.ShallowWater => 6.0f,
        WorldTerrainType.DeepWater => 99.0f,
        WorldTerrainType.Mountain => 8.0f,
        WorldTerrainType.MountainSnow => 15.0f,
        WorldTerrainType.Road => 0.4f,
        WorldTerrainType.River => 5.0f,
        WorldTerrainType.Lake => 99.0f,
        _ => 2.0f,
    };
}
