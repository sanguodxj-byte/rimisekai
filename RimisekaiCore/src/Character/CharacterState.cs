using System;
using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Housing;

namespace Rimisekai.Character;

/// <summary>
/// 角色。核心六项决定生活有效值和战斗面板。
/// 生活经验单独累计；战斗数值不存，现算。威胁等级单独存。
/// </summary>
public sealed class CharacterState
{
    public const int HpPerConstitution = 10;
    public const int BaseHp = 20;
    public const int HpPerLevel = 5;
    public const int BaseMp = 10;
    public const int MpPerInt = 5;
    public const int MpPerLevel = 3;
    public const int LifeCoreShare = 50;
    public const int LifeLevelShare = 200;
    public const int WeaponLevelShare = 200;
    public const int StyleLevelShare = 500;
    public const int StyleCoreShare = 100;
    public const int CoreExpPerPoint = 100;

    public int Id { get; }
    public string Name { get; set; } = "";
    public bool IsMaster { get; set; }
    public int FactionId { get; set; }
    public int EmploymentDays { get; set; }
    public int Threat { get; set; }
    public Vitals Condition { get; } = new();
    public Relations Relations { get; } = new();
    public Affect Affect { get; } = new();

    /// <summary>
    /// 身上的背包。物品要么在这里，要么在地图某件设施的 <see cref="Housing.Facility.Contents"/> 里，
    /// 没有“领地虚空库存”这回事。采集进包、交易买卖、送礼都走这里。
    /// </summary>
    public Stock Bag { get; } = new();

    /// <summary>说过哪些口上/地文。随存档走。</summary>
    public Voice.VoiceMemory Voice { get; } = new();

    public int[] Core { get; } = new int[AttributeMap.CoreCount];
    public int[] CoreExp { get; } = new int[AttributeMap.CoreCount];
    public int LevelExp { get; private set; }
    public int Level { get; private set; } = 1;
    public int[] LifeExp { get; } = new int[AttributeMap.LifeCount];
    public Proficiency[] Weapons { get; } = NewProficiencies(Enum.GetValues<WeaponType>().Length);
    public Proficiency[] Styles { get; } = NewProficiencies(Enum.GetValues<StyleType>().Length);

    private static Proficiency[] NewProficiencies(int count)
    {
        var list = new Proficiency[count];
        for (var i = 0; i < count; i++)
            list[i] = new Proficiency();
        return list;
    }

    public Dictionary<int, int> Base { get; } = new();
    public Dictionary<int, int> MaxBase { get; } = new();
    public Dictionary<int, int> Flags { get; } = new();
    public HashSet<int> Talents { get; } = new();

    public CharacterState(int id) => Id = id;

    public int this[CoreStat stat]
    {
        get => Core[(int)stat];
        set => Core[(int)stat] = value;
    }

    public int Life(LifeSkill skill)
    {
        var core = this[AttributeMap.CoreOf(skill)];
        var exp = LifeExp[(int)skill];
        return core + exp / 100;
    }

    public void GainLifeExp(LifeSkill skill, int amount)
    {
        if (amount <= 0)
            return;
        var gained = amount * this.LearnPercent() / 100;
        LifeExp[(int)skill] += gained;
        AddCoreExp(AttributeMap.CoreOf(skill), gained * LifeCoreShare / 100);
        LevelExp += gained * LifeLevelShare / 100;
        CheckLevelUp();
    }

    public void GainWeaponExp(WeaponType type, int amount)
    {
        if (amount <= 0)
            return;
        Weapons[(int)type].AddExp(amount * WeaponLevelShare / 100);
        LevelExp += amount * WeaponLevelShare / 100;
        CheckLevelUp();
    }

    public void GainStyleExp(StyleType style, int amount)
    {
        if (amount <= 0)
            return;
        Styles[(int)style].AddExp(amount * StyleLevelShare / 100);
        AddCoreExp(StyleMap.CoreOf(style), amount * StyleCoreShare / 100);
        LevelExp += amount * StyleLevelShare / 100;
        CheckLevelUp();
    }

