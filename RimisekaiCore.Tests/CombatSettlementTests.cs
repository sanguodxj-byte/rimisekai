using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Hub;
using Rimisekai.Quest;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

public sealed class CombatSettlementTests
{
    [Fact]
    public void Settle_returns_null_when_combat_is_ongoing()
    {
        var state = new GameState();
        var battle = new Battle(controlled: CombatSide.Attacker);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 20, MaxHp = 20 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 20, MaxHp = 20 });
        battle.StartBattle();

        var outcome = CombatSettlement.Settle(state, battle);
        Assert.Null(outcome);
    }

    [Fact]
    public void Settle_on_victory_credits_money_bag_items_and_completes_quest()
    {
        var state = new GameState();
        var master = state.Roster.Add("Master", master: true);
        master.Equip(WeaponType.Sword, null, true);
        var initialMood = master.Affect.Mood;

        var qDef = new QuestDef
        {
            Id = 10,
            Name = "讨伐野怪",
            CooldownDays = 3,
        };
        var qRun = state.Quests.Start(qDef, new[] { master.Id })!;

        master[CoreStat.Strength] = 30;
        master.Condition.RecoverFull();

        var battle = new Battle(d100: () => 0, controlled: CombatSide.Attacker);
        var hero = Deploy.FromCharacter(master, CombatSide.Attacker);
        var foe = new Combatant
        {
            Id = 99,
            Side = CombatSide.Defender,
            Hp = 2,
            MaxHp = 10,
            MoneyReward = 150,
            Loot = { new EnemyLoot { ItemId = "草药", Min = 2, Max = 2, RatePercent = 100 } },
        };

        battle.Add(hero);
        battle.Add(foe);
        battle.StartBattle();

        // 玩家一击斩杀敌人，触发自然胜负判定
        Assert.True(battle.Act(new CombatAction { ActorId = hero.Id, TargetId = foe.Id }));
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);

        var outcome = CombatSettlement.Settle(state, battle, qRun);
        Assert.NotNull(outcome);
        Assert.True(outcome.Won);
        Assert.Equal(CombatOutcome.AttackerWin, outcome.Result.Outcome);

        // 1. 金钱进账
        Assert.Equal(150, state.Money);
        Assert.Equal(150, outcome.Loot.Money);

        // 2. 缴获进主角背包（无虚空库存）
        Assert.Equal(2, master.Bag.Get("草药"));

        // 3. 经验回写
        Assert.True(master.Weapons[(int)WeaponType.Sword].Exp > 0);

        // 4. 任务通关：清算次数 + 1，冷却倒计时生效，心情凯旋 +8
        Assert.Equal(1, state.Quests.ClearCount.GetValueOrDefault(10));
        Assert.Equal(3, state.Quests.CooldownRemaining.GetValueOrDefault(10));
        Assert.True(master.Affect.Mood > initialMood);
    }

    [Fact]
    public void Settle_on_defeat_does_not_complete_quest()
    {
        var state = new GameState();
        var master = state.Roster.Add("Master", master: true);
        var qDef = new QuestDef { Id = 12, Name = "失败测试委托", CooldownDays = 2 };
        var qRun = state.Quests.Start(qDef, new[] { master.Id })!;

        var battle = new Battle(d100: () => 0, controlled: CombatSide.Attacker);
        var hero = new Combatant { Id = master.Id, Side = CombatSide.Attacker, Hp = 5, MaxHp = 10, Speed = 5 };
        var foe = new Combatant { Id = 88, Side = CombatSide.Defender, Hp = 50, MaxHp = 50, StrikePower = 30, Speed = 20 };
        battle.Add(hero);
        battle.Add(foe);
        battle.StartBattle();

        // 敌方先动：PendingActor 推进到敌方行动并由内置 AI 自动攻击击杀玩家
        _ = battle.PendingActor;
        Assert.Equal(CombatOutcome.DefenderWin, battle.Outcome);

        var outcome = CombatSettlement.Settle(state, battle, qRun);
        Assert.NotNull(outcome);
        Assert.False(outcome.Won);
        Assert.Equal(CombatOutcome.DefenderWin, outcome.Result.Outcome);

        // 败北不记通关，不起冷却
        Assert.Equal(0, state.Quests.ClearCount.GetValueOrDefault(12));
        Assert.Equal(0, state.Quests.CooldownRemaining.GetValueOrDefault(12));
    }
}
