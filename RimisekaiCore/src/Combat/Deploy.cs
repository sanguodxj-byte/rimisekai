using System;
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
        return new Combatant
        {
            Id = c.Id,
            Name = c.Name,
            Side = side,
            Hp = sheet.MaxHp,
            MaxHp = sheet.MaxHp,
            Mp = c.Condition.Mana,
            MaxMp = c.Condition.MaxMana,
            Attack = sheet.Attack,
            Defence = sheet.Defence,
            Dodge = sheet.Dodge,
            SpellPower = sheet.SpellPower,
            Threat = sheet.Threat,
            CritRate = Math.Min(
                BattleRules.CritRateCap,
                BattleRules.BaseCritRate + c[CoreStat.Perception] / 2),
            Weapon = strike.Weapon,
            Style = strike.Style,
            StrikePower = strike.Rounded,
            BaseHit = c.EquippedHit(BattleRules.BaseHit),
        };
    }

    /// <summary>敌人按目录行直接成军，面板行数值即战斗数值。同行敌人用不同 id 区分。</summary>
    public static Combatant FromEnemy(EnemyDef def, int id, CombatSide side)
    {
        var c = new Combatant
        {
            Id = id,
            Name = def.Name,
            Side = side,
            Hp = def.MaxHp,
            MaxHp = def.MaxHp,
            Attack = def.Attack,
            Defence = def.Defence,
            Dodge = def.Dodge,
            SpellPower = def.SpellPower,
            Threat = def.Threat,
            Armour = def.Armour,
            Targeting = def.Targeting,
            MoneyReward = def.Money,
            StrikePower = def.Attack,
        };
        c.Skills.AddRange(def.Skills);
        c.Loot.AddRange(def.Loot);
        return c;
    }
}