    /// <summary>升级：六项核心均匀 +1，蓝上限重算。</summary>
    private void CheckLevelUp()
    {
        var target = XpTable.LevelFor(LevelExp);
        while (Level < target)
        {
            Level++;
            for (var i = 0; i < Core.Length; i++)
                Core[i]++;
            SyncMana();
        }
    }

    public int MaxMana => BaseMp + this[CoreStat.Intellect] * MpPerInt + Level * MpPerLevel;

    public void SyncMana() => Condition.SetMaxMana(MaxMana);

    public int WeaponHit(WeaponType type, int baseHit) =>
        baseHit * Weapons[(int)type].HitMultiplier(1) / 100;

    public int WeaponDamage(WeaponType type, int baseDamage) =>
        baseDamage * Weapons[(int)type].DamageMultiplier(1) / 100;

    public WeaponType? MainWeapon { get; private set; }
    public WeaponType? OffWeapon { get; private set; }
    public bool OffHandShield { get; private set; }

    /// <summary>风格由装备配置推导，不允许只装副手。空手时无风格。</summary>
    public StyleType? EquippedStyle => DeriveStyle(MainWeapon, OffWeapon, OffHandShield);

    public static StyleType? DeriveStyle(WeaponType? main, WeaponType? off, bool offShield)
    {
        if (main == null)
            return null;
        if (offShield)
            return StyleType.Shield;
        if (off == null)
        {
            return main switch
            {
                WeaponType.Bow or WeaponType.Crossbow => StyleType.Ranged,
                WeaponType.Staff => StyleType.Spell,
                WeaponType.Unarmed => StyleType.Unarmed,
                _ => StyleType.OneHand,
            };
        }
        return off == main ? StyleType.TwoHand : StyleType.DualWield;
    }

    public bool Equip(WeaponType? main, WeaponType? off = null, bool offShield = false)
    {
        if (main == null && (off != null || offShield))
            return false;
        MainWeapon = main;
        OffWeapon = off;
        OffHandShield = offShield;
        return true;
    }

    public int EquippedHit(int baseHit) =>
        MainWeapon == null ? baseHit : WeaponHit(MainWeapon.Value, baseHit);

    public int EquippedDamage(int baseDamage) =>
        MainWeapon == null ? baseDamage : WeaponDamage(MainWeapon.Value, baseDamage);

    /// <summary>
    /// 攻击结算。加数 = 武器面板 + 5×熟练等级 + 2×流派主属性 + 1×流派副属性；
    /// 乘数 = 1 + 0.05×流派主属性 + 0.2×熟练等级 + 流派系数。空手按格斗算。
    /// </summary>
    public StrikeResult ResolveStrike(int weaponPanel) =>
        Resolve(weaponPanel, MainWeapon ?? WeaponType.Unarmed);

    public StrikeResult ResolveStrike(WeaponDef weapon) =>
        Resolve(weapon.Panel, weapon.Type);

    private StrikeResult Resolve(int weaponPanel, WeaponType proficiencyType)
    {
        var style = EquippedStyle ?? StyleType.Unarmed;
        var level = Weapons[(int)proficiencyType].Level;
        var main = this[StyleMap.CoreOf(style)];
        var secondary = this[StyleMap.SecondaryOf(style)];
        var addend = weaponPanel + 5 * level + 2 * main + secondary;
        var multiplier = 1 + 0.05 * main + 0.2 * level + StyleMap.FactorOf(style, this);
        return new StrikeResult(proficiencyType, style, level, addend, multiplier, addend * multiplier);
    }

    private void AddCoreExp(CoreStat stat, int amount)
    {
        var i = (int)stat;
        CoreExp[i] += amount;
        while (CoreExp[i] >= CoreExpPerPoint)
        {
            CoreExp[i] -= CoreExpPerPoint;
            Core[i]++;
        }
    }

