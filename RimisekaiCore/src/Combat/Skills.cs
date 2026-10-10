using System;
using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Combat;

/// <summary>
/// 基础两式：普通攻击与防御架势的常量标识与查询门面。
/// 技能数据严格由 content/defs/skills.json 驱动。
/// </summary>
public static class BattleSkills
{
    public const string AttackId = "attack";
    public const string GuardId = "guard";

    public static SkillDef Attack => SkillTable.Get(AttackId)
        ?? throw new KeyNotFoundException($"未在 skills.json 中找到基础技能 {AttackId}");

    public static SkillDef Guard => SkillTable.Get(GuardId)
        ?? throw new KeyNotFoundException($"未在 skills.json 中找到基础技能 {GuardId}");
}

/// <summary>一场战斗的结算单：结果、回合数、本方各人的收益。</summary>
public sealed class BattleResult
{
    public CombatOutcome Outcome { get; init; }
    public int Rounds { get; init; }
    public List<Row> Rows { get; } = new();

    public sealed class Row
    {
        public int CharacterId { get; init; }
        public string Name { get; init; } = "";
        public bool Survived { get; init; }
        public int DamageDealt { get; init; }
        public int Kills { get; init; }
        public int WeaponExp { get; init; }
        public int StyleExp { get; init; }
        public int Mood { get; init; }
        public int Level { get; init; } = 1;
        public WeaponType Weapon { get; init; } = WeaponType.Sword;
        public int WeaponLevel { get; init; } = 1;
        public StyleType Style { get; init; } = StyleType.OneHand;
        public int StyleLevel { get; init; } = 1;
        /// <summary>落账后该武器熟练的累计经验（每 <see cref="Proficiency.ExpPerLevel"/> 一级）；结算回写名册时填。</summary>
        public int WeaponTotalExp { get; set; }
        /// <summary>落账后该流派熟练的累计经验。</summary>
        public int StyleTotalExp { get; set; }
    }
}

/// <summary>
/// 战斗结算与奖励回写。数值集中在常量里，均为提案值，调参先过主人。
/// 经验走角色既有的分成管道（武器 200% 进熟练与等级、流派 500%），此处只给基数。
/// </summary>
public static class BattleRewards
{
    /// <summary>每打满这么多点伤害记 1 点武器经验。</summary>
    public const int DamagePerWeaponExp = 20;
    /// <summary>击坠一名敌人额外武器经验。</summary>
    public const int KillWeaponExp = 10;
    /// <summary>获胜时每名生还者的固定武器经验。</summary>
    public const int VictoryWeaponExp = 20;
    /// <summary>获胜时每名生还者的固定流派经验（武器的一半）。</summary>
    public const int VictoryStyleExp = 10;
    /// <summary>凯旋回心情，与 Quest.Complete 的凯旋一致。</summary>
    public const int VictoryMood = 8;

    /// <summary>算一方的结算单，不落账。</summary>
    public static BattleResult Summary(Battle battle, CombatSide side)
    {
        var result = new BattleResult { Outcome = battle.Outcome, Rounds = battle.Round };
        var won = battle.Outcome == WonOutcome(side);
        foreach (var m in battle.Members)
        {
            if (m.Side != side)
                continue;
            var weaponExp = m.DamageDealt / DamagePerWeaponExp + m.Kills * KillWeaponExp;
            var styleExp = weaponExp / 2;
            if (won && m.Alive)
            {
                weaponExp += VictoryWeaponExp;
                styleExp += VictoryStyleExp;
            }
            result.Rows.Add(new BattleResult.Row
            {
                CharacterId = m.Id,
                Name = m.Name,
                Survived = m.Alive,
                DamageDealt = m.DamageDealt,
                Kills = m.Kills,
                WeaponExp = weaponExp,
                StyleExp = styleExp,
                Mood = won && m.Alive ? VictoryMood : 0,
                Weapon = m.Weapon,
                WeaponLevel = m.WeaponLevel,
                Style = m.Style,
                StyleLevel = m.StyleLevel,
                Level = m.Level,
            });
        }
        return result;
    }

    /// <summary>把结算单落到名册上。只在名册里能找到的角色身上回写。</summary>
    public static void Apply(Battle battle, Roster roster, CombatSide side)
    {
        var won = battle.Outcome == WonOutcome(side);
        foreach (var m in battle.Members)
        {
            if (m.Side != side)
                continue;
            var character = roster.Find(m.Id);
            if (character == null)
                continue;
            var weaponExp = m.DamageDealt / DamagePerWeaponExp + m.Kills * KillWeaponExp;
            if (weaponExp > 0)
                character.GainWeaponExp(m.Weapon, weaponExp);
            var styleExp = weaponExp / 2;
            if (styleExp > 0)
                character.GainStyleExp(m.Style, styleExp);
            if (won && m.Alive)
            {
                character.GainWeaponExp(m.Weapon, VictoryWeaponExp);
                character.GainStyleExp(m.Style, VictoryStyleExp);
                character.Affect.AddMood(VictoryMood);
            }
        }
    }

    private static CombatOutcome WonOutcome(CombatSide side) =>
        side == CombatSide.Attacker ? CombatOutcome.AttackerWin : CombatOutcome.DefenderWin;
}

/// <summary>一方阵营的掉落汇总：金钱进账与物品清单，落袋由调用方决定。</summary>
public sealed class LootResult
{
    public long Money { get; set; }
    public List<(string ItemId, int Count)> Items { get; } = new();
}

public static class BattleLoot
{
    /// <summary>
    /// 掷一方的掉落：只从倒下的成员身上掷，金钱全额入账，
    /// 掉落逐行按百分比判定、按数量区间取值。未倒下者（如逃散的敌人）不掉落。
    /// </summary>
    public static LootResult Roll(Battle battle, CombatSide side, Func<int>? d100 = null)
    {
        var roll = d100 ?? (() => System.Random.Shared.Next(100));
        var result = new LootResult();
        foreach (var m in battle.Members)
        {
            if (m.Side != side || m.Alive)
                continue;
            result.Money += m.MoneyReward;
            foreach (var row in m.Loot)
            {
                if (roll() >= row.RatePercent)
                    continue;
                var span = row.Max - row.Min + 1;
                var count = row.Min + (span > 1 ? roll() % span : 0);
                if (count > 0)
                    result.Items.Add((row.ItemId, count));
            }
        }
        return result;
    }
}
