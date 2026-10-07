using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 一件**运行时生成**的武器实例。
///
/// 武器不是注册表里的一行：材料 × 类型只是基座，每座基座能衍生出无穷件实例，
/// 各自带品质、强化、附魔、祝福——名字也是当场拼的。
/// 实例不可堆叠，因此在背包/仓储里以 Id 记 1 件。
/// </summary>
public sealed class WeaponInstance
{
    /// <summary>实例 Id，形如 "wpn_7"。背包与仓储按它记数。</summary>
    public string Id { get; init; } = "";

    // ---- 基座两轴 ----
    public string MaterialDefName { get; init; } = "";
    public WeaponType Type { get; init; }

    /// <summary>
    /// 子类型 DefName（剑→刺剑/太刀，长枪→戟/镰刀）。空串 = 无子类型。
    /// 子类型与父类型**共享一份熟练度**，但有独特技能与不同的面板分配。
    /// 该轴后续扩展，这里先留位。
    /// </summary>
    public string Subtype { get; init; } = "";

    // ---- 实例字段 ----

    /// <summary>品质。既是价值倍率，也是面板乘数。</summary>
    public Quality Quality { get; init; }

    /// <summary>强化等级。0 = 未强化。</summary>
    public int Enhance { get; init; }

    /// <summary>附魔 DefName；空串表示没有附魔。</summary>
    public string Enchant { get; init; } = "";

    /// <summary>是否被祝福。</summary>
    public bool Blessed { get; init; }

    /// <summary>运行时生成的名字。</summary>
    public string Name { get; init; } = "";

    // ---- 派生 ----

    /// <summary>材料轴定义（全项目唯一那张材料表）。</summary>
    public MaterialDef? Material => DefDatabase<MaterialDef>.Get(MaterialDefName);

    /// <summary>类型轴定义。</summary>
    public WeaponTypeDef? Kind => DefDatabase<WeaponTypeDef>.All
        .FirstOrDefault(t => t.Type == Type);

    /// <summary>附魔定义；没有则 null。</summary>
    public EnchantDef? EnchantDef => Enchant.Length == 0
        ? null
        : DefDatabase<EnchantDef>.Get(Enchant);

    /// <summary>
    /// 面板值。基座（类型基础 + 材料加成）先按**品质乘数**整体缩放，
    /// 再加强化与附魔的加成，祝福另给一档。
    /// </summary>
    public int Panel
    {
        get
        {
            var kind = Kind;
            if (kind == null)
                return 0;
            var material = Material;
            var basePanel = kind.BasePanel + (material?.DamageBonus ?? 0);
            var total = basePanel * QualityOf.PanelFactor(Quality) / 100;
            total += Enhance * 2;
            if (EnchantDef is { } e)
                total += e.CoreBonus;
            if (Blessed)
                total += 3;
            return total;
        }
    }

    /// <summary>
    /// 市场价值：基座价 × 材料倍率 × 品质倍率，再加强化与祝福的溢价。
    /// </summary>
    public int Value
    {
        get
        {
            var kind = Kind;
            var material = Material;
            if (kind == null || material == null)
                return 1;
            var total = kind.BaseValue * material.ValueFactor / 100;
            total = total * QualityOf.ValueFactor(Quality) / 100;
            total += Enhance * 5;
            if (EnchantDef is { } e)
                total += 10 + e.Weight;
            if (Blessed)
                total = total * 3 / 2;
            return total < 1 ? 1 : total;
        }
    }

    /// <summary>
    /// 详情里逐行列明这件武器带着什么。
    /// 祝福与附魔已经写在名字里，这里只列其余字段，不重复。
    /// </summary>
    public IReadOnlyList<string> DescribeDetails()
    {
        var list = new List<string>
        {
            $"品质　{QualityOf.Label(Quality)}",
        };
        list.Add($"材料　{materialLabel()}");
        list.Add($"类型　{typeLabel()}");
        if (Enhance > 0)
            list.Add($"强化　+{Enhance}");
        if (EnchantDef is { } e)
            list.Add($"附魔　{e.Prefix}（{e.Effect}）");
        if (Blessed)
            list.Add("祝福　受祝福");
        return list;
    }

    private string materialLabel() => Material?.Label ?? "?";

    private string typeLabel()
    {
        var kind = Kind?.Label ?? "?";
        return Subtype.Length == 0 ? kind : $"{kind}·{Subtype}";
    }
}
