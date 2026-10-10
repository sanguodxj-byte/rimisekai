using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 附魔定义。附魔是武器实例上的一层前缀，带一组属性修正。
/// 一张表管全部附魔；运行时按权重掷，掷中就烙到实例上。
/// </summary>
public sealed class EnchantDef : Def
{
    /// <summary>前缀词，拼在武器名里，如"炽热的"。</summary>
    public string Prefix { get; init; } = "";

    /// <summary>主属性加成。</summary>
    public CoreStat? Core { get; init; }

    /// <summary>主属性加成点数。</summary>
    public int CoreBonus { get; init; }

    /// <summary>附加说明，界面与生成名都用得上。</summary>
    public string Effect { get; init; } = "";

    /// <summary>抽取权重（0 = 不参与随机）。</summary>
    public int Weight { get; init; } = 10;
}
