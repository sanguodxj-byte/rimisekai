using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Rimisekai.WorldMap.Math;

namespace Rimisekai.WorldMap;

public sealed class RoomTemplateDef
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Terrain { get; set; } = "Plains";
}

public sealed class TerrainDefEntry
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class PoiSettlementDefEntry
{
    public string Type { get; set; } = "";
    public List<string> Names { get; set; } = new();
}

public sealed class PoiScaleDef
{
    public string Type { get; set; } = "";
    public int BlocksW { get; set; } = 1;
    public int BlocksH { get; set; } = 1;
    public List<string> Districts { get; set; } = new();
}

public sealed class DistrictItemDef
{
    public string Name { get; set; } = "";
    public string Terrain { get; set; } = "Plains";
}

public sealed class DistrictTemplateDef
{
    public string Name { get; set; } = "";
    public List<DistrictItemDef> PreferredNature { get; set; } = new();
    public List<DistrictItemDef> Facilities { get; set; } = new();
}

public sealed class SettlementGrammarDef
{
    public List<string> SyllablePrefixes { get; set; } = new();
    public List<string> SyllableSuffixes { get; set; } = new();
    public Dictionary<string, List<string>> FeaturePrefixes { get; set; } = new();
    public Dictionary<string, List<string>> TypeSuffixes { get; set; } = new();
}

public sealed class MapDefsTable
{
    public List<TerrainDefEntry> Terrains { get; set; } = new();
    public List<PoiSettlementDefEntry> PoiSettlements { get; set; } = new();
    public List<RoomTemplateDef> PoiRoomTemplates { get; set; } = new();
    public List<PoiScaleDef> PoiScales { get; set; } = new();
    public List<DistrictTemplateDef> DistrictTemplates { get; set; } = new();
    public SettlementGrammarDef? SettlementGrammar { get; set; }
}

/// <summary>
/// 地图与房间定义数据表目录：
/// 驱动全部地貌、POI 与房间模板的生成，支持外部数据表扩充成百上千种房间。
/// </summary>
public sealed class MapCatalog
{
    private static MapCatalog? _instance;
    public static MapCatalog Default => _instance ??= CreateDefault();

    private readonly Dictionary<WorldTerrainType, string> _terrainNames = new();
    private readonly Dictionary<string, List<string>> _poiNames = new();
    private readonly Dictionary<WorldPoiType, PoiScaleDef> _poiScales = new();
    private readonly Dictionary<string, DistrictTemplateDef> _districtTemplates = new();

    private readonly List<RoomTemplateDef> _entrances = new();
    private readonly List<RoomTemplateDef> _exits = new();
    private readonly List<RoomTemplateDef> _roads = new();
    private readonly List<RoomTemplateDef> _openSpaces = new();
    private readonly List<RoomTemplateDef> _facilities = new();
    private readonly List<RoomTemplateDef> _treasures = new();
    private readonly List<RoomTemplateDef> _shrines = new();

    public string GetTerrainName(WorldTerrainType terrain) =>
        _terrainNames.TryGetValue(terrain, out var name) ? name : "平原";

    public RoomTemplateDef GetEntranceTemplate() =>
        _entrances.Count > 0 ? _entrances[0] : new RoomTemplateDef { Name = "大门", Category = "Entrance", Terrain = "Road" };

    public RoomTemplateDef GetExitTemplate() =>
        _exits.Count > 0 ? _exits[0] : new RoomTemplateDef { Name = "出口", Category = "Exit", Terrain = "Road" };

    public RoomTemplateDef GetRoadTemplate() =>
        _roads.Count > 0 ? _roads[0] : new RoomTemplateDef { Name = "道路", Category = "Thoroughfare", Terrain = "Road" };

    public RoomTemplateDef GetTreasureTemplate() =>
        _treasures.Count > 0 ? _treasures[0] : new RoomTemplateDef { Name = "藏宝库", Category = "Treasure", Terrain = "Plains" };

    public RoomTemplateDef GetShrineTemplate() =>
        _shrines.Count > 0 ? _shrines[0] : new RoomTemplateDef { Name = "神龛", Category = "Shrine", Terrain = "Plains" };

