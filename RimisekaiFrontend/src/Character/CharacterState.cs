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

    /// <summary>角色的身份标签（如女仆、圣骑士、学者等）。空串时由特质/默认规则推导。</summary>
    public string Identity { get; set; } = "";

    /// <summary>当前选定的立绘/头像差分序号（1 起步，默认 1）。</summary>
    public int PortraitDiff { get; set; } = 1;

    /// <summary>
    /// 威胁等级 1-3：队伍排位，越高越靠前。纯只读推导，由当前手持武器推导的流派决定，不存角色数据。
    /// </summary>
    public int ThreatTier => ThreatOf(EquippedStyle);

    /// <summary>流派 → 威胁等级：持盾 3，其他近战 2，远程与法术 1。</summary>
    public static int ThreatOf(StyleType? style) => style switch
    {
        StyleType.Shield => 3,
        StyleType.OneHand or StyleType.TwoHand or StyleType.DualWield or StyleType.Unarmed => 2,
        StyleType.Ranged or StyleType.Spell => 1,
        _ => 1,
    };
    public Vitals Condition { get; }
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

    /// <summary>技能池按哪个身份抽的（IdentitySkillPoolDef 的 defName）。NPC 即本身份；玩家可自选。</summary>
    public string PoolIdentity { get; set; } = "";
    /// <summary>抽到的身份技能 Id（见 <see cref="Combat.SkillPool"/>）。</summary>
    public List<string> SkillPool { get; } = new();

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

    public CharacterState(int id)
    {
        Id = id;
        Condition = new Vitals(() => Combat.MaxHp, () => Vitals.DefaultMax);
    }

    public int this[CoreStat stat]
    {
        get => Core[(int)stat];
        set => Core[(int)stat] = value;
    }

    /// <summary>生活等级 = 经验/100。对应核心属性是另一个影响值，不加进来（主人定）。</summary>
    public int Life(LifeSkill skill) => LifeExp[(int)skill] / 100;

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

    /// <summary>
    /// 升级：六项核心均匀 +1。体力上限由体质与等级即时推导（体力=生命值，
    /// 1:1 绝对实数映射，绝无比例换算），升级前满值则升级后保持满值。
    /// </summary>
    private void CheckLevelUp()
    {
        var target = XpTable.LevelFor(LevelExp);
        if (target <= Level)
            return;
        var wasFull = Condition.Stamina >= Condition.MaxStamina;
        while (Level < target)
        {
            Level++;
            for (var i = 0; i < Core.Length; i++)
                Core[i]++;
        }
        if (wasFull)
            Condition.RecoverFull();
    }

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

    // ---- 装备槽位 ----

    /// <summary>十格装备里各放着哪件实例的 Id。空串 = 空槽。</summary>
    private readonly string[] _equipped = new string[Rimisekai.Defs.EquipSlots.Count];

    /// <summary>某槽里装备的实例 Id；空槽返回空串。</summary>
    public string EquippedId(Rimisekai.Defs.EquipSlot slot) =>
        _equipped[(int)slot] ?? "";

    /// <summary>十格装备的实例 Id 快照（下标即槽位），供存档。</summary>
    public IReadOnlyList<string> EquippedIds() => _equipped;

    /// <summary>按存档恢复十格装备。长度不符或越界项忽略。</summary>
    public void RestoreEquipped(IReadOnlyList<string>? equipped)
    {
        if (equipped == null)
            return;
        for (var i = 0; i < _equipped.Length && i < equipped.Count; i++)
            _equipped[i] = equipped[i] ?? "";
    }

    /// <summary>把一件防具/饰品实例装进它该进的槽。槽位不匹配或已被占则拒绝。</summary>
    public bool EquipGear(Rimisekai.Defs.EquipInstance gear)
    {
        if (!Rimisekai.Defs.EquipSlots.Accepts(gear.Slot, gear.Kind))
            return false;
        if (!string.IsNullOrEmpty(_equipped[(int)gear.Slot]))
            return false;
        _equipped[(int)gear.Slot] = gear.Id;
        return true;
    }

    /// <summary>直接写某槽的实例 Id（武器实例也记在主副手槽）。合法性由 HubSession 换装入口判定。</summary>
    public void SetEquippedId(Rimisekai.Defs.EquipSlot slot, string id) =>
        _equipped[(int)slot] = id ?? "";

    /// <summary>卸下某槽的装备，返回它的实例 Id；空槽返回空串。</summary>
    public string UnequipGear(Rimisekai.Defs.EquipSlot slot)
    {
        var id = _equipped[(int)slot] ?? "";
        _equipped[(int)slot] = "";
        return id;
    }

    /// <summary>
    /// 装备提供的总防御。五件甲的防御之和，只认当前真的装在槽里的件。
    /// </summary>
    public int TotalDefence(Rimisekai.Defs.EquipRegistry registry)
    {
        var total = 0;
        foreach (var id in _equipped)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            var gear = registry.Get(id);
            if (gear != null)
                total += gear.Defence;
        }
        return total;
    }

    /// <summary>
    /// 饰品对某项战斗属性的总加成。两枚戒指与一条项链都算。
    /// </summary>
    public int StatBonus(Rimisekai.Defs.EquipRegistry registry, CoreStat stat)
    {
        var total = 0;
        foreach (var id in _equipped)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            var gear = registry.Get(id);
            if (gear != null && gear.BonusStat == stat)
                total += gear.BonusAmount;
        }
        return total;
    }

    /// <summary>已装备的饰品里加成了哪些属性（供界面列示）。</summary>
    public IReadOnlyList<(CoreStat Stat, int Amount)> StatBonuses(Rimisekai.Defs.EquipRegistry registry)
    {
        var totals = new Dictionary<CoreStat, int>();
        foreach (var id in _equipped)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            var gear = registry.Get(id);
            if (gear?.BonusStat is { } stat)
                totals[stat] = totals.GetValueOrDefault(stat) + gear.BonusAmount;
        }

        var list = new List<(CoreStat, int)>();
        foreach (var pair in totals)
            list.Add((pair.Key, pair.Value));
        return list;
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
        int employment, int faction,
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
        {
            Condition.Restore(
                vitals.GetValueOrDefault(0, Vitals.DefaultMax),
                vitals.GetValueOrDefault(1, Vitals.DefaultMax),
                vitals.GetValueOrDefault(2, 0));
        }
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
        SpellPower: this[CoreStat.Intellect] + this[CoreStat.Perception] / 2);

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
    int Attack, int MaxHp, int Defence, int Dodge, int SpellPower);

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
        if (master) c.Identity = "圣骑士";
        else if (name == "璐米埃尔") c.Identity = "女仆";
        c.PortraitDiff = 1;
        Members.Add(c);
        return c;
    }

    /// <summary>
    /// 预约一个还没入册的角色 Id。定时事件提前掷演员时用：
    /// 人此刻不在名册里，但 Id 要先占住，免得以后入场时与别人撞号。
    /// </summary>
    public int ReserveId() => _nextId++;

    public CharacterState? Find(int id) => Members.Find(c => c.Id == id);
    public CharacterState? Find(string name) => Members.Find(c => c.Name == name);

    /// <summary>把人移出名册（来访者告辞这类）。找不到返回 false。</summary>
    public bool Remove(int id)
    {
        var c = Members.Find(m => m.Id == id);
        return c != null && Members.Remove(c);
    }

    public void Attach(CharacterState character)
    {
        Members.Add(character);
        if (character.Id >= _nextId)
            _nextId = character.Id + 1;
    }

    /// <summary>
    /// 访客名册：来过领地店里的外人。不是住户——不睡、不吃、不排班，不在 <see cref="Members"/> 里；
    /// 记在这里是为了下次还能是同一个人再来（随存档走）。邀请入伙成功就挪进 <see cref="Members"/>。
    /// </summary>
    public List<CharacterState> Visitors { get; } = new();

    public CharacterState? Visitor(int id) => Visitors.Find(c => c.Id == id);

    /// <summary>住户或访客，按 Id 找人（交谈对象两边都可能）。</summary>
    public CharacterState? Person(int id) => Find(id) ?? Visitor(id);

    /// <summary>记下一位访客（Id 已向名册预约过）。</summary>
    public void AttachVisitor(CharacterState visitor)
    {
        Visitors.Add(visitor);
        if (visitor.Id >= _nextId)
            _nextId = visitor.Id + 1;
    }

    /// <summary>访客入伙：从访客名册挪进住户名册。不是访客返回 false。</summary>
    public bool Admit(int visitorId)
    {
        var visitor = Visitor(visitorId);
        if (visitor == null)
            return false;
        Visitors.Remove(visitor);
        Members.Add(visitor);
        return true;
    }
}
