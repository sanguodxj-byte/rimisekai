using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 内容包分两层：WorldTable 是静态表（房间/设施/通路/食物定义），
/// NewGameSeed 是开局初始状态（人物/金钱/库存/出生点）。日志绝不配表，运行时生成。
/// 字段名与 content/world.json、content/newgame.json 一一对应，改内容不用重新编译。
/// </summary>
public sealed class ContentPack
{
    public sealed class RoomEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public bool Open { get; set; }
        public int OpenCost { get; set; }
        public int Region { get; set; }
        public string Permission { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, int> Materials { get; set; } = new();
        public bool Buildable { get; set; }

        /// <summary>房间细分标签（室内/室外/工作间/娱乐室/卧室等）。</summary>
        public List<string> Tags { get; set; } = new();
    }

    public sealed class FacilityEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int RoomId { get; set; }
        public int Capacity { get; set; } = 1;
        public string Usage { get; set; } = "";
        public bool Built { get; set; } = true;
        public int BuildCost { get; set; }
        public string Effect { get; set; } = "";
        public string Yield { get; set; } = "";
        public string Skill { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, int> Materials { get; set; } = new();
        public bool Buildable { get; set; }

        /// <summary>
        /// 这件设施能支撑的日常行动（ActionKind 的名字，如 "Sleep"、"Meal"）。
        /// 不写 = 按用途查兜底表；写空数组 = 刻意不可交互（如桌类）。
        /// 用可空区分这两者，否则桌子会被兜底成可交互。
        /// </summary>
        public List<string>? Actions { get; set; }

        /// <summary>是不是桌子。桌子本身无行动，只让同桌吃饭的人不扣心情。</summary>
        public bool IsTable { get; set; }

        /// <summary>能不能存东西。物品只存在能存的设施里（或角色背包里），没有虚空库存。</summary>
        public bool Storage { get; set; }

        /// <summary>开局时这件设施里已存着的东西。</summary>
        public Dictionary<string, int> Contents { get; set; } = new();
    }

    public sealed class CharacterEntry
    {
        public string Name { get; set; } = "";
        public bool Master { get; set; }
        public int RoomId { get; set; } = -1;

        /// <summary>初始好感。缺省为 0（陌生人）。</summary>
        public int Favor { get; set; }

        /// <summary>初始素质。写 Trait 的名字，如 "Maid"。</summary>
        public List<string> Traits { get; set; } = new();

        /// <summary>
        /// 立绘的 res:// 路径。留空则按约定取 res://content/portraits/&lt;名字&gt;.png，
        /// 两者都没有对应文件时界面退回线稿人形。
        /// </summary>
        public string Portrait { get; set; } = "";
    }

    public sealed class LinkEntry
    {
        public int From { get; set; }
        public int To { get; set; }
    }

    public sealed class WorldTable
    {
        public string TerritoryName { get; set; } = "";
        public List<string> Foods { get; set; } = new();
        public List<RoomEntry> Rooms { get; set; } = new();
        public List<LinkEntry> Links { get; set; } = new();
        public List<FacilityEntry> Facilities { get; set; } = new();
    }

    public sealed class NewGameSeed
    {
        public long Money { get; set; }
        public int StartRoomId { get; set; } = -1;

        /// <summary>口上/地文文件的位置。留空则不加载台词，整条链路旁路。</summary>
        public string VoicePath { get; set; } = "";

        /// <summary>
        /// 口上/地文文件列表。一个角色一个文件，与 eraFL 的每角色一 XML 同构，
        /// 免得单个文件膨胀到没法维护。与 VoicePath 一起给出时两者都会加载。
        /// </summary>
        public List<string> VoicePaths { get; set; } = new();

        /// <summary>开局选中的角色名。用名字而不是 Id，避免依赖角色注册顺序。</summary>
        public string StartCharacterName { get; set; } = "";

        public Dictionary<string, int> Stock { get; set; } = new();
        public List<CharacterEntry> Characters { get; set; } = new();
    }

    public sealed class BuildingDefEntry
    {
        public string DefType { get; set; } = "";
        public string DefName { get; set; } = "";
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, int> Materials { get; set; } = new();
        public bool Buildable { get; set; } = true;

        // RoomDef 专有
        public List<string> Tags { get; set; } = new();
        public List<string> Facilities { get; set; } = new();

        // FacilityDef 专有
        public string Usage { get; set; } = "";
        public int Capacity { get; set; } = 1;
        public string Effect { get; set; } = "";
        public string Yield { get; set; } = "";
        public string Skill { get; set; } = "";
        public List<string>? Actions { get; set; }
        public bool IsTable { get; set; }
        public bool Storage { get; set; }
    }

    public sealed class BuildingPack
    {
        public List<BuildingDefEntry> Defs { get; set; } = new();
    }

    public WorldTable World { get; set; } = new();
    public NewGameSeed Seed { get; set; } = new();
    public BuildingPack Buildings { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>从 res:// 下的三个 JSON 读取静态表、开局种子与建筑表。</summary>
    public static ContentPack Load(string worldPath, string seedPath, string buildingsPath) => new()
    {
        World = LoadFile<WorldTable>(worldPath),
        Seed = LoadFile<NewGameSeed>(seedPath),
        Buildings = LoadFile<BuildingPack>(buildingsPath),
    };

    private static T LoadFile<T>(string resPath) where T : new()
    {
        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"Rimisekai: 无法读取内容包 {resPath}（{FileAccess.GetOpenError()}）");
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(file.GetAsText(), Options) ?? new T();
        }
        catch (JsonException e)
        {
            GD.PushError($"Rimisekai: 内容包 {resPath} 解析失败：{e.Message}");
            return new T();
        }
    }
}

