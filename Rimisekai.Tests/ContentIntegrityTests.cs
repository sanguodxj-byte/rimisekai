using System.Collections.Generic;
using System.Linq;
using Rimisekai.Defs;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 内容表的自洽性：一切引用都指得到东西。
///
/// 查两类引用——物品引用（配方、料钱、产出物）与开局摆位的设施定义引用。
/// 悬空引用不是崩溃，是静默少一件货/一口井，所以要在加载时兜住。
/// </summary>
public class ContentIntegrityTests
{
    public ContentIntegrityTests() => DefaultDefs.EnsureInitialized();

    [Fact]
    public void Every_item_reference_resolves()
    {
        var missing = new List<string>();

        void Check(string where, string itemId)
        {
            if (itemId.Length == 0)
                return;
            if (Items.Get(itemId) == null)
                missing.Add($"{where} → {itemId}");
        }

        foreach (var recipe in DefDatabase<RecipeDef>.All)
        {
            Check($"配方 {recipe.DefName} 的产出", recipe.ItemId);
            foreach (var cost in recipe.Costs)
                Check($"配方 {recipe.DefName} 的料", cost.ItemId);
        }

        foreach (var room in DefDatabase<RoomDef>.All)
            foreach (var cost in room.MaterialCost)
                Check($"房间 {room.DefName} 的料", cost.ItemId);

        foreach (var facility in DefDatabase<FacilityDef>.All)
        {
            foreach (var cost in facility.MaterialCost)
                Check($"设施 {facility.DefName} 的料", cost.ItemId);
            Check($"设施 {facility.DefName} 的产出", facility.YieldItemId);
        }

        foreach (var crop in DefDatabase<CropDef>.All)
        {
            Check($"作物 {crop.DefName} 的种子", crop.SeedItemId);
            Check($"作物 {crop.DefName} 的产物", crop.ProduceItemId);
        }

        Assert.True(missing.Count == 0,
            "内容表里有引用不到的物品：" + string.Join("；", missing));
    }

    [Fact]
    public void Every_facility_reference_resolves()
    {
        // 房间不再声明自己带哪些设施（两张表无耦合），只剩开局摆位引用设施定义。
        var facilities = new HashSet<string>(
            DefDatabase<FacilityDef>.All.Select(f => f.DefName));
        var missing = new List<string>();
        foreach (var place in DefLoader.AreaFacilities)
            if (!facilities.Contains(place.FacilityDefName))
                missing.Add($"开局摆位 → {place.FacilityDefName}");

        Assert.True(missing.Count == 0,
            "有引用不到的设施定义：" + string.Join("；", missing));
    }

    /// <summary>
    /// 开局摆位能照定义生成实例：引用的定义在、房间在、实例号不撞。
    /// </summary>
    [Fact]
    public void Starting_area_placements_resolve_to_one_definition_each()
    {
        var rooms = new HashSet<int>(
            DefDatabase<RoomDef>.All.Select(r => r.Id));

        var missingDef = new List<string>();
        var badRoom = new List<int>();
        var ids = new HashSet<int>();
        foreach (var place in DefLoader.AreaFacilities)
        {
            if (DefDatabase<FacilityDef>.Get(place.FacilityDefName) == null)
                missingDef.Add(place.FacilityDefName);
            if (!rooms.Contains(place.RoomId))
                badRoom.Add(place.RoomId);
            if (!ids.Add(place.Id))
                missingDef.Add($"实例号重复：{place.Id}");
        }

        // 开局据点非空：一间房都没有等于没布景。
        Assert.NotEmpty(DefLoader.AreaFacilities);
        Assert.True(missingDef.Count == 0,
            "开局摆位里引用不到的设施定义：" + string.Join("、", missingDef));
        Assert.True(badRoom.Count == 0,
            "开局摆位里的房间号不存在：" + string.Join("、", badRoom));
    }

    [Fact]
    public void Illustrations_config_and_assets_exist()
    {
        var configPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "content", "illustrations.json");
        if (!System.IO.File.Exists(configPath))
        {
            var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
            var cur = new System.IO.DirectoryInfo(baseDir);
            while (cur != null)
            {
                var p = System.IO.Path.Combine(cur.FullName, "content", "illustrations.json");
                if (System.IO.File.Exists(p))
                {
                    configPath = p;
                    break;
                }
                cur = cur.Parent;
            }
        }

        Assert.True(System.IO.File.Exists(configPath), "content/illustrations.json 必须存在");

        var json = System.IO.File.ReadAllText(configPath);
        var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(config);

        // 验证测试战斗背景图（图2天然岩石洞窟）已正确接线
        Assert.True(config.ContainsKey("combat"), "配置表必须包含 combat 类目");
        Assert.True(config["combat"].ContainsKey("default"), "combat 类目必须包含 default 测试背景");
        Assert.Equal("res://assets/combat_natural_cave.png", config["combat"]["default"]);

        // 验证所有配置中引用的图片文件均真实存在于磁盘
        var rootDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(configPath))!;
        foreach (var (cat, items) in config)
        {
            foreach (var (key, resPath) in items)
            {
                var relPath = resPath.Replace("res://", "").Replace('/', System.IO.Path.DirectorySeparatorChar);
                var fullPath = System.IO.Path.Combine(rootDir, relPath);
                Assert.True(System.IO.File.Exists(fullPath), $"插画配置表中的文件必须真实存在: [{cat}] {key} -> {fullPath}");
            }
        }
    }
}
