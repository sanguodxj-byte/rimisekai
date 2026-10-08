using System.Collections.Generic;
using System.Text.Json;
using Rimisekai.Character;
using Rimisekai.Clock;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;

namespace Rimisekai.Save;

public sealed class CostData
{
    public string ItemId { get; set; } = "";
    public int Count { get; set; }
}

public sealed class MemberData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Master { get; set; }
    public int Faction { get; set; }
    public int Employment { get; set; }
    public int[] Core { get; set; } = new int[AttributeMap.CoreCount];
    public int[] CoreExp { get; set; } = new int[AttributeMap.CoreCount];
    public int LevelExp { get; set; }
    public int[] LifeExp { get; set; } = new int[AttributeMap.LifeCount];
    public int[] WeaponExp { get; set; } = new int[System.Enum.GetValues<WeaponType>().Length];
    public int[] StyleExp { get; set; } = new int[System.Enum.GetValues<StyleType>().Length];
    public List<int> Talents { get; set; } = new();
    public WeaponType? MainWeapon { get; set; }
    public WeaponType? OffWeapon { get; set; }
    public bool OffHandShield { get; set; }

    /// <summary>十格装备里各放着哪件实例的 Id；空串 = 空槽。下标即 EquipSlot。</summary>
    public List<string> Equipped { get; set; } = new();
    public Dictionary<int, List<RelationFlag>> Relations { get; set; } = new();
    public int Stamina { get; set; } = Vitals.DefaultMax;
    public int Spirit { get; set; } = Vitals.DefaultMax;
    public int Favor { get; set; }

    /// <summary>衣服湿度（0=干爽，100=湿透）。</summary>
    public int Wetness { get; set; }
    public int Mood { get; set; } = 50;
    public int ChatDesire { get; set; }
    public int LastTalkAt { get; set; } = -1;
    public int LastMealDay { get; set; } = -1;
    public int LastMealWindow { get; set; } = -1;
    public int LastMealMinute { get; set; } = -1;
    public int LastPlayDay { get; set; } = -1;
    public int LastBoredDay { get; set; } = -1;
    public int IntimateDay { get; set; } = -1;
    public int[] IntimateRewards { get; set; } = new int[4];
    public Dictionary<int, int> Flags { get; set; } = new();
    public Dictionary<int, int> Base { get; set; } = new();
    public Dictionary<int, int> MaxBase { get; set; } = new();

    /// <summary>说过哪些口上/地文，以及每个时机最近一次开口的时刻。</summary>
    public Dictionary<string, int> VoiceSaidAt { get; set; } = new();
    public Dictionary<int, int> VoiceSpokeAt { get; set; } = new();

    /// <summary>LLM 层的记忆缓冲与近期对话。存下来，读档后模型还接得上话头。</summary>
    public List<string> VoiceMemories { get; set; } = new();
    public List<string> VoiceDialogue { get; set; } = new();

    /// <summary>场景事件上次跑过是第几天。键是事件 Id。</summary>
    public Dictionary<string, int> VoiceSceneLastDay { get; set; } = new();

    /// <summary>身上的背包。物品只在背包或设施里，没有虚空库存。</summary>
    public Dictionary<string, int> Bag { get; set; } = new();
}

public sealed class RoomData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Region { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Open { get; set; }
    public int OpenCost { get; set; }
    public RoomPermission Permission { get; set; }
    public RoomLock Lock { get; set; }
    public List<int> Links { get; set; } = new();
    public List<CostData> Materials { get; set; } = new();
    public bool Buildable { get; set; }
    public List<string> Tags { get; set; } = new();

    /// <summary>房间插画资源路径。</summary>
    public string Illustration { get; set; } = "";

    /// <summary>开拓出来的空房（可被已建房间安装顶替）。</summary>
    public bool Vacant { get; set; }
}

