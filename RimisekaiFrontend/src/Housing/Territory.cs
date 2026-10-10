using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Defs;

namespace Rimisekai.Housing;

public enum RoomPermission
{
    Public = 0,
    MasterOnly = 1,
    Faction = 2,
}

/// <summary>房间朝向（网格上 北＝y-1、东＝x+1、南＝y+1、西＝x-1）。</summary>
public enum RoomDir
{
    North,
    East,
    South,
    West,
}

/// <summary>
/// 房门的锁。只有主人的房间（摆着主人的床那间，<see cref="Territory.MasterBedroomId"/>）用得上：
/// 自动 = 主人不在屋内或正在睡时锁（女仆照进）；手动锁 = 主人在屋里时把门锁死，谁都进不来（主人一出门就回到自动）；
/// 手动敞开 = 谁都进得来。
/// </summary>
public enum RoomLock
{
    Auto = 0,
    Locked = 1,
    Unlocked = 2,
}

public sealed class Room
{
    /// <summary>
    /// 一间房最多摆几件设施。来自界面：地图与「此刻」之间的设施牌一排四块（约 216×140，图标在上、名字在下，手机上点得准），
    /// 不分页、不滚动，所以界面摆得下几块，房间表就只许放几件。改这个数要先改设施牌的排版。
    /// </summary>
    public const int MaxFacilities = 4;

    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int RegionId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Open { get; set; }
    public int OpenCost { get; init; }
    public RoomPermission Permission { get; set; } = RoomPermission.Public;

    /// <summary>门锁。只有主人的房间这个字段才有意义，见 <see cref="RoomLock"/>。</summary>
    public RoomLock Lock { get; set; } = RoomLock.Auto;

    public List<int> Links { get; } = new();
    public List<RecipeCost> MaterialCost { get; } = new();
    public bool Buildable { get; set; }

    /// <summary>房间插画资源路径（res:// 开头）。观察四周时显示。</summary>
    public string Illustration { get; set; } = "";

    /// <summary>
    /// 是不是一间「空房」——花钱开拓空地开出来的、还没装任何房间类型的毛坯。
    /// 已建好的房间（菜园、林场……）只能安装进空房；装进去时空房本体被顶替掉。
    /// </summary>
    public bool Vacant { get; set; }

    /// <summary>对口的工作（来自房间表）：在这间房里干这些活进度快 <see cref="Territory.RoomBonusPercent"/>%。</summary>
    public HashSet<ActionKind> BonusActions { get; } = new();

    /// <summary>
    /// 房间细分标签（如“室内”、“室外”、“工作间”、“娱乐室”、“卧室”等）。
    /// 至少有 1 个标签，目前无上限。
    /// </summary>
    public HashSet<string> Tags { get; } = new(System.StringComparer.OrdinalIgnoreCase);

    public bool HasTag(string tag) => Tags.Contains(tag);

    public void AddTag(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag))
            Tags.Add(tag.Trim());
    }

    /// <summary>
    /// 保证房间至少有 1 个标签。若为空则依据房间名称特征提供默认兜底。
    /// </summary>
    public void EnsureDefaultTag()
    {
        if (Tags.Count > 0)
            return;

        if (Name.Contains("院") || Name.Contains("林") || Name.Contains("山") || Name.Contains("田") || Name.Contains("井") || Name.Contains("园") || Name.Contains("池"))
            Tags.Add("室外");
        else if (Name.Contains("卧") || Name.Contains("寝") || Name.Contains("兵营"))
            Tags.Add("卧室");
        else if (Name.Contains("客") || Name.Contains("堂") || Name.Contains("厅") || Name.Contains("馆") || Name.Contains("剧"))
            Tags.Add("娱乐室");
        else if (Name.Contains("坊") || Name.Contains("铺") || Name.Contains("场") || Name.Contains("矿") || Name.Contains("房") || Name.Contains("库"))
            Tags.Add("工作间");
        else
            Tags.Add("室内");
    }
}

/// <summary>
/// 领地。房间、设施、库存、每人每天四段委派。
/// 结算按 6 小时一块跑：占设施、采集出货或工作台扣料制作。
/// </summary>
public sealed class Territory
{
    public const int MaxRegions = 3;
    public const int MaxRooms = 100;
    public const int ProgressPerTick = 10;
    public const int CraftProgressPerTick = 15;
    public const int FinishAt = 100;
    public const int GatherExp = 3;
    public const int CraftExp = 3;

    // ---------- 3×3 区域拼图（2026-10-01 主人定） ----------
    // 领地由最多 3×3 个 5×5 区块拼成。RegionId 是扁平编号，在拼图里的位置固定：
    //
    //     5  1  6          西北  北  东北
    //     4  0  2     ＝     西  中心  东
    //     7  3  8          西南  南  东南
    //
    // 解锁两段式：中心铺满 → 开四正（1/2/3/4）；四正里任意一块铺满 → 开四角（5/6/7/8）。
    // 「铺满」＝该区 25 格都有房间。

    /// <summary>每个区域固定 5×5 格。</summary>
    public const int RegionSize = 5;

    /// <summary>领地最多 3×3 个区域。</summary>
    public const int MaxTerritoryRegions = 9;

    /// <summary>起始区（正中心）。</summary>
    public const int CenterRegion = 0;

