using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Character;

namespace Rimisekai.Defs;

/// <summary>
/// 武器锻炉。材料 × 类型是基座，每炉出一件**实例**：
/// 掷品质、掷附魔、定祝福与强化，名字当场拼。
///
/// 与"注册表"的区别：注册表里一件武器一行，字段写死；
/// 这里一座基座能出无穷件，字段在生成时决定，因此内容包只需要几张轴表。
/// 独特的武器（固定名与属性）不走这里——它们逐件登记进 ThingDef 表。
/// </summary>
public static class WeaponForge
{
    private static int _nextId = 1;

    /// <summary>分配下一个实例 Id。</summary>
    public static string NextId() => $"wpn_{_nextId++}";

    /// <summary>重置 Id 计数（读档时对齐，避免与档内实例撞号）。</summary>
    public static void ResetIds(int next) => _nextId = next < 1 ? 1 : next;

    /// <summary>
    /// 锻一件武器。参数都给了就照办；没给的按权重随机——
    /// 品质与附魔必掷，祝福与强化按机会。
    /// 品质给 <see cref="Quality.Unique"/> 会抛异常：独特武器必须登记，不能随机生成。
    /// </summary>
    public static WeaponInstance Forge(string materialDefName, WeaponType type,
        Quality? quality = null, string? enchant = null, bool? blessed = null,
        int? enhance = null, string subtype = "", Random? rng = null)
    {
        rng ??= new Random();
        var material = DefDatabase<MaterialDef>.Get(materialDefName);
        var kind = DefDatabase<WeaponTypeDef>.All.FirstOrDefault(t => t.Type == type);
        if (material == null || kind == null || !material.WeaponUsable)
            throw new ArgumentException($"武器基座不存在：{materialDefName} × {type}");

        var rolledQuality = quality ?? RollQuality(rng);
        if (rolledQuality == Quality.Unique)
            throw new ArgumentException("独特武器固定名与属性，须逐件登记，不能随机生成。");

        var rolledEnchant = enchant ?? RollEnchant(rng);
        var rolledBlessed = blessed ?? (rng.Next(100) < 8);   // 8% 出祝福
        var rolledEnhance = enhance ?? (rng.Next(100) < 20 ? rng.Next(1, 4) : 0);

        // 名字只由祝福与附魔决定；品质、强化等进详情，不塞进名字。
        var draft = new WeaponInstance
        {
            Id = NextId(),
            MaterialDefName = materialDefName,
            Type = type,
            Subtype = subtype ?? "",
            Quality = rolledQuality,
            Enchant = rolledEnchant ?? "",
            Blessed = rolledBlessed,
            Enhance = rolledEnhance,
        };
        return new WeaponInstance
        {
            Id = draft.Id,
            MaterialDefName = draft.MaterialDefName,
            Type = draft.Type,
            Subtype = draft.Subtype,
            Quality = draft.Quality,
            Enchant = draft.Enchant,
            Blessed = draft.Blessed,
            Enhance = draft.Enhance,
            Name = NameOf(draft),
        };
    }

    /// <summary>
    /// 运行时起名。**只有祝福与附魔进名字**，其余（品质/强化/材料等级）在详情里看。
    /// 语序：祝福 → 附魔 → 材料 → 子类型 → 类型。
    /// 例："受祝福的炽热的精金太刀"。
    /// </summary>
    public static string NameOf(WeaponInstance w)
    {
        var parts = new List<string>();
        if (w.Blessed)
            parts.Add("受祝福的");
        if (w.EnchantDef is { } e && e.Prefix.Length > 0)
            parts.Add(e.Prefix);

        var material = w.Material?.Label ?? "?";
        var kind = w.Kind?.Label ?? "?";
        var core = w.Subtype.Length > 0 ? $"{material}{w.Subtype}" : $"{material}{kind}";
        return string.Join("", parts) + core;
    }

    /// <summary>掷一档品质（装备与武器共用同一套分布）。</summary>
    public static Quality RollQualityPublic(Random rng) => RollQuality(rng);

    private static Quality RollQuality(Random rng)
    {
        var roll = rng.Next(1000);
        // 12% 粗劣 / 48% 寻常 / 25% 精致 / 11% 史诗 / 4% 传说。
        if (roll < 120) return Quality.Crude;
        if (roll < 600) return Quality.Common;
        if (roll < 850) return Quality.Fine;
        if (roll < 960) return Quality.Epic;
        return Quality.Legendary;
    }

    /// <summary>掷一条附魔（装备与武器共用同一张附魔表）。</summary>
    public static string? RollEnchantPublic(Random rng) => RollEnchant(rng);

    private static string? RollEnchant(Random rng)
    {
        var pool = DefDatabase<EnchantDef>.All.Where(e => e.Weight > 0).ToList();
        if (pool.Count == 0)
            return null;
        var total = pool.Sum(e => e.Weight);
        var roll = rng.Next(total);
        foreach (var e in pool)
        {
            roll -= e.Weight;
            if (roll < 0)
                return e.DefName;
        }
        return null;
    }

    /// <summary>全部基座组合数（材料 × 类型）。</summary>
    public static int BaseCount() =>
        DefDatabase<MaterialDef>.All.Count(m => m.WeaponUsable)
        * DefDatabase<WeaponTypeDef>.All.Count;

    /// <summary>能打兵器的材料，由劣到优——布与皮不在其列。</summary>
    public static IReadOnlyList<MaterialDef> WeaponMaterials() =>
        DefDatabase<MaterialDef>.All
            .Where(m => m.WeaponUsable)
            .OrderBy(m => m.Tier)
            .ToList();
}
