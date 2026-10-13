using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Ink;

/// <summary>
/// 枚举到中文显示名的唯一映射。
/// 界面各处都从这里取，避免同一个枚举在不同面板写出不同叫法。
/// </summary>
public static class InkText
{
    public static string Bond(Bond bond) => bond switch
    {
        Character.Bond.Hatred => "憎恨",
        Character.Bond.Hostile => "敌意",
        Character.Bond.Dislike => "嫌恶",
        Character.Bond.None => "普通",
        Character.Bond.Fond => "友好",
        Character.Bond.Close => "亲密",
        Character.Bond.Lover => "爱慕",
        _ => "普通",
    };

    public static string CoreStat(CoreStat stat) => stat switch
    {
        Character.CoreStat.Constitution => "体质",
        Character.CoreStat.Dexterity => "灵巧",
        Character.CoreStat.Intellect => "智力",
        Character.CoreStat.Charm => "魅力",
        Character.CoreStat.Perception => "感知",
        Character.CoreStat.Strength => "力量",
        _ => "?",
    };

    public static string LifeSkill(LifeSkill skill) => skill switch
    {
        Character.LifeSkill.Cooking => "烹饪",
        Character.LifeSkill.Social => "社交",
        Character.LifeSkill.Mining => "采掘",
        Character.LifeSkill.Farming => "种植",
        Character.LifeSkill.Husbandry => "驯兽",
        Character.LifeSkill.Craft => "手工",
        Character.LifeSkill.Research => "研究",
        Character.LifeSkill.Smithing => "锻造",
        _ => "?",
    };

    public static string Weapon(WeaponType type) => type switch
    {
        WeaponType.Sword => "剑",
        WeaponType.Axe => "斧",
        WeaponType.Spear => "矛",
        WeaponType.Bow => "弓",
        WeaponType.Staff => "杖",
        WeaponType.Dagger => "短剑",
        WeaponType.Crossbow => "弩",
        WeaponType.Unarmed => "格斗",
        _ => "?",
    };

    public static string Style(StyleType style) => style switch
    {
        StyleType.OneHand => "单手",
        StyleType.TwoHand => "双手",
        StyleType.DualWield => "双持",
        StyleType.Ranged => "远射",
        StyleType.Spell => "法术",
        StyleType.Shield => "持盾",
        StyleType.Unarmed => "格斗",
        _ => "?",
    };

    /// <summary>工作行动名。名称唯一来源在核心的 ActionKindMap.LabelOf。</summary>
    public static string ActionKind(Housing.ActionKind action) => Housing.ActionKindMap.LabelOf(action);

    /// <summary>工种名。</summary>
    public static string WorkType(Housing.WorkType type) => Housing.WorkTypeMap.LabelOf(type);

    /// <summary>工作时段名。0时00分 / 6时00分 / 12时00分 / 18时00分（项目禁用冒号）。</summary>
    public static string FoodTier(Housing.FoodTier tier) => Housing.FoodTiers.Label(tier);

        public static string WorkSlot(int slot) => slot switch
    {
        0 => "0时00分",
        1 => "6时00分",
        2 => "12时00分",
        3 => "18时00分",
        _ => "?",
    };

    /// <summary>时段开关名（空闲 / 工作）。</summary>
    public static string SlotMode(Housing.SlotMode mode) => mode switch
    {
        Housing.SlotMode.Free => "空闲",
        Housing.SlotMode.Work => "工作",
        _ => "?",
    };

    /// <summary>工作优先级：0 画成空白（不做），1-4 直接显示档位。</summary>
    public static string Priority(int value) => value <= 0 ? "" : value.ToString();

    /// <summary>
    /// 货币格式化：三位一组空格分隔，带 G 后缀（如 10 000G）。
    /// 避免宋体西文逗号自带过大空隙导致的视觉异常。
    /// </summary>
    public static string Money(long amount)
    {
        if (amount < 0)
            return "-" + Money(-amount);

        var raw = amount.ToString();
        if (raw.Length <= 3)
            return $"{raw}G";

        var sb = new System.Text.StringBuilder();
        var head = raw.Length % 3;
        if (head > 0)
        {
            sb.Append(raw.Substring(0, head));
            if (head < raw.Length)
                sb.Append('\u2009');
        }
        for (var i = head; i < raw.Length; i += 3)
        {
            if (i > head)
                sb.Append('\u2009');
            sb.Append(raw.Substring(i, 3));
        }
        sb.Append('G');
        return sb.ToString();
    }

    // ---------- 战斗技能的面（技能页右栏详情用）----------

    /// <summary>技能种类名。</summary>
    public static string SkillKind(Catalog.SkillKind kind) => kind switch
    {
        Catalog.SkillKind.Strike => "打击",
        Catalog.SkillKind.Spell => "法术",
        Catalog.SkillKind.Heal => "治疗",
        Catalog.SkillKind.Buff => "增益",
        _ => "?",
    };