    /// <summary>四正（北/东/南/西）。</summary>
    public const int OrthogonalMask = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4);

    /// <summary>四角（西北/东北/西南/东南）。</summary>
    public const int DiagonalMask = (1 << 5) | (1 << 6) | (1 << 7) | (1 << 8);

    private const int RingNone = 0;
    private const int RingOrthogonal = 1;
    private const int RingDiagonal = 2;

    /// <summary>RegionId → 在 3×3 拼图里的格子坐标。</summary>
    private static readonly (int X, int Y)[] RegionCells =
    {
        (1, 1), // 0 中心
        (1, 0), // 1 北
        (2, 1), // 2 东
        (1, 2), // 3 南
        (0, 1), // 4 西
        (0, 0), // 5 西北
        (2, 0), // 6 东北
        (0, 2), // 7 西南
        (2, 2), // 8 东南
    };

    /// <summary>区域在 3×3 拼图里的格子坐标；越界返回 (-1, -1)。</summary>
    public static (int X, int Y) RegionCellOf(int regionId) =>
        regionId >= 0 && regionId < RegionCells.Length ? RegionCells[regionId] : (-1, -1);

    /// <summary>四正方向。</summary>
    public enum RegionDir
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    /// <summary>区域某个方向上的邻居区域 Id；没有则 -1。</summary>
    public static int RegionNeighbor(int regionId, RegionDir dir)
    {
        var (x, y) = RegionCellOf(regionId);
        if (x < 0)
            return -1;
        var (nx, ny) = dir switch
        {
            RegionDir.North => (x, y - 1),
            RegionDir.East => (x + 1, y),
            RegionDir.South => (x, y + 1),
            _ => (x - 1, y),
        };
        for (var i = 0; i < RegionCells.Length; i++)
        {
            if (RegionCells[i] == (nx, ny))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 区域朝某个方向的「连接点」格（该边正中的那一格）。
    /// 对面的互连点是它自己朝反方向的那个连接点——两边各要有一间房才谈得上过界。
    /// </summary>
    public static (int X, int Y) RegionGate(RegionDir dir) => dir switch
    {
        RegionDir.North => (2, 0),
        RegionDir.East => (4, 2),
        RegionDir.South => (2, 4),
        _ => (0, 2),
    };

    /// <summary>反方向。</summary>
    public static RegionDir Opposite(RegionDir dir) =>
        (RegionDir)(((int)dir + 2) % 4);

    /// <summary>区域名（给「去往XX」这类文案用）。</summary>
    public static string RegionName(int regionId) => regionId switch
    {
        1 => "北区",
        2 => "东区",
        3 => "南区",
        4 => "西区",
        5 => "西北区",
        6 => "东北区",
        7 => "西南区",
        8 => "东南区",
        _ => "中心区",
    };

    private int _unlockedMask = 1 << CenterRegion;

    /// <summary>已经开拓过几格空地。开拓定价按它每级涨 20%。随存档走。</summary>
    public int VacantDevelopCount { get; set; }

    /// <summary>已解锁区域的位掩码。</summary>
    public int UnlockedRegionMask => _unlockedMask;

    public void SetUnlockedRegionMask(int mask) =>
        _unlockedMask = mask & ((1 << MaxTerritoryRegions) - 1);

    /// <summary>这个区域解锁了没有。领地内区域（0..8）看掩码，POI 区域（≥9）看计数。</summary>
    public bool IsRegionUnlocked(int regionId)
    {
        if (regionId < 0)
            return false;
        if (regionId < MaxTerritoryRegions)
            return (_unlockedMask & (1 << regionId)) != 0;
        return regionId < UnlockedRegions;
    }

    private bool HasRing(int ring)
    {
        var mask = ring == RingOrthogonal ? OrthogonalMask : DiagonalMask;
        return (_unlockedMask & mask) == mask;
    }

    /// <summary>这个区域是不是「铺满」了——25 格都有房间。</summary>
    public bool IsRegionFull(int regionId)
    {
        if (regionId < 0 || regionId >= MaxTerritoryRegions)
            return false;
        var count = 0;
        foreach (var room in Rooms)
        {
            if (room.RegionId == regionId && room.X >= 0 && room.Y >= 0)
                count++;
        }
        return count >= RegionSize * RegionSize;
    }

    /// <summary>
    /// 按「铺满」推进解锁：中心铺满开四正；四正任一铺满开四角。
    /// 返回这次新开了哪些区域（没开就返回空表）。
    /// </summary>
    public List<int> TryUnlockByFill()
    {
        var opened = new List<int>();
        if (!HasRing(RingOrthogonal) && IsRegionFull(CenterRegion))
        {
            _unlockedMask |= OrthogonalMask;
            for (var i = 1; i <= 4; i++)
                opened.Add(i);
            return opened;
        }
        if (!HasRing(RingDiagonal))
        {
            for (var i = 1; i <= 4; i++)
            {
                if (IsRegionFull(i))
                {
                    _unlockedMask |= DiagonalMask;
                    for (var j = 5; j <= 8; j++)
                        opened.Add(j);
                    break;
                }
            }
        }
        return opened;
    }

    public string Name { get; set; } = "";

    /// <summary>
    /// 区域解锁的高水位线（含 POI 区域）。领地内的 0..8 看 <see cref="IsRegionUnlocked"/> 的掩码，
    /// POI 区域从 <see cref="MaxTerritoryRegions"/> 起顺延，靠这个计数把关。
    /// </summary>
    public int UnlockedRegions { get; private set; } = 1;

    /// <summary>可以给领地自定义取名的等级门槛。</summary>
    public const int NamingLevel = 1;

    /// <summary>
    /// 领地等级。达到 <see cref="NamingLevel"/> 后开放自定义命名。
    /// 由内容包或事件提升，本类不自行增长。
    /// </summary>
    public int Level { get; private set; } = 1;

    /// <summary>领地是否已可自定义命名。</summary>
    public bool CanName => Level >= NamingLevel;

    /// <summary>设定领地等级。低于 1 会被夹到 1。</summary>
    public void SetLevel(int level) => Level = Math.Max(1, level);

    public List<Room> Rooms { get; } = new();
    public Room? Room(int id) => Rooms.Find(r => r.Id == id);
    public List<Facility> Facilities { get; } = new();

    private readonly List<Recipe> _recipes = new();
    public List<Recipe> Recipes
    {
        get
        {
            if (_recipes.Count == 0)
            {
                Defs.DefLoader.EnsureInitialized();
                foreach (var def in Defs.DefDatabase<Defs.RecipeDef>.All)
                    _recipes.Add(def.ToRuntime());
            }
            return _recipes;
        }
    }

    /// <summary>各工种当前指定的生产目标产物（工种 Station => 目标 ItemId）。为空表示不限。</summary>
    public Dictionary<ActionKind, string> TargetCraftItems { get; } = new();

    public string GetTargetCraftItem(ActionKind station) =>
        TargetCraftItems.TryGetValue(station, out var item) ? item : "";

    /// <summary>
    /// 这门手艺此刻做不做这份配方：指定了目标就只做目标；没指定就做普通配方，
    /// 装备配方（兵器、甲）不会自己开炉——得有人下单，免得把铁锭全打成剑。
    /// </summary>
    public bool Makes(Recipe recipe, ActionKind task, Facility bench)
    {
        if (recipe.Station != task || recipe.Craft != bench.Craft)
            return false;
        var target = GetTargetCraftItem(task);
        return target.Length > 0 ? recipe.ItemId == target : recipe.Gear == null;
    }

    public void SetTargetCraftItem(ActionKind station, string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            TargetCraftItems.Remove(station);
        else
            TargetCraftItems[station] = itemId;
    }

    /// <summary>领地当前指定的烹饪目标料理（为空表示不限）。</summary>
    public string TargetCookItem
    {
        get => GetTargetCraftItem(ActionKind.Cook);
        set => SetTargetCraftItem(ActionKind.Cook, value);
    }

    public List<Guest> Guests { get; } = new();

    /// <summary>
    /// 运行时武器实例登记表。由 GameState 装配时挂上——
    /// 武器实例是跨背包/仓储/交易的同一份事实，报价要按实例自己的字段算。
    /// </summary>
    public Defs.WeaponRegistry Weapons { get; } = new();

    /// <summary>防具与饰品的运行时实例登记表。</summary>
    public Defs.EquipRegistry Equips { get; } = new();
    public Dictionary<int, Schedule> Schedules { get; } = new();
    public Dictionary<string, int> RoomEffects { get; } = new();

    /// <summary>
    /// 领地内所有能存货的设施。物品只存在这些设施里（或角色背包里），
    /// 没有领地级的虚空库存——凡是要“查全据点有多少”的地方都遍历这里。
    /// </summary>
    public IEnumerable<Facility> Storages => Facilities.FindAll(f => f.CanStore);

    /// <summary>据点所有设施存货 + 某个角色背包里，某物品的总数。</summary>
    public int CountWith(CharacterState? who, string itemId)
    {
        var total = who?.Bag.Get(itemId) ?? 0;
        foreach (var storage in Storages)
            total += storage.Contents.Get(itemId);
        return total;
    }

    /// <summary>
    /// 据点所有设施存货 + 某个角色背包，能否付得起这些材料。
    /// 据点级操作（建造/开拓/制作）用它，因此材料放在哪个货架上都能用。
    /// </summary>
    public bool CanPayWith(CharacterState? who, IReadOnlyList<RecipeCost> costs)
    {
        foreach (var cost in costs)
        {
            if (CountWith(who, cost.ItemId) < cost.Count)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 扣材料：先扣角色背包，不够的再从各设施存货里补。
    /// 调用前应先用 <see cref="CanPayWith"/> 确认付得起。
    /// </summary>
    public bool PayWith(CharacterState? who, IReadOnlyList<RecipeCost> costs)
    {
        if (!CanPayWith(who, costs))
            return false;
        foreach (var cost in costs)
        {
            var left = cost.Count;
            if (who != null)
            {
                var fromBag = Math.Min(left, who.Bag.Get(cost.ItemId));
                if (fromBag > 0)
                {
                    who.Bag.Add(cost.ItemId, -fromBag);
                    left -= fromBag;
                }
            }
            foreach (var storage in Storages)
            {
                if (left <= 0)
                    break;
                var take = Math.Min(left, storage.Contents.Get(cost.ItemId));
                if (take > 0)
                {
                    storage.Contents.Add(cost.ItemId, -take);
                    left -= take;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// 在指定房间找一样能吃的东西（背包优先，其次该房间设施的存货）。
    /// 找不到返回 null。吃东西必须有实物，不许凭空吃。
    /// </summary>
    public string? FindFoodIn(CharacterState who, int roomId)
    {
        foreach (var pair in who.Bag.Items)
        {
            if (pair.Value > 0 && IsFood(pair.Key))
                return pair.Key;
        }
        foreach (var facility in Facilities)
        {
            if (!facility.CanStore || !facility.Built || facility.RoomId != roomId)
                continue;
            foreach (var pair in facility.Contents.Items)
            {
                if (pair.Value > 0 && IsFood(pair.Key))
                    return pair.Key;
            }
        }
        return null;
    }

    /// <summary>
    /// 吃掉一样东西：背包优先，其次该房间设施的存货。成功返回被吃的物品 Id。
    /// </summary>
    public string? ConsumeFood(CharacterState who, int roomId)
    {
        var food = FindFoodIn(who, roomId);
        if (food == null)
            return null;
        if (who.Bag.Get(food) > 0)
        {
            who.Bag.Add(food, -1);
            return food;
        }
        foreach (var facility in Facilities)
        {
            if (!facility.CanStore || !facility.Built || facility.RoomId != roomId)
                continue;
            if (facility.Contents.Get(food) > 0)
            {
                facility.Contents.Add(food, -1);
                return food;
            }
        }
        return null;
    }

    /// <summary>
    /// 在某件设施处付料：只认这个人的背包 + 这件设施自己的存货。
    /// 工作台用这个——材料得有人搬到台子上，不能隔空从别的货架取。
    /// </summary>
    public bool CanPayAt(Facility bench, CharacterState who, IReadOnlyList<RecipeCost> costs)
    {
        foreach (var cost in costs)
        {
            var have = who.Bag.Get(cost.ItemId) + bench.Contents.Get(cost.ItemId);
            if (have < cost.Count)
                return false;
        }
        return true;
    }

    /// <summary>在某件设施处扣料：先扣这个人背包，不够再从这件设施自己的存货补。</summary>
    public bool PayAt(Facility bench, CharacterState who, IReadOnlyList<RecipeCost> costs)
    {
        if (!CanPayAt(bench, who, costs))
            return false;
        foreach (var cost in costs)
        {
            var left = cost.Count;
            var fromBag = System.Math.Min(left, who.Bag.Get(cost.ItemId));
            if (fromBag > 0)
            {
                who.Bag.Add(cost.ItemId, -fromBag);
                left -= fromBag;
            }
            if (left > 0)
                bench.Contents.Add(cost.ItemId, -left);
        }
        return true;
    }

    /// <summary>
    /// 产出归产出者：采集/制作出来的东西直接进他自己背包。
    /// 要放进库房由搬运逻辑（<see cref="Haul"/>）另行完成，产出本身不越权入库。
    /// </summary>
    public void Produce(CharacterState who, string itemId, int count)
    {
        if (count > 0 && itemId.Length > 0)
            who.Bag.Add(itemId, count);
    }

    /// <summary>
    /// 一份配方做完：普通配方出成品，装备配方锻一件实例（一件一单，做完撤掉指定目标）。都进做的人背包（之后照常搬进仓储）。
    /// 返回落进背包的 Id（装备是实例 Id）。
    /// </summary>
    public string Finish(CharacterState crafter, Recipe recipe)
    {
        if (recipe.Gear == null)
        {
            Produce(crafter, recipe.ItemId, recipe.OutputCount);
            return recipe.ItemId;
        }
        // 装备是一件一单：做完这件，这门手艺的指定目标就撤了。
        if (GetTargetCraftItem(recipe.Station) == recipe.ItemId)
            SetTargetCraftItem(recipe.Station, "");
        return ForgeGear(crafter, recipe.Gear, CraftQuality(crafter.Life(recipe.Skill)));
    }

    /// <summary>
    /// 照规格锻一件兵器或甲：材料 × 种类/槽位定基座，品质由手艺给定，不附魔、不祝福、不强化。
    /// 登记进武器/防具表，放进做的人背包，返回实例 Id。
    /// </summary>
    public string ForgeGear(CharacterState crafter, RecipeGear gear, Quality quality)
    {
        string id;
        if (gear.Weapon is { } type)
        {
            var weapon = WeaponForge.Forge(gear.Material, type, quality, "", false, 0);
            Weapons.Add(weapon);
            id = weapon.Id;
        }
        else
        {
            var armor = EquipForge.ForgeArmor(gear.Slot!.Value, gear.Material, quality, "", false, 0);
            Equips.Add(armor);
            id = armor.Id;
        }
        crafter.Bag.Add(id, 1);
        return id;
    }

    /// <summary>操练一回：手上那门兵器的熟练经验（进熟练前按角色既有分成翻倍）。</summary>
    public const int TrainWeaponExp = 10;

    /// <summary>操练一回：流派经验。</summary>
    public const int TrainStyleExp = 5;

    /// <summary>操练只练到这一级熟练；再往上得靠实战。</summary>
    public const int TrainLevelCap = 10;

    /// <summary>
    /// 在箭靶、操练场上练一回：涨手上兵器（空手按格斗）的熟练与流派经验。
    /// 熟练已到 <see cref="TrainLevelCap"/> 级就练不出东西了，返回 false。
    /// </summary>
    public static bool Drill(CharacterState c)
    {
        var weapon = c.MainWeapon ?? WeaponType.Unarmed;
        if (c.Weapons[(int)weapon].Level >= TrainLevelCap)
            return false;
        c.GainWeaponExp(weapon, TrainWeaponExp);
        c.GainStyleExp(c.EquippedStyle ?? StyleType.Unarmed, TrainStyleExp);
        return true;
    }

    /// <summary>手艺定品质：生活技能不到 8 粗糙，不到 25 普通，不到 50 精良，再往上史诗（开局的人手艺在 10 上下，出普通货）。</summary>
    public static Quality CraftQuality(int skill) => skill switch
    {
        < 8 => Quality.Crude,
        < 25 => Quality.Common,
        < 50 => Quality.Fine,
        _ => Quality.Epic,
    };

    // ---------- 耕地 ----------

    /// <summary>耕地状态判定，供玩家操作前置检查与 NPC 选活过滤共用。</summary>
    public enum FarmState
    {
        /// <summary>不是耕地，走普通采集。</summary>
        NotPlot,

        /// <summary>空地、应季、有种可播。</summary>
        ReadySow,

        /// <summary>作物长满，可收获。</summary>
        ReadyHarvest,

        /// <summary>作物生长中，无事可做。</summary>
        Growing,

        /// <summary>空地但不是这种作物能种的季节。</summary>
        OutOfSeason,

        /// <summary>空地、应季，但到处都没有种子。</summary>
        NoSeed,
    }

    /// <summary>耕地设施的作物定义。设施的 YieldItemId 命中某作物产物即为耕地。</summary>
    public CropDef? CropOf(Facility facility)
    {
        if (facility.YieldItemId.Length == 0)
            return null;
        DefLoader.EnsureInitialized();
        return DefDatabase<CropDef>.All.FirstOrDefault(c =>
            string.Equals(c.ProduceItemId, facility.YieldItemId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>这块地此刻的耕作状态。</summary>
    public FarmState PlotState(Facility facility, Season season, CharacterState? who)
    {
        var crop = CropOf(facility);
        if (crop == null)
            return FarmState.NotPlot;
        if (facility.CropDefName.Length == 0)
        {
            if (!crop.GrowsIn(season))
                return FarmState.OutOfSeason;
            return HasSeed(who, crop.SeedItemId, facility.RoomId)
                ? FarmState.ReadySow
                : FarmState.NoSeed;
        }
        return facility.Growth >= crop.GrowthDays
            ? FarmState.ReadyHarvest
            : FarmState.Growing;
    }

    /// <summary>
    /// 耕地上完成一次耕作（一格活干完）。空地播种，成熟收获，生长中空过。
    /// 收获产量沿用采集公式并套用产出系数；返回本次的产出日志（播种为无产出行）。
    /// handled 标记是不是耕地：false 时调用方走原采集路径。
    /// </summary>
    public WorkLog? FarmWork(CharacterState who, Facility facility, ActionKind task,
        Season season, Func<ActionKind, int>? yieldFor, out bool handled)
    {
        var crop = CropOf(facility);
        if (crop == null)
        {
            handled = false;
            return null;
        }
        handled = true;

        if (facility.CropDefName.Length == 0)
        {
            if (!crop.GrowsIn(season) || TakeSeed(who, crop.SeedItemId, facility.RoomId) <= 0)
                return null;
            facility.CropDefName = crop.DefName;
            facility.Growth = 0;
            who.GainLifeExp(ActionKindMap.SkillOf(task)!.Value, GatherExp);
            return new WorkLog
            {
                CharacterId = who.Id,
                Task = task,
                ItemId = crop.ProduceItemId,
                Count = 0,
                Skill = ActionKindMap.SkillOf(task)!.Value,
                Exp = GatherExp,
            };
        }

        if (facility.Growth < crop.GrowthDays)
            return null;

        var amount = Math.Clamp(Math.Max(1, who.Life(ActionKindMap.SkillOf(task)!.Value)) / 40, 1, 4);
        if (yieldFor != null)
            amount = Math.Max(1, amount * yieldFor(task) / 100);
        Produce(who, crop.ProduceItemId, amount);
        who.GainLifeExp(ActionKindMap.SkillOf(task)!.Value, GatherExp);
        facility.CropDefName = "";
        facility.Growth = 0;
        return new WorkLog
        {
            CharacterId = who.Id,
            Task = task,
            ItemId = crop.ProduceItemId,
            Count = amount,
            Skill = ActionKindMap.SkillOf(task)!.Value,
            Exp = GatherExp,
        };
    }

    /// <summary>取一份种子：背包优先，其次本房仓储，再次任意仓储。</summary>
    private int TakeSeed(CharacterState who, string seedItemId, int roomId)
    {
        if (who.Bag.Get(seedItemId) > 0)
        {
            who.Bag.Add(seedItemId, -1);
            return 1;
        }
        var storage = FindStockOf(seedItemId, roomId);
        if (storage != null && storage.Contents.Get(seedItemId) > 0)
        {
            storage.Contents.Add(seedItemId, -1);
            return 1;
        }
        return 0;
    }

    /// <summary>种子是否 anywhere 可得（某人背包或任何仓储）。who 为 null 时只查仓储。</summary>
    private bool HasSeed(CharacterState? who, string seedItemId, int roomId) =>
        (who != null && who.Bag.Get(seedItemId) > 0)
        || FindStockOf(seedItemId, roomId) != null;

    // ---------- 存取与搬运 ----------

    /// <summary>
    /// 从背包放进设施。受容量与过滤限制，实际只放得下这么多。
    /// 返回真正放进去的件数（0 表示这件设施不收）。
    /// </summary>
    public int StoreFrom(CharacterState who, Facility storage, string itemId, int count)
    {
        if (!storage.Accepts(itemId, Weapons) || count <= 0)
            return 0;
        var moved = System.Math.Min(count, System.Math.Min(who.Bag.Get(itemId), storage.FreeSpace()));
        if (moved <= 0)
            return 0;
        who.Bag.Add(itemId, -moved);
        storage.Contents.Add(itemId, moved);
        return moved;
    }

    /// <summary>
    /// 从设施取进背包。返回真正取出的件数。
    /// </summary>
    public int TakeFrom(CharacterState who, Facility storage, string itemId, int count)
    {
        if (count <= 0)
            return 0;
        var moved = System.Math.Min(count, storage.Contents.Get(itemId));
        if (moved <= 0)
            return 0;
        storage.Contents.Add(itemId, -moved);
        who.Bag.Add(itemId, moved);
        return moved;
    }

    /// <summary>
    /// 找一处能收下该物品的仓储设施：吃食优先送往餐桌，餐桌放不下就送进有餐桌的那间房（餐厅）的仓储
    /// ——人只在自己所在的房里找吃的，吃食放进别处的柜子就没人吃得着；其余物品优先本房，其次据点内任意。
    /// 找不到返回 null（没地方放）。
    /// </summary>
    public Facility? FindStorageFor(string itemId, int preferRoomId = -1)
    {
        if (IsFood(itemId))
        {
            var table = Facilities.Find(f => f.Built && f.IsTable && f.CanStore && f.Accepts(itemId, Weapons));
            if (table != null)
                return table;
            var pantry = Facilities.Find(f => f.Built && f.Accepts(itemId, Weapons)
                && Facilities.Exists(t => t.Built && t.IsTable && t.RoomId == f.RoomId));
            if (pantry != null)
                return pantry;
        }

        if (preferRoomId >= 0)
        {
            var here = Facilities.Find(f => f.Built && f.RoomId == preferRoomId && f.Accepts(itemId, Weapons));
            if (here != null)
                return here;
        }
        return Facilities.Find(f => f.Built && f.Accepts(itemId, Weapons));
    }

    /// <summary>
    /// 找一处存着该物品的设施：优先本房，其次据点内任意。
    /// 找不到返回 null（没处可取）。
    /// </summary>
    public Facility? FindStockOf(string itemId, int preferRoomId = -1)
    {
        if (preferRoomId >= 0)
        {
            var here = Facilities.Find(f => f.Built && f.Contents.Get(itemId) > 0 && f.RoomId == preferRoomId);
            if (here != null)
                return here;
        }
        return Facilities.Find(f => f.Built && f.Contents.Get(itemId) > 0);
    }

    /// <summary>
    /// 搬运：把某人背包里的一件东西放进指定仓储。返回实际搬过去的件数。
    /// 这是 NPC 搬运与玩家“放进货架”共同的底层。
    /// </summary>
    public int Haul(CharacterState who, Facility storage, string itemId, int count) =>
        StoreFrom(who, storage, itemId, count);

    /// <summary>井的产出、也是井里唯一能存的东西。</summary>
    public const string WellItemId = "水";

    /// <summary>每口井存水的上限。</summary>
    public const int WellWaterCap = 20;

    /// <summary>
    /// 井水回满。取水不是工作——有气力消耗的才是工作，取水与移动等价：
    /// 井就是现成的水源，水放在井的存货里，做饭缺水时由搬运行为从井里搬，
    /// 玩家下厨也直接从这里扣。产出井（yieldItemId 为水）每日回满。
    /// </summary>
    public void TopUpWells()
    {
        foreach (var facility in Facilities)
        {
            if (!facility.Built || facility.YieldItemId != WellItemId)
                continue;
            var shortOf = WellWaterCap - facility.Contents.Get(WellItemId);
            if (shortOf > 0)
                facility.Contents.Add(WellItemId, shortOf);
        }
    }

    /// <summary>新落的井自带一井水（之后每日回满，见 <see cref="TopUpWells"/>）。</summary>
    private static void SeedWellWater(Facility facility)
    {
        if (facility.YieldItemId == WellItemId && facility.Contents.Get(WellItemId) <= 0)
            facility.Contents.Add(WellItemId, WellWaterCap);
    }

    public bool UnlockRegion()
    {
        // 两段式解锁（2026-10-01 主人定）：
        //   中心铺满 → 开四正（北/东/南/西）；四正任一铺满 → 开四角。
        // 这个手动入口只负责「推进到下一档」，实际解锁由铺满触发（见 TryUnlockByFill）。
        if (!HasRing(RingOrthogonal))
        {
            _unlockedMask |= OrthogonalMask;
            return true;
        }
        if (!HasRing(RingDiagonal))
        {
            _unlockedMask |= DiagonalMask;
            return true;
        }
        return false;
    }

    public void SetUnlockedRegions(int count)
    {
        // 不再夹到 MaxRegions——POI 区域从 MaxTerritoryRegions 起顺延，可能远超 3。
        UnlockedRegions = System.Math.Max(1, count);
    }

    public bool AddGuest(Guest guest)
    {
        if (Guests.Exists(g => g.Id == guest.Id) || Rooms.Find(r => r.Id == guest.RoomId) == null)
            return false;
        Guests.Add(guest);
        return true;
    }

    public bool RemoveGuest(int guestId) => Guests.RemoveAll(g => g.Id == guestId) > 0;

    /// <summary>
    /// 是不是能吃的东西。唯一判据是 ThingDef.IsFood——
    /// 食物是物品定义自带的属性，不再由领地另记一份名单。
    /// </summary>
    public bool IsFood(string itemId)
    {
        Defs.DefaultDefs.EnsureInitialized();
        var def = Defs.Items.Get(itemId);
        return def != null && def.IsFood;
    }

    private readonly Dictionary<string, FoodTier> _foodTiers = new();

    public void SetFoodTier(string itemId, FoodTier tier) => _foodTiers[itemId] = tier;

    /// <summary>食物品级。优先显式设置，其次取物品定义，没有定义的物品按普通算。</summary>
    public FoodTier FoodTierOf(string itemId)
    {
        if (_foodTiers.TryGetValue(itemId, out var explicitTier))
            return explicitTier;
        Defs.DefaultDefs.EnsureInitialized();
        var def = Defs.Items.Get(itemId);
        return def != null && def.IsFood ? def.FoodTier : FoodTier.Plain;
    }

    /// <summary>卖出价相对基准价的折抵（商人抽成）。</summary>
    public const int SellRatioPercent = 60;

    /// <summary>今日集市行情：物品 → 存货与价格系数。每日 0 点重掷（RollMarketDay）。</summary>
    public Dictionary<string, MarketEntry> MarketDay { get; } = new();

    /// <summary>一件物品当天的集市行情。</summary>
    public sealed record MarketEntry(int Stock, int PricePercent);

    /// <summary>今日集市在售的武器（每日随机锻，买走即下架，玩家卖掉的会上架）。</summary>
    public List<MarketWeaponListing> MarketWeapons { get; } = new();

    /// <summary>集市在售武器的一行：武器实例 Id 与其当日价格系数。</summary>
    public sealed record MarketWeaponListing(string WeaponId, int PricePercent);

    /// <summary>今日武器行情系数（70-130）：集市售武与收购玩家武器同用一档。</summary>
    public int WeaponPricePercent { get; private set; } = 100;

    /// <summary>
    /// 重掷今日行情：存货按价值分档（便宜货常备，贵重看运气），价格系数 70-130。
    /// 存货 0 = 今日无货，买不了但仍可卖。
    /// 武器每日随机锻 3-6 件：材料/品质只取最便宜的三种，附魔低概率，不强化不祝福。
    /// 上架范围是全部物品定义——材料表本身也是物品，铁与布一样能买卖。
    /// </summary>
    public void RollMarketDay(Random random)
    {
        DefLoader.EnsureInitialized();
        MarketDay.Clear();
        foreach (var def in Defs.Items.All())
        {
            var stock = def.MarketValue switch
            {
                <= 5 => 4 + random.Next(9),
                <= 20 => 2 + random.Next(5),
                <= 60 => random.Next(5),
                _ => random.Next(3),
            };
            MarketDay[def.DefName] = new MarketEntry(stock, 70 + random.Next(61));
        }

        WeaponPricePercent = 70 + random.Next(61);
        MarketWeapons.Clear();
        // 上架武器只取最便宜的三种材料，要能打兵器——布与皮不在其列。
        var cheapMaterials = Defs.WeaponForge.WeaponMaterials().Take(3).ToList();
        // 类型只从锻造表真实存在的基座里抽——枚举里的 Unarmed 没有基座，抽到必炸。
        var types = DefDatabase<WeaponTypeDef>.All.Select(t => t.Type).ToArray();
        var qualities = new[] { Quality.Crude, Quality.Common, Quality.Fine };
        var enchantCount = DefDatabase<EnchantDef>.All.Count;
        // 武器每日随机上架 3-6 件：材料/品质只取前三种，附魔低概率，不强化不祝福。
        var weapons = 3 + random.Next(4);
        for (var i = 0; i < weapons; i++)
        {
            var material = cheapMaterials[random.Next(cheapMaterials.Count)];
            var type = types[random.Next(types.Length)];
            var quality = qualities[random.Next(qualities.Length)];
            var enchant = random.Next(100) < 10 && enchantCount > 0
                ? DefDatabase<EnchantDef>.All[random.Next(enchantCount)].DefName
                : "";
            var weapon = WeaponForge.Forge(material.DefName, type,
                quality: quality, enchant: enchant, blessed: false, enhance: 0);
            Weapons.Add(weapon);
            MarketWeapons.Add(new MarketWeaponListing(weapon.Id, WeaponPricePercent));
        }
    }

    /// <summary>读档回填集市行情：存货与价格系数（不重掷，读档即回到当天）。</summary>
    public void RestoreMarketDay(Dictionary<string, MarketEntry> saved)
    {
        foreach (var pair in saved)
            MarketDay[pair.Key] = pair.Value;
    }

    /// <summary>玩家把武器卖给集市：上架，可被买回。</summary>
    public void MarketListWeapon(string weaponId)
    {
        if (MarketWeapons.Find(l => l.WeaponId == weaponId) == null)
            MarketWeapons.Add(new MarketWeaponListing(weaponId, WeaponPricePercent));
    }

    /// <summary>集市在售武器被买走：下架。</summary>
    public void MarketDelistWeapon(string weaponId) =>
        MarketWeapons.RemoveAll(l => l.WeaponId == weaponId);

    /// <summary>读档回填武器行情：系数与在售清单（武器实例本身已随 Weapons 恢复）。</summary>
    public void RestoreWeaponMarket(int weaponPricePercent, Dictionary<string, int> listings)
    {
        WeaponPricePercent = weaponPricePercent;
        MarketWeapons.Clear();
        foreach (var pair in listings)
            MarketWeapons.Add(new MarketWeaponListing(pair.Key, pair.Value));
    }

    /// <summary>买入压库存，卖出抬库存——库存即价格。</summary>
    public void MarketBought(string itemId, int count)
    {
        var entry = MarketDay.GetValueOrDefault(itemId);
        if (entry == null)
            return;
        MarketDay[itemId] = entry with { Stock = Math.Max(0, entry.Stock - count) };
    }

    public void MarketSold(string itemId, int count)
    {
        var entry = MarketDay.GetValueOrDefault(itemId);
        if (entry == null)
            return;
        MarketDay[itemId] = entry with { Stock = entry.Stock + count };
    }

    /// <summary>
    /// 集市里的一件可交易对象：它此刻有多少货、买一份多少钱、卖一份收多少钱。
    /// 没有报价单——交易是**我的库存**与**市场库存**之间的互易，价格只由
    /// 基准价 × 当日系数（%）、卖出再打商人抽成决定。
    /// 库存 -1 = 市场永不出售（玩家的武器与设施），只能卖。
    /// </summary>
    public readonly record struct MarketListing(
        string ItemId, string Label, int Stock, int BuyPrice, int SellPrice)
    {
        /// <summary>市场此刻不肯卖（无货或根本不卖）。</summary>
        public bool SoldOut => Stock == 0;

        /// <summary>市场根本不收购（售价为 0）。</summary>
        public bool NoBuy => SellPrice <= 0;
    }

    /// <summary>
    /// 读一件物品此刻的市场行情。不再有显式报价单：
    /// 物品取集市当日库存与价格系数；武器按实例价值与当日武器系数；
    /// 设施只卖不买（ Stock = -1 ）。
    /// 不在表上的 Id 返回 null（不可交易）。
    /// </summary>
    public MarketListing? Listing(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return null;

        Defs.DefLoader.EnsureInitialized();
        var thing = Defs.Items.Get(itemId);
        if (thing != null)
        {
            var entry = MarketDay.GetValueOrDefault(itemId) ?? new MarketEntry(0, 100);
            var name = thing.Label.Length > 0 ? thing.Label : itemId;
            return ListingOf(itemId, name, thing.MarketValue, entry.Stock, entry.PricePercent);
        }

        // 运行时武器实例：集市在售的按当日武器系数买卖；玩家自己的只收不卖。
        var weapon = Weapons.Get(itemId);
        if (weapon != null)
        {
            var listing = MarketWeapons.Find(l => l.WeaponId == itemId);
            if (listing != null)
            {
                var buy = Math.Max(1, ScaleBy(weapon.Value, listing.PricePercent, 96));
                var sell = Math.Max(1, ScaleBy(weapon.Value, SellRatioPercent, listing.PricePercent));
                return new MarketListing(weapon.Id, weapon.Name, 1, buy, sell);
            }
            var sellOnly = Math.Max(1,
                ScaleBy(weapon.Value, SellRatioPercent, WeaponPricePercent));
            return new MarketListing(weapon.Id, weapon.Name, -1, 0, sellOnly);
        }

        // 甲与饰品实例：市场不卖成品甲，玩家做出来的只收不卖（同自己的武器）。
        var equip = Equips.Get(itemId);
        if (equip != null)
            return new MarketListing(equip.Id, equip.Name, -1, 0,
                Math.Max(1, ScaleBy(equip.Value, SellRatioPercent, WeaponPricePercent)));

        var facility = Defs.DefDatabase<Defs.FacilityDef>.All
            .FirstOrDefault(f => f.DefName.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (facility == null)
            return null;

        // 设施只卖不买：Stock = -1（市场无货可卖），收购价为 0（买方不收）。
        var facName = facility.Name.Length > 0 ? facility.Name : facility.DefName;
        var facValue = facility.Value();
        return new MarketListing(facility.DefName, facName, -1, 0,
            facValue > 0 ? Math.Max(1, facValue * SellRatioPercent / 100) : 0);
    }

    /// <summary>
    /// 两个百分比连乘只截断一次：价 × a% × b% 一路整数算到底再除，
    /// 分两步取整会把价格越压越低。
    /// </summary>
    private static int ScaleBy(int value, int percentA, int percentB) =>
        value * percentA * percentB / 10000;

    private static MarketListing ListingOf(string itemId, string label, int value,
        int stock, int pricePercent)
    {
        var base_ = value < 0 ? 0 : value;

        // 集市行情：当日价格系数之外，存货压价——卖 -5%/件（下限七成），买 -4%/件（下限六成）。
        var sellFactor = Math.Max(70, 100 - stock * 5);
        var buyFactor = Math.Max(60, 100 - stock * 4);
        var sell = base_ > 0
            ? Math.Max(1, base_ * SellRatioPercent / 100 * pricePercent / 100 * sellFactor / 100)
            : 0;
        var buy = base_ > 0
            ? Math.Max(1, base_ * pricePercent / 100 * buyFactor / 100)
            : 0;
        return new MarketListing(itemId, label, stock, buy, sell);
    }

    /// <summary>
    /// 今日集市在售的全部：物品定义（材料也是物品）+ 集市在售武器 + 可拆卖设施。
    /// 房间（RoomDef）不在此列。库存 0 也列出——标注“今日无货”，仍可卖。
    /// </summary>
    public IReadOnlyList<MarketListing> Listings()
    {
        var list = new List<MarketListing>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in Defs.Items.All())
        {
            var row = Listing(def.DefName);
            if (row != null && seen.Add(row.Value.ItemId))
                list.Add(row.Value);
        }
        foreach (var weapon in Weapons.All)
        {
            var row = Listing(weapon.Id);
            if (row != null && seen.Add(row.Value.ItemId))
                list.Add(row.Value);
        }
        foreach (var def in Defs.DefDatabase<Defs.FacilityDef>.All)
        {
            var row = Listing(def.DefName);
            if (row != null && seen.Add(row.Value.ItemId))
                list.Add(row.Value);
        }

        list.Sort((a, b) => string.CompareOrdinal(a.ItemId, b.ItemId));
        return list;
    }

    public bool AddRoom(Room room)
    {
        // 上限只管领地自己的房；兴趣点房（区号 ≥ MaxTerritoryRegions）是进场时的临时房，不占名额。
        if (room.RegionId < MaxTerritoryRegions && Rooms.Count(r => r.RegionId < MaxTerritoryRegions) >= MaxRooms)
            return false;
        if (!IsRegionUnlocked(room.RegionId))
            return false;
        if (Rooms.Exists(r => r.Id == room.Id))
            return false;
        room.EnsureDefaultTag();
        Rooms.Add(room);
        return true;
    }

    /// <summary>
    /// 按坐标找房间。**生产路径一律带区域**——不同区域的 5×5 坐标是重叠的，
    /// 不带区域找会跨区串台。
    /// </summary>
    public Room? RoomAt(int regionId, int x, int y) =>
        Rooms.Find(r => r.RegionId == regionId && r.X == x && r.Y == y);

    /// <summary>
    /// 不带区域的旧签名：全表找第一间。只给「不关心区域」的核对/测试用，
    /// 生产代码别用。
    /// </summary>
    public Room? RoomAt(int x, int y) => Rooms.Find(r => r.X == x && r.Y == y);

    /// <summary>主人此刻在哪间房（-1 = 不在领地里）。自动上锁按它判。</summary>
    public int MasterRoomId { get; set; } = -1;

    /// <summary>
    /// 主人的床：开局卧室那张（内容表 masterBed），之后是主人最近一次睡下的那张。
    /// 别人好感不够同床（<see cref="Character.Intimacy.SharesBed"/>）就不睡它——女仆也一样，她得有自己的床。
    /// </summary>
    public int MasterBedId { get; set; } = -1;

    /// <summary>主人的房间：主人的床摆在哪间。门锁（<see cref="Room.Lock"/>）只对这一间有意义；没有主人的床就是 -1。</summary>
    public int MasterBedroomId => Facilities.Find(f => f.Id == MasterBedId)?.RoomId ?? -1;

    /// <summary>主人是不是正在睡。睡着时主人的房间自动锁上。</summary>
    public bool MasterAsleep { get; set; }

    /// <summary>
    /// 有人睡着、把门反锁了的房间 → 那人是谁、放不放主人进来（肯与主人同床的人不拦主人）。
    /// 除了女仆，人一睡下就锁门，醒了才开（由领地时钟每格按「谁在哪张床上睡着」重算，不进存档）。
    /// </summary>
    public Dictionary<int, SleeperLock> SleeperLocks { get; } = new();

    public readonly record struct SleeperLock(int SleeperId, bool AdmitsMaster);

    /// <summary>
    /// 这间房的门此刻关没关上（对一个寻常外人而言）：有人睡着反锁了，或者是主人的房间且按门锁该锁。
    /// 具体某人进不进得去看 <see cref="BarsEntry"/>。
    /// </summary>
    public bool IsLocked(Room room)
    {
        if (room == null)
            return false;
        if (SleeperLocks.ContainsKey(room.Id))
            return true;
        return room.Id == MasterBedroomId && room.Lock switch
        {
            RoomLock.Locked => true,
            RoomLock.Unlocked => false,
            _ => MasterRoomId != room.Id || MasterAsleep,
        };
    }

    /// <summary>
    /// 这个人此刻进不进得了这间房。门锁一视同仁，主人也不例外：
    /// - 有人锁门睡下：除了睡着的那人自己，谁都进不去（肯与主人同床的人不拦主人）。
    /// - 主人的房间：主人自己随时进得去。手动锁上＝别人一律进不去，女仆也不例外；手动敞开＝谁都进得去；
    ///   自动＝主人不在屋里或在睡时锁上，只放女仆（她与主人同屋）。
    /// </summary>
    public bool BarsEntry(Room room, CharacterState who)
    {
        if (SleeperLocks.TryGetValue(room.Id, out var sleeper))
            return sleeper.SleeperId != who.Id && !(who.IsMaster && sleeper.AdmitsMaster);
        if (room.Id != MasterBedroomId || who.IsMaster)
            return false;
        return room.Lock switch
        {
            RoomLock.Locked => true,
            RoomLock.Unlocked => false,
            _ => (MasterRoomId != room.Id || MasterAsleep) && !who.SharesRoomWithMaster(),
        };
    }

    /// <summary>被请出门时去哪：隔壁第一间开着、这人进得去的房；没有就是 null（留在原地，不把人关死）。</summary>
    public Room? DoorOut(Room room, CharacterState who) =>
        room.Links.Select(id => Rooms.Find(r => r.Id == id))
            .FirstOrDefault(r => r != null && r.Open && !BarsEntry(r, who));

    /// <summary>
    /// 房间间的最短通路（BFS）。找不到返回空表。
    /// passable 用于"这个角色能不能进这间房"的额外判定；目标房间本身不查。
    /// barred 是门锁：进不去的房间（含目标）走不通；不给就按寻常外人算（<see cref="IsLocked"/>），
    /// 知道是谁走就传 <c>r => BarsEntry(r, who)</c>。
    /// </summary>
    public List<int> Route(int fromRoom, int toRoom, Func<Room, bool>? passable = null, Func<Room, bool>? barred = null)
    {
        barred ??= IsLocked;
        var queue = new Queue<int>();
        var prev = new Dictionary<int, int> { [fromRoom] = -1 };
        queue.Enqueue(fromRoom);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (id == toRoom)
                break;
            var room = Rooms.Find(r => r.Id == id);
            if (room == null)
                continue;
            foreach (var next in room.Links)
            {
                if (prev.ContainsKey(next))
                    continue;
                var node = Rooms.Find(r => r.Id == next);
                if (node == null || !node.Open || barred(node))
                    continue;
                if (next != toRoom && passable != null && !passable(node))
                    continue;
                prev[next] = id;
                queue.Enqueue(next);
            }
        }
        var path = new List<int>();
        if (!prev.ContainsKey(toRoom))
            return path;
        for (var id = toRoom; id != fromRoom; id = prev[id])
            path.Add(id);
        path.Reverse();
        return path;
    }


    // ---------- 房间朝向与门 ----------
    // 同一区域里网格四邻的两间房，共用的那条边就是一扇「门」：两房互在 Links 里＝门开（连通），否则是墙。
    // 人只能沿连通的门走（玩家前往、NPC 寻路都走 Links），非连通域过不去。

    public static readonly RoomDir[] RoomDirs = { RoomDir.North, RoomDir.East, RoomDir.South, RoomDir.West };

    public static (int Dx, int Dy) DirDelta(RoomDir dir) => dir switch
    {
        RoomDir.North => (0, -1),
        RoomDir.East => (1, 0),
        RoomDir.South => (0, 1),
        _ => (-1, 0),
    };

    public static string DirName(RoomDir dir) => dir switch
    {
        RoomDir.North => "北",
        RoomDir.East => "东",
        RoomDir.South => "南",
        _ => "西",
    };

    /// <summary>这间房某个朝向上的邻房（同区网格四邻）；房间没上网格或那边没房间则为 null。</summary>
    public Room? NeighborAt(Room room, RoomDir dir)
    {
        if (room.X < 0 || room.Y < 0)
            return null;
        var (dx, dy) = DirDelta(dir);
        return RoomAt(room.RegionId, room.X + dx, room.Y + dy);
    }

    /// <summary>两间房是不是同区网格四邻。</summary>
    public static bool Adjacent(Room a, Room b) =>
        a.RegionId == b.RegionId && a.X >= 0 && a.Y >= 0 && b.X >= 0 && b.Y >= 0
        && Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    /// <summary>某朝向的门开着没有：那边有房间且两房连通。</summary>
    public bool DoorOpen(Room room, RoomDir dir) =>
        NeighborAt(room, dir) is { } n && room.Links.Contains(n.Id);

    /// <summary>新上网格的房间：四面已开放的邻房一律开门连通（之后可在建造页里逐面改）。</summary>
    public void LinkNeighbors(Room room)
    {
        foreach (var dir in RoomDirs)
            if (NeighborAt(room, dir) is { Open: true } n)
                Link(room.Id, n.Id);
    }

    /// <summary>房间挪位后：同区里已不相邻的旧通路拆掉（门只开在共用的边上）。</summary>
    public void PruneDetachedLinks(Room room)
    {
        foreach (var id in new List<int>(room.Links))
            if (Room(id) is { } other && other.RegionId == room.RegionId && !Adjacent(room, other))
                Unlink(room.Id, id);
    }

    public bool Link(int fromId, int toId)
    {
        var from = Rooms.Find(r => r.Id == fromId);
        var to = Rooms.Find(r => r.Id == toId);
        if (from == null || to == null || fromId == toId)
            return false;
        if (!from.Links.Contains(toId))
            from.Links.Add(toId);
        if (!to.Links.Contains(fromId))
            to.Links.Add(fromId);
        return true;
    }

    public bool Unlink(int fromId, int toId)
    {
        var from = Rooms.Find(r => r.Id == fromId);
        var to = Rooms.Find(r => r.Id == toId);
        if (from == null || to == null)
            return false;
        var removed = from.Links.Remove(toId);
        removed |= to.Links.Remove(fromId);
        return removed;
    }

    public bool CanOpen(Room room, long money) =>
        !room.Open && IsRegionUnlocked(room.RegionId) && money >= room.OpenCost;

    /// <summary>
    /// 这间未开发的房间是不是「挨着已开发的地方」——判定只看两件事：
    /// 网格四邻（同一块内全局坐标差 1）或已有通路连到某间已开发的房间。
    /// 开发页据此决定哪几格可以点（点了才谈得上开拓）。
    /// </summary>
    public bool NearOpenRoom(Room room)
    {
        foreach (var other in Rooms)
        {
            if (!other.Open || other.Id == room.Id || other.RegionId != room.RegionId)
                continue;
            if (room.Links.Contains(other.Id))
                return true;
            if (other.X >= 0 && other.Y >= 0
                && System.Math.Abs(other.X - room.X) + System.Math.Abs(other.Y - room.Y) == 1)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 这个格子是不是「挨着已开发的地方」——只看**同一区域内**网格四邻有没有已开发的房间。
    /// 空格子（还没有房间实体）用它判定能不能开拓；已有房间实体的走 <see cref="NearOpenRoom"/>。
    /// </summary>
    public bool NearOpenAt(int regionId, int x, int y)
    {
        foreach (var other in Rooms)
        {
            if (!other.Open || other.X < 0 || other.Y < 0 || other.RegionId != regionId)
                continue;
            if (System.Math.Abs(other.X - x) + System.Math.Abs(other.Y - y) == 1)
                return true;
        }
        return false;
    }

    /// <summary>已开发（Open）的房间数。开拓定价按它递增——越开越贵。</summary>
    public int OpenRoomCount
    {
        get
        {
            var n = 0;
            foreach (var room in Rooms)
            {
                if (room.Open)
                    n++;
            }
            return n;
        }
    }

    public bool OpenRoom(int roomId, ref long money)
    {
        var room = Rooms.Find(r => r.Id == roomId);
        if (room == null || !CanOpen(room, money))
            return false;
        money -= room.OpenCost;
        room.Open = true;
        return true;
    }

    /// <summary>这间房里已摆的设施件数。</summary>
    public int FacilityCount(int roomId) => Facilities.FindAll(f => f.RoomId == roomId).Count;

    /// <summary>这间房还摆不摆得下一件设施（上限见 <see cref="Room.MaxFacilities"/>）。</summary>
    public bool HasFacilitySlot(int roomId) => FacilityCount(roomId) < Housing.Room.MaxFacilities;

    /// <summary>室内房间的标签：家具只能摆这种房，打地铺也只在这种房里打。</summary>
    public const string IndoorTag = "室内";

    /// <summary>城镇里的商店（聚落场景的房间标签）：人进了这一间才能买卖。</summary>
    public const string CityShopTag = "商店";

    /// <summary>室外房间的标签：田地、圈舍、资源点、井与营火只能建在这种房里。</summary>
    public const string OutdoorTag = "室外";

    /// <summary>房间对口工作的进度加成（百分比）。</summary>
    public const int RoomBonusPercent = 20;

    /// <summary>在这间房里干这项活的进度倍率（百分比）：对口 100+<see cref="RoomBonusPercent"/>，否则 100。</summary>
    public int RoomWorkPercent(int roomId, ActionKind task) =>
        Room(roomId)?.BonusActions.Contains(task) == true ? 100 + RoomBonusPercent : 100;

    /// <summary>设施的房间标签要求这间房满足不满足（家具要室内、田地圈舍要室外）。</summary>
    public static bool Fits(Room room, string roomTag) => roomTag.Length == 0 || room.HasTag(roomTag);

    /// <summary>摆不进去的缘由，界面上灰掉的那一行写它。</summary>
    public static string FitReason(string roomTag) => roomTag switch
    {
        IndoorTag => "只能摆在室内",
        OutdoorTag => "只能建在室外",
        _ => $"要{roomTag}的房间",
    };

    public bool AddFacility(Facility facility)
    {
        if (Rooms.Find(r => r.Id == facility.RoomId) == null)
            return false;
        if (!HasFacilitySlot(facility.RoomId))
            return false;
        if (Facilities.Exists(f => f.Id == facility.Id))
            return false;
        SeedWellWater(facility);
        Facilities.Add(facility);
        if (facility.Built && facility.EffectId.Length > 0)
            RoomEffects[facility.EffectId] = RoomEffects.GetValueOrDefault(facility.EffectId) + 1;
        return true;
    }

    public bool Build(int facilityId, ref long money)
    {
        var facility = Facilities.Find(f => f.Id == facilityId);
        var room = facility == null ? null : Rooms.Find(r => r.Id == facility.RoomId);
        if (facility == null || facility.Built || room == null || !room.Open || money < facility.BuildCost)
            return false;
        money -= facility.BuildCost;
        facility.Built = true;
        if (facility.EffectId.Length > 0)
            RoomEffects[facility.EffectId] = RoomEffects.GetValueOrDefault(facility.EffectId) + 1;
        return true;
    }

    public int Effect(string effectId) => RoomEffects.GetValueOrDefault(effectId);

    private void RegisterEffect(Facility facility, int delta)
    {
        if (!facility.Built || facility.EffectId.Length == 0)
            return;
        var value = RoomEffects.GetValueOrDefault(facility.EffectId) + delta;
        if (value > 0)
            RoomEffects[facility.EffectId] = value;
        else
            RoomEffects.Remove(facility.EffectId);
    }

    /// <summary>开发：设施被拆除时注销其房间效果。</summary>
    public void RemoveEffect(Facility facility)
    {
        if (!facility.Built || facility.EffectId.Length == 0)
            return;
        var value = RoomEffects.GetValueOrDefault(facility.EffectId) - 1;
        if (value > 0)
            RoomEffects[facility.EffectId] = value;
        else
            RoomEffects.Remove(facility.EffectId);
    }

    /// <summary>开发：收入建好而未放置的设施（不入任何房间，不参与房间效果）。</summary>
    public bool AddUnplacedFacility(Facility facility)
    {
        if (facility.RoomId >= 0)
            return false;
        if (Facilities.Exists(f => f.Id == facility.Id))
            return false;
        SeedWellWater(facility);
        Facilities.Add(facility);
        return true;
    }

    /// <summary>开发：把未放置的设施放进房间（建成设施自此计入房间效果）。</summary>
    public bool PlaceFacility(int roomId, Facility facility)
    {
        var room = Rooms.Find(r => r.Id == roomId);
        if (room == null || !room.Open || facility.RoomId >= 0 || !HasFacilitySlot(roomId)
            || !Fits(room, facility.RoomTag))
            return false;
        facility.RoomId = roomId;
        RegisterEffect(facility, +1);
        return true;
    }

    /// <summary>开发：把未放置的房间放到网格空位上。</summary>
    public bool PlaceRoom(int x, int y, Room room)
    {
        if (room.X >= 0 || x < 0 || y < 0 || RoomAt(room.RegionId, x, y) != null)
            return false;
        room.X = x;
        room.Y = y;
        return true;
    }

    /// <summary>开发菜单：可建造的房间一览（地形类房间不可建）。</summary>
    public List<Room> BuildableRooms()
    {
        var list = new List<Room>();
        foreach (var room in Rooms)
        {
            if (room.Buildable && list.Find(r => r.Name == room.Name) == null)
                list.Add(room);
        }
        return list;
    }

    /// <summary>开发菜单：可建造的设施一览（自然资源不可建）。</summary>
    public List<Facility> BuildableFacilities()
    {
        var list = new List<Facility>();
        foreach (var facility in Facilities)
        {
            if (facility.Buildable && list.Find(f => f.Name == facility.Name) == null)
                list.Add(facility);
        }
        return list;
    }

    /// <summary>
    /// 房间能支撑的行动：房内每件设施按自己的行动集取并集，
    /// 再并上房间本身承载的行动（观察——设施就是房间本身）。
    /// 界面"此处能干什么"直接读这个。
    /// 容量为零的设施进不去也没有互动，其行动不对外提供。
    /// </summary>
    public List<ActionKind> RoomActions(int roomId)
    {
        var actions = new List<ActionKind> { ActionKind.Observe };
        foreach (var facility in Facilities)
        {
            if (!facility.Built || facility.RoomId != roomId || facility.Capacity <= 0)
                continue;
            foreach (var action in facility.Actions)
            {
                if (!actions.Contains(action))
                    actions.Add(action);
            }
        }
        return actions;
    }

    public void AddRecipe(Recipe recipe) => Recipes.Add(recipe);

    public Schedule ScheduleOf(int characterId)
    {
        if (!Schedules.TryGetValue(characterId, out var schedule))
        {
            schedule = new Schedule();
            Schedules[characterId] = schedule;
        }
        return schedule;
    }

    /// <summary>
    /// 设某角色某段的安排：开关，以及工作时点名的那件设施。
    /// 空闲时设施自动清空；点名一件不存在的设施则拒绝。
    /// </summary>
    public bool Assign(int characterId, int slot, SlotMode mode, int facilityId = -1)
    {
        if (slot < 0 || slot >= WorkSlot.Count)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (mode != SlotMode.Free && facilityId >= 0)
        {
            var fac = Facilities.Find(f => f.Id == facilityId);
            if (fac == null)
                return false;
            // 工作模式下必须是工作设施（支持至少一项工作行动）
            if (mode == SlotMode.Work && TaskOf(fac) == ActionKind.None)
                return false;
        }
        ScheduleOf(characterId).Slots[slot] = new SlotAssignment
        {
            Mode = mode,
            FacilityId = mode == SlotMode.Free ? -1 : facilityId,
        };
        return true;
    }

    /// <summary>设施拆了：排到它的时段一律退回空闲。</summary>
    public void Unassign(int facilityId)
    {
        foreach (var schedule in Schedules.Values)
        {
            for (var slot = 0; slot < WorkSlot.Count; slot++)
            {
                if (schedule.Slots[slot].FacilityId == facilityId)
                    schedule.Slots[slot] = SlotAssignment.Free;
            }
        }
    }

    /// <summary>某角色某段的安排。</summary>
    public SlotAssignment AssignmentOf(int characterId, int slot) =>
        ScheduleOf(characterId).Slots[slot];

    /// <summary>
    /// 结算一个 6 小时段（测试钩子）：工作时段到点名的那件设施干活，
    /// 同一件设施按 Capacity 先到先得；空闲不出产。
    /// 真实时间不走这里——那在 <see cref="TerritoryClock.Step"/>。
    /// </summary>
    public List<WorkLog> ResolveSlot(int slot, Roster roster, Func<int, int>? roll = null, Season season = Season.Spring)
    {
        var logs = new List<WorkLog>();
        var used = new Dictionary<int, int>();
        foreach (var character in roster.Members)
        {
            if (character.IsMaster)
                continue;
            var assignment = ScheduleOf(character.Id).Slots[slot];
            if (assignment.Mode != SlotMode.Work || assignment.FacilityId < 0)
                continue;
            var facility = Facilities.Find(f => f.Id == assignment.FacilityId && f.Built);
            if (facility == null || used.GetValueOrDefault(facility.Id) >= facility.Capacity)
                continue;
            if (!character.Affect.AcceptsWork())
                continue;
            used[facility.Id] = used.GetValueOrDefault(facility.Id) + 1;
            var task = TaskOf(facility);
            var log = ActionKindMap.IsExtractive(task)
                ? Gather(slot, character, facility, task, roll, season)
                : Craft(slot, character, facility, task);
            if (log != null)
                logs.Add(log);
        }
        return logs;
    }

    /// <summary>设施支持的第一件工作行动；没有返回 None。</summary>
    private static ActionKind TaskOf(Facility facility)
    {
        foreach (var task in ActionKindMap.WorkOrdered)
        {
            if (facility.Supports(task))
                return task;
        }
        return ActionKind.None;
    }

    private WorkLog Gather(
        int slot, CharacterState character, Facility facility, ActionKind task,
        Func<int, int>? roll, Season season)
    {
        // 耕地按播种/收获结算；生长中或没种可播时这一格空过（无产出）。
        var farm = FarmWork(character, facility, task, season, null, out var handled);
        if (handled)
        {
            return farm ?? new WorkLog
            {
                CharacterId = character.Id,
                Slot = slot,
                Task = task,
                ItemId = facility.YieldItemId,
                Skill = ActionKindMap.SkillOf(task)!.Value,
            };
        }
        var stat = Math.Max(1, character.Life(ActionKindMap.SkillOf(task)!.Value));
        var amount = Math.Clamp(stat / 40, 1, 4);
        if (roll != null)
            amount = Math.Max(1, roll(amount));
        if (facility.YieldItemId.Length > 0)
            Produce(character, facility.YieldItemId, amount);
        character.GainLifeExp(ActionKindMap.SkillOf(task)!.Value, GatherExp);
        return new WorkLog
        {
            CharacterId = character.Id,
            Slot = slot,
            Task = task,
            ItemId = facility.YieldItemId,
            Count = amount,
            Skill = ActionKindMap.SkillOf(task)!.Value,
            Exp = GatherExp,
        };
    }

    private WorkLog? Craft(
        int slot, CharacterState character, Facility facility, ActionKind task)
    {
        var recipe = Recipes.Find(r => Makes(r, task, facility) && CanPayWith(character, r.Costs));
        if (recipe == null || !PayWith(character, recipe.Costs))
            return null;
        Finish(character, recipe);
        character.GainLifeExp(recipe.Skill, CraftExp);
        return new WorkLog
        {
            CharacterId = character.Id,
            Slot = slot,
            Task = task,
            ItemId = recipe.ItemId,
            Count = recipe.OutputCount,
            Skill = recipe.Skill,
            Exp = CraftExp,
        };
    }
}
