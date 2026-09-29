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
        Character.Bond.Fond => "好感",
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
        Character.LifeSkill.Haul => "搬运",
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

    /// <summary>工作时段名。0=0:00 1=6:00 2=12:00 3=18:00。</summary>
    public static string WorkSlot(int slot) => slot switch
    {
        0 => "0:00",
        1 => "6:00",
        2 => "12:00",
        3 => "18:00",
        _ => "?",
    };

    /// <summary>时段开关名（空闲 / 工作 / 不干活）。</summary>
    public static string SlotMode(Housing.SlotMode mode) => mode switch
    {
        Housing.SlotMode.Free => "空闲",
        Housing.SlotMode.Work => "工作",
        Housing.SlotMode.Rest => "不干活",
        _ => "?",
    };

    /// <summary>工作优先级：0 画成空白（不做），1-4 直接显示档位。</summary>
    public static string Priority(int value) => value <= 0 ? "" : value.ToString();

    /// <summary>
    /// 设施行动的按钮字。行动名不强行缩成两字，按日常说法写全。
    /// 这是行动名到显示文案的唯一映射，界面各处都从这里取。
    /// </summary>
}
