using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Hub;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 战斗后备位机制测试：
/// 1. 玩家方 4 首发 + 最多 2 后备；
/// 2. 场上有人退场（被击杀或 DoT 致死）时，后备位中威胁等级最高者顶上并接入时间轴；
/// 3. 团灭判定延迟到后备位全数耗尽后才成立；
/// 4. Encounters.Start 自动按威胁等级分配前 4 首发与后 2 后备。
/// </summary>
public class ReservesTests
{
    [Fact]
    public void Reserve_steps_in_immediately_when_starter_dies()
    {
        var battle = new Battle(d100: () => 0, controlled: CombatSide.Defender);

        // 4 名首发
        for (var i = 1; i <= 4; i++)
            battle.Add(new Combatant { Id = i, Name = $"首发{i}", Side = CombatSide.Attacker, Hp = 10, MaxHp = 10, ThreatTier = i, Speed = 10 });

        // 2 名后备：后备甲(威胁3)，后备乙(威胁1)
        battle.AddReserve(new Combatant { Id = 101, Name = "后备甲", Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, ThreatTier = 3, Speed = 10 });
        battle.AddReserve(new Combatant { Id = 102, Name = "后备乙", Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, ThreatTier = 1, Speed = 10 });

        Assert.Equal(4, battle.Members.Count);
        Assert.Equal(2, battle.Reserves.Count);

        // 敌人一击斩杀首发 1 (10血全部打空)
        var foe = new Combatant { Id = 99, Side = CombatSide.Defender, Hp = 100, MaxHp = 100, StrikePower = 20, Speed = 20 };
        battle.Add(foe);
        battle.StartBattle();

        // 敌人出手击杀首发 1
        Assert.True(battle.Act(new CombatAction { ActorId = 99, TargetId = 1 }));
        Assert.False(battle.Find(1)!.Alive);

        // 验证：后备甲（威胁 3 高于威胁 1）自动顶上进入 Members！后备位剩 1 人！
        Assert.Equal(1, battle.Reserves.Count);
        Assert.Equal(102, battle.Reserves[0].Id); // 剩下的是后备乙

        var sub = battle.Find(101);
        Assert.NotNull(sub);
        Assert.True(sub.Alive);
        // 验证：顶上者正确排入时间轴（NextActAt > 当前战斗时间）
        Assert.True(sub.NextActAt >= battle.Time);
        // 战斗继续进行，未判负
        Assert.Equal(CombatOutcome.Ongoing, battle.Outcome);
    }