    public void Restore(
        int[] core, int[] coreExp, int levelExp, int[] lifeExp,
        int[] weaponExp, int[] styleExp, IEnumerable<int> talents,
        int threat, int employment, int faction,
        WeaponType? main, WeaponType? off, bool offShield,
        Dictionary<int, List<RelationFlag>>? relations,
        Dictionary<int, int>? flags, Dictionary<int, int>? vitals)
    {
        CopyInto(Core, core);
        CopyInto(CoreExp, coreExp);
        LevelExp = levelExp;
        Level = XpTable.LevelFor(levelExp);
        CopyInto(LifeExp, lifeExp);
        for (var i = 0; i < Weapons.Length && i < weaponExp.Length; i++)
            Weapons[i].Restore(weaponExp[i]);
        for (var i = 0; i < Styles.Length && i < styleExp.Length; i++)
            Styles[i].Restore(styleExp[i]);
        Talents.Clear();
        foreach (var id in talents)
            Talents.Add(id);
        Threat = threat;
        EmploymentDays = employment;
        FactionId = faction;
        MainWeapon = main;
        OffWeapon = off;
        OffHandShield = offShield;
        if (relations != null)
            foreach (var pair in relations)
                Relations.Set(pair.Key, pair.Value);
        if (flags != null)
            foreach (var pair in flags)
                Flags[pair.Key] = pair.Value;
        if (vitals != null)
            Condition.Restore(
                vitals.GetValueOrDefault(0, Vitals.DefaultMax),
                vitals.GetValueOrDefault(1, Vitals.DefaultMax),
                vitals.GetValueOrDefault(2, Vitals.DefaultMax),
                vitals.GetValueOrDefault(3, Vitals.DefaultMax),
                vitals.GetValueOrDefault(4, 0),
                vitals.GetValueOrDefault(5, 0),
                vitals.GetValueOrDefault(6, 0),
                vitals.GetValueOrDefault(7, 10));
        SyncMana();
    }

    private static void CopyInto(int[] target, int[] source)
    {
        var n = System.Math.Min(target.Length, source.Length);
        for (var i = 0; i < n; i++)
            target[i] = source[i];
    }

    public CombatSheet Combat => new(
        Attack: this[CoreStat.Strength] + this[CoreStat.Dexterity] / 2,
        MaxHp: BaseHp + this[CoreStat.Constitution] * HpPerConstitution + Level * HpPerLevel,
        Defence: this[CoreStat.Constitution] + this[CoreStat.Strength] / 2,
        Dodge: this[CoreStat.Dexterity] + this[CoreStat.Perception] / 2,
        SpellPower: this[CoreStat.Intellect] + this[CoreStat.Perception] / 2,
        Threat: Threat);

    public int Get(Dictionary<int, int> slot, int key) =>
        slot.TryGetValue(key, out var v) ? v : 0;

    public void Set(Dictionary<int, int> slot, int key, int value) => slot[key] = value;

    public void RestoreBase()
    {
        foreach (var pair in MaxBase)
            Base[pair.Key] = pair.Value;
    }
}

public readonly record struct CombatSheet(
    int Attack, int MaxHp, int Defence, int Dodge, int SpellPower, int Threat);

public readonly record struct StrikeResult(
    WeaponType Weapon, StyleType Style, int WeaponLevel,
    double Addend, double Multiplier, double Total)
{
    public int Rounded => (int)System.Math.Round(Total);
}

public sealed class Roster
{
    private int _nextId = 1;
    public List<CharacterState> Members { get; } = new();

    public CharacterState? Master => Members.Find(c => c.IsMaster);

    public CharacterState Add(string name, bool master = false)
    {
        var c = new CharacterState(_nextId++) { Name = name, IsMaster = master };
        Members.Add(c);
        return c;
    }

    public CharacterState? Find(int id) => Members.Find(c => c.Id == id);

    public void Attach(CharacterState character)
    {
        Members.Add(character);
        if (character.Id >= _nextId)
            _nextId = character.Id + 1;
    }
}
