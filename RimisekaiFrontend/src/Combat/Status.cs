using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Combat;

/// <summary>状态大类：增益与弱化。</summary>
public enum StatusCategory
{
    /// <summary>增益：数值上升、点数护盾。</summary>
    Buff,

    /// <summary>弱化：数值下降、持续伤害。</summary>
    Debuff,
}

/// <summary>状态种类。一条状态是其中一种。</summary>
public enum StatusKind
{
    /// <summary>数值百分比增减：作用于某项战斗数值，正增益、负弱化。</summary>
    StatMod = 0,

    /// <summary>持续伤害（中毒/灼烧/流血）：每轮结算 <see cref="StatusEffect.Power"/> 点伤害，可击杀。</summary>
    Dot = 1,

    /// <summary>
    /// 机制点数：<see cref="StatusEffect.Points"/> 为余量。
    /// 护盾（吸收物理）是首个用途；连击计数、魔力解放这类战斗机制点数也走这一种。
    /// </summary>
    Points = 2,
}

/// <summary>
/// 一条生效中的状态（增益或弱化）。同名（Token）刷新：移除旧条目再上新的。
/// 三种类各取所需：StatMod 读 Stat/Percent；DoT 读 Power；Shield 读 Points。
/// 控制打断（打断咏唱）不是状态——那是技能（Control）命中时的即时效果。
/// </summary>
public sealed class StatusEffect
{
    /// <summary>刷新键：同 Token 再上同状态时替换旧条目。</summary>
    public string Token { get; init; } = "";

    /// <summary>显示名（中毒/灼烧/铁壁……）。</summary>
    public string Name { get; init; } = "";

    public StatusCategory Category { get; init; }

    public StatusKind Kind { get; init; }

    /// <summary>StatMod 的作用面。其余种类为 None。</summary>
    public StatusStat Stat { get; init; } = StatusStat.None;

    /// <summary>StatMod 的幅度（百分点，可负）。</summary>
    public int Percent { get; init; }

    /// <summary>DoT 的每轮伤害。</summary>
    public int Power { get; set; }

    /// <summary>点数余量（机制点数，如护盾吸收量）。</summary>
    public int Points { get; set; }

    /// <summary>施加者 Id。DoT 的击杀记账用。</summary>
    public int SourceId { get; init; }

    public int RoundsLeft { get; set; }

    public bool IsDebuff => Category == StatusCategory.Debuff;
}
