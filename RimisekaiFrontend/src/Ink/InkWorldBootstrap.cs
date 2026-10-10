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
/// 启动用的世界。内容全部来自 Core 的 ContentDefs（代码 Def），本类只做装配，不含任何设定。
/// </summary>
public static class InkWorldBootstrap
{
    /// <summary>按内容包建一份世界，并按开局种子指定的起始位置开一个据点会话。</summary>
    public static HubSession OpenHub(out GameState state) =>
        OpenHub(new ContentPack(), out state);

    /// <summary>困难开局：只有玩家自己。</summary>
    public static HubSession OpenHubHard(out GameState state) =>
        OpenHub(new ContentPack(hard: true), out state);

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

        // 挂接运行时 LLM 生成器（双模支持：有配置走动态生成，出错或无网络自动安全回退高质静态文案）
        state.Voice.Generation.Generator ??= new Rimisekai.Voice.HttpVoiceGenerator(apiKey: Rimisekai.Voice.LocalApiKeys.Get("xjbh"));
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
            out var scenes, out var events, out var personas, out var error))
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
        state.Voice.Events.RegisterRange(events);
        foreach (var pair in personas)
            state.Voice.Generation.Personas[pair.Key] = pair.Value;
    }

    public static GameState Create() => Create(new ContentPack());

    /// <summary>
    /// 新开局的世界种子：为 null 时每局随机掷一个（大世界地形、聚落与领地选址随之而定）；
    /// 截图 / 核对工具设成定值，画面可复现。
    /// </summary>
    public static int? WorldSeedOverride { get; set; }

    public static GameState Create(ContentPack pack)
    {
        ContentDefs.EnsureInitialized();
        var state = new GameState { Money = pack.Seed.Money };
        state.RegenerateWorld(WorldSeedOverride ?? (int)(System.Random.Shared.Next(1, int.MaxValue)));
        state.Territory.Name = ContentDefs.TerritoryName;

        var generator = new CharacterGenerator();
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
                    GD.PushError($"Rimisekai: 开局角色 {entry.Name} 的素质 {name} 不认识。");
            }

            // 生活履历：初始属性、身份装备、经验由生成器发；名字/主仆/好感/素质以种子为准。
            generator.Populate(character, entry.Traits);
        }

        // 开局物资进玩家背包（物品只在背包或设施里，没有领地虚空库存）。
        var master = state.Roster.Master;
        if (master != null)
        {
            foreach (var pair in pack.Seed.BagStock)
                master.Bag.Add(pair.Key, pair.Value);
        }

        LoadVoice(state, pack);

        // 可建造目录：房间与设施定义。权威只在 DefDatabase，不另存一份目录。
        foreach (var def in ContentDefs.BuildingRooms)
            DefDatabase<RoomDef>.Register(def);
        foreach (var def in ContentDefs.BuildingFacilities)
            DefDatabase<FacilityDef>.Register(def);

        // 开局领地：房间、连通、已摆好的设施。
        foreach (var def in ContentDefs.AreaRooms)
        {
            DefDatabase<RoomDef>.Register(def);
            state.Territory.AddRoom(def.ToRuntime());
        }
        foreach (var pair in ContentDefs.AreaLinks)
        {
            foreach (var to in pair.Value)
                state.Territory.Link(pair.Key, to);
        }
        // 开局已摆好的设施：照定义表里那一份生成实例，实例号与所在房间按摆位来。
        foreach (var place in ContentDefs.AreaFacilities)
        {
            var facility = DefDatabase<FacilityDef>.GetNamed(place.FacilityDefName).ToRuntime();
            facility.Id = place.Id;
            facility.RoomId = place.RoomId;
            foreach (var pair in place.Contents)
                facility.Contents.Add(pair.Key, pair.Value);
            state.Territory.AddFacility(facility);
            if (place.MasterBed)
                state.Territory.MasterBedId = facility.Id;
        }

        return state;
    }

}
