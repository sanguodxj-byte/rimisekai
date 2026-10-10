using System;
using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Combat;

/// <summary>把角色与敌人目录行快照成参战者。战斗数值不回写，开打前定格。</summary>
public static class Deploy
{
    /// <summary>
    /// 从角色快照参战者。武器面板按武器种类查目录（查不到按空手面板 0），
    /// 出手总量与基础命中走角色既有的 ResolveStrike/EquippedHit，蓝量取当前值。
    /// 血量按满值进战斗：战斗血与体力气力是两套账。
    /// 出手总量与法力按 <see cref="BattleRules.PowerPercent"/> 折算进战斗。
    /// </summary>
    public static Combatant FromCharacter(CharacterState c, CombatSide side, GameCatalog? catalog = null)
    {
        var sheet = c.Combat;
        WeaponDef? weapon = null;
        if (catalog != null && c.MainWeapon != null)
        {
            foreach (var w in catalog.Weapons.Values)
            {
                if (w.Type == c.MainWeapon)
                {
                    weapon = w;
                    break;
                }
            }
        }
        var strike = weapon != null ? c.ResolveStrike(weapon) : c.ResolveStrike(0);

        // 能力表：普通攻击与防御架势玩家侧角色自带（敌人没有防御动作）；
        // 流派能力按门槛解锁（流派＋熟练，必要时还有属性／生活技能／素质／前置）——
        // 换武器就换一套能力。
        var skills = new List<string> { BattleSkills.AttackId, BattleSkills.GuardId };
        foreach (var known in SkillTable.Known(c))
            if (!skills.Contains(known.Id))
                skills.Add(known.Id);

        var isTired = c.Condition.Tired;
        var tiredMult = isTired ? 0.5 : 1.0;

        // 体力就是生命值，1:1 无比例换算：生命值高了体力一样也多
        var curHp = Math.Clamp(c.Condition.Stamina, 1, sheet.MaxHp);

        var c2 = new Combatant
        {
            Id = c.Id,
            Name = c.Name,
            Side = side,
            IsPlayer = c.IsMaster,
            Hp = curHp,
            MaxHp = sheet.MaxHp,
            Attack = Math.Max(1, (int)Math.Round(sheet.Attack * tiredMult)),
            Defence = (int)Math.Round(sheet.Defence * tiredMult),
            Dodge = (int)Math.Round(sheet.Dodge * tiredMult),
            SpellPower = Math.Max(1, (int)Math.Round(sheet.SpellPower * tiredMult * BattleRules.PowerPercent / 100.0)),
            CritRate = Math.Min(
                BattleRules.CritRateCap,
                BattleRules.BaseCritRate + c[CoreStat.Perception] / 2),
            Speed = Math.Max(1, c[CoreStat.Speed]),
            ThreatTier = Math.Min(5, Math.Max(1, c.ThreatTier)),
            QuickChant = c.QuickChant(),
            Level = c.Level,
            Weapon = strike.Weapon,
            WeaponLevel = Math.Max(1, c.Weapons[(int)strike.Weapon].Level),
            Style = strike.Style,
            StyleLevel = Math.Max(1, c.Styles[(int)strike.Style].Level),
            StrikePower = Math.Max(1, (int)Math.Round(strike.Rounded * tiredMult * BattleRules.PowerPercent / 100.0)),
            BaseHit = c.EquippedHit(BattleRules.BaseHit),
        };
        foreach (var id in skills)
            if (!c2.Skills.Contains(id))
                c2.Skills.Add(id);
        return c2;
    }

    /// <summary>
    /// 敌人成军：怪物视为没有生活技能的角色——按目录行的武器、天资与属性/经验池交给角色生成器掷出身板，
    /// 再走与角色同一条 <see cref="FromCharacter"/> 快照（生命、出手、防御、闪避、法力、速度、流派能力同一套公式）。
    /// 目录行只管站位（威胁层、列、占格）、护甲、行动点、立绘、赏金、掉落与额外技能。
    /// 同行敌人按 <paramref name="id"/> 区分，个体之间有差异；同一 id 掷出的身板固定。
    /// 敌人没有防御架势。
    /// </summary>
    public static Combatant FromEnemy(EnemyDef def, int id, CombatSide side)
    {
        var rng = new Random(unchecked(StableHash(def.Id) * 31 + id));
        var body = new CharacterGenerator(rng).RollMonster(id, def.Name, def.Weapon, def.Primary, def.Secondary,
            def.CorePool, def.ExpPool);
        var sheet = FromCharacter(body, side);
        var c = new Combatant
        {
            Id = id,
            Name = def.Name,
            Side = side,
            IsPlayer = false,
            Hp = sheet.MaxHp,
            MaxHp = sheet.MaxHp,
            Attack = sheet.Attack,
            Defence = sheet.Defence,
            Dodge = sheet.Dodge,
            SpellPower = sheet.SpellPower,
            CritRate = sheet.CritRate,
            Speed = sheet.Speed,
            QuickChant = sheet.QuickChant,
            Level = sheet.Level,
            Weapon = sheet.Weapon,
            WeaponLevel = sheet.WeaponLevel,
            Style = sheet.Style,
            StyleLevel = sheet.StyleLevel,
            StrikePower = sheet.StrikePower,
            BaseHit = sheet.BaseHit,
            Armour = def.Armour,
            ThreatTier = def.ThreatTier,
            Column = def.Column,
            Size = def.Size,
            ActionPoints = def.ActionPoints,
            Portrait = def.Portrait,
            MoneyReward = def.Money,
        };
        foreach (var skill in sheet.Skills)
            if (skill != BattleSkills.GuardId && !c.Skills.Contains(skill))
                c.Skills.Add(skill);
        foreach (var skill in def.Skills)
            if (!c.Skills.Contains(skill))
                c.Skills.Add(skill);
        c.Loot.AddRange(def.Loot);
        return c;
    }

    /// <summary>跨进程稳定的字符串散列（FNV-1a）：同一敌人行在不同次运行里掷出同一副身板。</summary>
    private static int StableHash(string text)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var ch in text)
                h = (h ^ ch) * 16777619;
            return h;
        }
    }
}