    /// <summary>
    /// 根据 POI 类型获取推荐拼接规模 (blocksW, blocksH) 与各 5x5 块的分区主题列表。
    /// </summary>
    public (int blocksW, int blocksH, IReadOnlyList<string> districts) GetPoiScale(WorldPoiType type)
    {
        if (_poiScales.TryGetValue(type, out var scale) && scale.BlocksW > 0 && scale.BlocksH > 0)
        {
            return (scale.BlocksW, scale.BlocksH, scale.Districts);
        }

        // 默认规模降级
        return type switch
        {
            WorldPoiType.Capital => (2, 2, new[] { "CapitalGateDistrict", "CapitalMarketDistrict", "CapitalCraftDistrict", "CapitalSanctuaryDistrict" }),
            WorldPoiType.Town or WorldPoiType.Fortress => (2, 1, new[] { "TownGateDistrict", "TownMarketDistrict" }),
            WorldPoiType.Castle => (1, 2, new[] { "CastleGateDistrict", "CastleKeepDistrict" }),
            _ => (1, 1, new[] { "VillageDistrict" }),
        };
    }

    /// <summary>
    /// 根据分区主题提供房间定义。优先保证自然开阔的草坪、庭院、道路，绝不硬塞一堆功能设施。
    /// </summary>
    public RoomTemplateDef GetDistrictRoomTemplate(string districtName, int index)
    {
        if (_districtTemplates.TryGetValue(districtName, out var dist))
        {
            var pool = new List<RoomTemplateDef>();
            // 自然地貌占多数比例（留足草坪、庭院、林地、道路）
            foreach (var n in dist.PreferredNature)
                pool.Add(new RoomTemplateDef { Name = n.Name, Category = "Nature", Terrain = n.Terrain });
            // 少量专属功能设施
            foreach (var f in dist.Facilities)
                pool.Add(new RoomTemplateDef { Name = f.Name, Category = "Facility", Terrain = f.Terrain });

            if (pool.Count > 0)
                return pool[(int)((uint)index % (uint)pool.Count)];
        }

        return GetFacilityTemplate(index);
    }

    public RoomTemplateDef GetFacilityTemplate(int index)
    {
        var pool = new List<RoomTemplateDef>();
        pool.AddRange(_openSpaces);
        pool.AddRange(_facilities);
        if (pool.Count == 0)
            return new RoomTemplateDef { Name = "庭院", Category = "OpenSpace", Terrain = "Grassland" };
        return pool[(int)((uint)index % (uint)pool.Count)];
    }

    public SettlementGrammarDef Grammar { get; } = new();

    /// <summary>
    /// 运行时程序化动态生成纯正日式西幻轻小说聚落名（根据种子、坐标与地貌特征拼缀派生，绝非静态死板列表）。
    /// </summary>
    public string GenerateSettlementName(WorldPoiType type, int seed, int index, WorldTerrainType localTerrain = WorldTerrainType.Plains)
    {
        var h = VariantHasher.HashCoord(seed, index, 0x534554); // "SET"

        if (type == WorldPoiType.Capital)
        {
            var p = Grammar.SyllablePrefixes.Count > 0 ? Grammar.SyllablePrefixes[(int)(h % (uint)Grammar.SyllablePrefixes.Count)] : "王都";
            var sufs = Grammar.TypeSuffixes.TryGetValue("Capital", out var list) && list.Count > 0 ? list : new List<string> { "王都", "帝国王都" };
            var suf = sufs[(int)((h >> 3) % (uint)sufs.Count)];
            return (h & 1) == 0 ? $"{p}{suf}" : $"{suf}·{p}";
        }

        if (type == WorldPoiType.Town)
        {
            var isSyllable = (h % 3) != 0;
            if (isSyllable && Grammar.SyllablePrefixes.Count > 0 && Grammar.SyllableSuffixes.Count > 0)
            {
                var p = Grammar.SyllablePrefixes[(int)(h % (uint)Grammar.SyllablePrefixes.Count)];
                var s = Grammar.SyllableSuffixes[(int)((h >> 4) % (uint)Grammar.SyllableSuffixes.Count)];
                var townSuf = (h & 2) == 0 ? "镇" : "城";
                return $"{p}{s}{townSuf}";
            }
            var featKey = localTerrain == WorldTerrainType.Mountain ? "Mountain" : (localTerrain == WorldTerrainType.River ? "River" : "Grassland");
            if (Grammar.FeaturePrefixes.TryGetValue(featKey, out var feats) && feats.Count > 0)
            {
                var fp = feats[(int)((h >> 2) % (uint)feats.Count)];
                return $"{fp}镇";
            }
            return "边境镇";
        }

        if (type == WorldPoiType.Village)
        {
            if ((h & 1) == 0 && Grammar.SyllablePrefixes.Count > 0)
            {
                var p = Grammar.SyllablePrefixes[(int)(h % (uint)Grammar.SyllablePrefixes.Count)];
                var s = Grammar.SyllableSuffixes.Count > 0 ? Grammar.SyllableSuffixes[(int)((h >> 3) % (uint)Grammar.SyllableSuffixes.Count)] : "";
                return $"{p}{s}村";
            }
            var featKey = localTerrain == WorldTerrainType.Forest ? "Forest" : (localTerrain == WorldTerrainType.River ? "River" : "Grassland");
            if (Grammar.FeaturePrefixes.TryGetValue(featKey, out var feats) && feats.Count > 0)
            {
                var fp = feats[(int)((h >> 2) % (uint)feats.Count)];
                return $"{fp}村";
            }
            return "开拓村";
        }

        if (type == WorldPoiType.Castle || type == WorldPoiType.Fortress)
        {
            var p = Grammar.SyllablePrefixes.Count > 0 ? Grammar.SyllablePrefixes[(int)(h % (uint)Grammar.SyllablePrefixes.Count)] : "守望";
            var s = (h & 2) == 0 ? "要塞" : "砦";
            return $"{p}{s}";
        }

        if (type == WorldPoiType.Ruin)
        {
            if (Grammar.TypeSuffixes.TryGetValue("Ruin", out var ruins) && ruins.Count > 0)
                return ruins[(int)(h % (uint)ruins.Count)];
            return "古代遗迹";
        }

        if (type == WorldPoiType.Monastery)
        {
            if (Grammar.TypeSuffixes.TryGetValue("Monastery", out var shrines) && shrines.Count > 0)
                return shrines[(int)(h % (uint)shrines.Count)];
            return "大教会";
        }

        return "村";
    }

