using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 怪物模板（content/defs/monsters.json）：全项目唯一的怪物数据源。野外、地城、委托都从这里取，
/// 战斗开始时按 <see cref="Cr"/> → 等级 → 属性池/经验池交给角色生成器掷身板（见 <see cref="ChallengeDef"/>）。
/// 栖息标签（地城类型、房间地形、野外地形）与 CR 一起建出抽怪的两张查找表（<see cref="Combat.MonsterIndex"/>）。
/// 字段写法见 docs/怪物表规范.md。
/// </summary>
public sealed class MonsterDef : Def
{
    /// <summary>模板 id（小写 snake_case），即 defName。</summary>
    public string Id
    {
        get => DefName;
        init => DefName = value;
    }

    /// <summary>玩家可见名，即 label。</summary>
    public string Name
    {
        get => Label;
        init => Label = value;
    }

    public string Portrait { get; init; } = "";
    public WeaponType? Weapon { get; init; }
    public CoreStat? Primary { get; init; }
    public CoreStat? Secondary { get; init; }

    /// <summary>挑战等级：管属性生成与装备掉落。</summary>
    public ChallengeRating Cr { get; init; }

    /// <summary>默认站位层 1–4（4 最前排）；只管站位，不代表强弱。</summary>
    public int ThreatTier { get; init; } = 1;
    public int Size { get; init; } = 1;
    public int ActionPoints { get; init; } = 1;
    public int Armour { get; init; }
    public long Money { get; init; }
    public List<string> Skills { get; init; } = new();
    public List<EnemyLoot> Loot { get; init; } = new();
    public EnemyGear? Gear { get; init; }

    /// <summary>族群：遭遇条目按族群挑怪，叙事与阵容对得上（狼群里只出狼）。</summary>
    public string Family { get; init; } = "";

    /// <summary>
    /// 素质（TraitDef 的 DefName），与角色身份授予的素质（如女仆）同一套，生成时授予怪物身板。
    /// 带首领素质（Boss）的只当正主，不带的只当随从。
    /// </summary>
    public List<Trait> Traits { get; init; } = new();

    /// <summary>可出现的地城类型（dungeon_defs.json dungeons[].id）。</summary>
    public List<string> Dungeons { get; init; } = new();

    /// <summary>地城里可出现的房间地形（room_defs.json 的 terrain）；空＝该地城的任何房间。</summary>
    public List<string> Rooms { get; init; } = new();

    /// <summary>野外可出现的地形（map_defs.json terrains[].type）。</summary>
    public List<string> Terrains { get; init; } = new();

    public bool CanLead => Rimisekai.Character.Traits.MarksBoss(Traits);
    public bool CanFollow => !CanLead;
}
