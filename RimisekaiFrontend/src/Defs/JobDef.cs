using Rimisekai.Housing;

namespace Rimisekai.Defs;

/// <summary>
/// 工作/行为定义（对应角色此刻所做的事情）。
/// 驱动状态机的生命周期、中断优先级与行为快照描述。
/// </summary>
public sealed class JobDef : Def
{
    /// <summary>映射的传统 ActionKind 枚举（保证与既有系统兼容）。</summary>
    public ActionKind LegacyGoal { get; init; } = ActionKind.None;

    /// <summary>打断优先级（越高越不容易被打断，如睡眠比闲逛优先级高）。</summary>
    public int Priority { get; init; }

    /// <summary>是否属于生理必需项（如睡眠、吃饭）。</summary>
    public bool IsVitalNeed { get; init; }

    /// <summary>快照叙述格式，如“{0}在{1}睡觉。”</summary>
    public string ReportFormat { get; init; } = "";
}
