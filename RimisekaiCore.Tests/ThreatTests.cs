using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Combat;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 威胁等级站位：1-5 级，越高越靠前。玩家侧：点哪列打那列最前面的（未点列取全场最前排），
/// 确定性不掷骰；敌方（含自动战斗的我方）：随机打一个威胁等级最高的；
/// 打击格整列，全体全打。
/// </summary>
public class ThreatTests
{
    private static SkillDef Shot() => new()
    {
        Id = "shot", Kind = SkillKind.Spell, Target = SkillTarget.Enemy,
        Power = 100, Range = SkillRange.Ranged,
    };

    private sealed record FoeSpec(EnemyDef Def, int Hp);

    private static FoeSpec Foe(int id, string name, int tier, int column, int hp = 50, int size = 1) => new(new EnemyDef
    {
        Id = $"foe{id}", Name = name,
        ThreatTier = tier, Column = column, Size = size,
    }, hp);

    /// <summary>
    /// 站位测试只看打谁、不看身板：按目录行的站位成军，身板钉死（生命取 hp、出手 5、无防御闪避），
    /// 免得生成器掷出的数值左右「打不打得死」。
    /// </summary>
    private static Combatant Unit(FoeSpec foe, int id, CombatSide side)
    {
        var rolled = Deploy.FromEnemy(foe.Def, id, side);
        var c = new Combatant
        {
            Id = id, Name = rolled.Name, Side = side, Hp = foe.Hp, MaxHp = foe.Hp,
            Attack = 5, StrikePower = 5, Speed = 10,
            ThreatTier = rolled.ThreatTier, Column = rolled.Column, Size = rolled.Size,
        };
        return c;
    }

