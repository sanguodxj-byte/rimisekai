namespace Rimisekai.Defs;

/// <summary>
/// 全局基础定义类（参考 RimWorld 的 Def 体系）。
/// 纯数据驱动，所有物品、品类、设施、行为（Job）均继承自此类，由 DefDatabase 统一管理。
/// </summary>
public abstract class Def
{
    /// <summary>全局唯一不重复标识符（如 "Item_Bread", "Job_Sleep", "Cat_Food"）。</summary>
    public string DefName { get; init; } = "";

    /// <summary>玩家可见的本地化中文标签。</summary>
    public string Label { get; init; } = "";

    /// <summary>详细描述或补充说明。</summary>
    public string Description { get; init; } = "";

    public override string ToString() => string.IsNullOrEmpty(Label) ? DefName : Label;
}
