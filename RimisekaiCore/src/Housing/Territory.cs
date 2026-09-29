using System;
using System.Collections.Generic;
using Rimisekai.Character;

namespace Rimisekai.Housing;

public enum RoomPermission
{
    Public = 0,
    MasterOnly = 1,
    Faction = 2,
}

public sealed class Room
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int RegionId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Open { get; set; }
    public int OpenCost { get; init; }
    public RoomPermission Permission { get; set; } = RoomPermission.Public;
    public List<int> Links { get; } = new();
    public List<RecipeCost> MaterialCost { get; } = new();
    public bool Buildable { get; set; }

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

    public string Name { get; set; } = "";
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
    public List<Facility> Facilities { get; } = new();
    public List<Recipe> Recipes { get; } = new();
    public List<Guest> Guests { get; } = new();
    public List<MarketOffer> Market { get; } = new();
    public HashSet<string> Foods { get; } = new();
    public Dictionary<string, FoodTier> FoodTiers { get; } = new();
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

    // ---------- 存取与搬运 ----------

    /// <summary>
    /// 从背包放进设施。受容量与过滤限制，实际只放得下这么多。
    /// 返回真正放进去的件数（0 表示这件设施不收）。
    /// </summary>
    public int StoreFrom(CharacterState who, Facility storage, string itemId, int count)
    {
        if (!storage.Accepts(itemId) || count <= 0)
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
    /// 找一处能收下该物品的仓储设施：优先本房，其次据点内任意。
    /// 找不到返回 null（没地方放）。
    /// </summary>
    public Facility? FindStorageFor(string itemId, int preferRoomId = -1)
    {
        if (preferRoomId >= 0)
        {
            var here = Facilities.Find(f => f.Built && f.RoomId == preferRoomId && f.Accepts(itemId));
            if (here != null)
                return here;
        }
        return Facilities.Find(f => f.Built && f.Accepts(itemId));
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

    public bool UnlockRegion()
    {
        if (UnlockedRegions >= MaxRegions)
            return false;
        UnlockedRegions++;
        return true;
    }

    public void SetUnlockedRegions(int count)
    {
        UnlockedRegions = System.Math.Clamp(count, 1, MaxRegions);
    }

    public bool AddGuest(Guest guest)
    {
        if (Guests.Exists(g => g.Id == guest.Id) || Rooms.Find(r => r.Id == guest.RoomId) == null)
            return false;
        Guests.Add(guest);
        return true;
    }

    public bool RemoveGuest(int guestId) => Guests.RemoveAll(g => g.Id == guestId) > 0;

    public void AddFood(string itemId)
    {
        if (itemId.Length > 0)
            Foods.Add(itemId);
    }

    public void SetFoodTier(string itemId, FoodTier tier)
    {
        if (itemId.Length > 0)
        {
            Foods.Add(itemId);
            FoodTiers[itemId] = tier;
        }
    }

    public bool IsFood(string itemId)
    {
        Defs.DefaultDefs.EnsureInitialized();
        var def = Defs.DefDatabase<Defs.ThingDef>.Get(itemId);
        if (def != null && def.IsFood)
            return true;
        return Foods.Contains(itemId);
    }

    public FoodTier FoodTierOf(string itemId)
    {
        if (FoodTiers.TryGetValue(itemId, out var tier))
            return tier;
        Defs.DefaultDefs.EnsureInitialized();
        var def = Defs.DefDatabase<Defs.ThingDef>.Get(itemId);
        if (def != null && def.IsFood)
            return def.FoodTier;
        return FoodTier.Plain;
    }

    public void AddOffer(MarketOffer offer)
    {
        var index = Market.FindIndex(o => o.ItemId == offer.ItemId);
        if (index < 0)
            Market.Add(offer);
        else
            Market[index] = offer;
    }

    public bool AddRoom(Room room)
    {
        if (Rooms.Count >= MaxRooms || room.RegionId < 0 || room.RegionId >= UnlockedRegions)
            return false;
        if (Rooms.Exists(r => r.Id == room.Id))
            return false;
        room.EnsureDefaultTag();
        Rooms.Add(room);
        return true;
    }

    public Room? RoomAt(int x, int y) => Rooms.Find(r => r.X == x && r.Y == y);

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
        !room.Open && room.RegionId < UnlockedRegions && money >= room.OpenCost;

    public bool OpenRoom(int roomId, ref long money)
    {
        var room = Rooms.Find(r => r.Id == roomId);
        if (room == null || !CanOpen(room, money))
            return false;
        money -= room.OpenCost;
        room.Open = true;
        return true;
    }

    public bool AddFacility(Facility facility)
    {
        if (Rooms.Find(r => r.Id == facility.RoomId) == null)
            return false;
        if (Facilities.Exists(f => f.Id == facility.Id))
            return false;
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
        Facilities.Add(facility);
        return true;
    }

    /// <summary>开发：把未放置的设施放进房间（建成设施自此计入房间效果）。</summary>
    public bool PlaceFacility(int roomId, Facility facility)
    {
        var room = Rooms.Find(r => r.Id == roomId);
        if (room == null || !room.Open || facility.RoomId >= 0)
            return false;
        facility.RoomId = roomId;
        RegisterEffect(facility, +1);
        return true;
    }

    /// <summary>开发：把未放置的房间放到网格空位上。</summary>
    public bool PlaceRoom(int x, int y, Room room)
    {
        if (room.X >= 0 || x < 0 || y < 0 || RoomAt(x, y) != null)
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
    /// </summary>
    public List<ActionKind> RoomActions(int roomId)
    {
        var actions = new List<ActionKind> { ActionKind.Observe };
        foreach (var facility in Facilities)
        {
            if (!facility.Built || facility.RoomId != roomId)
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

    /// <summary>设某角色某段的开关（空闲 / 工作 / 不干活）。</summary>
    public void Assign(int characterId, int slot, SlotMode mode)
    {
        if (slot < 0 || slot >= WorkSlot.Count)
            throw new ArgumentOutOfRangeException(nameof(slot));
        ScheduleOf(characterId).Slots[slot] = mode;
    }

    // ---------- 工作优先级 ----------

    /// <summary>
    /// 每个角色对每类工作的优先级：0 = 不做，1-4 = 档位（1 最高）。
    /// 工作时段里干什么由它决定，与时段开关是两件事（对应 RimWorld 的 Work 页与 Schedule 页）。
    /// </summary>
    public Dictionary<int, Dictionary<ActionKind, int>> Priorities { get; } = new();

    /// <summary>某角色对某类工作的优先级。没设过返回 0（不做）。</summary>
    public int PriorityOf(int characterId, ActionKind task) =>
        Priorities.TryGetValue(characterId, out var map) && map.TryGetValue(task, out var value)
            ? value
            : 0;

    /// <summary>设某角色对某类工作的优先级。0 或负数 = 不做。</summary>
    public void SetPriority(int characterId, ActionKind task, int priority)
    {

        if (!Priorities.TryGetValue(characterId, out var map))
        {
            map = new Dictionary<ActionKind, int>();
            Priorities[characterId] = map;
        }
        var value = ActionKindMap.ClampPriority(priority);
        if (value <= 0)
            map.Remove(task);
        else
            map[task] = value;
    }

    /// <summary>
    /// 该角色能干的工作，按"档位升序、同档按工作类型行序"排好。
    /// 只列出优先级 &gt; 0 的；空表表示这人什么活都没派。
    /// </summary>
    public List<ActionKind> TasksByPriority(int characterId)
    {
        var list = new List<ActionKind>();
        foreach (var task in ActionKindMap.WorkOrdered)
        {
            if (PriorityOf(characterId, task) > 0)
                list.Add(task);
        }
        list.Sort((a, b) =>
        {
            var byPriority = PriorityOf(characterId, a).CompareTo(PriorityOf(characterId, b));
            return byPriority != 0
                ? byPriority
                : Array.IndexOf(ActionKindMap.WorkOrdered, a).CompareTo(Array.IndexOf(ActionKindMap.WorkOrdered, b));
        });
        return list;
    }

    /// <summary>
    /// 结算一个 6 小时委派块。同一设施按 Capacity 先到先得。
    /// 工作时段按优先级表挑活；空闲与不干活不出产。
    /// </summary>
    public List<WorkLog> ResolveSlot(int slot, Roster roster, Func<int, int>? roll = null)
    {
        var logs = new List<WorkLog>();
        var used = new Dictionary<int, int>();
        foreach (var character in roster.Members)
        {
            if (character.IsMaster)
                continue;
            var mode = ScheduleOf(character.Id).Slots[slot];
            if (mode != SlotMode.Work)
                continue;
            var log = ResolveOne(slot, character, used, roll);
            if (log != null)
                logs.Add(log);
        }
        return logs;
    }

    private WorkLog? ResolveOne(
        int slot, CharacterState character, Dictionary<int, int> used, Func<int, int>? roll)
    {
        foreach (var task in TasksByPriority(character.Id))
        {
            if (!character.WillWork(WorkTypeMap.IsHard(ActionKindMap.TypeOf(task)!.Value)) || !character.Affect.AcceptsWork())
                return null;
            var facility = Claim(task, used);
            if (facility == null)
                continue;
            used[facility.Id] = used.GetValueOrDefault(facility.Id) + 1;
            return ActionKindMap.IsExtractive(task)
                ? Gather(slot, character, facility, task, false, roll)
                : Craft(slot, character, facility, task, false);
        }
        return null;
    }

    private Facility? Claim(ActionKind task, Dictionary<int, int> used) =>
        Facilities.Find(f => f.Supports(task) && HasSeat(f, used));

    private static bool HasSeat(Facility facility, Dictionary<int, int> used) =>
        used.GetValueOrDefault(facility.Id) < facility.Capacity;

    private WorkLog Gather(
        int slot, CharacterState character, Facility facility, ActionKind task, bool fallback, Func<int, int>? roll)
    {
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
            Fallback = fallback,
        };
    }

    private WorkLog? Craft(
        int slot, CharacterState character, Facility facility, ActionKind task, bool fallback)
    {
        var recipe = PickRecipe(character, task, "");
        if (recipe == null || !PayWith(character, recipe.Costs))
            return null;
        // 成品优先进同房仓储，没有仓储就进制作者背包。
        Produce(character, recipe.ItemId, recipe.OutputCount);
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
            Fallback = fallback,
        };
    }

    private Recipe? PickRecipe(CharacterState character, ActionKind station, string orderItemId)
    {
        if (orderItemId.Length > 0)
        {
            var ordered = Recipes.Find(r => r.Station == station && r.ItemId == orderItemId);
            if (ordered != null && CanPayWith(character, ordered.Costs))
                return ordered;
        }
        return Recipes.Find(r => r.Station == station && CanPayWith(character, r.Costs));
    }
}