public sealed class FacilityData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int RoomId { get; set; }
    public FacilityUsage Usage { get; set; }
    public int Capacity { get; set; } = 1;
    public string YieldItemId { get; set; } = "";

    /// <summary>耕地上种着的作物 DefName（空 = 空地）。</summary>
    public string CropDefName { get; set; } = "";

    /// <summary>作物已生长的天数。</summary>
    public int Growth { get; set; }
    public bool Built { get; set; } = true;
    public int BuildCost { get; set; }
    public string EffectId { get; set; } = "";
    public List<CostData> Materials { get; set; } = new();
    public bool Buildable { get; set; }

    /// <summary>能不能存东西（内容包声明）。</summary>
    public bool Storage { get; set; }

    /// <summary>存储容量（0 = 不限）。</summary>
    public int StorageCapacity { get; set; }

    /// <summary>存储过滤：物品 Id 与品类 DefName 混存。</summary>
    public List<string> StorageFilter { get; set; } = new();

    /// <summary>设施里存着的东西。</summary>
    public Dictionary<string, int> Contents { get; set; } = new();

    /// <summary>设施支持的行动。</summary>
    public List<ActionKind> Actions { get; set; } = new();

    /// <summary>是不是桌子。</summary>
    public bool IsTable { get; set; }
}

public sealed class RecipeData
{
    public string ItemId { get; set; } = "";
    public ActionKind Station { get; set; }
    public int Output { get; set; } = 1;
    public List<CostData> Costs { get; set; } = new();
}

/// <summary>
/// 一个时段的存档镜像：开关，以及工作/娱乐时点名的那件设施。
/// </summary>
public sealed class AssignmentData
{
    /// <summary>时段开关。缺省 Free。</summary>
    public SlotMode Mode { get; set; } = SlotMode.Free;

    /// <summary>工作/娱乐时点名的那件设施 Id；-1 表示没点名。</summary>
    public int Facility { get; set; } = -1;
}

public sealed class GuestData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int RoomId { get; set; }
    public string Purpose { get; set; } = "";
}

public sealed class TerritoryData
{
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;
    public int UnlockedRegions { get; set; } = 1;

    /// <summary>3×3 区域拼图的解锁位掩码（领地内 0..8）。</summary>
    public int UnlockedRegionMask { get; set; } = 1;

    /// <summary>开拓过几格空地（定价按它每级涨 20%）。</summary>
    public int VacantDevelopCount { get; set; }

    public List<RoomData> Rooms { get; set; } = new();
    public List<FacilityData> Facilities { get; set; } = new();
    public List<RecipeData> Recipes { get; set; } = new();
    public Dictionary<int, List<AssignmentData>> Schedules { get; set; } = new();

    public List<GuestData> Guests { get; set; } = new();

    /// <summary>今日集市行情：物品 Id → [存货, 价格系数]。</summary>
    public Dictionary<string, int[]> MarketDay { get; set; } = new();

    /// <summary>还原后的行情条目（存货＋价格系数），供 RestoreMarketDay 回填。</summary>
    public Dictionary<string, Territory.MarketEntry> MarketDayEntries()
    {
        var result = new Dictionary<string, Territory.MarketEntry>();
        foreach (var pair in MarketDay)
        {
            if (pair.Value.Length >= 2)
                result[pair.Key] = new Territory.MarketEntry(pair.Value[0], pair.Value[1]);
        }
        return result;
    }

    /// <summary>今日武器行情系数（70-130）。</summary>
    public int WeaponPricePercent { get; set; } = 100;

    /// <summary>集市在售武器：武器 Id → 价格系数。</summary>
    public Dictionary<string, int> MarketWeapons { get; set; } = new();

    /// <summary>运行时生成的武器实例。</summary>
    public List<WeaponInstanceData> Weapons { get; set; } = new();

    /// <summary>运行时生成的防具与饰品实例。</summary>
    public List<EquipInstanceData> Equips { get; set; } = new();
}