    /// <summary>技能目标名。</summary>
    public static string SkillTarget(Catalog.SkillTarget target) => target switch
    {
        Catalog.SkillTarget.Enemy => "单体敌人",
        Catalog.SkillTarget.Ally => "单体友方",
        Catalog.SkillTarget.Self => "自身",
        Catalog.SkillTarget.AllEnemies => "全体敌人",
        Catalog.SkillTarget.AllAllies => "全体友方",
        Catalog.SkillTarget.FoesColumn => "整列敌人",
        _ => "?",
    };

    /// <summary>技能射程名。</summary>
    public static string SkillRange(Catalog.SkillRange range) => range switch
    {
        Catalog.SkillRange.Melee => "近程",
        Catalog.SkillRange.Ranged => "远程",
        _ => "?",
    };

    /// <summary>状态作用面名。</summary>
    public static string StatusStat(Catalog.StatusStat stat) => stat switch
    {
        Catalog.StatusStat.Attack => "攻击",
        Catalog.StatusStat.Defence => "防御",
        Catalog.StatusStat.Dodge => "闪避",
        Catalog.StatusStat.SpellPower => "法术",
        Catalog.StatusStat.Speed => "速度",
        Catalog.StatusStat.Damage => "伤害",
        Catalog.StatusStat.Crit => "暴击率",
        _ => "?",
    };

    /// <summary>身份核心技能的机制种类名；基础技能为「基础」。</summary>
    public static string CoreKind(Catalog.CoreKind kind) => kind switch
    {
        Catalog.CoreKind.Stance => "核心·姿态",
        Catalog.CoreKind.Charge => "核心·机制点",
        Catalog.CoreKind.Reaction => "核心·反应",
        Catalog.CoreKind.Aura => "核心·光环",
        _ => "基础",
    };

    /// <summary>
    /// 技能效果句：由数值拼成一句话，如「对单体敌人造成 250% 威力的法术伤害，命中 +5，并使其防御 -10%，持续 1 回合。」
    /// 没有直接效果的（核心技能的姿态、机制点等）返回空串，由说明文字交代。
    /// </summary>
    public static string SkillEffect(Catalog.SkillDef s)
    {
        var target = SkillTarget(s.Target);
        var them = s.Target == Catalog.SkillTarget.Self ? "自身" : "其";
        var stat = s.Status switch
        {
            Combat.StatusKind.StatMod => $"{(s.StatusStat == Catalog.StatusStat.SpellPower ? "法强" : StatusStat(s.StatusStat))} {s.StatusPercent:+0;-0}%，持续 {s.StatusRounds} 回合",
            Combat.StatusKind.Dot => $"每回合受到 {s.StatusPower} 点伤害，持续 {s.StatusRounds} 回合",
            Combat.StatusKind.Points => $"获得 {s.StatusPower} 点护盾，持续 {s.StatusRounds} 回合",
            _ => "",
        };
        var parts = new System.Collections.Generic.List<string>();
        switch (s.Kind)
        {
            case Catalog.SkillKind.Strike:
            case Catalog.SkillKind.Spell:
                parts.Add($"对{target}造成 {s.Power}% 威力的{(s.Kind == Catalog.SkillKind.Spell ? "法术" : "物理")}伤害");
                if (s.HitMod != 0)
                    parts.Add($"命中 {s.HitMod:+0;-0}");
                if (stat.Length > 0)
                    parts.Add($"并使{them}{stat}");
                break;
            case Catalog.SkillKind.Heal:
                parts.Add($"为{target}回复 {s.Power}% 威力的生命");
                if (stat.Length > 0)
                    parts.Add($"并使{them}{stat}");
                break;
            default:
                if (stat.Length > 0)
                    parts.Add($"使{target}{stat}");
                break;
        }
        if (parts.Count == 0)
            return "";
        if (s.Control)
            parts.Add("打断其咏唱");
        var line = string.Join("，", parts) + "。";
        return s.ChantRounds > 0 ? $"咏唱 {s.ChantRounds} 回合后，{line}" : line;
    }

    /// <summary>技能等阶名：零阶到五阶（见 <see cref="Combat.SkillTier"/>）。</summary>
    public static string SkillTier(int tier) => $"{"零一二三四五"[tier]}阶";

    // ---------- 技能门槛（技能盘瓦片与右栏详情用） ----------

    /// <summary>素质的中文名，从素质目录查（唯一来源在 Core）。</summary>
    public static string TraitName(Character.Trait trait)
    {
        foreach (var def in Character.Traits.Catalog)
            if (def.Trait == trait)
                return def.Name;
        return trait.ToString();
    }

    /// <summary>
    /// 设施行动的按钮字。行动名不强行缩成两字，按日常说法写全。
    /// 这是行动名到显示文案的唯一映射，界面各处都从这里取。
    /// </summary>
}
