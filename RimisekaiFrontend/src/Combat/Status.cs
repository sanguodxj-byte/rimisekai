using System.Collections.Generic;
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
    /// <summary>姿态：<see cref="StatusEffect.Mods"/> 一直生效到战斗结束；一人同时一种。</summary>
    Stance = 3,
    /// <summary>机制点：<see cref="StatusEffect.Stacks"/> 层，每层一份 Mods；再积刷新时长。</summary>
    Charge = 4,
    /// <summary>反应：挂着等 <see cref="StatusEffect.Trigger"/>，发动 <see cref="StatusEffect.UsesLeft"/> 次后撤。</summary>
    Reaction = 5,
    /// <summary>光环：施放者给全体友方的 Mods，一直生效到战斗结束。</summary>
    Aura = 6,
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
    /// <summary>多项数值修正（姿态 / 机制点每层 / 光环 / 反应挂着时）。</summary>
    public IReadOnlyList<StatEffect> Mods { get; init; } = System.Array.Empty<StatEffect>();
    /// <summary>机制点层数；其余种类为 1。</summary>
    public int Stacks { get; set; } = 1;
    /// <summary>到战斗结束才撤（姿态、光环），不随回合递减。</summary>
    public bool Permanent { get; init; }
    /// <summary>反应的触发时机。</summary>
    public SkillTrigger Trigger { get; init; } = SkillTrigger.None;
    /// <summary>反应还能发动几次。</summary>
    public int UsesLeft { get; set; }

    public bool IsDebuff => Category == StatusCategory.Debuff;
}