    [Fact]
    public void Player_melee_strikes_whatever_target_is_named()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, StrikePower = 10 });
        battle.Add(Unit(Foe(2, "后排", 2, 2), 2, CombatSide.Defender));
        battle.Add(Unit(Foe(3, "前排", 5, 1), 3, CombatSide.Defender));

        // 锁前排是 UI 遮挡（每列最前才可点）的事，核心只认点到的目标
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.True(battle.Find(2)!.Hp < 50);
        Assert.Equal(50, battle.Find(3)!.Hp);
    }

    [Fact]
    public void Player_ranged_picks_the_frontmost_of_the_named_column()
    {
        var catalog = new GameCatalog();
        catalog.Skills["shot"] = Shot();
        var battle = new Battle(catalog, d100: () => 0);
        var mage = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, SpellPower = 10 };
        mage.Skills.Add("shot");
        battle.Add(mage);
        battle.Add(Unit(Foe(2, "甲", 2, 1), 2, CombatSide.Defender));
        battle.Add(Unit(Foe(3, "乙", 5, 1), 3, CombatSide.Defender));
        battle.Add(Unit(Foe(4, "丙", 3, 2), 4, CombatSide.Defender));

        // 点第一列打该列最前面的（威胁最高者）；第二列只有丙
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "shot", TargetColumn = 1 }));
        Assert.Equal(20, battle.Find(3)!.Hp);
        Assert.Equal(50, battle.Find(2)!.Hp);
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "shot", TargetColumn = 2 }));
        Assert.Equal(20, battle.Find(4)!.Hp);
    }

    [Fact]
    public void Front_death_promotes_the_next_tier()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, StrikePower = 30 });
        battle.Add(Unit(Foe(2, "后排", 2, 2), 2, CombatSide.Defender));
        battle.Add(Unit(Foe(3, "前排", 5, 1, hp: 30), 3, CombatSide.Defender));

        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 3 }));
        Assert.False(battle.Find(3)!.Alive);
        // 前排倒下，UI 里该列次级浮出成可点层，点选照打
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.True(battle.Find(2)!.Hp < 50);
    }

    [Fact]
    public void Column_and_all_aoe_shapes()
    {
        var catalog = new GameCatalog();
        catalog.Skills["column"] = new SkillDef
        {
            Id = "column", Kind = SkillKind.Spell, Target = SkillTarget.FoesColumn, Power = 100,
        };
        catalog.Skills["all"] = new SkillDef
        {
            Id = "all", Kind = SkillKind.Spell, Target = SkillTarget.AllEnemies, Power = 100,
        };
        var battle = new Battle(catalog, d100: () => 0);
        var mage = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, SpellPower = 10 };
        mage.Skills.Add("column");
        mage.Skills.Add("all");
        battle.Add(mage);
        battle.Add(Unit(Foe(2, "一列上", 5, 1), 2, CombatSide.Defender));
        battle.Add(Unit(Foe(3, "一列下", 2, 1), 3, CombatSide.Defender));
        battle.Add(Unit(Foe(4, "二列", 3, 2), 4, CombatSide.Defender));

        // 打击格：一列两只全中，二列不碰
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "column", TargetColumn = 1 }));
        Assert.True(battle.Find(2)!.Hp < 50);
        Assert.True(battle.Find(3)!.Hp < 50);
        Assert.Equal(50, battle.Find(4)!.Hp);

        // 全体：剩下的二列也中
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "all" }));
        Assert.True(battle.Find(4)!.Hp < 50);
    }

    [Fact]
    public void Enemy_melee_hits_the_front_party_tier_and_randomizes_ties()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, ThreatTier = 2 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, ThreatTier = 5 });
        battle.Add(new Combatant { Id = 3, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, StrikePower = 5, Speed = 20 });

        // 敌方近战只打威胁最高的 5 级队友
        Assert.NotNull(battle.PendingActor);
        var hit = battle.Events.First(e => e.Kind == CombatEventKind.Hit && e.ActorId == 3);
        Assert.Equal(2, hit.TargetId);

        // 同级并列：骰 0 取队首先入队者
        battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, ThreatTier = 5 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, ThreatTier = 5 });
        battle.Add(new Combatant { Id = 3, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, StrikePower = 5, Speed = 20 });
        Assert.NotNull(battle.PendingActor);
        Assert.Equal(1, battle.Events.First(e => e.Kind == CombatEventKind.Hit && e.ActorId == 3).TargetId);
    }

    [Fact]
    public void Elite_covering_two_columns_is_hittable_from_both()
    {
        var catalog = new GameCatalog();
        catalog.Skills["shot"] = Shot();

        // 精英占 2x2：锚在列1，覆盖一二两列，选哪列都打得中它
        var def = Foe(9, "精英", 4, 1, size: 2);

        foreach (var column in new[] { 1, 2 })
        {
            var battle = new Battle(catalog, d100: () => 0);
            var mage = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, SpellPower = 10 };
            mage.Skills.Add("shot");
            battle.Add(mage);
            battle.Add(Unit(def, 9, CombatSide.Defender));

            Assert.True(battle.Act(new CombatAction
            {
                ActorId = 1, SkillId = "shot", TargetColumn = column,
            }));
            Assert.True(battle.Find(9)!.Hp < 50);
        }
    }

    [Fact]
    public void Enemy_ranged_hits_one_of_the_highest_tier()
    {
        var catalog = new GameCatalog();
        catalog.Skills["volley_shot"] = new SkillDef
        {
            Id = "volley_shot", Kind = SkillKind.Spell, Target = SkillTarget.Enemy,
            Power = 100, Range = SkillRange.Ranged,
        };
        var battle = new Battle(catalog, d100: () => 99);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, ThreatTier = 5 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, ThreatTier = 1 });
        var archer = new Combatant { Id = 3, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, SpellPower = 10, Speed = 20 };
        archer.Skills.Add("volley_shot");
        battle.Add(archer);

        // 敌方随机打一个威胁等级最高的：只可能是 5 级的 1 号，1 级的 2 号不受影响
        Assert.NotNull(battle.PendingActor);
        Assert.True(battle.Find(1)!.Hp < 40);
        Assert.Equal(40, battle.Find(2)!.Hp);
    }

    [Fact]
    public void Enemies_step_forward_when_front_row_falls()
    {
        var battle = new Battle(d100: () => 0);
        // 前排哥布林 (ThreatTier 4) 与后排哥布林 (ThreatTier 2)
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, IsPlayer = true, Hp = 50, MaxHp = 50, StrikePower = 20, Speed = 20 };
        var front = new Combatant { Id = 10, Side = CombatSide.Defender, Column = 1, ThreatTier = 4, Hp = 10, MaxHp = 10 };
        var rear = new Combatant { Id = 11, Side = CombatSide.Defender, Column = 1, ThreatTier = 2, Hp = 20, MaxHp = 20 };
        battle.Add(hero);
        battle.Add(front);
        battle.Add(rear);
        battle.StartBattle();

        // 玩家一击击杀前排 10 号
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 10 }));
        Assert.False(front.Alive);

        // 验证：后排 11 号自动向前推进填补空位，ThreatTier 变为 4（最前排）！
        Assert.Equal(4, rear.ThreatTier);
    }

    [Fact]
    public void Boss_2x2_steps_forward_when_both_front_cells_are_cleared()
    {
        var battle = new Battle(d100: () => 0);
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, IsPlayer = true, Hp = 50, MaxHp = 50, StrikePower = 30, Speed = 30 };
        // 前排两只小哥布林分别守在列 2 和列 3 (ThreatTier 4)
        var guard2 = new Combatant { Id = 20, Side = CombatSide.Defender, Column = 2, ThreatTier = 4, Hp = 10, MaxHp = 10 };
        var guard3 = new Combatant { Id = 21, Side = CombatSide.Defender, Column = 3, ThreatTier = 4, Hp = 10, MaxHp = 10 };
        // 中间 2x2 大地精 Boss 占列 2 和 3 (ThreatTier 3)
        var boss = new Combatant { Id = 99, Name = "大地精", Side = CombatSide.Defender, Column = 2, Size = 2, ThreatTier = 3, Hp = 100, MaxHp = 100 };

        battle.Add(hero);
        battle.Add(guard2);
        battle.Add(guard3);
        battle.Add(boss);
        battle.StartBattle();

        // 击杀前排列 2 小怪，由于列 3 仍有阻挡，2x2 Boss 暂不能前移
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 20 }));
        Assert.False(guard2.Alive);
        Assert.Equal(3, boss.ThreatTier);

        // 再次击杀前排列 3 小怪，前排两列全清空！
        hero.NextActAt = battle.Time; // 模拟下次出手
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 21 }));
        Assert.False(guard3.Alive);

        // 验证：2x2 大地精 Boss 顺势大步向前迈进，ThreatTier 推进到最前排 4！
        Assert.Equal(4, boss.ThreatTier);
    }
}
