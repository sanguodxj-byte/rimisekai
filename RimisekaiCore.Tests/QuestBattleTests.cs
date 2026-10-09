using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Combat;
using Rimisekai.Defs;
using Rimisekai.Quest;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 委托只有纯战斗（普通 / 首领 / 连战）与地城探索两类；连战每清一波有补给回合。
/// </summary>
public sealed class QuestBattleTests
{
    private static Combatant Ally(int id, int hp = 100) => new()
    {
        Id = id, Name = $"我{id}", Side = CombatSide.Attacker, Hp = hp, MaxHp = 100,
        Attack = 10, StrikePower = 10, Speed = 10,
    };

    private static Combatant Foe(int id, int column = 1) => new()
    {
        Id = id, Name = "狼", Side = CombatSide.Defender, Hp = 10, MaxHp = 10,
        Attack = 1, StrikePower = 1, Speed = 10, Column = column, MoneyReward = 5,
    };

    private static void KillFoes(Battle battle)
    {
        foreach (var f in battle.Members.Where(m => m.Side == CombatSide.Defender))
            f.Hp = 0;
        battle.JudgeOutcome();
    }

    [Fact]
    public void Shipped_quests_are_battle_or_dungeon_only_and_formations_do_not_overlap()
    {
        DefaultDefs.EnsureInitialized();
        var quests = DefDatabase<QuestDef>.All.ToList();
        Assert.NotEmpty(quests);
        Assert.Contains(quests, q => q.Kind == QuestKind.Dungeon);
        foreach (var battle in new[] { QuestBattle.Normal, QuestBattle.Boss, QuestBattle.Waves })
            Assert.Contains(quests, q => q.Kind == QuestKind.Battle && q.Battle == battle);

        foreach (var q in quests)
        {
            Assert.DoesNotContain("采", q.Description);
            if (q.Battle == QuestBattle.Boss)
                Assert.Contains(q.Foes, f => f.Size > 1);
            if (q.Battle == QuestBattle.Waves)
                Assert.NotEmpty(q.Waves);
            else
                Assert.Empty(q.Waves);
            // 每一波阵型在 4×4 格上互不压格：被盖住的敌人看不见也点不到。
            foreach (var wave in new[] { q.Foes }.Concat(q.Waves))
            {
                var grid = new string?[4, 4];
                foreach (var f in wave)
                {
                    var depth = 4 - System.Math.Clamp(f.ThreatTier, 1, 4);
                    var left = System.Math.Clamp(f.Column, 1, 4) - 1;
                    for (var dr = 0; dr < System.Math.Max(1, f.Size) && depth + dr < 4; dr++)
                        for (var dc = 0; dc < System.Math.Max(1, f.Size) && left + dc < 4; dc++)
                        {
                            Assert.True(grid[left + dc, depth + dr] == null, $"{q.Label}：{f.Name} 压住了 {grid[left + dc, depth + dr]}");
                            grid[left + dc, depth + dr] = f.Name;
                        }
                }
            }
        }
    }

    [Fact]
    public void Waves_battle_supplies_between_waves_then_wins_after_the_last()
    {
        var battle = new Battle();
        battle.Add(Ally(1, hp: 40));
        battle.Add(Foe(101));
        battle.QueueWave(new List<Combatant> { Foe(201, 1), Foe(202, 2) });
        battle.StartBattle();
        Assert.Equal(1, battle.WaveIndex);
        Assert.Equal(2, battle.WaveTotal);

        KillFoes(battle);
        Assert.True(battle.SupplyRound);
        Assert.Equal(CombatOutcome.Ongoing, battle.Outcome);
        Assert.Null(battle.PendingActor);
        Assert.False(battle.StepTurn(out var none));
        Assert.Null(none);
        Assert.Equal(2, battle.NextWave.Count);

        Assert.True(battle.Resupply());
        Assert.False(battle.SupplyRound);
        Assert.Equal(2, battle.WaveIndex);
        Assert.Equal(40 + BattleRules.SupplyHealPercent, battle.Find(1)!.Hp);
        Assert.Equal(2, battle.Members.Count(m => m.Side == CombatSide.Defender && m.Alive));
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.WaveCleared);
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.WaveArrived && e.Amount == 2);

        KillFoes(battle);
        Assert.False(battle.SupplyRound);
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);
        // 三只倒下的敌人都留在名单里，掉落与战绩从他们身上算。
        Assert.Equal(3, battle.Members.Count(m => m.Side == CombatSide.Defender));
    }

    [Fact]
    public void Supply_round_can_withdraw_as_fled()
    {
        var battle = new Battle();
        battle.Add(Ally(1));
        battle.Add(Foe(101));
        battle.QueueWave(new List<Combatant> { Foe(201) });
        battle.StartBattle();
        KillFoes(battle);

        Assert.True(battle.Withdraw());
        Assert.Equal(CombatOutcome.Fled, battle.Outcome);
        Assert.False(battle.Resupply());
    }

    [Fact]
    public void Kind_text_names_the_battle_type()
    {
        Assert.Equal("普通战斗", new QuestDef { Kind = QuestKind.Battle }.KindText);
        Assert.Equal("首领战", new QuestDef { Kind = QuestKind.Battle, Battle = QuestBattle.Boss }.KindText);
        Assert.Equal("连战 3 波", new QuestDef
        {
            Kind = QuestKind.Battle, Battle = QuestBattle.Waves,
            Waves = { new List<EnemyDef>(), new List<EnemyDef>() },
        }.KindText);
        Assert.Equal("地城探索", new QuestDef { Kind = QuestKind.Dungeon }.KindText);
    }
}
