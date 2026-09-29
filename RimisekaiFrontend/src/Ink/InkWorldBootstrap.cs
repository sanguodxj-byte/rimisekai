using System.Collections.Generic;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Defs;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Save;
using Rimisekai.Voice;

namespace Rimisekai.Ink;

/// <summary>
/// 启动用的世界。内容全部来自 content/world.json，本类只做装配，不含任何设定。
/// 等正式数据目录（XML/CSV）接上后，本类应改为从正式数据源装配。
/// </summary>
public static class InkWorldBootstrap
{
    public const string ContentPath = "res://content/world.json";
    public const string SeedPath = "res://content/newgame.json";
    public const string SeedPathHard = "res://content/newgame_hard.json";
    public const string BuildingsPath = "res://content/buildings.json";

    /// <summary>按内容包建一份世界，并按开局种子指定的起始位置开一个据点会话。</summary>
    public static HubSession OpenHub(out GameState state) =>
        OpenHub(ContentPack.Load(ContentPath, SeedPath, BuildingsPath), out state);

    /// <summary>困难开局：只有玩家自己。</summary>
    public static HubSession OpenHubHard(out GameState state) =>
        OpenHub(ContentPack.Load(ContentPath, SeedPathHard, BuildingsPath), out state);

    public static HubSession OpenHub(ContentPack pack, out GameState state)
    {
        state = Create(pack);
        return OpenHub(state, pack);
    }

    public static HubSession OpenHub(GameState state, ContentPack pack)
    {
        // 读档进来的世界没经过 Create，台词要在这里补灌一次。
        // 已灌过就跳过，避免重复注册把世界通用台词叠两遍。
        if (!state.Voice.Enabled)
            LoadVoice(state, pack);

        var hub = new HubSession(state);

        if (pack.Seed.StartRoomId >= 0)
            hub.Enter(pack.Seed.StartRoomId);

        // 开场日志不配表，按实际出生房间生成一句。
        var startRoom = state.Territory.Rooms.Find(r => r.Id == pack.Seed.StartRoomId);
        if (startRoom != null)
            hub.Write($"你在{startRoom.Name}醒来。");

        // 把开局种子里指定了房间的角色就位。
        foreach (var entry in pack.Seed.Characters)
        {
            if (entry.RoomId < 0)
                continue;
            var character = FindByName(state, entry.Name);
            if (character != null)
                hub.Place(character.Id, entry.RoomId);
        }

        // 选中起始角色。用名字查找，不依赖注册顺序。
        if (pack.Seed.StartCharacterName.Length > 0)
        {
            var start = FindByName(state, pack.Seed.StartCharacterName);
            if (start != null)
                hub.Select(start.Id);
        }

        return hub;
    }

    private static CharacterState? FindByName(GameState state, string name)
    {
        foreach (var character in state.Roster.Members)
        {
            if (character.Name == name)
                return character;
        }
        return null;
    }

    /// <summary>
    /// 把 content/voice.json 灌进世界的台词调度。读不到或解析失败就保持无台词，
    /// 整条口上链路自动旁路，游戏照常跑。
    /// </summary>
    private static void LoadVoice(GameState state, ContentPack pack)
    {
        var paths = new List<string>();
        if (pack.Seed.VoicePath.Length > 0)
            paths.Add(pack.Seed.VoicePath);
        foreach (var path in pack.Seed.VoicePaths)
        {
            if (path.Length > 0 && !paths.Contains(path))
                paths.Add(path);
        }

        foreach (var path in paths)
            LoadVoiceFile(state, path);
    }

