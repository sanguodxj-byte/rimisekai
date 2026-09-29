using System;

namespace Rimisekai.Character;

public enum WeaponType
{
    Sword,
    Axe,
    Spear,
    Bow,
    Staff,
    Dagger,
    Crossbow,
    Unarmed,
}

public enum StyleType
{
    OneHand,
    TwoHand,
    DualWield,
    Ranged,
    Spell,
    Shield,
    Unarmed,
}

public static class StyleMap
{
    public static CoreStat CoreOf(StyleType style) => style switch
    {
        StyleType.OneHand => CoreStat.Dexterity,
        StyleType.TwoHand => CoreStat.Strength,
        StyleType.DualWield => CoreStat.Dexterity,
        StyleType.Ranged => CoreStat.Perception,
        StyleType.Spell => CoreStat.Intellect,
        StyleType.Shield => CoreStat.Constitution,
        StyleType.Unarmed => CoreStat.Strength,
        _ => CoreStat.Dexterity,
    };

    public static CoreStat SecondaryOf(StyleType style) => style switch
    {
        StyleType.OneHand => CoreStat.Strength,
        StyleType.TwoHand => CoreStat.Constitution,
        StyleType.DualWield => CoreStat.Perception,
        StyleType.Ranged => CoreStat.Dexterity,
        StyleType.Spell => CoreStat.Perception,
        StyleType.Shield => CoreStat.Strength,
        StyleType.Unarmed => CoreStat.Constitution,
        _ => CoreStat.Strength,
    };

    /// <summary>流派系数。持盾、格斗按公式现算。</summary>
    public static double FactorOf(StyleType style, CharacterState character) => style switch
    {
        StyleType.OneHand => 1.0,
        StyleType.TwoHand => 1.5,
        StyleType.DualWield => 0.6,
        StyleType.Ranged => 0.8,
        StyleType.Spell => 0.2,
        StyleType.Shield => 0.6 + 0.1 * character[CoreStat.Constitution],
        StyleType.Unarmed => 0.5
            + 0.05 * character[CoreStat.Strength]
            + 0.05 * character[CoreStat.Constitution]
            + 0.05 * character[CoreStat.Dexterity],
        _ => 1.0,
    };
}

/// <summary>熟练度等级。经验只涨等级，命中和伤害乘数在结算最后乘。</summary>
public sealed class Proficiency
{
    public const int ExpPerLevel = 100;

    public int Exp { get; private set; }
    public int Level => Exp / ExpPerLevel;

    public void AddExp(int amount)
    {
        if (amount > 0)
            Exp += amount;
    }

    public void Restore(int exp) => Exp = exp < 0 ? 0 : exp;

    public int HitMultiplier(int percentPerLevel) => 100 + Level * percentPerLevel;
    public int DamageMultiplier(int percentPerLevel) => 100 + Level * percentPerLevel;
}
