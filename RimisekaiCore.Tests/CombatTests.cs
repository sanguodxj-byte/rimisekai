using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Command;
using Rimisekai.Combat;
using Rimisekai.Session;
using Xunit;

namespace Rimisekai.Tests;

public sealed class CombatTests
{
    private static CharacterState SwordUser(int id = 7)
    {
        var c = new CharacterState(id) { Name = "甲" };
        c[CoreStat.Strength] = 12;
        c[CoreStat.Dexterity] = 8;
        c[CoreStat.Constitution] = 10;
        c[CoreStat.Perception] = 4;
        c[CoreStat.Intellect] = 6;
        c.GainWeaponExp(WeaponType.Sword, 50);
        c.Equip(WeaponType.Sword);
        c.SyncMana();
        return c;
    }

    [Fact]
    public void Deploy_snapshots_character_strike_and_hit()
    {
        var catalog = new GameCatalog();
        catalog.Weapons["sword"] = new WeaponDef { Id = "sword", Name = "剑", Type = WeaponType.Sword, Panel = 20 };
        var c = SwordUser();
        c.Condition.RecoverMana(c.Condition.MaxMana);

        var unit = Deploy.FromCharacter(c, CombatSide.Attacker, catalog);

        Assert.Equal(7, unit.Id);
        Assert.Equal(c.Combat.MaxHp, unit.MaxHp);
        Assert.Equal(c.Combat.MaxHp, unit.Hp);
        Assert.Equal(c.Condition.MaxMana, unit.Mp);
        // 武器经验顺带升了 3 级，六项各 +3：面板 20 + 5×熟练1 + 2×灵巧11 + 力量15 = 62，
        // 乘数 1 + 0.55 + 0.2 + 1 = 2.75 → 170
        Assert.Equal(170, unit.StrikePower);
        Assert.Equal(WeaponType.Sword, unit.Weapon);
        Assert.Equal(StyleType.OneHand, unit.Style);
        Assert.Equal(c.EquippedHit(BattleRules.BaseHit), unit.BaseHit);
        // 暴击率 = 3 + 感知/2；武器经验带升级，感知已 4+3=7 → 3+3 = 6
        Assert.Equal(6, unit.CritRate);
        Assert.Contains(BattleSkills.AttackId, unit.Skills);
        Assert.Contains(BattleSkills.GuardId, unit.Skills);
    }

