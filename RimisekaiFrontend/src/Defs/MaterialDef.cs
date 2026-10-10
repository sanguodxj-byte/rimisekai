namespace Rimisekai.Defs;

/// <summary>
/// 材料定义。全项目只有这一张材料表，兵器与防具共用。
///
/// 材料继承 <see cref="ThingDef"/>——材料本身就是物品，背包与仓储里放的就是它，
/// 不再另立一份同名的 ThingDef，因此“铁”“布”在项目里只存在一处。
/// </summary>
public sealed class MaterialDef : ThingDef
{
    /// <summary>材料等级，由劣到优。只用于排序与强弱判定。</summary>
    public int Tier { get; init; }

    /// <summary>能不能打兵器。布与皮不行——没有布剑、皮矛。</summary>
    public bool WeaponUsable { get; init; }

    /// <summary>能不能缝甲。</summary>
    public bool ArmorUsable { get; init; }

    /// <summary>兵器面板加成：叠加到武器种类的基座面板上，再随品质整体缩放。</summary>
    public int DamageBonus { get; init; }

    /// <summary>甲的防御加成：叠加到每件甲的槽位底防上，再随品质整体缩放。</summary>
    public int ArmorBonus { get; init; }

    /// <summary>价值倍率（百分比，100 = 按基座原价）。</summary>
    public int ValueFactor { get; init; } = 100;
}
