using System.Collections.Generic;

namespace Rimisekai.Defs;

/// <summary>
/// 身份定义。身份轴决定角色的称呼与职责语汇，并授予随身份附带的素质。
/// 身份不再携带任何属性加成——属性只有核心七维，掷骰时与身份完全无关。
/// 生成随机角色时从这张表里掷骰挑一条。
/// </summary>
public sealed class IdentityDef : Def
{
    /// <summary>惯用口头禅。写进 persona 供 LLM 生成层取用。</summary>
    public string Catchphrase { get; init; } = "";

    /// <summary>随身份授予的素质（TraitDef 的 DefName）。</summary>
    public List<string> Grants { get; init; } = new();

    /// <summary>初始主手武器（可空，只有战斗类身份自带武器）。</summary>
    public string? MainWeapon { get; init; }

    /// <summary>初始副手武器（可空）。</summary>
    public string? OffWeapon { get; init; }

    /// <summary>初始副手是否持盾。</summary>
    public bool OffShield { get; init; }
}

/// <summary>
/// 身份技能池（拟案，待主人核定）：defName＝身份的 defName，按身份取索引。每个身份 20 式基础技能＋3 式核心技能
/// （<see cref="Rimisekai.Catalog.CoreKind"/>）。角色的技能池从这里抽（见 <see cref="Rimisekai.Combat.SkillPool"/>）。
/// 预设由 LLM 生成，日后可直接换成运行时 LLM 生成的同格式数据。
/// </summary>
public sealed class IdentitySkillPoolDef : Def
{
    public List<Rimisekai.Catalog.SkillDef> Skills { get; init; } = new();
}

/// <summary>
/// 随机角色的独有层素材（经历 / 转折 / 来意 / 口癖）与名字音节。
/// 全部是纯文本表，生成器只掷骰拼装，不含任何硬编码。
/// </summary>
public sealed class PersonaPartsDef : Def
{
    /// <summary>出身经历。</summary>
    public List<string> Origins { get; init; } = new();

    /// <summary>人生转折。</summary>
    public List<string> Turns { get; init; } = new();

    /// <summary>来到领地的缘由。</summary>
    public List<string> Reasons { get; init; } = new();

    /// <summary>口癖。</summary>
    public List<string> Tics { get; init; } = new();

    /// <summary>名字首字。</summary>
    public List<string> NameStarts { get; init; } = new();

    /// <summary>名字中字（可省）。</summary>
    public List<string> NameMids { get; init; } = new();

    /// <summary>名字末字。</summary>
    public List<string> NameEnds { get; init; } = new();
}

/// <summary>
/// 房间初始连通定义。
/// </summary>
public sealed class AreaLinkDef : Def
{
    public int From { get; init; }
    public List<int> To { get; init; } = new();
}
