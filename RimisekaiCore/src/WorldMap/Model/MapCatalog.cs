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

    /// <summary>大地图上穿过一格这种地貌要花的分钟数（5 的倍数）；0 = 走不过去。</summary>
    public int Travel { get; set; }

    /// <summary>每踏进一格这种地貌撞上野外遭遇的千分率。</summary>
    public int Encounter { get; set; }
}

/// <summary>
/// 一条遭遇：有敌人＝战斗（可迎战、可绕开），没有敌人＝事件（一枚钮收下结果）。
/// 结果按字段落账：钱（可负）、额外耗时、同行者心情。
/// </summary>
public sealed class EncounterDef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>出现在哪些地貌；空表示任何地貌。</summary>
    public List<string> Terrains { get; set; } = new();
    public int Weight { get; set; } = 1;

    /// <summary>危险等级下限：离领地越远等级越高（见 <c>HubSession.DangerTier</c>）。</summary>
    public int MinTier { get; set; } = 1;
    public List<Rimisekai.Catalog.EnemyDef> Foes { get; set; } = new();

    /// <summary>战斗遭遇选择绕开时多花的分钟数。</summary>
    public int DetourMinutes { get; set; }
    public int Money { get; set; }
    public int MoneyMin { get; set; }
    public int MoneyMax { get; set; }
    public int Minutes { get; set; }
    public int Mood { get; set; }

    /// <summary>事件的那枚钮写什么。</summary>
    public string AcceptLabel { get; set; } = "";
}

public sealed class WildsDef
{
    /// <summary>走在路上时遭遇率打的折扣（百分比）。</summary>
    public int RoadPercent { get; set; } = 100;

    /// <summary>领地周围这么多格内不起遭遇。</summary>
    public int SafeRadius { get; set; }
    public List<EncounterDef> Events { get; set; } = new();
}

public sealed class DungeonDef
{
    /// <summary>地城的块拼法：每式是一组 5×5 块坐标（单块、横两块、竖两块、L 形、一字三块……），按兴趣点种子挑一式。</summary>
    public List<List<List<int>>> Layouts { get; set; } = new();

    /// <summary>地城里普通石室有守卫的百分率。</summary>
    public int GuardPercent { get; set; }
    public List<EncounterDef> Guards { get; set; } = new();
    public List<EncounterDef> Bosses { get; set; } = new();
    public EncounterDef Treasure { get; set; } = new();
    public EncounterDef Shrine { get; set; } = new();

    /// <summary>首领倒下后的日志，{0} 为地城名。</summary>
    public string ClearedText { get; set; } = "";

    /// <summary>迷雾里望见、还没走进去的石室，格上写这个。</summary>
    public string FogName { get; set; } = "";

    /// <summary>委托地城（包接送）。</summary>
    public QuestDungeonDef Quest { get; set; } = new();
}

/// <summary>
/// 委托地城：接下地图类委托，马车把编成的人送进一座现生成的地城，最深处是委托的正主；
/// 正主倒下即委托了结、接回领地；战败或撤离同样接回，委托不算数。
/// </summary>
public sealed class QuestDungeonDef
{
    /// <summary>委托难度每这么多星，地城危险等级高一级（1–4）。</summary>
    public double StarsPerTier { get; set; } = 1;

    /// <summary>正主那一间的描写。</summary>
    public string BossText { get; set; } = "";

    /// <summary>出行钮在委托地城里换成这个字（撤离）。</summary>
    public string LeaveLabel { get; set; } = "";

    /// <summary>送到地头、了结接回、撤离接回的日志，{0} 为委托名。</summary>
    public string ArriveText { get; set; } = "";
    public string DoneText { get; set; } = "";
    public string AbandonText { get; set; } = "";

    /// <summary>委托板上按日现生成的地城探索委托（见 <see cref="Rimisekai.Quest.QuestBoard"/>）。</summary>
    public QuestBoardDef Board { get; set; } = new();
}

/// <summary>
/// 委托板现生成地城探索委托：每天张贴 <see cref="PerDay"/> 张，每张挂 <see cref="LifeDays"/> 天，了结即撕下；
/// 地点按张贴顺序轮转 <see cref="Sites"/>，发单的聚落取世界上的一处聚落，难度在星数区间里按半星掷，
/// 正主从 <c>dungeon.bosses</c> 里按危险等级挑，酬金＝<see cref="MoneyBase"/>＋<see cref="MoneyPerStar"/>×星数（取整到十）。
/// </summary>
public sealed class QuestBoardDef
{
    public int PerDay { get; set; }
    public int LifeDays { get; set; }

    /// <summary>生成委托的编号从这里起，不与 quests.json 的编号相撞。</summary>
    public int IdBase { get; set; }
    public double StarsMin { get; set; }
    public double StarsMax { get; set; }

