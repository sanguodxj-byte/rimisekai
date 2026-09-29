using System.Collections.Generic;
using System.Text.Json;
using Rimisekai.Character;
using Rimisekai.Clock;
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
    public int Threat { get; set; }
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
    public Dictionary<int, List<RelationFlag>> Relations { get; set; } = new();
    public int Stamina { get; set; } = Vitals.DefaultMax;
    public int MaxStamina { get; set; } = Vitals.DefaultMax;
    public int Spirit { get; set; } = Vitals.DefaultMax;
    public int MaxSpirit { get; set; } = Vitals.DefaultMax;
    public int Fatigue { get; set; }
    public int Favor { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; } = 10;
    public int Mood { get; set; } = 50;
    public int ChatDesire { get; set; }
    public int LastTalkAt { get; set; } = -1;
    public int LastMealDay { get; set; } = -1;
    public int LastMealWindow { get; set; } = -1;
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
    public List<int> Links { get; set; } = new();
    public List<CostData> Materials { get; set; } = new();
    public bool Buildable { get; set; }
    public List<string> Tags { get; set; } = new();
}

public sealed class FacilityData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int RoomId { get; set; }
    public FacilityUsage Usage { get; set; }
    public int Capacity { get; set; } = 1;
    public string YieldItemId { get; set; } = "";
    public bool Built { get; set; } = true;
    public int BuildCost { get; set; }
    public string EffectId { get; set; } = "";
    public List<CostData> Materials { get; set; } = new();
    public bool Buildable { get; set; }

    /// <summary>能不能存东西（内容包声明）。</summary>
    public bool Storage { get; set; }

    /// <summary>设施里存着的东西。</summary>
    public Dictionary<string, int> Contents { get; set; } = new();
}

public sealed class RecipeData
{
    public string ItemId { get; set; } = "";
    public ActionKind Station { get; set; }
    public int Output { get; set; } = 1;
    public List<CostData> Costs { get; set; } = new();
}

/// <summary>
/// 一个时段的存档镜像。旧档只有 Task/Workplace/Fallback/Order 四字段；
/// 新档写 Mode（空闲/工作/不干活）。读旧档时按 Task 迁移成 Mode。
/// </summary>
public sealed class AssignmentData
{
    /// <summary>新档：时段开关。缺省 Free。</summary>
    public SlotMode Mode { get; set; } = SlotMode.Free;

    /// <summary>旧档遗留：当时的委派任务。仅用于迁移。</summary>
    public ActionKind? Task { get; set; }
    public int Workplace { get; set; } = -1;
    public ActionKind? Fallback { get; set; }
    public string Order { get; set; } = "";

    /// <summary>旧档是否带了"曾派过活"的痕迹（用于迁移判定）。</summary>
    public bool HasLegacyTask =>
        Task.HasValue || Workplace >= 0 || Fallback.HasValue || Order.Length > 0;
}

/// <summary>某角色的工作优先级存档镜像：工作类型 → 档位（1-4）。</summary>
public sealed class WorkPriorityData
{
    public ActionKind Task { get; set; }
    public int Priority { get; set; }
}

public sealed class GuestData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int RoomId { get; set; }
    public string Purpose { get; set; } = "";
}

public sealed class OfferData
{
    public string ItemId { get; set; } = "";
    public int BuyPrice { get; set; }
    public int SellPrice { get; set; }
}

public sealed class TerritoryData
{
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;
    public int UnlockedRegions { get; set; } = 1;
    public List<RoomData> Rooms { get; set; } = new();
    public List<FacilityData> Facilities { get; set; } = new();
    public List<RecipeData> Recipes { get; set; } = new();
    public List<string> Foods { get; set; } = new();
    public Dictionary<string, FoodTier> FoodTiers { get; set; } = new();
    public Dictionary<int, List<AssignmentData>> Schedules { get; set; } = new();

    /// <summary>工作优先级：角色 Id → 该角色的工作类型档位表。</summary>
    public Dictionary<int, List<WorkPriorityData>> WorkPriorities { get; set; } = new();
    public List<GuestData> Guests { get; set; } = new();
    public List<OfferData> Market { get; set; } = new();
}

