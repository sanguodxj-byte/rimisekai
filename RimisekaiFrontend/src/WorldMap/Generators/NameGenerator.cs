using System;
using System.Collections.Generic;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.WorldMap.Generators;

/// <summary>
/// Rimisekai 日式西幻轻小说风格命名生成器。
/// 核心准则：
/// 1. 大门就是大门，道路就是道路，禁止强行堆砌修饰词；
/// 2. 地貌与设施名称保持简练、纯朴、直观；
/// 3. POI 聚落采用纯正日式西幻轻小说（Narou/开荒/异世界物语）风格命名。
/// </summary>
public static class NameGenerator
{
    // ==========================================
    // 1. 真实物理地貌（大门是大门，道路是道路）
    // ==========================================
    public static (string en, string zh) GenerateTerrainName(WorldTerrainType terrain, int seed = 0, int index = 0) => terrain switch
    {
        WorldTerrainType.Road => ("Road", "道路"),
        WorldTerrainType.Grassland => ("Grassland", "草原"),
        WorldTerrainType.Plains => ("Plains", "平原"),
        WorldTerrainType.Forest => ("Forest", "森林"),
        WorldTerrainType.DenseForest => ("Deep Forest", "密林"),
        WorldTerrainType.Jungle => ("Jungle", "树林"),
        WorldTerrainType.Taiga => ("Pine Forest", "针叶林"),
        WorldTerrainType.Mountain => ("Mountain", "山岳"),
        WorldTerrainType.MountainSnow => ("Snow Peak", "雪峰"),
        WorldTerrainType.Hills => ("Hills", "丘陵"),
        WorldTerrainType.Rocky => ("Rocky Area", "岩地"),
        WorldTerrainType.River => ("River", "河流"),
        WorldTerrainType.Lake => ("Lake", "湖泊"),
        WorldTerrainType.ShallowWater => ("Shallows", "浅滩"),
        WorldTerrainType.DeepWater => ("Sea", "大海"),
        WorldTerrainType.Sand => ("Wasteland", "荒漠"),
        WorldTerrainType.Snow => ("Snowfield", "雪原"),
        WorldTerrainType.Ice => ("Ice Field", "冰原"),
        WorldTerrainType.Swamp => ("Swamp", "沼泽"),
        WorldTerrainType.Bog => ("Mire", "泥沼"),
        WorldTerrainType.Savanna => ("Savanna", "稀树草地"),
        _ => ("Plains", "平原"),
    };

    public static (string en, string zh) GenerateRegionName(WorldTerrainType terrain, int seed = 0, int index = 0) =>
        GenerateTerrainName(terrain, seed, index);

    public static (string en, string zh) GenerateRiverName(int seed = 0, int index = 0)
    {
        var names = new (string en, string zh)[]
        {
            ("River", "河流"),
            ("Stream", "小溪"),
            ("River", "河道"),
            ("Clear Stream", "清溪"),
            ("River Crossing", "渡口"),
        };
        return names[(uint)index % (uint)names.Length];
    }

    public static (string en, string zh) GenerateRoadName(int seed = 0, int index = 0)
    {
        var names = new (string en, string zh)[]
        {
            ("Main Road", "道路"),
            ("Highway", "街道"),
            ("Forest Path", "林道"),
            ("Footpath", "小道"),
            ("Post Road", "驿路"),
        };
        return names[(uint)index % (uint)names.Length];
    }

    /// <summary>
    /// 运行时程序化动态生成纯正日式西幻轻小说风格的 POI 聚落与遗迹名称（完全由数据表驱动并在运行时拼缀）。
    /// </summary>
    public static (string en, string zh) GeneratePoiName(WorldPoiType type, int seed, int index)
    {
        var nameZh = MapCatalog.Default.GenerateSettlementName(type, seed, index);
        var nameEn = $"{type} {index}";
        return (nameEn, nameZh);
    }
}