/// <summary>一件防具/饰品实例的存档镜像。</summary>
public sealed class EquipInstanceData
{
    public string Id { get; set; } = "";
    public EquipSlot Slot { get; set; }
    public EquipKind Kind { get; set; }
    public string MaterialDefName { get; set; } = "";
    public string Accessory { get; set; } = "";
    public Quality Quality { get; set; }
    public int Enhance { get; set; }
    public string Enchant { get; set; } = "";
    public bool Blessed { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>一件运行时武器实例的存档镜像。</summary>
public sealed class WeaponInstanceData
{
    public string Id { get; set; } = "";
    public string MaterialDefName { get; set; } = "";
    public WeaponType Type { get; set; }
    public int Enhance { get; set; }
    public string Enchant { get; set; } = "";
    public bool Blessed { get; set; }
    public Quality Quality { get; set; }
    public string Name { get; set; } = "";
}

public sealed class SaveData
{
    public int Day { get; set; } = 1;
    public int Minutes { get; set; }
    public long Money { get; set; }
    public int Prestige { get; set; }
    public Weather Weather { get; set; }
    public int WorldSeed { get; set; } = 42;

    /// <summary>地城里已了结的石室（兴趣点编号＜＜32 | 生成器房号）。</summary>
    public List<long> DungeonRooms { get; set; } = new();

    /// <summary>首领已倒下的地城（兴趣点编号）。</summary>
    public List<int> DungeonsCleared { get; set; } = new();

    /// <summary>遗迹里走过的石室（地城迷雾按此揭开）。</summary>
    public List<long> DungeonVisited { get; set; } = new();
    public List<MemberData> Members { get; set; } = new();
    public TerritoryData Territory { get; set; } = new();
    public Dictionary<int, int> ClearCount { get; set; } = new();
    public Dictionary<int, int> Cooldown { get; set; } = new();

    /// <summary>已演过的整局一次事件 Id（Once 语义跨存档生效）。</summary>
    public List<string> FiredEvents { get; set; } = new();
    public bool ReturnedFromCombat { get; set; }
    public HubSnapshot? Hub { get; set; }

    /// <summary>定时事件各行已生成出来的成品正文。</summary>
    public List<Voice.SceneTextEntry> SceneTexts { get; set; } = new();
}

/// <summary>一个暂存演员：它属于哪一条定时事件，以及角色本身。</summary>
public sealed class StagedActorData
{
    /// <summary>所属事件的 Id。</summary>
    public string EventId { get; set; } = "";
    public MemberData Actor { get; set; } = new();

    /// <summary>
    /// 该角色的人设。生成层的人设表是运行时的，不随存档走，
    /// 而暂存演员还可能有没生成完的台词，因此这里单独存一份，
    /// 读档后补回生成层，续生成才不会丢掉身份语气。
    /// </summary>
    public string Persona { get; set; } = "";
}

public static class SaveSystem
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Save(GameState state, HubSession? hub = null) =>
        JsonSerializer.Serialize(Capture(state, hub), Options);

    public static SaveData Capture(GameState state, HubSession? hub = null)
    {
        var data = new SaveData
        {
            Day = state.Clock.Day,
            Minutes = state.Clock.Minutes,
            Money = state.Money,
            Prestige = state.Prestige,
            Weather = state.Weather,
            WorldSeed = state.WorldSeed,
            DungeonRooms = new List<long>(state.Dungeons.SpentRooms),
            DungeonsCleared = new List<int>(state.Dungeons.Cleared),
            DungeonVisited = new List<long>(state.Dungeons.VisitedRooms),
            ReturnedFromCombat = state.ReturnedFromCombat,
            Hub = hub?.Snapshot(),
        };
        foreach (var c in state.Roster.Members)
            data.Members.Add(CaptureMember(c));
        var t = data.Territory;
        t.Name = state.Territory.Name;
        t.Level = state.Territory.Level;
        // 人在兴趣点里时，已解锁区域数临时借给了兴趣点的区号；存的是领地自己的。
        t.UnlockedRegions = hub?.TerritoryUnlockedRegions ?? state.Territory.UnlockedRegions;
        t.UnlockedRegionMask = state.Territory.UnlockedRegionMask;
        t.VacantDevelopCount = state.Territory.VacantDevelopCount;
        // 兴趣点的房间是进场时按种子现生成的临时房，不进存档（读档即人在据点）。
        foreach (var r in state.Territory.Rooms)
        {
            if (r.RegionId >= Territory.MaxTerritoryRegions)
                continue;
            t.Rooms.Add(new RoomData
            {
                Id = r.Id, Name = r.Name, Region = r.RegionId, X = r.X, Y = r.Y,
                Open = r.Open, OpenCost = r.OpenCost, Permission = r.Permission,
                Lock = r.Lock,
                Links = new List<int>(r.Links),
                Materials = ToCosts(r.MaterialCost),
                Buildable = r.Buildable,
                Tags = new List<string>(r.Tags),
                Illustration = r.Illustration,
                Vacant = r.Vacant,
            });
        }
        foreach (var f in state.Territory.Facilities)
        {
            t.Facilities.Add(new FacilityData
            {
                Id = f.Id, Name = f.Name, RoomId = f.RoomId, Usage = f.Usage,
                Capacity = f.Capacity, YieldItemId = f.YieldItemId,
                CropDefName = f.CropDefName, Growth = f.Growth,
                Built = f.Built, BuildCost = f.BuildCost, EffectId = f.EffectId,
                Materials = ToCosts(f.MaterialCost),
                Buildable = f.Buildable,
                Storage = f.CanStore,
                StorageCapacity = f.StorageCapacity,
                StorageFilter = new List<string>(f.StorageFilter),
                Contents = new Dictionary<string, int>(f.Contents.Items),
                Actions = new List<ActionKind>(f.Actions),
                IsTable = f.IsTable,
            });
        }
        foreach (var r in state.Territory.Recipes)
        {
            var recipe = new RecipeData
            {
                ItemId = r.ItemId, Station = r.Station, Output = r.OutputCount,
            };
            foreach (var cost in r.Costs)
                recipe.Costs.Add(new CostData { ItemId = cost.ItemId, Count = cost.Count });
            t.Recipes.Add(recipe);
        }
        foreach (var w in state.Territory.Weapons.All)
        {
            t.Weapons.Add(new WeaponInstanceData
            {
                Id = w.Id,
                MaterialDefName = w.MaterialDefName,
                Type = w.Type,
                Enhance = w.Enhance,
                Enchant = w.Enchant,
                Blessed = w.Blessed,
                Quality = w.Quality,
                Name = w.Name,
            });
        }
        foreach (var e in state.Territory.Equips.All)
        {
            t.Equips.Add(new EquipInstanceData
            {
                Id = e.Id,
                Slot = e.Slot,
                Kind = e.Kind,
                MaterialDefName = e.MaterialDefName,
                Accessory = e.Accessory,
                Quality = e.Quality,
                Enhance = e.Enhance,
                Enchant = e.Enchant,
                Blessed = e.Blessed,
                Name = e.Name,
            });
        }
        foreach (var pair in state.Territory.Schedules)
        {
            var slots = new List<AssignmentData>();
            foreach (var assignment in pair.Value.Slots)
                slots.Add(new AssignmentData
                {
                    Mode = assignment.Mode,
                    Facility = assignment.FacilityId,
                });
            t.Schedules[pair.Key] = slots;
        }
        foreach (var g in state.Territory.Guests)
            t.Guests.Add(new GuestData { Id = g.Id, Name = g.Name, RoomId = g.RoomId, Purpose = g.Purpose });
        foreach (var pair in state.Territory.MarketDay)
            t.MarketDay[pair.Key] = new[] { pair.Value.Stock, pair.Value.PricePercent };
        t.WeaponPricePercent = state.Territory.WeaponPricePercent;
        foreach (var listing in state.Territory.MarketWeapons)
            t.MarketWeapons[listing.WeaponId] = listing.PricePercent;
        foreach (var pair in state.Quests.ClearCount)
            data.ClearCount[pair.Key] = pair.Value;
        foreach (var pair in state.Quests.CooldownRemaining)
            data.Cooldown[pair.Key] = pair.Value;
        foreach (var id in state.FiredEvents)
            data.FiredEvents.Add(id);
        data.SceneTexts = state.SceneTexts.Export();
        return data;
    }

    /// <summary>把一个角色的全部可变状态收成存档行。名册成员与暂存演员共用。</summary>
    public static MemberData CaptureMember(CharacterState c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Master = c.IsMaster,
        Faction = c.FactionId,
        Employment = c.EmploymentDays,
        Core = (int[])c.Core.Clone(),
        CoreExp = (int[])c.CoreExp.Clone(),
        LevelExp = c.LevelExp,
        LifeExp = (int[])c.LifeExp.Clone(),
        WeaponExp = WeaponExps(c),
        StyleExp = StyleExps(c),
        Talents = new List<int>(c.Talents),
        MainWeapon = c.MainWeapon,
        OffWeapon = c.OffWeapon,
        OffHandShield = c.OffHandShield,
        Relations = RelationsOf(c),
        Stamina = c.Condition.Stamina,
        Spirit = c.Condition.Spirit,
        Favor = c.Condition.Favor,
        Wetness = c.Condition.Wetness,
        Mood = c.Affect.Mood,
        ChatDesire = c.Affect.ChatDesire,
        LastTalkAt = c.Affect.LastTalkAt,
        LastMealDay = c.Affect.LastMealDay,
        LastMealWindow = c.Affect.LastMealWindow,
        LastMealMinute = c.Affect.LastMealMinute,
        LastPlayDay = c.Affect.LastPlayDay,
        LastBoredDay = c.Affect.LastBoredDay,
        IntimateDay = c.Affect.IntimateDay,
        IntimateRewards = (int[])c.Affect.IntimateRewards.Clone(),
        Flags = new Dictionary<int, int>(c.Flags),
        Base = new Dictionary<int, int>(c.Base),
        MaxBase = new Dictionary<int, int>(c.MaxBase),
        VoiceSaidAt = new Dictionary<string, int>(c.Voice.SaidAt),
        VoiceSpokeAt = VoiceSpokes(c),
        VoiceMemories = new List<string>(c.Voice.Memories),
        VoiceDialogue = new List<string>(c.Voice.RecentDialogue),
        VoiceSceneLastDay = new Dictionary<string, int>(c.Voice.SceneLastDay),
        Bag = new Dictionary<string, int>(c.Bag.Items),
        Equipped = new List<string>(c.EquippedIds()),
    };

    public static GameState Restore(SaveData data)
    {
        var state = new GameState();
        state.Clock.SetTime(data.Day, data.Minutes);
        state.Money = data.Money;
        state.Prestige = data.Prestige;
        state.Weather = data.Weather;
        if (data.WorldSeed != state.WorldSeed)
            state.RegenerateWorld(data.WorldSeed);
        state.Dungeons.SpentRooms.UnionWith(data.DungeonRooms);
        state.Dungeons.Cleared.UnionWith(data.DungeonsCleared);
        state.Dungeons.VisitedRooms.UnionWith(data.DungeonVisited);
        state.Territory.Name = data.Territory.Name;
        state.Territory.SetLevel(data.Territory.Level);
        state.Territory.SetUnlockedRegions(data.Territory.UnlockedRegions);
        state.Territory.SetUnlockedRegionMask(data.Territory.UnlockedRegionMask);
        state.Territory.VacantDevelopCount = data.Territory.VacantDevelopCount;
        foreach (var r in data.Territory.Rooms)
        {
            var room = new Room
            {
                Id = r.Id, Name = r.Name, RegionId = r.Region,
                X = r.X, Y = r.Y, Open = r.Open, OpenCost = r.OpenCost, Permission = r.Permission,
                Lock = r.Lock,
                Buildable = r.Buildable,
                Illustration = r.Illustration ?? "",
                Vacant = r.Vacant,
            };
            foreach (var cost in r.Materials)
                room.MaterialCost.Add(new RecipeCost(cost.ItemId, cost.Count));
            if (r.Tags != null)
            {
                foreach (var tag in r.Tags)
                    room.AddTag(tag);
            }
            state.Territory.AddRoom(room);
        }
        foreach (var r in data.Territory.Rooms)
        {
            foreach (var link in r.Links)
                state.Territory.Link(r.Id, link);
        }
        foreach (var f in data.Territory.Facilities)
        {
            var facility = new Facility
            {
                Id = f.Id, Name = f.Name, RoomId = f.RoomId, Usage = f.Usage,
                Capacity = f.Capacity, YieldItemId = f.YieldItemId,
                CropDefName = f.CropDefName, Growth = f.Growth,
                Built = f.Built, BuildCost = f.BuildCost, EffectId = f.EffectId,
                Buildable = f.Buildable, CanStore = f.Storage,
                IsTable = f.IsTable,
            };
            if (f.Actions != null)
            {
                foreach (var action in f.Actions)
                    facility.Actions.Add(action);
            }
            foreach (var cost in f.Materials)
                facility.MaterialCost.Add(new RecipeCost(cost.ItemId, cost.Count));
            foreach (var pair in f.Contents)
                facility.Contents.Add(pair.Key, pair.Value);
            facility.StorageCapacity = f.StorageCapacity;
            foreach (var entry in f.StorageFilter)
                facility.StorageFilter.Add(entry);
            // 未放置的设施（RoomId=-1）走专用入口，AddFacility 会因找不到房间而拒绝。
            if (facility.RoomId < 0)
                state.Territory.AddUnplacedFacility(facility);
            else
                state.Territory.AddFacility(facility);
        }
        foreach (var r in data.Territory.Recipes)
        {
            var recipe = new Recipe
            {
                ItemId = r.ItemId, Station = r.Station, OutputCount = r.Output,
            };
            foreach (var cost in r.Costs)
                recipe.Costs.Add(new RecipeCost(cost.ItemId, cost.Count));
            state.Territory.AddRecipe(recipe);
        }
        foreach (var w in data.Territory.Weapons)
        {
            state.Territory.Weapons.Add(new Defs.WeaponInstance
            {
                Id = w.Id,
                MaterialDefName = w.MaterialDefName,
                Type = w.Type,
                Enhance = w.Enhance,
                Enchant = w.Enchant ?? "",
                Blessed = w.Blessed,
                Quality = w.Quality,
                Name = w.Name ?? "",
            });
        }
        foreach (var e in data.Territory.Equips)
        {
            state.Territory.Equips.Add(new Defs.EquipInstance
            {
                Id = e.Id,
                Slot = e.Slot,
                Kind = e.Kind,
                MaterialDefName = e.MaterialDefName,
                Accessory = e.Accessory ?? "",
                Quality = e.Quality,
                Enhance = e.Enhance,
                Enchant = e.Enchant ?? "",
                Blessed = e.Blessed,
                Name = e.Name ?? "",
            });
        }
        foreach (var pair in data.Territory.Schedules)
        {
            for (var slot = 0; slot < pair.Value.Count && slot < WorkSlot.Count; slot++)
            {
                var a = pair.Value[slot];
                state.Territory.Assign(pair.Key, slot, a.Mode, a.Facility);
            }
        }
        foreach (var g in data.Territory.Guests)
            state.Territory.AddGuest(new Guest { Id = g.Id, Name = g.Name, RoomId = g.RoomId, Purpose = g.Purpose });
        state.Territory.RestoreMarketDay(data.Territory.MarketDayEntries());
        state.Territory.RestoreWeaponMarket(data.Territory.WeaponPricePercent, data.Territory.MarketWeapons);
        foreach (var m in data.Members)
            state.Roster.Attach(RestoreMember(m));
        foreach (var pair in data.ClearCount)
            state.Quests.ClearCount[pair.Key] = pair.Value;
        foreach (var pair in data.Cooldown)
            state.Quests.CooldownRemaining[pair.Key] = pair.Value;
        foreach (var id in data.FiredEvents)
            state.FiredEvents.Add(id);
        state.SceneTexts.Import(data.SceneTexts);
        state.ReturnedFromCombat = data.ReturnedFromCombat;
        return state;
    }

    /// <summary>把一行存档还原成一个角色（不入名册；名册成员与暂存演员共用）。</summary>
    public static CharacterState RestoreMember(MemberData m)
    {
        var c = new CharacterState(m.Id) { Name = m.Name, IsMaster = m.Master };
        c.Restore(m.Core, m.CoreExp, m.LevelExp, m.LifeExp, m.WeaponExp, m.StyleExp,
            m.Talents, m.Employment, m.Faction,
            m.MainWeapon, m.OffWeapon, m.OffHandShield, m.Relations, m.Flags,
            new Dictionary<int, int>
            {
                [0] = m.Stamina, [1] = m.Spirit, [2] = m.Favor,
            });
        c.Condition.RestoreWetness(m.Wetness);
        c.RestoreEquipped(m.Equipped);
        c.Affect.Mood = m.Mood;
        c.Affect.ChatDesire = m.ChatDesire;
        c.Affect.LastTalkAt = m.LastTalkAt;
        c.Affect.LastMealDay = m.LastMealDay;
        c.Affect.LastMealWindow = m.LastMealWindow;
        c.Affect.LastMealMinute = m.LastMealMinute;
        c.Affect.LastPlayDay = m.LastPlayDay;
        c.Affect.LastBoredDay = m.LastBoredDay;
        c.Affect.IntimateDay = m.IntimateDay;
        if (m.IntimateRewards.Length == 4)
            m.IntimateRewards.CopyTo(c.Affect.IntimateRewards, 0);
        foreach (var pair in m.VoiceSaidAt)
            c.Voice.SaidAt[pair.Key] = pair.Value;
        foreach (var pair in m.VoiceSpokeAt)
            c.Voice.LastSpokeAt[(Rimisekai.Voice.VoiceTrigger)pair.Key] = pair.Value;
        c.Voice.Memories.AddRange(m.VoiceMemories);
        c.Voice.RecentDialogue.AddRange(m.VoiceDialogue);
        foreach (var pair in m.VoiceSceneLastDay)
            c.Voice.SceneLastDay[pair.Key] = pair.Value;
        foreach (var pair in m.Base)
            c.Base[pair.Key] = pair.Value;
        foreach (var pair in m.MaxBase)
            c.MaxBase[pair.Key] = pair.Value;
        foreach (var pair in m.Bag)
            c.Bag.Add(pair.Key, pair.Value);
        return c;
    }

    public static GameState Load(string json)
    {
        var data = JsonSerializer.Deserialize<SaveData>(json);
        if (data == null)
            return new GameState();
        return Restore(data);
    }

    private static List<CostData> ToCosts(IReadOnlyList<RecipeCost> costs)
    {
        var list = new List<CostData>();
        foreach (var cost in costs)
            list.Add(new CostData { ItemId = cost.ItemId, Count = cost.Count });
        return list;
    }

    private static Dictionary<int, List<RelationFlag>> RelationsOf(CharacterState c)
    {
        var map = new Dictionary<int, List<RelationFlag>>();
        foreach (var pair in c.Relations.All())
            map[pair.Key] = new List<RelationFlag>(pair.Value);
        return map;
    }

    private static int[] WeaponExps(CharacterState c)
    {
        var list = new int[c.Weapons.Length];
        for (var i = 0; i < list.Length; i++)
            list[i] = c.Weapons[i].Exp;
        return list;
    }

    private static int[] StyleExps(CharacterState c)
    {
        var list = new int[c.Styles.Length];
        for (var i = 0; i < list.Length; i++)
            list[i] = c.Styles[i].Exp;
        return list;
    }

    private static Dictionary<int, int> VoiceSpokes(CharacterState c)
    {
        var map = new Dictionary<int, int>();
        foreach (var pair in c.Voice.LastSpokeAt)
            map[(int)pair.Key] = pair.Value;
        return map;
    }
}
