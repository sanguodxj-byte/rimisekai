using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 饰品类型表。**类型决定它加哪一项战斗属性**，一类对应一项属性。
///
/// 饰器件件不是戒指就是项链，名字只由材料加物件名词构成（铁戒指、秘银项链）；
/// 类型加什么属性写在详情里，不进名字。
/// </summary>
public sealed class AccessoryDef : Def
{
    /// <summary>加成的战斗属性。</summary>
    public CoreStat Core { get; init; }

    /// <summary>基础加成点数（材料倍率与品质乘数都算在这之上）。</summary>
    public int BaseBonus { get; init; }

    /// <summary>抽取权重（0 = 不参与随机）。</summary>
    public int Weight { get; init; } = 10;
}