    /// <summary>开局时的星数上限，<see cref="RampDays"/> 天里线性涨到 <see cref="StarsMax"/>。</summary>
    public double StarsEarly { get; set; }
    public int RampDays { get; set; }
    public int MoneyBase { get; set; }
    public int MoneyPerStar { get; set; }

    /// <summary>奖励行，{0} 为金币数。</summary>
    public string RewardText { get; set; } = "";

    /// <summary>人数上限＝此数＋危险等级。</summary>
    public int PartyBase { get; set; }
    public List<QuestSiteDef> Sites { get; set; } = new();
}

/// <summary>委托地点：名称即委托名；描述里 {0} 为发单的聚落名；传言任挑一条。</summary>
public sealed class QuestSiteDef
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Rumors { get; set; } = new();
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
    public WildsDef Wilds { get; set; } = new();
    public DungeonDef Dungeon { get; set; } = new();
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
    private readonly Dictionary<WorldTerrainType, int> _terrainTravel = new();
    private readonly Dictionary<WorldTerrainType, int> _terrainEncounter = new();

    /// <summary>野外遭遇表。</summary>
    public WildsDef Wilds { get; private set; } = new();

    /// <summary>地城内容表：守卫、首领、宝库、神龛。</summary>
    public DungeonDef Dungeon { get; private set; } = new();

    /// <summary>踏进一格这种地貌撞上野外遭遇的千分率；没配就是数据表缺项，直接抛。</summary>
    public int GetEncounterPermille(WorldTerrainType terrain) =>
        _terrainEncounter.TryGetValue(terrain, out var permille)
            ? permille
            : throw new KeyNotFoundException($"未在 map_defs.json 中配置地貌 {terrain} 的 encounter");
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
        _terrainNames.TryGetValue(terrain, out var name) ? name : terrain.ToString();

    /// <summary>
    /// 大地图上踏进一格这种地貌要花的分钟数；0 = 不可通行。严格由 map_defs.json 的 travel 字段驱动，
    /// 没配就是数据表缺项，直接抛。
    /// </summary>
    public int GetTravelMinutes(WorldTerrainType terrain) =>
        _terrainTravel.TryGetValue(terrain, out var minutes)
            ? minutes
            : throw new KeyNotFoundException($"未在 map_defs.json 中配置地貌 {terrain} 的 travel");

    public RoomTemplateDef GetEntranceTemplate() => _entrances[0];

    public RoomTemplateDef GetExitTemplate() => _exits[0];

    public RoomTemplateDef GetRoadTemplate() => _roads[0];

    public RoomTemplateDef GetTreasureTemplate() => _treasures[0];

    public RoomTemplateDef GetShrineTemplate() => _shrines[0];

    /// <summary>
    /// 根据 POI 类型获取推荐拼接规模 (blocksW, blocksH) 与各 5x5 块的分区主题列表。
    /// 严格由 map_defs.json 驱动。
    /// </summary>
    public (int blocksW, int blocksH, IReadOnlyList<string> districts) GetPoiScale(WorldPoiType type)
    {
        if (_poiScales.TryGetValue(type, out var scale) && scale.BlocksW > 0 && scale.BlocksH > 0)
            return (scale.BlocksW, scale.BlocksH, scale.Districts);

        throw new KeyNotFoundException($"未在 map_defs.json 中配置 POI 类型 {type} 的规模");
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
            throw new InvalidOperationException("map_defs.json 中缺少 OpenSpace 或 Facility 房间模板");
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
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            // 敌人行的武器、天资属性写成字串（"unarmed"、"speed"）。
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        var table = JsonSerializer.Deserialize<MapDefsTable>(json, options) ?? new MapDefsTable();
        return BuildFromTable(table);
    }

    public static MapCatalog LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"地图定义文件不存在: {filePath}");
        var json = File.ReadAllText(filePath);
        return LoadFromJson(json);
    }

    /// <summary>外部提供的地图配置读取委托（如 Godot 虚拟文件系统 res://content/map_defs.json）。</summary>
    public static Func<string?>? CustomJsonProvider { get; set; }

    public static MapCatalog CreateDefault()
    {
        if (CustomJsonProvider != null)
        {
            var customJson = CustomJsonProvider();
            if (!string.IsNullOrEmpty(customJson))
                return LoadFromJson(customJson);
        }

        // 严格从数据表 content/map_defs.json 读取，禁止硬编码备用表
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
                return LoadFromFile(p);
        }

        throw new FileNotFoundException("未找到地图配置文件 content/map_defs.json，禁止硬编码回退。");
    }

    private static MapCatalog BuildFromTable(MapDefsTable table)
    {
        var catalog = new MapCatalog { Wilds = table.Wilds, Dungeon = table.Dungeon };

        foreach (var t in table.Terrains)
        {
            if (Enum.TryParse<WorldTerrainType>(t.Type, ignoreCase: true, out var parsed))
            {
                catalog._terrainNames[parsed] = t.Name;
                catalog._terrainTravel[parsed] = t.Travel;
                catalog._terrainEncounter[parsed] = t.Encounter;
            }
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