    public string GetPoiSettlementName(WorldPoiType type, int index) =>
        GenerateSettlementName(type, 42, index);

    public static MapCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var table = JsonSerializer.Deserialize<MapDefsTable>(json, options) ?? new MapDefsTable();
        return BuildFromTable(table);
    }

    public static MapCatalog LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return CreateDefault();
        var json = File.ReadAllText(filePath);
        return LoadFromJson(json);
    }

    private static MapCatalog CreateDefault()
    {
        // 尝试从项目约定位置 content/map_defs.json 读取
        var paths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "content", "map_defs.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "content", "map_defs.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "content", "map_defs.json"),
            "D:\\123\\rimisekai\\content\\map_defs.json",
        };

        foreach (var p in paths)
        {
            if (File.Exists(p))
            {
                try
                {
                    return LoadFromFile(p);
                }
                catch
                {
                    // 降级使用内置标准化表
                }
            }
        }

        // 内置备用默认表（与 content/map_defs.json 完全一致）
        var table = new MapDefsTable
        {
            Terrains =
            {
                new() { Type = "Road", Name = "道路" },
                new() { Type = "Grassland", Name = "草原" },
                new() { Type = "Plains", Name = "平原" },
                new() { Type = "Forest", Name = "森林" },
                new() { Type = "DenseForest", Name = "密林" },
                new() { Type = "Jungle", Name = "树林" },
                new() { Type = "Taiga", Name = "针叶林" },
                new() { Type = "Mountain", Name = "山岳" },
                new() { Type = "MountainSnow", Name = "雪峰" },
                new() { Type = "Hills", Name = "丘陵" },
                new() { Type = "Rocky", Name = "岩地" },
                new() { Type = "River", Name = "河流" },
                new() { Type = "Lake", Name = "湖泊" },
                new() { Type = "ShallowWater", Name = "浅滩" },
                new() { Type = "DeepWater", Name = "大海" },
                new() { Type = "Sand", Name = "荒漠" },
                new() { Type = "Snow", Name = "雪原" },
                new() { Type = "Ice", Name = "冰原" },
                new() { Type = "Swamp", Name = "沼泽" },
                new() { Type = "Bog", Name = "泥沼" },
                new() { Type = "Savanna", Name = "稀树草地" },
            },
            PoiSettlements =
            {
                new() { Type = "Capital", Names = { "王都", "帝国王都", "莱茵王都" } },
                new() { Type = "Town", Names = { "边境镇", "艾尔姆镇", "港口镇", "鲁米纳镇", "商业镇", "阿斯特城" } },
                new() { Type = "Village", Names = { "开拓村", "水车村", "弗洛拉村", "绿树村", "贝尔村", "农家村" } },
                new() { Type = "Castle", Names = { "边境要塞", "守望砦", "关口要塞", "北岭要塞" } },
                new() { Type = "Fortress", Names = { "边境要塞", "守望砦", "关口要塞", "北岭要塞" } },
                new() { Type = "Ruin", Names = { "古代遗迹", "哥布林洞窟", "旧魔导遗址", "迷宫入口" } },
                new() { Type = "Monastery", Names = { "大教会", "修道院", "祈祷之泉" } },
            },
            PoiRoomTemplates =
            {
                new() { Id = 1, Category = "Entrance", Name = "大门", Terrain = "Road" },
                new() { Id = 2, Category = "Exit", Name = "出口", Terrain = "Road" },
                new() { Id = 3, Category = "Thoroughfare", Name = "道路", Terrain = "Road" },
                new() { Id = 4, Category = "OpenSpace", Name = "中央广场", Terrain = "Plains" },
                new() { Id = 5, Category = "OpenSpace", Name = "庭院", Terrain = "Grassland" },
                new() { Id = 6, Category = "OpenSpace", Name = "草地", Terrain = "Grassland" },
                new() { Id = 7, Category = "Nature", Name = "树林", Terrain = "Forest" },
                new() { Id = 8, Category = "Facility", Name = "工坊", Terrain = "Plains" },
                new() { Id = 9, Category = "Facility", Name = "仓库", Terrain = "Plains" },
                new() { Id = 10, Category = "Facility", Name = "酒馆", Terrain = "Plains" },
                new() { Id = 11, Category = "Facility", Name = "旅馆", Terrain = "Plains" },
                new() { Id = 12, Category = "Facility", Name = "教会", Terrain = "Plains" },
                new() { Id = 13, Category = "Facility", Name = "马厩", Terrain = "Plains" },
                new() { Id = 14, Category = "Facility", Name = "后院", Terrain = "Grassland" },
                new() { Id = 15, Category = "Treasure", Name = "藏宝库", Terrain = "Plains" },
                new() { Id = 16, Category = "Shrine", Name = "神龛", Terrain = "Plains" },
            },
        };

        return BuildFromTable(table);
    }

    private static MapCatalog BuildFromTable(MapDefsTable table)
    {
        var catalog = new MapCatalog();

        foreach (var t in table.Terrains)
        {
            if (Enum.TryParse<WorldTerrainType>(t.Type, ignoreCase: true, out var parsed))
                catalog._terrainNames[parsed] = t.Name;
        }

        foreach (var p in table.PoiSettlements)
        {
            catalog._poiNames[p.Type] = new List<string>(p.Names);
        }

        foreach (var s in table.PoiScales)
        {
            if (Enum.TryParse<WorldPoiType>(s.Type, ignoreCase: true, out var poiType))
                catalog._poiScales[poiType] = s;
        }

        foreach (var d in table.DistrictTemplates)
        {
            catalog._districtTemplates[d.Name] = d;
        }

        if (table.SettlementGrammar != null)
        {
            catalog.Grammar.SyllablePrefixes.AddRange(table.SettlementGrammar.SyllablePrefixes);
            catalog.Grammar.SyllableSuffixes.AddRange(table.SettlementGrammar.SyllableSuffixes);
            foreach (var pair in table.SettlementGrammar.FeaturePrefixes)
                catalog.Grammar.FeaturePrefixes[pair.Key] = new List<string>(pair.Value);
            foreach (var pair in table.SettlementGrammar.TypeSuffixes)
                catalog.Grammar.TypeSuffixes[pair.Key] = new List<string>(pair.Value);
        }

        foreach (var r in table.PoiRoomTemplates)
        {
            switch (r.Category.ToLowerInvariant())
            {
                case "entrance":
                    catalog._entrances.Add(r);
                    break;
                case "exit":
                    catalog._exits.Add(r);
                    break;
                case "thoroughfare":
                case "road":
                    catalog._roads.Add(r);
                    break;
                case "openspace":
                    catalog._openSpaces.Add(r);
                    break;
                case "facility":
                case "nature":
                    catalog._facilities.Add(r);
                    break;
                case "treasure":
                    catalog._treasures.Add(r);
                    break;
                case "shrine":
                    catalog._shrines.Add(r);
                    break;
                default:
                    catalog._facilities.Add(r);
                    break;
            }
        }

        return catalog;
    }
}
