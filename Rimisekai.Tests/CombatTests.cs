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
        return c;
    }

    [Fact]
    public void Deploy_snapshots_character_strike_and_hit()
    {
        var weapons = new Rimisekai.Defs.WeaponRegistry();
        var sword = Rimisekai.Defs.WeaponForge.Forge("铁", WeaponType.Sword, Rimisekai.Defs.Quality.Common, "", false, 0);
        weapons.Add(sword);
        var c = SwordUser();
        c.SetEquippedId(Rimisekai.Defs.EquipSlot.MainHand, sword.Id);

        var unit = Deploy.FromCharacter(c, CombatSide.Attacker, weapons);

        Assert.Equal(7, unit.Id);
        Assert.Equal(c.Combat.MaxHp, unit.MaxHp);
        Assert.Equal(c.Combat.MaxHp, unit.Hp);
        // 主手那件铁剑：面板（剑 18 + 铁 7）× 普通 100% = 25。
        // 武器经验顺带升了 3 级，六项各 +3：面板 25 + 5×熟练1 + 2×灵巧11 + 力量15 = 67，
        // 乘数 1 + 0.55 + 0.2 + 1 = 2.75 → 184；进战斗按参战折算
        Assert.Equal(25, sword.Panel);
        Assert.Equal((int)System.Math.Round(c.ResolveStrike(25).Rounded * BattleRules.PowerPercent / 100.0), unit.StrikePower);
        Assert.True(unit.StrikePower > Deploy.FromCharacter(c, CombatSide.Attacker).StrikePower);
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
            Id = "spark", Kind = SkillKind.Spell, Target = SkillTarget.Enemy, Power = 100,
        };
        var battle = new Battle(catalog, d100: () => 99);
        var caster = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9, SpellPower = 10 };
        caster.Skills.Add("spark");
        battle.Add(caster);
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, Defence = 8 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "spark", TargetId = 2 }));
        var hit = battle.Events.First(e => e.Kind == CombatEventKind.Hit);
        // 法强 10 × 100% × 3 = 30；防御 8 折 16% 减伤，法术只吃一半 8%：30 × 92/100 = 27
        // 法术必中不看骰，骰 99 也不落空
        Assert.Equal(27, hit.Amount);
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
        var hero = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40, Dodge = 9, Defence = 10, StrikePower = 20 };
        hero.Skills.Add(BattleSkills.GuardId);   // 防御架势是玩家侧动作，部署时带上
        battle.Add(hero);
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, StrikePower = 20 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = BattleSkills.GuardId }));
        Assert.Equal(15, battle.Find(1)!.EffDefence);
        // 同回合敌人打过来：防御 15 折 30% 减伤，20 × 70/100 = 14
        Assert.NotNull(battle.PendingActor);
        var onGuard = battle.Events.Last(e => e.Kind == CombatEventKind.Hit && e.TargetId == 1);
        Assert.Equal(14, onGuard.Amount);

        // 推进一轮：架势到期卸掉，恢复原防御
        battle.AdvanceRound();
        Assert.Equal(10, battle.Find(1)!.EffDefence);
    }

    [Fact]
    public void Debuff_lowers_attack_then_expires()
    {
        var catalog = new GameCatalog();
        catalog.Skills["weaken"] = new SkillDef
        {
            Id = "weaken", Kind = SkillKind.Buff, Target = SkillTarget.Enemy,
            Status = StatusKind.StatMod, StatusStat = StatusStat.Attack,
            StatusPercent = -30, StatusRounds = 2,
        };
        var battle = new Battle(catalog, d100: () => 0);
        var caster = new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30, Dodge = 9, StrikePower = 4 };
        caster.Skills.Add("weaken");
        battle.Add(caster);
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 60, MaxHp = 60, Attack = 10, StrikePower = 10 });

        Assert.True(battle.Act(new CombatAction { ActorId = 1, SkillId = "weaken", TargetId = 2 }));
        Assert.Equal(7, battle.Find(2)!.EffStrikePower);

        // 敌人还手 7（弱化中），再推一轮到弱化到期，还手 10。
        Assert.Equal(1, battle.PendingActor!.Id);
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));
        Assert.Equal(7, battle.Find(2)!.EffStrikePower);
        battle.AdvanceRound();
        battle.AdvanceRound();   // 弱化到期
        Assert.Equal(10, battle.Find(2)!.EffStrikePower);
        Assert.Equal(1, battle.PendingActor!.Id);
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 2 }));

        var amounts = battle.Events
            .Where(e => e.Kind == CombatEventKind.Hit && e.ActorId == 2)
            .Select(e => e.Amount)
            .ToList();
        Assert.Equal(new[] { 7, 10 }, amounts);
        Assert.Equal(10, battle.Find(2)!.EffStrikePower);
    }

    [Fact]
    public void Enemy_row_deploys_its_sheet_and_skills()
    {
        // 怪物视为没有生活技能的角色：身板由角色生成器掷出，再走角色同一条快照公式。
        var def = new EnemyDef
        {
            Id = "slime", Name = "软泥", Weapon = WeaponType.Unarmed,
            Primary = CoreStat.Constitution, Secondary = CoreStat.Strength,
            Armour = 5, Money = 12, ThreatTier = 2, Column = 3,
            Skills = { "acid" },
        };
        var unit = Deploy.FromEnemy(def, 11, CombatSide.Defender);
        var body = new CharacterGenerator(new System.Random(0)).RollMonster(11, "软泥", WeaponType.Unarmed,
            CoreStat.Constitution, CoreStat.Strength);
        Assert.Equal(11, unit.Id);
        Assert.Equal("软泥", unit.Name);
        Assert.False(unit.IsPlayer);
        Assert.Equal(unit.MaxHp, unit.Hp);
        // 生命公式与角色同：20 + 体质×10 + 等级×5；体质至少底线 6 加主属性 5。
        Assert.True(unit.MaxHp >= CharacterState.BaseHp + 11 * CharacterState.HpPerConstitution + CharacterState.HpPerLevel);
        Assert.True(unit.StrikePower > 1);
        Assert.InRange(unit.Speed, 10, 16);
        Assert.Equal(WeaponType.Unarmed, unit.Weapon);
        Assert.Equal(5, unit.Armour);
        Assert.Equal(12, unit.MoneyReward);
        Assert.Equal(2, unit.ThreatTier);
        Assert.Equal(3, unit.Column);
        Assert.Contains("acid", unit.Skills);
        Assert.Contains(BattleSkills.AttackId, unit.Skills);
        Assert.DoesNotContain(BattleSkills.GuardId, unit.Skills);
        // 同一 id 身板固定；没有生活技能。
        Assert.Equal(unit.MaxHp, Deploy.FromEnemy(def, 11, CombatSide.Defender).MaxHp);
        Assert.All(body.LifeExp, exp => Assert.Equal(0, exp));
    }

    [Fact]
    public void Ai_answers_for_uncontrolled_side()
    {
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 30, MaxHp = 30 });
        battle.Add(new Combatant { Id = 2, Side = CombatSide.Defender, Hp = 30, MaxHp = 30, Dodge = 9, StrikePower = 5, Speed = 20 });

        // 敌方速度快先手，一读当前行动者它已代打完毕：照常进攻，没有防御动作
        Assert.Equal(1, battle.PendingActor!.Id);
        var answer = battle.Events.First(e => e.Kind == CombatEventKind.Hit && e.ActorId == 2);
        Assert.Equal(1, answer.TargetId);
        Assert.Equal(5, answer.Amount);
        Assert.DoesNotContain(BattleSkills.GuardId, battle.Find(2)!.Skills);
    }

    [Fact]
    public void Flee_rolls_chance_and_consumes_turn_on_fail()
    {
        var rolls = new Queue<int>(new[] { 99, 0, 0, 0, 0, 0 });
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
            battle.AdvanceRound();
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
            Id = "spark", Kind = SkillKind.Spell, Target = SkillTarget.Enemy, Power = 100,
        };
        var roster = new Roster();
        var battle = new Battle(catalog, d100: () => 0);
        var hero = new Combatant
        {
            Id = 1, Side = CombatSide.Attacker, Hp = 40, MaxHp = 40,
            Dodge = 9, StrikePower = 12, SpellPower = 10
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
            Dodge = 9, StrikePower = 10, SpellPower = 10, CritRate = 100,
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
    public void Loot_rolls_only_from_the_fallen()
    {
        var def = new EnemyDef
        {
            Id = "slime", Name = "软泥", Money = 30, CorePool = 0, ExpPool = 0,
            Loot = { new EnemyLoot { ItemId = "炼金尘", Min = 2, Max = 4, RatePercent = 60 } },
        };
        var battle = new Battle(d100: () => 0);
        battle.Add(new Combatant { Id = 1, Side = CombatSide.Attacker, Hp = 300, MaxHp = 300, Dodge = 9, StrikePower = 10 });
        var slime = Deploy.FromEnemy(def, 2, CombatSide.Defender);
        slime.Hp = 10;
        battle.Add(slime);
        while (battle.PendingActor != null && battle.Outcome == CombatOutcome.Ongoing)
            Assert.True(battle.Act(new CombatAction { ActorId = battle.PendingActor.Id, TargetId = 2 }));
        Assert.Equal(CombatOutcome.AttackerWin, battle.Outcome);

        // 骰 59 < 60 掉落成立；数量 = 2 + 3 % (4-2+1) = 2
        var rolls = new Queue<int>(new[] { 59, 3 });
        var loot = BattleLoot.Roll(battle, CombatSide.Defender, () => rolls.Dequeue());
        Assert.Equal(30, loot.Money);
        var (itemId, count) = Assert.Single(loot.Items);
        Assert.Equal("炼金尘", itemId);
        Assert.Equal(2, count);

        // 骰 60 不小于 60：不掉
        rolls = new Queue<int>(new[] { 60, 0 });
        loot = BattleLoot.Roll(battle, CombatSide.Defender, () => rolls.Dequeue());
        Assert.Equal(30, loot.Money);
        Assert.Empty(loot.Items);
    }

    [Fact]
    public void Ally_and_enemy_get_independent_turns_on_timeline()
    {
        var battle = new Battle(d100: () => 0);
        // 玩家 (速度 15，先动)
        battle.Add(new Combatant { Id = 1, Name = "玩家", Side = CombatSide.Attacker, IsPlayer = true, Hp = 50, MaxHp = 50, StrikePower = 10, Speed = 15 });
        // 队友 (速度 12，次动)：同属控制方，同样等玩家下指令
        battle.Add(new Combatant { Id = 2, Name = "队友", Side = CombatSide.Attacker, IsPlayer = false, Hp = 50, MaxHp = 50, StrikePower = 10, Speed = 12 });
        // 敌人 (速度 10，三动)
        battle.Add(new Combatant { Id = 3, Name = "哥布林", Side = CombatSide.Defender, IsPlayer = false, Hp = 100, MaxHp = 100, StrikePower = 8, Speed = 10 });

        battle.StartBattle();

        // 1. 玩家回合：StepTurn 返回 false 等待玩家决策，玩家直接攻击敌人 3
        var step1 = battle.StepTurn(out var actor1);
        Assert.False(step1);
        Assert.NotNull(actor1);
        Assert.Equal(1, actor1.Id);
        Assert.True(battle.Act(new CombatAction { ActorId = 1, TargetId = 3 }));
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.Hit && e.ActorId == 1 && e.TargetId == 3);

        // 2. 队友回合：控制方未开自动战斗，同样暂停等玩家下指令，由玩家代队友出手。
        var step2 = battle.StepTurn(out var actor2);
        Assert.False(step2);
        Assert.NotNull(actor2);
        Assert.Equal(2, actor2.Id);
        Assert.True(battle.Act(new CombatAction { ActorId = 2, TargetId = 3 }));
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.Hit && e.ActorId == 2 && e.TargetId == 3);

        // 3. 敌人回合：非控制方始终由 AI 自动出招（StepTurn 返回 true）。
        var step3 = battle.StepTurn(out var actor3);
        Assert.True(step3);
        Assert.NotNull(actor3);
        Assert.Equal(3, actor3.Id);
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.Hit && e.ActorId == 3);
    }

    [Fact]
    public void AutoBattle_lets_ai_drive_the_controlled_side()
    {
        var battle = new Battle(d100: () => 0) { AutoBattle = true };
        battle.Add(new Combatant { Id = 1, Name = "玩家", Side = CombatSide.Attacker, IsPlayer = true, Hp = 50, MaxHp = 50, StrikePower = 10, Speed = 15 });
        battle.Add(new Combatant { Id = 3, Name = "哥布林", Side = CombatSide.Defender, IsPlayer = false, Hp = 100, MaxHp = 100, StrikePower = 8, Speed = 10 });

        battle.StartBattle();

        // 开启自动战斗后，我方回合不再暂停，直接由 AI 代打。
        var step = battle.StepTurn(out var actor);
        Assert.True(step);
        Assert.NotNull(actor);
        Assert.Equal(1, actor.Id);
        Assert.Contains(battle.Events, e => e.Kind == CombatEventKind.Hit && e.ActorId == 1);
    }

    [Fact]
    public void Continuous_combat_turns_cycle_without_stalling()
    {
        var catalog = new GameCatalog();
        catalog.Skills["frost_bind"] = new SkillDef
        {
            Id = "frost_bind", Kind = SkillKind.Spell, Target = SkillTarget.Enemy,
            Power = 150, ChantRounds = 1, Range = SkillRange.Ranged
        };

        var battle = new Battle(catalog, d100: () => 0);
        var hero = new Combatant { Id = 1, Name = "玩家", Side = CombatSide.Attacker, IsPlayer = true, Hp = 200, MaxHp = 200, StrikePower = 10, Speed = 15 };
        var mage = new Combatant { Id = 2, Name = "法师", Side = CombatSide.Attacker, IsPlayer = false, Hp = 150, MaxHp = 150, SpellPower = 20, Speed = 12 };
        mage.Skills.Add("frost_bind");
        var foe = new Combatant { Id = 3, Name = "哥布林", Side = CombatSide.Defender, IsPlayer = false, Hp = 1000, MaxHp = 1000, StrikePower = 8, Speed = 10 };

        battle.Add(hero);
        battle.Add(mage);
        battle.Add(foe);
        battle.StartBattle();

        var turnsExecuted = 0;
        var lastTime = -1L;

        for (var step = 0; step < 25 && battle.Outcome == CombatOutcome.Ongoing; step++)
        {
            if (battle.StepTurn(out var actor))
            {
                // 敌方出手或咏唱释放
                turnsExecuted++;
            }
            else if (actor != null && actor.Side == CombatSide.Attacker)
            {
                // 我方回合：玩家下达攻击指令
                var skill = (actor.Id == 2 && actor.Skills.Contains("frost_bind") && actor.Chanting == null)
                    ? "frost_bind"
                    : BattleSkills.AttackId;
                Assert.True(battle.Act(new CombatAction { ActorId = actor.Id, SkillId = skill, TargetId = 3 }));
                turnsExecuted++;
            }
            Assert.True(battle.Time >= lastTime);
            lastTime = battle.Time;
        }

        Assert.True(turnsExecuted >= 15);
    }
}
