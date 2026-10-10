using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 工种定义。工作只是行动的一类，工种是工作的分类：
/// 每种工作挂一项核心属性，属性决定干活快慢。
/// 三层自上而下：属性 → 工种 → 行动 → 设施。
/// </summary>
public sealed class WorkTypeDef : Def
{
    /// <summary>干活速度由这项核心属性决定。</summary>
    public CoreStat Core { get; init; } = CoreStat.Dexterity;

    /// <summary>工种在界面里的行序，小的排前面。</summary>
    public int Order { get; init; }

    /// <summary>是不是重活。懒散、怕痛的人拒干。</summary>
    public bool Hard { get; init; }
}
