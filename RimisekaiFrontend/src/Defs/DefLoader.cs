using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Rimisekai.Catalog;
using Rimisekai.Quest;

namespace Rimisekai.Defs;

/// <summary>
/// 定义表加载器。把 content/defs/ 下的 *.xml 与 *.json 灌进 DefDatabase，
/// 是内容定义的唯一入口：CS 层只写逻辑，一切内容都是数据行。
///
/// 表格式：一个文件一个 defType，形如
/// <code>{ "defType": "RoomDef", "defs": [ { "defName": "...", ... } ] }</code>
/// 也允许 {"defs":[{...},{...}]} 混合多类——此时每行自带 defType 字段。
/// </summary>
public static class DefLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly object Lock = new();
    private static readonly HashSet<string> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static bool _defaultInitialized;

    private static readonly List<RoomDef> _buildingRooms = new();
    private static readonly List<FacilityDef> _buildingFacilities = new();
    private static readonly List<RoomDef> _areaRooms = new();
    private static readonly List<AreaFacilityDef> _areaFacilities = new();
    private static readonly Dictionary<int, int[]> _areaLinks = new();
    private static string _territoryName = "领地";

    public static IReadOnlyList<RoomDef> BuildingRooms => _buildingRooms;
    public static IReadOnlyList<FacilityDef> BuildingFacilities => _buildingFacilities;
    public static IReadOnlyList<RoomDef> AreaRooms => _areaRooms;
    public static IReadOnlyList<AreaFacilityDef> AreaFacilities => _areaFacilities;
    public static IReadOnlyDictionary<int, int[]> AreaLinks => _areaLinks;
    public static string TerritoryName => _territoryName;

    /// <summary>
    /// 自定义目录内容读取器（供 Godot 虚拟文件系统 res://content/defs 回填）。
    /// 返回 key 为文件名、value 为文件内容文本的键值对集合。
    /// </summary>
    public static Func<IEnumerable<(string FileName, string Content)>>? CustomContentProvider { get; set; }

    /// <summary>
    /// 确保默认数据表已从 content/defs 载入 DefDatabase。
    /// 自动探寻常见工作目录或输出目录，仅执行一次。
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_defaultInitialized)
            return;

        lock (Lock)
        {
            lock (Lock)
            {
                if (_defaultInitialized)
                    return;

                if (CustomContentProvider != null)
                {
                    try
                    {
                        var files = CustomContentProvider();
                        var count = 0;
                        foreach (var (fileName, content) in files)
                        {
                            if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                                count += LoadXml(content, fileName);
                            else if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                count += LoadJson(content, fileName);
                        }
                        if (count > 0)
                        {
                            _defaultInitialized = true;
                            return;
                        }
                    }
                    catch
                    {
                        // 降级使用常规文件探测
                    }
                }

                // 自动从当前目录和基目录向上探查 content/defs
                var searchStarts = new[]
                {
                    Directory.GetCurrentDirectory(),
                    AppDomain.CurrentDomain.BaseDirectory,
                };

                foreach (var start in searchStarts)
                {
                    var cur = new DirectoryInfo(start);
                    while (cur != null)
                    {
                        var check = Path.Combine(cur.FullName, "content", "defs");
                        if (Directory.Exists(check) && File.Exists(Path.Combine(check, "things.json")))
                        {
                            LoadDirectory(check);
                            _defaultInitialized = true;
                            return;
                        }
                        cur = cur.Parent;
                    }
                }

                if (Directory.Exists("D:\\123\\rimisekai\\content\\defs"))
                {
                    LoadDirectory("D:\\123\\rimisekai\\content\\defs");
                    _defaultInitialized = true;
                    return;
                }

                _defaultInitialized = true;
            }
        }
    }

    /// <summary>把一份表文本灌进数据库。重复加载同一文件无效。返回装入的行数。</summary>
    public static int LoadJson(string json, string? origin = null)
    {
        if (origin != null)
        {
            lock (Lock)
            {
                if (!Loaded.Add(origin))
                    return 0;
            }
        }

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var root = doc.RootElement;
        string? fileType = null;
        if (root.TryGetProperty("defType", out var defTypeProp))
            fileType = defTypeProp.GetString();

        if (!root.TryGetProperty("defs", out var defs) || defs.ValueKind != JsonValueKind.Array)
            return 0;

        var count = 0;
        foreach (var element in defs.EnumerateArray())
        {
            if (LoadOne(element, fileType))
                count++;
        }
        return count;
    }

    /// <summary>读一个文件并灌进数据库。按扩展名分派 JSON 与 XML。</summary>
    public static int LoadFile(string path)
    {
        if (!File.Exists(path))
            return 0;
        var origin = Path.GetFullPath(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xml" => LoadXml(File.ReadAllText(path), origin),
            _ => LoadJson(File.ReadAllText(path), origin),
        };
    }

    /// <summary>灌一个目录下的全部 *.json 与 *.xml（按文件名排序）。</summary>
    public static int LoadDirectory(string dir)
    {
        if (!Directory.Exists(dir))
            return 0;

        var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var file in files)
            count += LoadFile(file);
        return count;
    }

    /// <summary>
    /// 灌一份 XML 表。根元素 <c>Defs</c>，子元素名即 defType，
    /// 每个子元素是一行 def——与 RimWorld 的表结构一致。
    /// </summary>
    public static int LoadXml(string xml, string? origin = null)
    {
        if (origin != null)
        {
            lock (Lock)
            {
                if (!Loaded.Add(origin))
                    return 0;
            }
        }

        var doc = XDocument.Parse(xml);
        var root = doc.Root;
        if (root == null)
            return 0;

        var count = 0;
        foreach (var element in root.Elements())
        {
            if (LoadXmlOne(element))
                count++;
        }
        return count;
    }

    /// <summary>把一个 XML 元素翻成 JSON 再走同一条注册路径，类型分派只写一份。</summary>
    private static bool LoadXmlOne(XElement element) =>
        LoadOne(JsonSerializer.SerializeToElement(ElementToMap(element), Options),
            element.Name.LocalName);

    private static object? ParseScalar(string value)
    {
        if (int.TryParse(value, out var i))
            return i;
        if (float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var f))
            return f;
        if (bool.TryParse(value, out var b))
            return b;
        return value;
    }

    /// <summary>
    /// 元素 → JSON 对象。有子元素的是对象，没有的是叶子；
    /// 同名子节点出现多次收成数组（如多条 costs），与 JSON 的列表语义对齐。
    /// </summary>
    private static Dictionary<string, object?> ElementToMap(XElement element)
    {
        var map = new Dictionary<string, object?>();
        foreach (var group in element.Elements().GroupBy(c => c.Name.LocalName))
        {
            var items = group.ToList();
            map[group.Key] = items.Count == 1
                ? ElementToValue(items[0])
                : items.Select(ElementToValue).ToList();
        }
        return map;
    }

    private static object? ElementToValue(XElement element) =>
        element.HasElements ? ElementToMap(element) : ParseScalar(element.Value);

    private static bool LoadOne(JsonElement element, string? fileType)
    {
        var type = fileType;
        if (element.TryGetProperty("defType", out var rowType))
        {
            var value = rowType.GetString();
            if (!string.IsNullOrEmpty(value))
                type = value;
        }

        var raw = element.GetRawText();
        switch (type)
        {
            case "ThingCategoryDef":
                return Register(JsonSerializer.Deserialize<ThingCategoryDef>(raw, Options));
            case "ThingDef":
                return Register(JsonSerializer.Deserialize<ThingDef>(raw, Options));
            case "CropDef":
                return Register(JsonSerializer.Deserialize<CropDef>(raw, Options));
            case "MaterialDef":
                return Register(JsonSerializer.Deserialize<MaterialDef>(raw, Options));
            case "WeaponTypeDef":
                return Register(JsonSerializer.Deserialize<WeaponTypeDef>(raw, Options));
            case "EnchantDef":
                return Register(JsonSerializer.Deserialize<EnchantDef>(raw, Options));
            case "ArmorSlotDef":
                return Register(JsonSerializer.Deserialize<ArmorSlotDef>(raw, Options));
            case "AccessoryDef":
                return Register(JsonSerializer.Deserialize<AccessoryDef>(raw, Options));
            case "ActionDef":
                return Register(JsonSerializer.Deserialize<ActionDef>(raw, Options));
            case "RecipeDef":
                return Register(JsonSerializer.Deserialize<RecipeDef>(raw, Options));
            case "WorkTypeDef":
                return Register(JsonSerializer.Deserialize<WorkTypeDef>(raw, Options));
            case "QuestDef":
                return Register(JsonSerializer.Deserialize<QuestDef>(raw, Options));
            case "JobDef":
                return Register(JsonSerializer.Deserialize<JobDef>(raw, Options));
            case "TraitDef":
                return Register(JsonSerializer.Deserialize<TraitDef>(raw, Options));
            case "TraitExclusionDef":
                return Register(JsonSerializer.Deserialize<TraitExclusionDef>(raw, Options));
            case "IdentityDef":
                return Register(JsonSerializer.Deserialize<IdentityDef>(raw, Options));
            case "PersonaPartsDef":
                return Register(JsonSerializer.Deserialize<PersonaPartsDef>(raw, Options));
            case "IdentitySkillPoolDef":
                return Register(JsonSerializer.Deserialize<IdentitySkillPoolDef>(raw, Options));
            case "SkillDef":
                return Register(JsonSerializer.Deserialize<SkillDef>(raw, Options));
            case "RoomDef":
            {
                var r = JsonSerializer.Deserialize<RoomDef>(raw, Options);
                if (!Register(r)) return false;
                if (fileType == "BuildingCatalog") _buildingRooms.Add(r!);
                else if (fileType == "StartingArea") _areaRooms.Add(r!);
                return true;
            }
            case "FacilityDef":
            {
                var f = JsonSerializer.Deserialize<FacilityDef>(raw, Options);
                if (!Register(f)) return false;
                if (fileType == "BuildingCatalog") _buildingFacilities.Add(f!);
                return true;
            }
            case "NewGameSeedDef":
                return Register(JsonSerializer.Deserialize<NewGameSeedDef>(raw, Options));
            case "WorldSeedDef":
            {
                var w = JsonSerializer.Deserialize<WorldSeedDef>(raw, Options);
                if (!Register(w)) return false;
                if (!string.IsNullOrEmpty(w!.TerritoryName))
                    _territoryName = w.TerritoryName;
                return true;
            }
            // 开局摆位：不是定义，不进 DefDatabase，只进开局布局表。
            case "AreaFacilityDef":
            {
                var a = JsonSerializer.Deserialize<AreaFacilityDef>(raw, Options)!;
                if (_areaFacilities.FindAll(x => x.RoomId == a.RoomId).Count >= Rimisekai.Housing.Room.MaxFacilities)
                    throw new InvalidDataException(
                        $"开局摆位：房间 {a.RoomId} 的设施超过上限 {Rimisekai.Housing.Room.MaxFacilities} 件（{a.FacilityDefName}）。");
                _areaFacilities.Add(a);
                return true;
            }
            case "AreaLinkDef":
            {
                var l = JsonSerializer.Deserialize<AreaLinkDef>(raw, Options);
                if (l != null && l.To != null)
                {
                    _areaLinks[l.From] = l.To.ToArray();
                    return true;
                }
                return false;
            }
            default:
                return false;
        }
    }

    private static bool Register<T>(T? def) where T : Def
    {
        if (def == null || string.IsNullOrEmpty(def.DefName))
            return false;
        DefDatabase<T>.Register(def);
        return true;
    }

    /// <summary>清空已加载记录（测试用：换一份表重载时调用）。</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            Loaded.Clear();
            _defaultInitialized = false;
            _buildingRooms.Clear();
            _buildingFacilities.Clear();
            _areaRooms.Clear();
            _areaFacilities.Clear();
            _areaLinks.Clear();
        }
    }
}
