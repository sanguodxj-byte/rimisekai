using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 武器类型轴。武器由"材料 × 类型"组合而成，这张表是类型那一轴。
/// 新增一种类型只需在这里加一行，全部材料自动各有一件对应武器。
/// </summary>
public sealed class WeaponTypeDef : Def
{
    /// <summary>对应的战斗枚举值。</summary>
    public WeaponType Type { get; init; }

    /// <summary>持握风格（单手/双手/远程/施法），由类型决定。</summary>
    public StyleType Style { get; init; }

    /// <summary>主属性（力量/敏捷/感知/智力）。</summary>
    public CoreStat Core { get; init; }

    /// <summary>基础面板值。</summary>
    public int BasePanel { get; init; }

    /// <summary>基础价值（材料倍率在此基础上缩放）。</summary>
    public int BaseValue { get; init; }
}
