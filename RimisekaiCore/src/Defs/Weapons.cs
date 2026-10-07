using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 武器基座：材料 × 类型两张轴表。
///
/// 轴表只是**配方**——铁剑、钢长枪这些是基座名，不是货。
/// 真正进背包的是一件件 <see cref="WeaponInstance"/>，由 <see cref="WeaponForge"/>
/// 在运行时生成，各自带品质/强化/附魔/祝福，名字也是当场拼的。
/// 只有 weaponUsable 的材料能打兵器——布与皮不行。
/// </summary>
public static class Weapons
{
    /// <summary>材料轴（按 Tier 由劣到优排序）。全项目只此一张材料表。</summary>
    public static IReadOnlyList<MaterialDef> Materials =>
        DefDatabase<MaterialDef>.All
            .Where(m => m.WeaponUsable)
            .OrderBy(m => m.Tier)
            .ToList();

    /// <summary>类型轴。</summary>
    public static IReadOnlyList<WeaponTypeDef> Types =>
        DefDatabase<WeaponTypeDef>.All;

    /// <summary>基座组合数（材料 × 类型）。</summary>
    public static int BaseCount() => Materials.Count * Types.Count;

    /// <summary>全部基座组合，逐条给出（材料, 类型）。</summary>
    public static IEnumerable<(MaterialDef Material, WeaponTypeDef Kind)> Bases()
    {
        foreach (var material in Materials)
            foreach (var kind in Types)
                yield return (material, kind);
    }
}
