namespace Rimisekai.Character;

/// <summary>核心属性。生活技能和战斗面板都从这里长出来。</summary>
public enum CoreStat
{
    Constitution = 0,
    Dexterity = 1,
    Intellect = 2,
    Charm = 3,
    Perception = 4,
    Strength = 5,

    /// <summary>速度。仅战斗作用：决定行动间的间隔（跑条）。</summary>
    Speed = 6,
}

/// <summary>
/// 生活技能。每项只吃对应的一项核心属性，技能值 = 核心属性 + 该项经验/100，
/// 干活速度与产出量都由它算。
/// </summary>
public enum LifeSkill
{
    /// <summary>烹饪。体质。</summary>
    Cooking = 0,

    /// <summary>社交。魅力。表演、招待、交易都算这项。</summary>
    Social = 1,

    /// <summary>采掘。力量。挖矿、伐木、采石。</summary>
    Mining = 2,

    /// <summary>种植。感知。耕作、采摘。</summary>
    Farming = 3,

    /// <summary>驯兽。魅力。饲养。</summary>
    Husbandry = 4,

    /// <summary>手工。灵巧。木工、缝纫。</summary>
    Craft = 5,

    /// <summary>研究。智力。炼金。</summary>
    Research = 6,

    /// <summary>锻造。力量。</summary>
    Smithing = 7,
}

public static class AttributeMap
{
    public static CoreStat CoreOf(LifeSkill skill) => skill switch
    {
        LifeSkill.Cooking => CoreStat.Constitution,
        LifeSkill.Social => CoreStat.Charm,
        LifeSkill.Mining => CoreStat.Strength,
        LifeSkill.Farming => CoreStat.Perception,
        LifeSkill.Husbandry => CoreStat.Charm,
        LifeSkill.Craft => CoreStat.Dexterity,
        LifeSkill.Research => CoreStat.Intellect,
        LifeSkill.Smithing => CoreStat.Strength,
        _ => CoreStat.Dexterity,
    };

    public const int CoreCount = 7;
    public const int LifeCount = 8;
}
