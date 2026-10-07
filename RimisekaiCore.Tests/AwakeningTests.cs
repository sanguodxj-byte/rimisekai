using System;
using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Combat;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 觉醒机制单元测试：
/// 1. 双方受到伤害根据其最大生命百分比累计觉醒槽（满 100 满值）；
/// 2. 满值后可点击触发觉醒（TriggerAwakening），清空槽并激活下一次我方行动效果翻倍；
/// 3. 我方下一次攻击/法术/治疗效果值 2 倍翻倍，出手后自动消耗；
/// 4. 敌方行动不消耗我方觉醒加成。
/// </summary>
public class AwakeningTests
{
    [Fact]
    public void Damage_accumulates_awakening_gauge_proportionally()
    {
        var battle = new Battle(d100: () => 0);
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 100, MaxHp = 100, StrikePower = 20, Speed = 20 };
        var foe = new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 50, MaxHp = 50, StrikePower = 10, Speed = 10 };

        battle.Add(hero);
        battle.Add(foe);
        battle.StartBattle();

        Assert.Equal(0, battle.AwakeningGauge);
        Assert.False(battle.IsAwakeningReady);

        // 主角攻击敌人：造成伤害，敌人 50 血扣血累加觉醒槽
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.True(battle.AwakeningGauge > 0);
        var expectedGauge = (int)Math.Round((50f - battle.Find(2)!.Hp) / 50f * 100f);
        Assert.Equal(expectedGauge, battle.AwakeningGauge);
    }

    [Fact]
    public void Full_gauge_triggers_awakening_and_doubles_next_action_effect()
    {
        var battle = new Battle(d100: () => 0);
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 200, MaxHp = 200, StrikePower = 20, Speed = 20 };
        var foe = new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 500, MaxHp = 500, StrikePower = 5, Speed = 10 };

        battle.Add(hero);
        battle.Add(foe);
        battle.StartBattle();

        // 强行注入未满槽：触发失败
        battle.AwakeningGauge = 80;
        Assert.False(battle.IsAwakeningReady);
        Assert.False(battle.TriggerAwakening());
        Assert.False(battle.AwakeningActive);

        // 强行注满 100：触发成功，清空槽量并激活翻倍
        battle.AwakeningGauge = 100;
        Assert.True(battle.IsAwakeningReady);
        Assert.True(battle.TriggerAwakening());
        Assert.Equal(0, battle.AwakeningGauge);
        Assert.True(battle.AwakeningActive);

        // 记录普通出手预期伤害
        var baseDmg = 20; // StrikePower 20 * Power 100% = 20
        var foeHpBefore = foe.Hp;

        // 觉醒状态下出手：伤害翻倍为 40！
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        var dmgDealt = foeHpBefore - foe.Hp;
        Assert.Equal(baseDmg * 2, dmgDealt);

        // 出手后觉醒加成自动消耗重置
        Assert.False(battle.AwakeningActive);

        // 下一次出手恢复正常伤害（未翻倍）
        var foeHpBefore2 = foe.Hp;
        battle.Find(1)!.NextActAt = battle.Time; // 再次轮到主角
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        var normalDmg = foeHpBefore2 - foe.Hp;
        Assert.Equal(baseDmg, normalDmg);
    }

    [Fact]
    public void Enemy_turn_does_not_consume_player_awakening()
    {
        var battle = new Battle(d100: () => 0, controlled: CombatSide.Attacker);
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 200, MaxHp = 200, StrikePower = 20, Speed = 10 };
        var foe = new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 500, MaxHp = 500, StrikePower = 5, Speed = 30 };

        battle.Add(hero);
        battle.Add(foe);
        battle.StartBattle();

        // 玩家激活觉醒
        battle.AwakeningGauge = 100;
        Assert.True(battle.TriggerAwakening());
        Assert.True(battle.AwakeningActive);

        // 敌人速度快先手，由 AI 代打出手攻击玩家
        var pending = battle.PendingActor;
        Assert.NotNull(pending);
        Assert.Equal(1, pending.Id); // 轮到玩家

        // 敌方代打期间绝不消耗我方的觉醒加成！
        Assert.True(battle.AwakeningActive);

        // 轮到我方出手才消耗并翻倍
        var foeHpBefore = foe.Hp;
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.Equal(40, foeHpBefore - foe.Hp);
        Assert.False(battle.AwakeningActive);
    }
}
