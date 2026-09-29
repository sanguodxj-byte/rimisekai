using Rimisekai.Housing;

namespace Rimisekai.Defs;

/// <summary>
/// 内置默认 Def 初始化器。在游戏或系统启动时将基础品类、物品、行为定义装载进 DefDatabase。
/// </summary>
public static class DefaultDefs
{
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        RegisterCategories();
        RegisterJobs();
        RegisterItems();
    }

    private static void RegisterCategories()
    {
        DefDatabase<ThingCategoryDef>.Register(new ThingCategoryDef
        {
            DefName = "Root",
            Label = "全部",
            Description = "所有物品的顶层根分类。",
        });

        DefDatabase<ThingCategoryDef>.Register(new ThingCategoryDef
        {
            DefName = "Food",
            Label = "食物",
            ParentCategory = "Root",
            Description = "供角色食用的餐点与干粮。",
        });

        DefDatabase<ThingCategoryDef>.Register(new ThingCategoryDef
        {
            DefName = "RawMaterial",
            Label = "原材料",
            ParentCategory = "Root",
            Description = "采掘、伐木等产出的基础建材与工匠用料。",
        });

        DefDatabase<ThingCategoryDef>.Register(new ThingCategoryDef
        {
            DefName = "Medicine",
            Label = "医药",
            ParentCategory = "Root",
            Description = "疗伤草药与药剂制品。",
        });

        DefDatabase<ThingCategoryDef>.Register(new ThingCategoryDef
        {
            DefName = "Manufactured",
            Label = "加工品",
            ParentCategory = "Root",
            Description = "工作台制作产出的制品。",
        });
    }

    private static void RegisterJobs()
    {
        DefDatabase<JobDef>.Register(new JobDef
        {
            DefName = "Sleep",
            Label = "睡觉",
            LegacyGoal = ActionKind.Sleep,
            Priority = 100,
            IsVitalNeed = true,
            ReportFormat = "{0}在{1}睡觉。",
        });

        DefDatabase<JobDef>.Register(new JobDef
        {
            DefName = "Meal",
            Label = "吃饭",
            LegacyGoal = ActionKind.Meal,
            Priority = 80,
            IsVitalNeed = true,
            ReportFormat = "{0}在{1}吃饭。",
        });

        DefDatabase<JobDef>.Register(new JobDef
        {
            DefName = "Work",
            Label = "工作",
            LegacyGoal = ActionKind.None,
            Priority = 50,
            ReportFormat = "{0}在{1}。{2}",
        });

        DefDatabase<JobDef>.Register(new JobDef
        {
            DefName = "Haul",
            Label = "搬运",
            LegacyGoal = ActionKind.Haul,
            Priority = 60,
            ReportFormat = "{0}正把{2}送去{1}。",
        });

        DefDatabase<JobDef>.Register(new JobDef
        {
            DefName = "Rest",
            Label = "休息",
            LegacyGoal = ActionKind.Rest,
            Priority = 30,
            ReportFormat = "{0}在{1}歇着。",
        });

        DefDatabase<JobDef>.Register(new JobDef
        {
            DefName = "Play",
            Label = "消遣",
            LegacyGoal = ActionKind.Loiter,
            Priority = 25,
            ReportFormat = "{0}在{1}消遣。",
        });

        DefDatabase<JobDef>.Register(new JobDef
        {
            DefName = "SeekChat",
            Label = "搭话",
            LegacyGoal = ActionKind.SeekChat,
            Priority = 40,
            ReportFormat = "{0}正想找你说话。",
        });

        DefDatabase<JobDef>.Register(new JobDef
        {
            DefName = "Loiter",
            Label = "闲转",
            LegacyGoal = ActionKind.Loiter,
            Priority = 10,
            ReportFormat = "{0}在{1}。{2}",
        });
    }

    private static void RegisterItems()
    {
        // 食物
        DefDatabase<ThingDef>.Register(new ThingDef
        {
            DefName = "干粮",
            Label = "干粮",
            Category = "Food",
            IsFood = true,
            FoodTier = FoodTier.Plain,
            Nutrition = 25,
            MarketValue = 2,
            Description = "便于随身携带的干粮。",
        });

        DefDatabase<ThingDef>.Register(new ThingDef
        {
            DefName = "bread",
            Label = "面包",
            Category = "Food",
            IsFood = true,
            FoodTier = FoodTier.Plain,
            Nutrition = 30,
            MarketValue = 3,
            Description = "烘烤好的朴素面包。",
        });

        // 基础建材
        DefDatabase<ThingDef>.Register(new ThingDef
        {
            DefName = "木材",
            Label = "木材",
            Category = "RawMaterial",
            MarketValue = 1,
            Description = "伐木取得的原木料。",
        });

        DefDatabase<ThingDef>.Register(new ThingDef
        {
            DefName = "石材",
            Label = "石材",
            Category = "RawMaterial",
            MarketValue = 1,
            Description = "开采加工的石材块。",
        });

        DefDatabase<ThingDef>.Register(new ThingDef
        {
            DefName = "矿石",
            Label = "矿石",
            Category = "RawMaterial",
            MarketValue = 2,
            Description = "从矿脉开采出的金属原矿。",
        });

        DefDatabase<ThingDef>.Register(new ThingDef
        {
            DefName = "水",
            Label = "水",
            Category = "RawMaterial",
            MarketValue = 1,
            Description = "水井里打上来的洁净淡水。",
        });

        // 农牧与草药
        DefDatabase<ThingDef>.Register(new ThingDef
        {
            DefName = "药草",
            Label = "药草",
            Category = "Medicine",
            MarketValue = 3,
            Description = "采摘的野外药草，可做初步疗伤之用。",
        });

        DefDatabase<ThingDef>.Register(new ThingDef
        {
            DefName = "小麦",
            Label = "小麦",
            Category = "RawMaterial",
            MarketValue = 1,
            Description = "收获的农作物小麦。",
        });
    }
}