    [Fact]
    public void Strike_hits_dodges_and_applies_defence()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9, StrikePower = 10 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 30, MaxHp = 30, Defence = 4 });

        Assert.Equal(1, battle.PendingActor!.Id);
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        var hit = battle.Events.First(e => e.Kind == CombatEventKind.Hit);
        // 防御 4 折 8% 减伤：10 × 92/100 = 9
        Assert.Equal(9, hit.Amount);
        Assert.Equal(21, hit.HpAfter);
        Assert.Equal(9, battle.Find(1)!.DamageDealt);
    }

    [Fact]
    public void Strike_can_miss_by_roll()
    {
        var battle = new Battle(d100: () => 99);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9, StrikePower = 10 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 30, MaxHp = 30 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.Equal(CombatEventKind.Miss, battle.Events.Last(e => e.ActorId == 1).Kind);
        Assert.Equal(30, battle.Find(2)!.Hp);
        Assert.Equal(0, battle.Find(1)!.DamageDealt);
    }

    [Fact]
    public void Spell_scales_power_and_half_defence_without_roll()
    {
        var catalog = new GameCatalog();
        catalog.Skills["spark"] = new SkillDef
        {
            Id = "spark", Kind = SkillKind.Spell, Target = SkillTarget.Enemy, Power = 100, MpCost = 3,
        };
        var battle = new Battle(catalog, d100: () => 99);
        var caster = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9, SpellPower = 10, Mp = 10, MaxMp = 10 };
        caster.Skills.Add("spark");
        battle.Add(caster);
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, Defence = 8 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "spark", TargetId = 2 }));
        var hit = battle.Events.First(e => e.Kind == CombatEventKind.Hit);
        // 法强 10 × 100% × 3 = 30；防御 8 折 16% 减伤，法术只吃一半 8%：30 × 92/100 = 27
        // 法术必中不看骰，骰 99 也不落空
        Assert.Equal(27, hit.Amount);
        Assert.Equal(7, battle.Find(1)!.Mp);
    }

    [Fact]
    public void Heal_restores_missing_hp_only()
    {
        var catalog = new GameCatalog();
        catalog.Skills["mend"] = new SkillDef
        {
            Id = "mend", Kind = SkillKind.Heal, Target = SkillTarget.Ally, Power = 150,
        };
        var battle = new Battle(catalog, d100: () => 0);
        var healer = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9, SpellPower = 6 };
        healer.Skills.Add("mend");
        battle.Add(healer);
        battle.Add(new Combatant { Id = 3, Side = CombatSide.Attacker, Hp = 45, MaxHp = 50 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 30, MaxHp = 30 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "mend", TargetId = 3 }));
        var heal = battle.Events.First(e => e.Kind == CombatEventKind.Heal);
        // 法强 6 × 150% × 2 = 18，只补缺口 5
        Assert.Equal(5, heal.Amount);
        Assert.Equal(50, battle.Find(3)!.Hp);
    }

    [Fact]
    public void Guard_raises_defence_until_round_end()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, Dodge = 9, Defence = 10, StrikePower = 20 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, StrikePower = 20 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = BattleSkills.GuardId }));
        Assert.Equal(15, battle.Find(1)!.EffDefence);
        // 同回合敌人打过来：防御 15 折 30% 减伤，20 × 70/100 = 14
        Assert.NotNull(battle.PendingActor);
        var onGuard = battle.Events.Last(e => e.Kind == CombatEventKind.Hit && e.TargetId == 1);
        Assert.Equal(14, onGuard.Amount);

        // 下一回合架势卸掉，恢复原防御
        Assert.Equal(1, battle.PendingActor!.Id);
        Assert.Equal(10, battle.Find(1)!.EffDefence);
    }

    [Fact]
    public void Debuff_lowers_attack_then_expires()
    {
        var catalog = new GameCatalog();
        catalog.Skills["weaken"] = new SkillDef
        {
            Id = "weaken", Kind = SkillKind.Buff, Target = SkillTarget.Enemy,
            Stat = StatusStat.Attack, StatusPercent = -30, StatusRounds = 2,
        };
        var battle = new Battle(catalog, d100: () => 0);
        var caster = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9, StrikePower = 4 };
        caster.Skills.Add("weaken");
        battle.Add(caster);
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, Attack = 10, StrikePower = 10 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "weaken", TargetId = 2 }));
        Assert.Equal(7, battle.Find(2)!.EffAttack);

        // 走满三回合：敌方的还手依次是 7 / 7 / 10，第二回合结束状态到期
        for (var round = 0; round < 3; round++)
        {
            Assert.Equal(1, battle.PendingActor!.Id);
            Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        }
        var amounts = battle.Events
            .Where(e => e.Kind == CombatEventKind.Hit && e.ActorId == 2)
            .Select(e => e.Amount)
            .ToList();
        Assert.Equal(new[] { 7, 7, 10 }, amounts);
        Assert.Equal(10, battle.Find(2)!.EffAttack);
    }

    [Fact]
    public void Enemy_row_deploys_its_sheet_and_skills()
    {
        var def = new EnemyDef
        {
            Id = "slime", Name = "软泥", MaxHp = 40, Attack = 8,
            Defence = 3, Dodge = 2, SpellPower = 0, Threat = 2,
            Armour = 5, Targeting = TargetMode.HuntWeak, Money = 12,
            Skills = { "acid" },
        };
        var unit = Deploy.FromEnemy(def, 11, CombatSide.Defender);
        Assert.Equal(11, unit.Id);
        Assert.Equal("软泥", unit.Name);
        Assert.Equal(40, unit.Hp);
        Assert.Equal(8, unit.StrikePower);
        Assert.Equal(3, unit.Defence);
        Assert.Equal(2, unit.EffDodge);
        Assert.Equal(2, unit.Threat);
        Assert.Equal(5, unit.Armour);
        Assert.Equal(TargetMode.HuntWeak, unit.Targeting);
        Assert.Equal(12, unit.MoneyReward);
        Assert.Equal(0, unit.CritRate);
        Assert.Contains("acid", unit.Skills);
        Assert.Contains(BattleSkills.AttackId, unit.Skills);
    }

    [Fact]
    public void Mp_and_cooldown_gate_skills()
    {
        var catalog = new GameCatalog();
        catalog.Skills["flame"] = new SkillDef
        {
            Id = "flame", Kind = SkillKind.Spell, Target = SkillTarget.Enemy,
            Power = 100, MpCost = 10, Cooldown = 2,
        };
        var battle = new Battle(catalog, d100: () => 0);
        var caster = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, Dodge = 9, SpellPower = 10, Mp = 10, MaxMp = 10 };
        caster.Skills.Add("flame");
        battle.Add(caster);
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 80, MaxHp = 80 });

        // 第 1 回合：吟唱成功，进冷却
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "flame", TargetId = 2 }));
        Assert.Equal(0, battle.Find(1)!.Mp);
        Assert.NotNull(battle.PendingActor);
        Assert.Equal(1, battle.Find(1)!.Cooldowns["flame"]);

        // 第 2 回合：冷却未走完，菜单里也没有它
        Assert.False(battle.Act(new CombatAction { ActorId = 1, SkillId = "flame", TargetId = 2 }));
        Assert.DoesNotContain(battle.Menu(), s => s.Id == "flame");
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.NotNull(battle.PendingActor);
        Assert.False(battle.Find(1)!.Cooldowns.ContainsKey("flame"));

        // 第 3 回合：冷却已清，但蓝没了
        Assert.False(battle.Act(new CombatAction { ActorId = 1, SkillId = "flame", TargetId = 2 }));
    }

    [Fact]
    public void Ai_answers_for_uncontrolled_side_and_guards_when_hurt()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 30, MaxHp = 30, Dodge = 9, StrikePower = 5 });

        // 守方躲闪更高，一读当前行动者就轮到它，AI 立即代打
        Assert.Equal(1, battle.PendingActor!.Id);
        var answer = battle.Events.First(e => e.Kind == CombatEventKind.Hit && e.ActorId == 2);
        Assert.Equal(1, answer.TargetId);
        Assert.Equal(5, answer.Amount);

        battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, StrikePower = 5 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 5, MaxHp = 40, Dodge = 9, StrikePower = 5 });
        // 敌人残血，AI 先架势保命而不是还手
        Assert.Equal(1, battle.PendingActor!.Id);
        Assert.Equal(CombatEventKind.Status, battle.Events.Last(e => e.ActorId == 2).Kind);
    }

    [Fact]
    public void Flee_rolls_chance_and_consumes_turn_on_fail()
    {
        var rolls = new Queue<int>(new[] { 99, 0, 0, 0 });
        var battle = new Battle(d100: () => rolls.Dequeue());
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 30, MaxHp = 30, StrikePower = 5 });

        // 99 ≥ 60+9 逃跑失败，耗掉这次行动，敌人还打了一下（索敌骰 0、命中骰 0）
        Assert.False(battle.TryFlee());
        Assert.Equal(CombatOutcome.Ongoing, battle.Outcome);
        Assert.NotNull(battle.PendingActor);
        Assert.Equal(25, battle.Find(1)!.Hp);

        // 下一回合再跑，0 < 69 逃成
        Assert.True(battle.TryFlee());
        Assert.Equal(CombatOutcome.Fled, battle.Outcome);
        Assert.Null(battle.PendingActor);
    }

    [Fact]
    public void Round_limit_judges_draw()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 1000, MaxHp = 1000, Dodge = 9, StrikePower = 1 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 1000, MaxHp = 1000, StrikePower = 1 });

        var steps = 0;
        while (battle.PendingActor != null)
        {
            Assert.True(++steps < 200);
            Assert.True(battle.Act(new CombatAction { ActorId = battle.PendingActor.Id, TargetId = 2 }));
        }
        Assert.Equal(CombatOutcome.Draw, battle.Outcome);
        Assert.Equal(BattleRules.RoundLimit + 1, battle.Round);
    }

    [Fact]
    public void Rewards_pay_exp_and_mood_on_victory()
    {
        var roster = new Roster();
        var fighter = roster.Add("剑士");
        fighter.Equip(WeaponType.Sword);

        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant
        {
            Id = fighter.Id, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40,
            Dodge = 9, StrikePower = 30, Weapon = WeaponType.Sword, Style = StyleType.OneHand,
        });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 50, MaxHp = 50 });

        while (battle.PendingActor != null && battle.Outcome == CombatOutcome.Ongoing)
            Assert.True(battle.Act(new CombatAction { ActorId = battle.PendingActor.Id, TargetId = 2 }));
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);

        var summary = BattleRewards.Summary(battle, CombatSide.Attacker);
        var row = Assert.Single(summary.Rows);
        Assert.Equal(fighter.Id, row.CharacterId);
        Assert.Equal(60, row.DamageDealt);
        Assert.Equal(1, row.Kills);
        // 伤害 60/20 = 3 + 击坠 10 + 凯旋 20 = 33 武器经验；流派 = 33/2 = 16
        Assert.Equal(33, row.WeaponExp);
        Assert.Equal(16, row.StyleExp);
        Assert.Equal(BattleRewards.VictoryMood, row.Mood);

        BattleRewards.Apply(battle, roster, CombatSide.Attacker);
        Assert.Equal(33 * 2, fighter.Weapons[(int)WeaponType.Sword].Exp);
        Assert.Equal(16 * 5, fighter.Styles[(int)StyleType.OneHand].Exp);
        Assert.Equal(50 + BattleRewards.VictoryMood, fighter.Affect.Mood);
    }

    [Fact]
    public void Session_routes_commands_until_battle_ends()
    {
        var catalog = new GameCatalog();
        catalog.Skills["spark"] = new SkillDef
        {
            Id = "spark", Kind = SkillKind.Spell, Target = SkillTarget.Enemy, Power = 100, MpCost = 3,
        };
        var roster = new Roster();
        var battle = new Battle(catalog, d100: () => 0);
        var hero = new Combatant
        {
            Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40,
            Dodge = 9, StrikePower = 12, SpellPower = 10, Mp = 10, MaxMp = 10,
        };
        hero.Skills.Add("spark");
        battle.Add(hero);
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 20, MaxHp = 20 });

        var session = new BattleSession(roster, battle);
        var ids = new List<int>();
        foreach (var def in session.Available())
            ids.Add(def.Id);
        Assert.Equal(4, ids.Count);
        Assert.Contains(BattleSession.CmdAttack, ids);
        Assert.Contains(BattleSession.CmdGuard, ids);
        Assert.Contains(BattleSession.CmdFlee, ids);
        Assert.Contains(100, ids);

        // 打偏目标按非法拒绝
        Assert.Equal(CommandResult.Rejected,
            session.Submit(new CommandRequest { CommandId = BattleSession.CmdAttack, TargetIds = { 1 } }));
        // 普攻没打死，会话继续
        Assert.Equal(CommandResult.Executed,
            session.Submit(new CommandRequest { CommandId = BattleSession.CmdAttack, TargetIds = { 2 } }));
        Assert.Equal(8, battle.Find(2)!.Hp);
        Assert.False(session.Exited);
        // 法术收尾，会话随战果退出
        Assert.Equal(CommandResult.ExitSession,
            session.Submit(new CommandRequest { CommandId = 100, TargetIds = { 2 } }));
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);
        Assert.True(session.Exited);
        // 战斗结束后再提交一律拒绝
        Assert.Equal(CommandResult.Rejected,
            session.Submit(new CommandRequest { CommandId = BattleSession.CmdAttack, TargetIds = { 2 } }));
    }

    [Fact]
    public void Session_flee_command_ends_on_success()
    {
        var roster = new Roster();
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 5 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 30, MaxHp = 30, Dodge = 5 });
        var session = new BattleSession(roster, battle);

        Assert.Equal(CommandResult.ExitSession, session.Submit(new CommandRequest { CommandId = BattleSession.CmdFlee }));
        Assert.Equal(CombatOutcome.Fled, battle.Outcome);
        Assert.True(session.Exited);
    }

    [Fact]
    public void Crit_multiplies_strike_but_never_spell()
    {
        var catalog = new GameCatalog();
        catalog.Skills["spark"] = new SkillDef { Id = "spark", Kind = SkillKind.Spell, Target = SkillTarget.Enemy, Power = 100 };
        var battle = new Battle(catalog, d100: () => 0);
        battle.Add(new Combatant
        {
            Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40,
            Dodge = 9, StrikePower = 10, SpellPower = 10, Mp = 10, MaxMp = 10, CritRate = 100,
        });
        battle.Find(1)!.Skills.Add("spark");
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 90, MaxHp = 90 });

        // 普攻必暴：10 × 150% = 15
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.Equal(15, battle.Events.First(e => e.Kind == CombatEventKind.Hit).Amount);

        // 法术必中但不暴击：30 点就是 30 点
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "spark", TargetId = 2 }));
        var spell = battle.Events.Last(e => e.Kind == CombatEventKind.Hit);
        Assert.Equal(30, spell.Amount);
    }

    [Fact]
    public void Shield_blocks_strikes_but_spells_pierce()
    {
        var catalog = new GameCatalog();
        catalog.Skills["ward"] = new SkillDef
        {
            Id = "ward", Kind = SkillKind.Buff, Target = SkillTarget.Self, ShieldPoints = 8,
        };
        catalog.Skills["pierce"] = new SkillDef
        {
            Id = "pierce", Kind = SkillKind.Spell, Target = SkillTarget.Enemy, Power = 100, MpCost = 3,
        };

        // 物理先被护盾吃：10 点伤害漏 2 点
        var battle = new Battle(catalog, d100: () => 0);
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, Dodge = 9, StrikePower = 5 };
        hero.Skills.Add("ward");
        battle.Add(hero);
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, StrikePower = 10 });
        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "ward" }));
        Assert.Equal(8, battle.Find(1)!.Shield);
        Assert.NotNull(battle.PendingActor);
        Assert.Equal(2, battle.Events.Last(e => e.Kind == CombatEventKind.Hit && e.TargetId == 1).Amount);
        Assert.Equal(0, battle.Find(1)!.Shield);
        Assert.Equal(38, battle.Find(1)!.Hp);

        // 法术穿透护盾：盾原封不动，血直接掉（施法者躲闪更高，先手代打）
        battle = new Battle(catalog, d100: () => 0);
        hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, Shield = 8 };
        battle.Add(hero);
        var caster = new Combatant
        {
            Id = 2, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, Dodge = 9,
            StrikePower = 10, SpellPower = 10, Mp = 10, MaxMp = 10,
        };
        caster.Skills.Add("pierce");
        battle.Add(caster);
        Assert.NotNull(battle.PendingActor);
        Assert.Equal(10, battle.Find(1)!.Hp);
        Assert.Equal(8, battle.Find(1)!.Shield);
    }

    [Fact]
    public void Targeting_modes_redirect_enemy_ai()
    {
        Combatant Hunter(TargetMode mode) => new()
        {
            Id = 9, Side = CombatSide.Defender, Hp = 40, MaxHp = 40,
            Dodge = 9, StrikePower = 5, Targeting = mode,
        };

        // 噬弱：咬住残血的那个
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Attacker, Hp = 10, MaxHp = 30 });
        battle.Add(Hunter(TargetMode.HuntWeak));
        Assert.Equal(1, battle.PendingActor!.Id);
        Assert.Equal(2, battle.Events.Last(e => e.ActorId == 9).TargetId);

        // 猎强：优先打攻击最高的那个
        battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Attack = 5 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Attack = 15 });
        battle.Add(Hunter(TargetMode.HuntStrong));
        Assert.Equal(1, battle.PendingActor!.Id);
        Assert.Equal(2, battle.Events.Last(e => e.ActorId == 9).TargetId);
    }

    [Fact]
    public void Balanced_targeting_rolls_threat_weights()
    {
        // 敌视 3:1 → 总权重 4；骰 0 落在重的一方，骰 99 落在轻的一方
        Battle Make(int[] rolls)
        {
            var queue = new Queue<int>(rolls);
            var battle = new Battle(d100: () => queue.Dequeue());
            battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Threat = 3 });
            battle.Add(new Combatant { Id = 2, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Threat = 1 });
            battle.Add(new Combatant { Id = 9, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, Dodge = 9, StrikePower = 5 });
            return battle;
        }

        var heavy = Make(new[] { 0, 0 });
        Assert.Equal(1, heavy.PendingActor!.Id);
        Assert.Equal(1, heavy.Events.Last(e => e.ActorId == 9).TargetId);

        var light = Make(new[] { 99, 0 });
        Assert.Equal(1, light.PendingActor!.Id);
        Assert.Equal(2, light.Events.Last(e => e.ActorId == 9).TargetId);

        // 挑衅类状态直接放大敌视权重：3 × (100+300)% = 12
        var taunted = new Combatant { Threat = 3 };
        taunted.Statuses.Add(new BattleStatus { Token = "t", Stat = StatusStat.Threat, Percent = 300, RoundsLeft = 2 });
        Assert.Equal(12, taunted.EffThreat);
    }

    [Fact]
    public void Loot_rolls_only_from_the_fallen()
    {
        var def = new EnemyDef
        {
            Id = "slime", Name = "软泥", MaxHp = 10, Money = 30,
            Loot = { new EnemyLoot { ItemId = "凝胶", Min = 2, Max = 4, RatePercent = 60 } },
        };
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9, StrikePower = 10 });
        battle.Add(Deploy.FromEnemy(def, 2, CombatSide.Defender));
        while (battle.PendingActor != null && battle.Outcome == CombatOutcome.Ongoing)
            Assert.True(battle.Act(new CombatAction { ActorId = battle.PendingActor.Id, TargetId = 2 }));
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);

        // 骰 59 < 60 掉落成立；数量 = 2 + 3 % (4-2+1) = 2
        var rolls = new Queue<int>(new[] { 59, 3 });
        var loot = BattleLoot.Roll(battle, CombatSide.Defender, () => rolls.Dequeue());
        Assert.Equal(30, loot.Money);
        var (itemId, count) = Assert.Single(loot.Items);
        Assert.Equal("凝胶", itemId);
        Assert.Equal(2, count);

        // 骰 60 不小于 60：不掉
        rolls = new Queue<int>(new[] { 60, 0 });
        loot = BattleLoot.Roll(battle, CombatSide.Defender, () => rolls.Dequeue());
        Assert.Equal(30, loot.Money);
        Assert.Empty(loot.Items);
    }
}
