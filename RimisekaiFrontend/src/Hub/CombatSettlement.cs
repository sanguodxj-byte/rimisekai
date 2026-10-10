using System.Collections.Generic;
using System.Linq;
using Rimisekai.Combat;
using Rimisekai.Quest;
using Rimisekai.Save;

namespace Rimisekai.Hub;

/// <summary>
/// 战后结算入口：把一场打完的战斗落成账——
/// 汇总本方战绩单与敌方掉落，回写武器/流派经验与心情，金钱进账、缴获入袋，
/// 胜利时登记任务通关与冷却。
///
/// 与 <see cref="Encounters.Start"/> 首尾对称：一个起战、一个收战。
/// 画面侧只拿返回值去渲染结算弹窗，账目不在 UI 里算。
/// </summary>
public static class CombatSettlement
{
    /// <summary>一次结算的战果：供 UI 直接铺成结算弹窗。</summary>
    public sealed class Outcome
    {
        public BattleResult Result { get; init; } = null!;
        public LootResult Loot { get; init; } = null!;

        /// <summary>是否打赢（决定任务是否记通关）。</summary>
        public bool Won { get; init; }
    }

    /// <summary>
    /// 结算并落账。战斗仍在进行中（未分胜负）时返回 null——那是中途撤离，不该出结算单。
    /// <paramref name="questRun"/> 为本场对应的任务运行时，传 null 表示测试战斗。
    /// </summary>
    public static Outcome? Settle(GameState state, Battle battle, QuestRun? questRun = null)
    {
        if (battle.Outcome == CombatOutcome.Ongoing)
            return null;

        // 1. 汇总本方战绩单与敌方掉落（只从倒下的敌人身上掷）。
        var result = BattleRewards.Summary(battle, CombatSide.Attacker);
        var loot = BattleLoot.Roll(battle, CombatSide.Defender);

        // 2. 落账：名册回写武器/流派经验与心情，金钱进账，缴获进产出者背包（无虚空库存）。
        // 熟练涨了，门槛够了的技能自动激活：落账前后各记一次已激活的技能，多出来的记进结算单。
        var before = result.Rows.ToDictionary(r => r.CharacterId,
            r => state.Roster.Find(r.CharacterId) is { } who ? SkillTable.Known(who).Select(s => s.Id).ToHashSet() : new HashSet<string>());
        BattleRewards.Apply(battle, state.Roster, CombatSide.Attacker);
        // 结算单记下落账后的熟练累计，界面据此画进度条、判升级。
        foreach (var row in result.Rows)
            if (state.Roster.Find(row.CharacterId) is { } c)
            {
                row.WeaponTotalExp = c.Weapons[(int)row.Weapon].Exp;
                row.StyleTotalExp = c.Styles[(int)row.Style].Exp;
                if (SkillTable.Known(c).FirstOrDefault(s => !before[row.CharacterId].Contains(s.Id)) is { } skill)
                    row.NewSkill = skill.Id;
            }
        state.Money += loot.Money;
        var carrier = state.Roster.Master;
        if (carrier != null)
        {
            foreach (var (itemId, count) in loot.Items)
                state.Territory.Produce(carrier, itemId, count);
            // 战斗里喝掉的药剂从背包里扣。
            foreach (var (itemId, count) in battle.Consumed)
                carrier.Bag.Add(itemId, -count);
        }

        // 3. 任务结算：胜则通关（记通关次数、起冷却、凯旋回心情），发酬金与物品奖励。
        var won = battle.Outcome == CombatOutcome.AttackerWin;
        if (won && questRun != null)
        {
            state.Quests.Complete(questRun, state.Roster);
            state.Money += questRun.Def.RewardMoney;
            if (carrier != null)
                foreach (var item in questRun.Def.RewardItems)
                    state.Territory.Produce(carrier, item.ItemId, item.Count);
        }

        return new Outcome { Result = result, Loot = loot, Won = won };
    }
}
