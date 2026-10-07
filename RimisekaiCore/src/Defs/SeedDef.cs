using System.Collections.Generic;

namespace Rimisekai.Defs;

/// <summary>开局角色的一条初始描述。</summary>
public sealed class CharacterSeed
{
    public string Name { get; init; } = "";
    public bool Master { get; init; }
    public int RoomId { get; init; } = -1;
    public int Favor { get; init; }

    /// <summary>起始素质（TraitDef 的 DefName）。</summary>
    public List<string> Traits { get; init; } = new();

    public string Portrait { get; init; } = "";
}

/// <summary>
/// 开局种子。初始资源、出生点、初始角色与台词包位置。
/// 一份种子就是一个开局档位（标准 / 困难），新增档位只加一行表。
/// </summary>
public sealed class NewGameSeedDef : Def
{
    public long Money { get; init; }
    public int StartRoomId { get; init; } = -1;

    /// <summary>口上/地文主文件。留空则不加载。</summary>
    public string VoicePath { get; init; } = "";

    /// <summary>按角色拆分的台词文件，与主文件一起加载。</summary>
    public List<string> VoicePaths { get; init; } = new();

    /// <summary>开局选中的角色名。用名字而不是 Id，避免依赖注册顺序。</summary>
    public string StartCharacterName { get; init; } = "";

    /// <summary>开局库存：主角背包里的初始物资。物品 Id -> 件数。</summary>
    public Dictionary<string, int> BagStock { get; init; } = new();

    /// <summary>初始角色名单。</summary>
    public List<CharacterSeed> Characters { get; init; } = new();
}

/// <summary>
/// 世界种子：与具体开局档位无关的世界级设定（领地名、可食用名单）。
/// 领地名这类"世界叫什么"的东西与开局档位正交，因此单列一张表。
/// </summary>
public sealed class WorldSeedDef : Def
{
    /// <summary>领地初始名。</summary>
    public string TerritoryName { get; init; } = "";

    /// <summary>可食用物品名单（进食行动的候选），物品 Id 列表。</summary>
    public List<string> Foods { get; init; } = new();
}