public sealed class SaveData
{
    public int Day { get; set; } = 1;
    public int Minutes { get; set; }
    public long Money { get; set; }
    public int Prestige { get; set; }
    public Weather Weather { get; set; }
    public int WorldSeed { get; set; } = 42;
    public List<MemberData> Members { get; set; } = new();
    public TerritoryData Territory { get; set; } = new();
    public Dictionary<int, int> ClearCount { get; set; } = new();
    public Dictionary<int, int> Cooldown { get; set; } = new();
    public HubSnapshot? Hub { get; set; }
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
            Hub = hub?.Snapshot(),
        };
        foreach (var c in state.Roster.Members)
        {
            data.Members.Add(new MemberData
            {
                Id = c.Id,
                Name = c.Name,
                Master = c.IsMaster,
                Faction = c.FactionId,
                Employment = c.EmploymentDays,
                Threat = c.Threat,
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
                MaxStamina = c.Condition.MaxStamina,
                Spirit = c.Condition.Spirit,
                MaxSpirit = c.Condition.MaxSpirit,
                Fatigue = c.Condition.Fatigue,
                Favor = c.Condition.Favor,
                Mana = c.Condition.Mana,
                MaxMana = c.Condition.MaxMana,
                Mood = c.Affect.Mood,
                ChatDesire = c.Affect.ChatDesire,
                LastTalkAt = c.Affect.LastTalkAt,
                LastMealDay = c.Affect.LastMealDay,
                LastMealWindow = c.Affect.LastMealWindow,
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
            });
        }
        var t = data.Territory;
        t.Name = state.Territory.Name;
        t.Level = state.Territory.Level;
        t.UnlockedRegions = state.Territory.UnlockedRegions;
        foreach (var r in state.Territory.Rooms)
        {
            t.Rooms.Add(new RoomData
            {
                Id = r.Id, Name = r.Name, Region = r.RegionId, X = r.X, Y = r.Y,
                Open = r.Open, OpenCost = r.OpenCost, Permission = r.Permission,
                Links = new List<int>(r.Links),
                Materials = ToCosts(r.MaterialCost),
                Buildable = r.Buildable,
                Tags = new List<string>(r.Tags),
            });
        }
        foreach (var f in state.Territory.Facilities)
        {
            t.Facilities.Add(new FacilityData
            {
                Id = f.Id, Name = f.Name, RoomId = f.RoomId, Usage = f.Usage,
                Capacity = f.Capacity, YieldItemId = f.YieldItemId,
                Built = f.Built, BuildCost = f.BuildCost, EffectId = f.EffectId,
                Materials = ToCosts(f.MaterialCost),
                Buildable = f.Buildable,
                Storage = f.CanStore,
                Contents = new Dictionary<string, int>(f.Contents.Items),
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
        t.Foods.AddRange(state.Territory.Foods);
        foreach (var pair in state.Territory.FoodTiers)
            t.FoodTiers[pair.Key] = pair.Value;
        foreach (var pair in state.Territory.Schedules)
        {
            var slots = new List<AssignmentData>();
            foreach (var mode in pair.Value.Slots)
                slots.Add(new AssignmentData { Mode = mode });
            t.Schedules[pair.Key] = slots;
        }
        foreach (var pair in state.Territory.Priorities)
        {
            var list = new List<WorkPriorityData>();
            foreach (var item in pair.Value)
                list.Add(new WorkPriorityData { Task = item.Key, Priority = item.Value });
            t.WorkPriorities[pair.Key] = list;
        }
        foreach (var g in state.Territory.Guests)
            t.Guests.Add(new GuestData { Id = g.Id, Name = g.Name, RoomId = g.RoomId, Purpose = g.Purpose });
        foreach (var o in state.Territory.Market)
            t.Market.Add(new OfferData { ItemId = o.ItemId, BuyPrice = o.BuyPrice, SellPrice = o.SellPrice });
        foreach (var pair in state.Quests.ClearCount)
            data.ClearCount[pair.Key] = pair.Value;
        foreach (var pair in state.Quests.CooldownRemaining)
            data.Cooldown[pair.Key] = pair.Value;
        return data;
    }

    public static GameState Restore(SaveData data)
    {
        var state = new GameState();
        state.Clock.SetTime(data.Day, data.Minutes);
        state.Money = data.Money;
        state.Prestige = data.Prestige;
        state.Weather = data.Weather;
        state.WorldSeed = data.WorldSeed;
        state.World = WorldMap.Generators.WorldGenerator.Generate(data.WorldSeed, 128, 128);
        state.Territory.Name = data.Territory.Name;
        state.Territory.SetLevel(data.Territory.Level);
        state.Territory.SetUnlockedRegions(data.Territory.UnlockedRegions);
        foreach (var r in data.Territory.Rooms)
        {
            var room = new Room
            {
                Id = r.Id, Name = r.Name, RegionId = r.Region,
                X = r.X, Y = r.Y, Open = r.Open, OpenCost = r.OpenCost, Permission = r.Permission,
                Buildable = r.Buildable,
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
                Built = f.Built, BuildCost = f.BuildCost, EffectId = f.EffectId,
                Buildable = f.Buildable, CanStore = f.Storage,
            };
            foreach (var cost in f.Materials)
                facility.MaterialCost.Add(new RecipeCost(cost.ItemId, cost.Count));
            foreach (var pair in f.Contents)
                facility.Contents.Add(pair.Key, pair.Value);
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
        foreach (var food in data.Territory.Foods)
            state.Territory.AddFood(food);
        foreach (var pair in data.Territory.FoodTiers)
            state.Territory.SetFoodTier(pair.Key, pair.Value);
        foreach (var pair in data.Territory.Schedules)
        {
            for (var slot = 0; slot < pair.Value.Count && slot < WorkSlot.Count; slot++)
            {
                var a = pair.Value[slot];
                state.Territory.Assign(pair.Key, slot, MigrateMode(a));
            }
        }
        foreach (var pair in data.Territory.WorkPriorities)
        {
            foreach (var item in pair.Value)
                state.Territory.SetPriority(pair.Key, item.Task, item.Priority);
        }
        foreach (var g in data.Territory.Guests)
            state.Territory.AddGuest(new Guest { Id = g.Id, Name = g.Name, RoomId = g.RoomId, Purpose = g.Purpose });
        foreach (var o in data.Territory.Market)
            state.Territory.AddOffer(new MarketOffer { ItemId = o.ItemId, BuyPrice = o.BuyPrice, SellPrice = o.SellPrice });
        foreach (var m in data.Members)
        {
            var c = new CharacterState(m.Id) { Name = m.Name, IsMaster = m.Master };
            c.Restore(m.Core, m.CoreExp, m.LevelExp, m.LifeExp, m.WeaponExp, m.StyleExp,
                m.Talents, m.Threat, m.Employment, m.Faction,
                m.MainWeapon, m.OffWeapon, m.OffHandShield, m.Relations, m.Flags,
                new Dictionary<int, int>
                {
                    [0] = m.Stamina, [1] = m.MaxStamina, [2] = m.Spirit,
                    [3] = m.MaxSpirit, [4] = m.Fatigue, [5] = m.Favor,
                    [6] = m.Mana, [7] = m.MaxMana,
                });
            c.Affect.Mood = m.Mood;
            c.Affect.ChatDesire = m.ChatDesire;
            c.Affect.LastTalkAt = m.LastTalkAt;
            c.Affect.LastMealDay = m.LastMealDay;
            c.Affect.LastMealWindow = m.LastMealWindow;
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
            state.Roster.Attach(c);
        }
        foreach (var pair in data.ClearCount)
            state.Quests.ClearCount[pair.Key] = pair.Value;
        foreach (var pair in data.Cooldown)
            state.Quests.CooldownRemaining[pair.Key] = pair.Value;
        return state;
    }

    /// <summary>
    /// 时段开关的迁移：新档直接读 Mode；旧档只有委派任务，
    /// 按"生产任务算上工、Rest 算不干活、其余算空闲"折成新口径。
    /// </summary>
    private static SlotMode MigrateMode(AssignmentData a)
    {
        if (!a.HasLegacyTask)
            return a.Mode;
            return SlotMode.Work;
        return SlotMode.Free;
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