    /// <summary>
    /// 读一个台词文件并并入世界。多个文件按顺序叠加：
    /// 后读的覆盖先读的同 Id 台词，因此通用包放前面、角色专属放后面即可覆盖。
    /// </summary>
    private static void LoadVoiceFile(GameState state, string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"Rimisekai: 无法读取台词文件 {path}（{FileAccess.GetOpenError()}）");
            return;
        }

        if (!VoicePackJson.TryParse(file.GetAsText(), out var characters, out var world,
            out var scenes, out var personas, out var error))
        {
            GD.PushError($"Rimisekai: 台词文件 {path} 解析失败：{error}");
            return;
        }

        foreach (var pair in characters)
        {
            // 同名角色跨文件时并入同一个台词库，而不是整体替换。
            var existing = state.Voice.PackOf(pair.Key);
            if (existing == null)
                state.Voice.Register(pair.Key, pair.Value);
            else
                existing.RegisterRange(pair.Value.Lines);
        }
        state.Voice.World.RegisterRange(world.Lines);
        state.Voice.Scenes.RegisterRange(scenes);
        foreach (var pair in personas)
            state.Voice.Generation.Personas[pair.Key] = pair.Value;
    }

    public static GameState Create() => Create(ContentPack.Load(ContentPath, SeedPath, BuildingsPath));

    public static GameState Create(ContentPack pack)
    {
        var state = new GameState { Money = pack.Seed.Money };
        state.Territory.Name = pack.World.TerritoryName;
        foreach (var food in pack.World.Foods)
            state.Territory.AddFood(food);

        foreach (var entry in pack.Seed.Characters)
        {
            var character = state.Roster.Add(entry.Name, entry.Master);
            if (entry.Master)
                character.FactionId = GameState.PlayerFaction;
            if (entry.Favor != 0)
                character.Condition.AddFavor(entry.Favor);
            foreach (var name in entry.Traits)
            {
                if (System.Enum.TryParse<Trait>(name, ignoreCase: true, out var trait))
                    character.Grant(trait);
                else
                    GD.PushError($"Rimisekai: 内容包里 {entry.Name} 的素质 {name} 不认识。");
            }
        }

        // 开局物资进玩家背包（物品只在背包或设施里，没有领地虚空库存）。
        var master = state.Roster.Master;
        if (master != null)
        {
            foreach (var pair in pack.Seed.Stock)
                master.Bag.Add(pair.Key, pair.Value);
        }

        LoadVoice(state, pack);

        foreach (var entry in pack.World.Rooms)
        {
            var def = new RoomDef
            {
                DefName = string.IsNullOrEmpty(entry.Name) ? $"WorldRoom_{entry.Id}" : entry.Name,
                Id = entry.Id,
                Name = entry.Name,
                RegionId = entry.Region,
                X = entry.X,
                Y = entry.Y,
                OpenCost = entry.OpenCost,
                Permission = ParsePermission(entry.Permission, entry.Id),
                StartOpen = entry.Open,
                Description = entry.Description,
                MaterialCost = ToCosts(entry.Materials),
                Buildable = entry.Buildable,
                Tags = new List<string>(entry.Tags),
            };
            state.Catalog.Rooms[def.Id] = def;
            DefDatabase<RoomDef>.Register(def);
            state.Territory.AddRoom(def.ToRuntime());
        }

        foreach (var link in pack.World.Links)
            state.Territory.Link(link.From, link.To);

        // 统一 Defs 流加载：解析 buildings.json 中的全部 RoomDef 与 FacilityDef
        foreach (var defEntry in pack.Buildings.Defs)
        {
            if (defEntry.DefType == "RoomDef")
            {
                var roomDef = new RoomDef
                {
                    DefName = string.IsNullOrEmpty(defEntry.DefName) ? $"Room_{defEntry.Id}" : defEntry.DefName,
                    Id = defEntry.Id,
                    Label = defEntry.Label,
                    Description = defEntry.Description,
                    MaterialCost = ToCosts(defEntry.Materials),
                    Buildable = defEntry.Buildable,
                    Tags = new List<string>(defEntry.Tags),
                    FacilityDefs = new List<string>(defEntry.Facilities),
                };
                state.Catalog.Rooms[roomDef.Id] = roomDef;
                DefDatabase<RoomDef>.Register(roomDef);
            }
            else if (defEntry.DefType == "FacilityDef")
            {
                var facilityDef = new FacilityDef
                {
                    DefName = string.IsNullOrEmpty(defEntry.DefName) ? $"Facility_{defEntry.Id}" : defEntry.DefName,
                    Id = defEntry.Id,
                    Label = defEntry.Label,
                    Usage = ParseUsage(defEntry.Usage, defEntry.Id),
                    Capacity = defEntry.Capacity,
                    EffectId = defEntry.Effect,
                    YieldItemId = defEntry.Yield,
                    Description = defEntry.Description,
                    MaterialCost = ToCosts(defEntry.Materials),
                    Buildable = defEntry.Buildable,
                    Actions = ParseActions(defEntry.Actions, defEntry.Id),
                    IsTable = defEntry.IsTable,
                    Storage = defEntry.Storage,
                };
                state.Catalog.Facilities[facilityDef.Id] = facilityDef;
                DefDatabase<FacilityDef>.Register(facilityDef);
            }
        }

        foreach (var entry in pack.World.Facilities)
        {
            var def = new FacilityDef
            {
                DefName = string.IsNullOrEmpty(entry.Name) ? $"Facility_{entry.Id}" : entry.Name,
                Id = entry.Id,
                Name = entry.Name,
                RoomId = entry.RoomId,
                Usage = ParseUsage(entry.Usage, entry.Id),
                Capacity = entry.Capacity,
                BuildCost = entry.BuildCost,
                StartBuilt = entry.Built,
                EffectId = entry.Effect,
                YieldItemId = entry.Yield,
                Description = entry.Description,
                MaterialCost = ToCosts(entry.Materials),
                Buildable = entry.Buildable,
                Actions = ParseActions(entry.Actions, entry.Id),
                IsTable = entry.IsTable,
                Storage = entry.Storage,
                Contents = new Dictionary<string, int>(entry.Contents),
            };
            state.Catalog.Facilities[def.Id] = def;
            DefDatabase<FacilityDef>.Register(def);
            state.Territory.AddFacility(def.ToRuntime());
        }

        return state;
    }

    private static RoomPermission ParsePermission(string name, int roomId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return RoomPermission.Public;
        if (System.Enum.TryParse<RoomPermission>(name, ignoreCase: true, out var permission))
            return permission;
        GD.PushError($"Rimisekai: 内容包里房间 {roomId} 的权限 {name} 不认识。");
        return RoomPermission.Public;
    }

    private static FacilityUsage ParseUsage(string name, int facilityId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FacilityUsage.Plain;
        if (System.Enum.TryParse<FacilityUsage>(name, ignoreCase: true, out var usage))
            return usage;
        GD.PushError($"Rimisekai: 内容包里设施 {facilityId} 的用途 {name} 不认识。");
        return FacilityUsage.Plain;
    }

    private static List<RecipeCost> ToCosts(Dictionary<string, int> materials)
    {
        var list = new List<RecipeCost>();
        foreach (var pair in materials)
            list.Add(new RecipeCost(pair.Key, pair.Value));
        return list;
    }

    /// <summary>
    /// 读设施的 actions 字段。null 表示内容包没写（运行时按用途兜底），
    /// 空表表示刻意不可交互（桌类）。名字不认识就报错并跳过该条。
    /// </summary>
    private static List<ActionKind> ParseActions(List<string>? names, int facilityId)
    {
        var list = new List<ActionKind>();
        if (names == null)
            return list;
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (System.Enum.TryParse<ActionKind>(name, ignoreCase: true, out var action))
                list.Add(action);
            else
                GD.PushError($"Rimisekai: 内容包里设施 {facilityId} 的行动 {name} 不认识。");
        }
        return list;
    }

}