    [Fact]
    public void Delayed_wipe_until_all_reserves_depleted()
    {
        var battle = new Battle(d100: () => 0, controlled: CombatSide.Defender);

        // 1 名首发 + 1 名后备
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 5, MaxHp = 5, Speed = 10 });
        battle.AddReserve(new Combatant { Id = 2, Side = CombatSide.Attacker, Hp = 5, MaxHp = 5, Speed = 10 });

        var foe = new Combatant { Id = 99, Side = CombatSide.Defender, Hp = 100, MaxHp = 100, StrikePower = 20, Speed = 20 };
        battle.Add(foe);
        battle.StartBattle();

        // 敌人击杀首发 1
        battle.Act(new CombatAction { ActorId = 99, TargetId = 1 });
        Assert.False(battle.Find(1)!.Alive);

        // 此时后备 2 顶上，战斗仍然是 Ongoing！
        Assert.Equal(CombatOutcome.Ongoing, battle.Outcome);
        Assert.True(battle.Find(2)!.Alive);

        // 敌人再次击杀后备 2（后备耗尽）
        battle.Act(new CombatAction { ActorId = 99, TargetId = 2 });
        Assert.False(battle.Find(2)!.Alive);

        // 此时全部后备耗尽，团灭判定成立！
        Assert.Equal(CombatOutcome.DefenderWin, battle.Outcome);
    }

    [Fact]
    public void Encounters_start_allocates_starters_and_reserves_by_threat_order()
    {
        var state = new GameState();
        var master = state.Roster.Add("Master", master: true);
        master.Equip(WeaponType.Sword, null, true); // 持盾 => ThreatTier 3

        var m1 = state.Roster.Add("队员1"); m1.Equip(WeaponType.Spear, null, true); // 持盾 => ThreatTier 3
        var m2 = state.Roster.Add("队员2"); m2.Equip(WeaponType.Sword); // 单手近战 => ThreatTier 2
        var m3 = state.Roster.Add("队员3"); m3.Equip(WeaponType.Axe); // 单手近战 => ThreatTier 2
        var m4 = state.Roster.Add("队员4"); m4.Equip(WeaponType.Bow); // 远程 => ThreatTier 1
        var m5 = state.Roster.Add("队员5"); m5.Equip(WeaponType.Crossbow); // 远程 => ThreatTier 1

        var goblins = new List<EnemyDef> { new() { Id = "g1", Name = "哥布林", MaxHp = 10, Attack = 2 } };

        var session = Encounters.Start(state, goblins);
        Assert.NotNull(session);
        var battle = session.Battle;

        // 场上进攻方首发 4 人：包含 Master(5), 队员1(4), 队员2(3), 队员3(2)
        var attackers = battle.Members.Where(m => m.Side == CombatSide.Attacker).ToList();
        Assert.Equal(4, attackers.Count);
        Assert.Equal(master.Id, attackers[0].Id);
        Assert.Equal(m1.Id, attackers[1].Id);
        Assert.Equal(m2.Id, attackers[2].Id);
        Assert.Equal(m3.Id, attackers[3].Id);

        // 后备位 2 人：队员4(1), 队员5(1)
        Assert.Equal(2, battle.Reserves.Count);
        Assert.Equal(m4.Id, battle.Reserves[0].Id);
        Assert.Equal(m5.Id, battle.Reserves[1].Id);
    }

    [Fact]
    public void Quest_party_only_deploys_the_listed_members()
    {
        var state = new GameState();
        var master = state.Roster.Add("Master", master: true);
        master.Equip(WeaponType.Sword, null, true);

        var companion = state.Roster.Add("随行"); companion.Equip(WeaponType.Spear, null, true);
        var leftHome = state.Roster.Add("留守"); leftHome.Equip(WeaponType.Sword);
        var alsoHome = state.Roster.Add("也留守"); alsoHome.Equip(WeaponType.Axe);

        var goblins = new List<EnemyDef> { new() { Id = "g1", Name = "哥布林", MaxHp = 10, Attack = 2 } };

        // 任务编成：只带「随行」一人（玩家本人由调用方一并列入）。
        var session = Encounters.Start(state, goblins, null, "迷宫地下城",
            new[] { companion.Id, master.Id });
        Assert.NotNull(session);
        var battle = session.Battle;

        var deployed = battle.Members.Where(m => m.Side == CombatSide.Attacker).Select(m => m.Id).ToList();
        deployed.AddRange(battle.Reserves.Select(m => m.Id));

        Assert.Contains(master.Id, deployed);
        Assert.Contains(companion.Id, deployed);
        // 没编进队伍的人绝不上阵，也不占后备位。
        Assert.DoesNotContain(leftHome.Id, deployed);
        Assert.DoesNotContain(alsoHome.Id, deployed);
        Assert.Equal(2, deployed.Count);
    }

    [Fact]
    public void Solo_quest_deploys_only_the_master()
    {
        var state = new GameState();
        var master = state.Roster.Add("Master", master: true);
        master.Equip(WeaponType.Sword, null, true);
        var companion = state.Roster.Add("随行"); companion.Equip(WeaponType.Spear, null, true);

        var goblins = new List<EnemyDef> { new() { Id = "g1", Name = "哥布林", MaxHp = 10, Attack = 2 } };

        // 单人出征：名单只有玩家本人，随从不上阵。
        var session = Encounters.Start(state, goblins, null, "迷宫地下城", new[] { master.Id });
        Assert.NotNull(session);
        var battle = session.Battle;

        var deployed = battle.Members.Where(m => m.Side == CombatSide.Attacker).Select(m => m.Id).ToList();
        deployed.AddRange(battle.Reserves.Select(m => m.Id));

        Assert.Equal(new[] { master.Id }, deployed);
        Assert.DoesNotContain(companion.Id, deployed);
    }
}
