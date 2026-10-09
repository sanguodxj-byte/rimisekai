using System;
using System.Linq;
using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Character;

namespace Rimisekai.Combat;

public enum CombatSide
{
    Attacker = 1,
    Defender = 0,
}

public enum CombatOutcome
{
    Ongoing,
    AttackerWin,
    DefenderWin,
    /// <summary>控制方整体撤离。</summary>
    Fled,
    /// <summary>回合数打满仍不分胜负，视防守方守住。</summary>
    Draw,
}

public enum CombatEventKind
{
    /// <summary>新回合开始。</summary>
    Round,
    Miss,
    Hit,
    Heal,
    /// <summary>落了一个增益/减益，Amount 记百分比或点数。</summary>
    Status,
    /// <summary>持续伤害结算，Amount 记本轮伤害。</summary>
    Dot,
    /// <summary>尝试逃跑，Amount 1=成功 0=失败。</summary>
    Flee,
    /// <summary>开始咏唱。</summary>
    Chant,
    /// <summary>咏唱完成，法术施放。</summary>
    SpellFire,
    /// <summary>咏唱被控制打断，法术作废。</summary>
    Interrupt,
    End,
    /// <summary>连战清掉一波，进入补给回合。Amount 记清掉的是第几波。</summary>
    WaveCleared,
    /// <summary>补给回合的回复，ActorId 为受补给者，Amount 记回复量。</summary>
    Supply,
    /// <summary>连战下一波敌人补上。Amount 记这是第几波。</summary>
    WaveArrived,
}

/// <summary>战斗里的一次结算记录。只带 Id 与数字，文案由 UI 侧拼。</summary>
public sealed class BattleEvent
{
    public CombatEventKind Kind { get; init; }
    public int Round { get; init; }
    public int ActorId { get; init; }
    public string SkillId { get; init; } = "";
    public int TargetId { get; init; }
    public int Amount { get; init; }
    public int HpAfter { get; init; }
}

/// <summary>一次出手指令：谁、用哪式、打谁。</summary>
public sealed class CombatAction
{
    public int ActorId { get; init; }
    public string SkillId { get; init; } = BattleSkills.AttackId;
    public int TargetId { get; init; }

    /// <summary>远程与打击格选择的列 1-4；0 = 未选列（近战/自身/全体用不到）。</summary>
    public int TargetColumn { get; init; }
}

/// <summary>战斗中的一个参战者。数值在开战时从角色/敌人行快照进来，不回写角色表。</summary>
public sealed class Combatant
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public CombatSide Side { get; init; }

    private readonly bool? _isPlayer;

    /// <summary>是否主人本人（据点界面等处标识用）。战斗指挥全员参与，与是否玩家操作无关。未显式指定时，进攻方 Id 1 默认为主人（兼容测试）。</summary>
    public bool IsPlayer
    {
        get => _isPlayer ?? (Side == CombatSide.Attacker && Id == 1);
        init => _isPlayer = value;
    }

    public int Hp { get; set; }
    public int MaxHp { get; init; }
    public int Attack { get; init; }
    public int Defence { get; init; }
    public int Dodge { get; init; }
    public int SpellPower { get; init; }

    /// <summary>护甲：实数减挡，物理全额、法术减半。</summary>
    public int Armour { get; init; }
    /// <summary>暴击率（百分点）与暴击倍率（百分比）。角色按感知折算，敌人默认不暴击。</summary>
    public int CritRate { get; init; }
    public int CritMultiplier { get; init; } = BattleRules.CritMultiplier;
    public bool Alive => Hp > 0;

    /// <summary>出手结算快照：武器与流派（熟练回写用）、面板总量、基础命中。</summary>
    public int Level { get; init; } = 1;
    public WeaponType Weapon { get; init; } = WeaponType.Unarmed;
    public int WeaponLevel { get; init; } = 1;
    public StyleType Style { get; init; } = StyleType.Unarmed;
    public int StyleLevel { get; init; } = 1;
    public int StrikePower { get; init; }
    public int BaseHit { get; init; } = BattleRules.BaseHit;

    /// <summary>会用的技能。普通攻击与防御架势人人自带。</summary>
    public List<string> Skills { get; } = new() { BattleSkills.AttackId };

    /// <summary>
    /// 咏唱中：正在咏唱的技能 Id（null = 没在咏唱）。
    /// 咏唱期间被任意控制状态命中即打断，法术作废。
    /// </summary>
    public string? Chanting { get; set; }
    public long ChantFireAt { get; set; } = -1;
    public int ChantTargetId { get; set; }
    public int ChantTargetColumn { get; set; } = -1;

    /// <summary>速咏被动：咏唱回合 -1（下限 1）。随角色快照进来。</summary>
    public bool QuickChant { get; init; }

    /// <summary>行动速度。决定跑条上的行动间隔，仅战斗作用。</summary>
    public int Speed { get; init; } = 10;

    /// <summary>下次可行动的时点（跑条位置）。</summary>
    public long NextActAt { get; set; }
    public List<StatusEffect> Statuses { get; } = new();

    /// <summary>奖励结算用的战绩。</summary>
    public int DamageDealt { get; set; }
    public int Kills { get; set; }

    /// <summary>击坠后的金钱与掉落，由目录行带入；掉落只从倒下者身上掷。</summary>
    public long MoneyReward { get; init; }
    public List<EnemyLoot> Loot { get; } = new();

    /// <summary>物理出手总量吃 Attack 类状态修正。</summary>
    public int EffStrikePower => Mod(StrikePower, StatusStat.Attack);
    public int EffDefence => Mod(Defence, StatusStat.Defence);
    /// <summary>威胁等级 1-5：越高越靠前。玩家方决定头像排位，敌方决定遮盖层级；前排阵亡后排向前推进时动态改变。</summary>
    public int ThreatTier { get; set; } = 1;
    /// <summary>站位列 1-4：跨列占位以其最左列声明。</summary>
    public int Column { get; init; } = 1;

    /// <summary>占位边长：1=1x1，2=精英 2x2，4=首领 4x4。</summary>
    public int Size { get; init; } = 1;

    /// <summary>行动点数：内容声明的行动段数，战斗界面在首领血条下方按数目画实心菱。默认 1。</summary>
    public int ActionPoints { get; init; } = 1;

    /// <summary>立绘资产名（assets/portraits/monster/ 下的文件名），空串表示暂无立绘。</summary>
    public string Portrait { get; init; } = "";

    public int EffDodge => Mod(Dodge, StatusStat.Dodge);
    public int EffSpellPower => Mod(SpellPower, StatusStat.SpellPower);

    /// <summary>防御折成的减伤百分比（已含状态与封顶）。</summary>
    public int DefPercent => Math.Min(
        EffDefence * BattleRules.DefPercentPerPoint, BattleRules.DefCapPercent);

    /// <summary>有效速度：吃加速/减速状态，下限 1。</summary>
    public int EffSpeed => Math.Max(1, Mod(Speed, StatusStat.Speed));

    /// <summary>行动间隔：基准耗时 ÷ 有效速度，下限 20。</summary>
    public long ActInterval => Math.Max(20, BattleRules.ActTicks * 10 / EffSpeed);

    private int Mod(int baseValue, StatusStat stat)
    {
        var percent = 0;
        foreach (var s in Statuses)
            if (s.Kind == StatusKind.StatMod && s.Stat == stat)
                percent += s.Percent;
        return Math.Max(0, baseValue * (100 + percent) / 100);
    }
}

/// <summary>战斗数值规则。全部集中在这里，调参先过主人。</summary>
public static class BattleRules
{
    /// <summary>物理基础命中（百分点），吃武器熟练加成后再减目标闪避。</summary>
    public const int BaseHit = 90;
    /// <summary>命中上下限。</summary>
    public const int MinHit = 5;
    public const int MaxHit = 100;
    /// <summary>防御换算减伤：每点防御折 2% 减伤，封顶 60%。</summary>
    public const int DefPercentPerPoint = 2;
    public const int DefCapPercent = 60;
    /// <summary>法术威力 = 法强 × Power% × 此系数。</summary>
    public const int SpellScale = 3;
    /// <summary>治疗量 = 法强 × Power% × 此系数。</summary>
    public const int HealScale = 2;
    /// <summary>法术只吃减伤与护甲的这一份（分母）。</summary>
    public const int SpellDefShare = 2;
    /// <summary>暴击倍率（百分比），只对打击类生效。</summary>
    public const int CritMultiplier = 150;
    /// <summary>暴击率基准与上限（百分点）。</summary>
    public const int BaseCritRate = 3;
    public const int CritRateCap = 20;
    public const int MinDamage = 1;
    /// <summary>回合上限，打满判 Draw。</summary>
    public const int RoundLimit = 30;
    /// <summary>连战补给回合：在场我方各回复最大生命的百分比。</summary>
    public const int SupplyHealPercent = 30;
    /// <summary>防御架势：防御提升幅度（基础值百分比）。</summary>
    public const int GuardPercent = 50;
    /// <summary>逃跑基准率，按双方均躲闪差修正，夹在 FleeMin~FleeMax。</summary>
    public const int FleeBase = 60;
    public const int FleeMin = 20;
    public const int FleeMax = 95;
    /// <summary>咏唱回合下限：再快也要一回合咏唱，被动只能减到这一步。</summary>
    public const int MinChantRounds = 1;

    /// <summary>基准行动耗时：速度 10 的角色每此时长行动一次。</summary>
    public const int ActTicks = 100;

    /// <summary>一"轮"的时长：状态/冷却/DoT 的结算单位。</summary>
    public const int RoundTicks = 1000;
}

/// <summary>
/// 共享速度跑条战斗。全员按行动速度排进同一条时间轴：
/// 行动间隔 = 基准耗时 ÷ 速度，速度越高出手越密。
/// 控制方（默认进攻方）由 <see cref="Act"/>/<see cref="TryFlee"/> 下指令，
/// 另一侧由内置 AI 代打；法术咏唱占用时间轴，被任意控制命中即打断。
/// 状态/持续伤害按整轮结算（<see cref="BattleRules.RoundTicks"/>），打满 RoundLimit 判 Draw。
/// </summary>
public sealed class Battle
{
    /// <summary>参战上限：敌方满阵 4x4 十六只，加玩家队伍，留足余量。</summary>
    public const int MaxMembers = 24;

    /// <summary>战斗发生地的地区/地城名称（与领地同级，如“迷宫地下城”）。战斗绝不在领地内。</summary>
    public string PlaceName { get; init; } = "迷宫地下城";

    private readonly GameCatalog? _catalog;
    private readonly Func<int>? _d100;
    private readonly CombatSide _controlled;
    private bool _started;
    private long _time;

    public List<Combatant> Members { get; } = new();

    /// <summary>后备位（上限 2 人）：场上四人有人退场时，按威胁等级降序自动顶上接入时间轴。</summary>
    public List<Combatant> Reserves { get; } = new();

    public long Time => _time;
    public int Round { get; private set; } = 1;
    public CombatOutcome Outcome { get; private set; } = CombatOutcome.Ongoing;
    public List<BattleEvent> Events { get; } = new();

    public const int MaxAwakening = 100;

    /// <summary>觉醒计量槽（0~100）：根据双方受到伤害占最大生命百分比累计进度。</summary>
    public int AwakeningGauge { get; set; }

    /// <summary>觉醒是否满值可点击触发。</summary>
    public bool IsAwakeningReady => AwakeningGauge >= MaxAwakening;

    /// <summary>觉醒是否已激活：激活后下一个我方行动效果值翻倍，出手后自动消耗重置。</summary>
    public bool AwakeningActive { get; set; }

    /// <summary>战斗是否已启动开战循环。</summary>
    public bool BattleBegan { get; set; }

    /// <summary>当前行动角色使用的行动名称（攻击、防御、技能名、道具名等），供中上方行动指示容器展示。</summary>
    public string CurrentActionName { get; set; } = BattleSkills.Attack.Name;

    /// <summary>自动战斗：开启后控制方整队交给内置 AI 代打，玩家不再逐个下指令。</summary>
    public bool AutoBattle { get; set; }

    /// <summary>满值后触发觉醒，消耗全部槽量并使下一个我方行动效果值翻倍。</summary>
    public bool TriggerAwakening()
    {
        if (!IsAwakeningReady || AwakeningActive)
            return false;
        AwakeningGauge = 0;
        AwakeningActive = true;
        Events.Add(new BattleEvent
        {
            Kind = CombatEventKind.Status,
            Round = Round,
            ActorId = 0,
            SkillId = "awakening",
            Amount = 200,
        });
        return true;
    }

    /// <summary>根据双方受到伤害占最大生命百分比累加觉醒进度。</summary>
    private void AccumulateAwakening(int damage, int maxHp)
    {
        if (maxHp <= 0 || damage <= 0)
            return;
        var percent = Math.Max(1, (int)Math.Round((float)damage / maxHp * 100f));
        AwakeningGauge = Math.Min(MaxAwakening, AwakeningGauge + percent);
    }

    /// <summary>UI 只替这一侧下指令，另一侧由内置 AI 代打。</summary>
    public CombatSide ControlledSide => _controlled;

    public Battle(GameCatalog? catalog = null, Func<int>? d100 = null, CombatSide controlled = CombatSide.Attacker)
    {
        _catalog = catalog;
        _d100 = d100;
        _controlled = controlled;
    }

    public bool Add(Combatant c)
    {
        if (Members.Count >= MaxMembers || Outcome != CombatOutcome.Ongoing)
            return false;
        Members.Add(c);
        return true;
    }

    /// <summary>添加后备成员（上限 2 人），场上有人倒下时替补登场。</summary>
    public bool AddReserve(Combatant c)
    {
        if (Reserves.Count >= 2 || Outcome != CombatOutcome.Ongoing)
            return false;
        Reserves.Add(c);
        return true;
    }

    public Combatant? Find(int id) => Members.Find(m => m.Id == id);

    /// <summary>当前时间轴上最先到期的行动者（存活、未在咏唱中且 NextActAt 最小）。</summary>
    public Combatant? CurrentActor =>
        Members.Where(m => m.Alive && m.Chanting == null).OrderBy(m => m.NextActAt).FirstOrDefault();

    /// <summary>
    /// 时间轴单步流转：向前推进到下一个行动者。
    /// 若下一个行动者属于控制方（我方），暂停推进并等待玩家输入（返回 false，actedUnit 为行动者）；
    /// 仅当开启自动战斗（<see cref="AutoBattle"/>）时，控制方才交给内置 AI 代打。
    /// 非控制方（敌方）始终由 AI 自动执行其回合行动（返回 true，actedUnit 为行动者）；
    /// 战斗结束返回 false，actedUnit 为 null。
    /// </summary>
    public bool StepTurn(out Combatant? actedUnit)
    {
        actedUnit = null;
        if (Outcome != CombatOutcome.Ongoing || SupplyRound)
            return false;

        if (!_started)
            StartBattle();

        // 检查是否有咏唱到点完成并施放
        if (Members.Any(m => m.Alive && m.Chanting != null && m.ChantFireAt <= _time))
        {
            var done = Members.Find(m => m.Alive && m.Chanting != null && m.ChantFireAt <= _time)!;
            ReleaseChant(done);
            actedUnit = done;
            return true;
        }

        var nextAct = Members.Where(m => m.Alive && m.Chanting == null)
            .Select(m => m.NextActAt).DefaultIfEmpty(long.MaxValue).Min();
        var nextFire = Members.Where(m => m.Alive && m.Chanting != null)
            .Select(m => m.ChantFireAt).DefaultIfEmpty(long.MaxValue).Min();
        var boundary = Round * (long)BattleRules.RoundTicks;
        var t = Math.Min(Math.Min(nextAct, nextFire), boundary);

        if (t > _time)
            AdvanceTime(t);

        if (Members.Any(m => m.Alive && m.Chanting != null && m.ChantFireAt <= _time))
        {
            var done = Members.Find(m => m.Alive && m.Chanting != null && m.ChantFireAt <= _time)!;
            ReleaseChant(done);
            actedUnit = done;
            return true;
        }

        if (_time >= boundary)
        {
            EndRound();
            return true;
        }

        var actor = Members.Where(m => m.Alive && m.Chanting == null).OrderBy(m => m.NextActAt).FirstOrDefault();
        if (actor == null)
            return false;

        // 我方回合：等玩家下指令；只有开了自动战斗才由 AI 代打。
        if (actor.Side == _controlled && !AutoBattle)
        {
            actedUnit = actor;
            return false;
        }

        // 自动战斗中的我方、以及敌方：由 AI 自动执行其回合行动。
        AiTurn(actor);
        actor.NextActAt = _time + actor.ActInterval;
        actedUnit = actor;
        return true;
    }

    /// <summary>
    /// 时间轴上下一个该行动的人。非控制方或队友由 AI 自动代打；
    /// 咏唱中的成员不行动（其完成时点占着时间轴，到点自动施放）。
    /// 战斗结束返回 null。
    /// </summary>
    public Combatant? PendingActor
    {
        get
        {
            if (Outcome != CombatOutcome.Ongoing || SupplyRound)
                return null;
            if (!_started)
                StartBattle();
            while (Outcome == CombatOutcome.Ongoing && !SupplyRound)
            {
                var nextAct = Members.Where(m => m.Alive && m.Chanting == null)
                    .Select(m => m.NextActAt).DefaultIfEmpty(long.MaxValue).Min();
                var nextFire = Members.Where(m => m.Alive && m.Chanting != null)
                    .Select(m => m.ChantFireAt).DefaultIfEmpty(long.MaxValue).Min();
                var boundary = Round * (long)BattleRules.RoundTicks;
                var t = Math.Min(Math.Min(nextAct, nextFire), boundary);
                if (t > _time)
                    AdvanceTime(t);

                if (Members.Any(m => m.Alive && m.Chanting != null && m.ChantFireAt <= _time))
                {
                    var done = Members.Find(m => m.Alive && m.Chanting != null && m.ChantFireAt <= _time)!;
                    ReleaseChant(done);
                    continue;
                }
                if (_time >= boundary)
                {
                    EndRound();
                    continue;
                }

                var actor = Members.Where(m => m.Alive && m.Chanting == null).OrderBy(m => m.NextActAt).FirstOrDefault();
                if (actor == null)
                    continue;
                // 我方回合等玩家指令；只有自动战斗时才由 AI 代打。
                if (actor.Side == _controlled && !AutoBattle)
                    return actor;
                AiTurn(actor);
                actor.NextActAt = _time + actor.ActInterval;
            }
            return null;
        }
    }

    public bool CanAct(CombatAction action)
    {
        if (!_started)
            StartBattle();
        var actor = PendingActor;
        if (actor == null || action.ActorId != actor.Id)
            return false;
        var def = SkillOf(actor, action.SkillId);
        if (def == null || ResolveTargets(actor, def, action.TargetId, action.TargetColumn).Count == 0)
            return false;
        return true;
    }

    /// <summary>当前行动者出一次手。非法动作返回 false，不产生任何变化。</summary>
    public bool Act(CombatAction action)
    {
        if (!CanAct(action))
            return false;
        var actor = Members.Find(m => m.Id == action.ActorId)!;
        var def = SkillOf(actor, action.SkillId)!;
        Perform(actor, def, action.TargetId, action.TargetColumn);
        // 出完手排回时间轴：咏唱占轴到完成点，其余按行动间隔。
        actor.NextActAt = def.ChantRounds > 0
            ? _time + def.ChantRounds * (long)BattleRules.RoundTicks
            : _time + ActionInterval(actor);
        return true;
    }

    /// <summary>
    /// 控制方整体尝试撤离。成功按 Fled 收场；失败耗掉当前行动者的一次行动。
    /// 返回是否逃成。战斗已分胜负时返回 false。
    /// </summary>
    public bool TryFlee()
    {
        var actor = PendingActor;
        if (actor == null)
            return false;
        CurrentActionName = "撤退";
        var ours = Members.FindAll(m => m.Alive && m.Side == _controlled);
        var theirs = Members.FindAll(m => m.Alive && m.Side != _controlled);
        var chance = BattleRules.FleeBase + AvgDodge(ours) - AvgDodge(theirs);
        chance = Math.Clamp(chance, BattleRules.FleeMin, BattleRules.FleeMax);
        var success = Roll() < chance;
        Events.Add(new BattleEvent
        {
            Kind = CombatEventKind.Flee,
            Round = Round,
            ActorId = actor.Id,
            Amount = success ? 1 : 0,
            HpAfter = actor.Hp,
        });
        if (success)
        {
            SetOutcome(CombatOutcome.Fled);
            return true;
        }
        actor.NextActAt = _time + actor.ActInterval;
        return false;
    }

    /// <summary>当前行动者立即可用的技能表，UI 菜单直接读。</summary>
    public List<SkillDef> Menu()
    {
        var list = new List<SkillDef>();
        var actor = PendingActor;
        if (actor == null)
            return list;
        foreach (var id in actor.Skills)
        {
            var def = SkillOf(actor, id);
            if (def != null)
                list.Add(def);
        }
        return list;
    }

    /// <summary>查技能定义：目录优先，缺了退回内置两式。</summary>
    public SkillDef? Lookup(string skillId)
    {
        if (_catalog != null && _catalog.Skills.TryGetValue(skillId, out var def))
            return def;
        if (skillId == BattleSkills.AttackId)
            return BattleSkills.Attack;
        if (skillId == BattleSkills.GuardId)
            return BattleSkills.Guard;
        // 能力表：内置技能目录（按流派与熟练等级解锁的那套）。
        return SkillTable.Get(skillId);
    }

    private SkillDef? SkillOf(Combatant actor, string skillId) =>
        actor.Skills.Contains(skillId) ? Lookup(skillId) : null;

    public void StartBattle()
    {
        _started = true;
        _time = 0;
        // 开局收拢阵型：前排留空时后排直接上前，不留真空档。
        AdvanceEnemyFormation();
        // 首次行动时点 = 各自的行动间隔：速度高者先手。
        foreach (var m in Members)
            if (m.Alive)
                m.NextActAt = m.ActInterval;
        Events.Add(new BattleEvent { Kind = CombatEventKind.Round, Round = Round });
    }

    /// <summary>行动间隔：基准 1000 ÷ 速度，下限 100。速度 10 与旧基准等速。</summary>
    public static long ActionInterval(Combatant c) => c.ActInterval;

    /// <summary>
    /// 推进战斗时钟到 t：途中每跨过 <see cref="BattleRules.RoundTicks"/> 结算一次
    /// （状态时长、冷却、DoT、咏唱完成施放）。
    /// </summary>
    /// <summary>推进到下一个整轮结算点（状态/冷却/DoT/咏唱）。</summary>
    public void AdvanceRound() => AdvanceTime(Round * (long)BattleRules.RoundTicks);

    /// <summary>推进战斗时钟到 t：途中每跨过 <see cref="BattleRules.RoundTicks"/> 结算一次
    /// （状态时长、DoT、咏唱完成施放；冷却走自己的回合，不在此列）。测试与 UI 快进也走这里。</summary>
    public void AdvanceTime(long t)
    {
        while (_time < t && Outcome == CombatOutcome.Ongoing)
        {
            var boundary = Round * (long)BattleRules.RoundTicks;
            if (boundary > t)
            {
                _time = t;
                return;
            }
            _time = boundary;
            EndRound();
        }
    }

    /// <summary>当前时点之后最近的事件时点：行动 / 咏唱完成 / 整轮结算。</summary>
    private long NextEventTime()
    {
        var t = long.MaxValue;
        foreach (var m in Members)
        {
            if (!m.Alive)
                continue;
            if (m.Chanting != null)
                t = Math.Min(t, m.ChantFireAt);
            else
                t = Math.Min(t, m.NextActAt);
        }
        return Math.Min(t, Round * (long)BattleRules.RoundTicks);
    }

    private void EndRound()
    {
        foreach (var m in Members)
        {
            // 弱化的持续伤害：直接扣血（可击杀，记账归施加者），不吃护盾与减伤。
            foreach (var dot in m.Statuses.Where(s => s.Kind == StatusKind.Dot).ToList())
            {
                if (dot.Power <= 0)
                    continue;
                m.Hp = Math.Max(0, m.Hp - dot.Power);
                AccumulateAwakening(dot.Power, m.MaxHp);
                var source = Members.Find(x => x.Id == dot.SourceId);
                if (source != null)
                    source.DamageDealt += dot.Power;
                Events.Add(new BattleEvent
                {
                    Kind = CombatEventKind.Dot, Round = Round, ActorId = dot.SourceId,
                    SkillId = dot.Token, TargetId = m.Id, Amount = dot.Power, HpAfter = m.Hp,
                });
                if (!m.Alive)
                {
                    if (source != null)
                        source.Kills++;
                    if (m.Side == _controlled)
                        CheckReserves(m.Side);
                    else
                        AdvanceEnemyFormation();
                }
            }

            // 时长递减：到期的状态移除（咏唱结算在状态之后）。
            for (var i = m.Statuses.Count - 1; i >= 0; i--)
            {
                m.Statuses[i].RoundsLeft--;
                if (m.Statuses[i].RoundsLeft <= 0)
                    m.Statuses.RemoveAt(i);
            }

            // 咏唱完成由时间轴驱动（Clock 跨过 ChantFireAt 时施放），此处不处理。
        }
        Round++;
        if (Round - _waveStartRound > BattleRules.RoundLimit)
            SetOutcome(CombatOutcome.Draw);
    }

    private void Perform(Combatant actor, SkillDef def, int targetId, int column = 0)
    {
        CurrentActionName = def.Name;
        // 法术：进入咏唱。咏唱占用时间轴，到点自动施放；
        // 咏唱期间被任意控制状态命中即打断，法术作废。速咏被动缩短时长（下限一轮）。
        if (def.ChantRounds > 0)
        {
            var rounds = Math.Max(BattleRules.MinChantRounds,
                def.ChantRounds - (actor.QuickChant ? 1 : 0));
            actor.Chanting = def.Id;
            actor.ChantTargetId = targetId;
            actor.ChantTargetColumn = column;
            actor.ChantFireAt = _time + rounds * (long)BattleRules.RoundTicks;
            actor.NextActAt = actor.ChantFireAt;
            Events.Add(new BattleEvent
            {
                Kind = CombatEventKind.Chant, Round = Round, ActorId = actor.Id,
                SkillId = def.Id, TargetId = targetId,
            });
            return;
        }

        foreach (var target in ResolveTargets(actor, def, targetId, column))
            ApplySkill(actor, def, target);

        if (actor.Side == _controlled && AwakeningActive)
            AwakeningActive = false;
    }

    /// <summary>咏唱完成：按咏唱开始时的技能施放，目标按当时存活重定向。</summary>
    private void ReleaseChant(Combatant actor)
    {
        var def = Lookup(actor.Chanting!);
        actor.Chanting = null;
        if (def == null)
            return;
        CurrentActionName = def.Name;
        foreach (var target in ResolveTargets(actor, def, actor.ChantTargetId, actor.ChantTargetColumn))
            ApplySkill(actor, def, target);
        Events.Add(new BattleEvent
        {
            Kind = CombatEventKind.SpellFire, Round = Round, ActorId = actor.Id,
            SkillId = def.Id, TargetId = actor.ChantTargetId,
        });

        actor.NextActAt = _time + ActionInterval(actor);

        if (actor.Side == _controlled && AwakeningActive)
            AwakeningActive = false;
    }

    /// <summary>控制命中咏唱者：咏唱被打断，法术作废。</summary>
    private void InterruptChant(Combatant target)
    {
        if (target.Chanting == null)
            return;
        Events.Add(new BattleEvent
        {
            Kind = CombatEventKind.Interrupt, Round = Round, ActorId = target.Id,
            SkillId = target.Chanting,
        });
        target.Chanting = null;
        target.ChantTargetId = -1;
    }

    /// <summary>
    /// 结算真正的落点。玩家侧：点哪列就打那列最前面的（未点列取全场最前排），确定性不掷骰，
    /// 预测值与实际落点永远一致；敌方（含自动战斗的我方）：随机打一个威胁等级最高的。
    /// 打击格与全体照直返回整批。敌方人数上限 4x4 是内容约束，站位由敌人行声明。
    /// </summary>
    public List<Combatant> ResolveTargets(Combatant actor, SkillDef def, int targetId, int column = 0)
    {
        var list = new List<Combatant>();
        switch (def.Target)
        {
            case SkillTarget.Enemy:
            {
                var foes = AliveFoes(actor);
                if (foes.Count == 0)
                    break;

                // 显式指定合法存活对手目标优先（如测试或玩家点选）
                if (targetId > 0)
                {
                    var named = Members.Find(m => m.Id == targetId && m.Alive && m.Side != actor.Side);
                    if (named != null)
                        list.Add(named);
                    break;
                }

                if (actor.Side == _controlled && !AutoBattle)
                {
                    // 玩家侧：点哪列就打那列最前面的；未点列取全场最前排。
                    var pool = column > 0 ? InColumn(foes, column) : foes;
                    if (pool.Count > 0)
                        list.Add(FrontmostIn(pool));
                }
                else
                {
                    // 敌方与自动战斗的我方：随机打一个威胁等级最高的。
                    var front = FrontGroup(foes);
                    list.Add(front[Roll() % front.Count]);
                }
                break;
            }
            case SkillTarget.FoesColumn:
                list.AddRange(InColumn(AliveFoes(actor), column));
                break;
            case SkillTarget.Ally:
            {
                var ally = Members.Find(m => m.Id == targetId && m.Alive && m.Side == actor.Side);
                if (ally != null)
                    list.Add(ally);
                break;
            }
            case SkillTarget.Self:
                list.Add(actor);
                break;
            case SkillTarget.AllEnemies:
                list.AddRange(AliveFoes(actor));
                break;
            case SkillTarget.AllAllies:
                foreach (var m in Members)
                    if (m.Alive && m.Side == actor.Side)
                        list.Add(m);
                break;
        }
        return list;
    }

    private List<Combatant> AliveFoes(Combatant actor) =>
        Members.FindAll(m => m.Alive && m.Side != actor.Side);

    /// <summary>占在某一列上的对手：跨列的精英/首领，其覆盖到的列都算。</summary>
    private static List<Combatant> InColumn(List<Combatant> pool, int column) =>
        pool.FindAll(m => m.Column <= column && column < m.Column + m.Size);

    /// <summary>最前排：对手里威胁等级最高的那批（并列都在最前）。</summary>
    private static List<Combatant> FrontGroup(List<Combatant> pool)
    {
        var top = pool.Max(m => m.ThreatTier);
        return pool.FindAll(m => m.ThreatTier == top);
    }

    /// <summary>
    /// 最前面的那一个（确定性，不掷骰）：威胁等级最高的一批里，取列号最小者；
    /// 同列再按 Id 取小，保证结果唯一稳定。玩家侧落点与预测值因此永远一致。
    /// </summary>
    private static Combatant FrontmostIn(List<Combatant> pool) =>
        FrontGroup(pool).OrderBy(m => m.Column).ThenBy(m => m.Id).First();

    private void ApplySkill(Combatant actor, SkillDef def, Combatant target)
    {
        // 控制类技能命中咏唱者：咏唱被打断，法术作废。任意行动种类皆可携带控制。
        if (def.Control && target.Chanting != null)
            InterruptChant(target);
        switch (def.Kind)
        {
            case SkillKind.Strike:
            {
                var hit = Math.Clamp(
                    actor.BaseHit + def.HitMod - target.EffDodge,
                    BattleRules.MinHit, BattleRules.MaxHit);
                if (Roll() >= hit)
                {
                    Events.Add(new BattleEvent
                    {
                        Kind = CombatEventKind.Miss, Round = Round, ActorId = actor.Id,
                        SkillId = def.Id, TargetId = target.Id, HpAfter = target.Hp,
                    });
                    return;
                }
                var mult = (actor.Side == _controlled && AwakeningActive) ? 2 : 1;
                var damage = ResolveDamage(actor, target, def) * mult;
                if (actor.CritRate > 0 && Roll() < actor.CritRate)
                    damage = damage * actor.CritMultiplier / 100;
                Damage(actor, target, damage, def.Id);
                break;
            }
            case SkillKind.Spell:
            {
                // 法术必中、不暴击，只吃一半减伤与护甲
                var mult = (actor.Side == _controlled && AwakeningActive) ? 2 : 1;
                var damage = ResolveDamage(actor, target, def) * mult;
                Damage(actor, target, damage, def.Id);
                break;
            }
            case SkillKind.Heal:
            {
                var mult = (actor.Side == _controlled && AwakeningActive) ? 2 : 1;
                var amount = Math.Max(1, actor.EffSpellPower * def.Power / 100 * BattleRules.HealScale) * mult;
                var healed = Math.Min(amount, target.MaxHp - target.Hp);
                target.Hp += healed;
                Events.Add(new BattleEvent
                {
                    Kind = CombatEventKind.Heal, Round = Round, ActorId = actor.Id,
                    SkillId = def.Id, TargetId = target.Id, Amount = healed, HpAfter = target.Hp,
                });
                break;
            }
        case SkillKind.Buff:
        {
            ApplyStatus(actor, def, target);
            break;
        }
    }

    // 附加状态：打击/法术命中后附带（淬毒、冻伤这类），增益类上面已直接生效。
    if (def.Status.HasValue && def.Kind != SkillKind.Buff)
        ApplyStatus(actor, def, target);
}

    /// <summary>
    /// 上状态：按技能的状态定义组一条 StatusEffect，同 Token 刷新。
    /// 大类自动推导：DoT 恒为弱化，StatMod 按幅度正负，Shield 为增益。
    /// </summary>
    private void ApplyStatus(Combatant actor, SkillDef def, Combatant target)
    {
        if (def.Status == null || def.StatusRounds <= 0)
            return;
        var category = def.Status == StatusKind.Dot ? StatusCategory.Debuff
            : def.StatusPercent < 0 ? StatusCategory.Debuff
            : StatusCategory.Buff;

        // 同 Token 刷新：移除旧条目再上新的。
        target.Statuses.RemoveAll(s => s.Token == def.Id);
        target.Statuses.Add(new StatusEffect
        {
            Token = def.Id,
            Name = def.Name,
            Category = category,
            Kind = def.Status.Value,
            Stat = def.StatusStat,
            Percent = def.StatusPercent,
            Power = def.StatusPower,
            Points = def.StatusPower,
            SourceId = actor.Id,
            RoundsLeft = def.StatusRounds,
        });
        Events.Add(new BattleEvent
        {
            Kind = CombatEventKind.Status, Round = Round, ActorId = actor.Id,
            SkillId = def.Id, TargetId = target.Id,
            Amount = def.Status == StatusKind.Dot ? def.StatusPower : def.StatusPercent,
            HpAfter = target.Hp,
        });
    }

    /// <summary>
    /// 预估一项技能对**默认目标**的伤害/治疗量，供界面画在技能按钮右侧。
    /// 与 <see cref="ResolveDamage"/> 同一条公式、同一个取目标口径
    /// （近战取最前排、远程按威胁权重、全体取全体），因此界面显示的数值
    /// 就是实际打出的一般水平；未计暴击与护盾（那是随机成分，不承诺）。
    /// 返回 0 表示这项技能不产生可预估的直接量（增益/管理等）。
    /// </summary>
    public int Predict(int actorId, string skillId)
    {
        var actor = Find(actorId);
        if (actor == null)
            return 0;
        // 只预估这名角色确实拥有的技能：不拿内置兜底值给按钮上的数字，
        // 否则没这技能也会显示一个像模像样的伤害量，纯属误导。
        var def = SkillOf(actor, skillId);
        if (def == null)
            return 0;

        if (def.Kind == SkillKind.Heal)
        {
            var hurt = WeakestHurt(actor.Side);
            if (hurt == null)
                return 0;
            return Math.Max(BattleRules.MinDamage, actor.EffSpellPower * def.Power / 100 * BattleRules.HealScale);
        }

        if (def.Kind is SkillKind.Strike or SkillKind.Spell)
        {
            var targets = ResolveTargets(actor, def, 0);
            if (targets.Count == 0)
                return 0;
            // AOE 取全体之和对单人技能的期望值上限，体现「这一式能打多少」。
            var total = 0;
            foreach (var t in targets)
                total += ResolveDamage(actor, t, def);
            return Math.Max(BattleRules.MinDamage, total);
        }

        return 0;
    }

    /// <summary>
    /// 双层减伤结算：先按目标防御折出的减伤百分比乘算，再扣护甲实数；
    /// 法术两层各吃一半。返回未计暴击与护盾的伤害，保底 1。
    /// </summary>
    private int ResolveDamage(Combatant actor, Combatant target, SkillDef def)
    {
        if (def.Kind == SkillKind.Spell)
        {
            var raw = actor.EffSpellPower * def.Power / 100 * BattleRules.SpellScale;
            var percent = target.DefPercent / BattleRules.SpellDefShare;
            var armour = target.Armour / BattleRules.SpellDefShare;
            return Math.Max(BattleRules.MinDamage, raw * (100 - percent) / 100 - armour);
        }
        var strike = actor.EffStrikePower * def.Power / 100;
        return Math.Max(
            BattleRules.MinDamage,
            strike * (100 - target.DefPercent) / 100 - target.Armour);
    }

    private void Damage(Combatant actor, Combatant target, int damage, string skillId)
    {
        target.Hp = Math.Max(0, target.Hp - damage);
        actor.DamageDealt += damage;
        AccumulateAwakening(damage, target.MaxHp);
        if (!target.Alive)
        {
            actor.Kills++;
            if (Reserves.Exists(r => r.Side == target.Side))
                CheckReserves(target.Side);
            else if (target.Side != _controlled)
                AdvanceEnemyFormation();
        }
        Events.Add(new BattleEvent
        {
            Kind = CombatEventKind.Hit, Round = Round, ActorId = actor.Id,
            SkillId = skillId, TargetId = target.Id, Amount = damage, HpAfter = target.Hp,
        });
        Judge();
    }

    /// <summary>
    /// 当敌方前排有单位阵亡后，后排存活单位向前走位填补空位。
    /// 每列（或多格单位覆盖的全部列）前方若无存活阻挡，单位沿深度向前推进（ThreatTier 上升）。
    /// </summary>
    public void AdvanceEnemyFormation()
    {
        var foes = Members.FindAll(m => m.Alive && m.Side != _controlled);
        if (foes.Count == 0)
            return;

        bool moved;
        do
        {
            moved = false;
            // 占用网格：4 列 x 4 层深度 (depth 0=最前, 3=最后)
            var grid = new Combatant?[4, 4];
            foreach (var f in foes)
            {
                var depth = 4 - Math.Clamp(f.ThreatTier, 1, 4);
                var left = Math.Clamp(f.Column, 1, 4) - 1;
                for (var dr = 0; dr < f.Size && depth + dr < 4; dr++)
                    for (var dc = 0; dc < f.Size && left + dc < 4; dc++)
                        grid[left + dc, depth + dr] = f;
            }

            // 按威胁等级降序（即深度由浅到深，靠前者先尝试推进）
            var sorted = foes.OrderByDescending(f => f.ThreatTier).ToList();
            foreach (var f in sorted)
            {
                var depth = 4 - Math.Clamp(f.ThreatTier, 1, 4);
                if (depth <= 0)
                    continue; // 已在最前排

                var left = Math.Clamp(f.Column, 1, 4) - 1;
                var targetDepth = depth - 1;

                var canStepForward = true;
                for (var dc = 0; dc < f.Size && left + dc < 4; dc++)
                {
                    var occ = grid[left + dc, targetDepth];
                    if (occ != null && occ != f)
                    {
                        canStepForward = false;
                        break;
                    }
                }

                if (canStepForward)
                {
                    for (var dr = 0; dr < f.Size && depth + dr < 4; dr++)
                        for (var dc = 0; dc < f.Size && left + dc < 4; dc++)
                            grid[left + dc, depth + dr] = null;

                    f.ThreatTier = Math.Min(4, f.ThreatTier + 1);

                    var newDepth = depth - 1;
                    for (var dr = 0; dr < f.Size && newDepth + dr < 4; dr++)
                        for (var dc = 0; dc < f.Size && left + dc < 4; dc++)
                            grid[left + dc, newDepth + dr] = f;

                    moved = true;
                }
            }
        } while (moved);
    }

    /// <summary>后备顶上：当阵营有成员倒下且后备位有人时，按威胁等级最高者顶上并接入时间轴。</summary>
    private void CheckReserves(CombatSide side)
    {
        var forSide = Reserves.FindAll(r => r.Side == side);
        if (forSide.Count == 0)
            return;

        var activeCount = Members.Count(m => m.Side == side && m.Alive);
        while (activeCount < 4 && forSide.Count > 0)
        {
            var candidate = forSide.OrderByDescending(r => r.ThreatTier).ThenBy(r => r.Id).First();
            forSide.Remove(candidate);
            Reserves.Remove(candidate);

            // 顶上者接入时间轴：从当前时钟 + 自身行动间隔开始排队
            candidate.NextActAt = _time + candidate.ActInterval;
            Members.Add(candidate);
            activeCount++;

            Events.Add(new BattleEvent
            {
                Kind = CombatEventKind.Status,
                Round = Round,
                ActorId = candidate.Id,
                Amount = candidate.ThreatTier,
            });
        }
    }

    /// <summary>触发胜负判定（全歼敌方或我方全倒）。</summary>
    public void JudgeOutcome() => Judge();

    private void Judge()
    {
        var atkAlive = Members.Exists(m => m.Side == CombatSide.Attacker && m.Alive)
                       || Reserves.Exists(r => r.Side == CombatSide.Attacker);
        var defAlive = Members.Exists(m => m.Side == CombatSide.Defender && m.Alive)
                       || Reserves.Exists(r => r.Side == CombatSide.Defender);
        if (atkAlive && defAlive)
            return;
        // 连战：这一波清了、后面还有波次，就进补给回合而不是收场。
        if (atkAlive && _waves.Count > 0)
        {
            if (!SupplyRound)
            {
                SupplyRound = true;
                Events.Add(new BattleEvent { Kind = CombatEventKind.WaveCleared, Round = Round, Amount = WaveIndex });
            }
            return;
        }
        SetOutcome(atkAlive ? CombatOutcome.AttackerWin : CombatOutcome.DefenderWin);
    }

    // ---- 连战 ----

    private readonly List<List<Combatant>> _waves = new();
    private int _waveStartRound;

    /// <summary>此刻是第几波（从 1 起）。</summary>
    public int WaveIndex { get; private set; } = 1;

    /// <summary>总波数；非连战为 1。</summary>
    public int WaveTotal => WaveIndex + _waves.Count;

    /// <summary>下一波的敌人（补给回合里给 UI 预告用）；没有下一波为空。</summary>
    public IReadOnlyList<Combatant> NextWave => _waves.Count > 0 ? _waves[0] : System.Array.Empty<Combatant>();

    /// <summary>
    /// 补给回合：一波清完、下一波还没上。期间时间轴停住，没有人行动，
    /// 等玩家 <see cref="Resupply"/> 迎下一波，或 <see cref="Withdraw"/> 见好就收。
    /// </summary>
    public bool SupplyRound { get; private set; }

    /// <summary>排上一波连战敌人（按排队先后依次补上）。开打前、开打后都可排。</summary>
    public void QueueWave(List<Combatant> foes)
    {
        if (foes.Count > 0)
            _waves.Add(foes);
    }

    /// <summary>
    /// 结束补给回合：在场我方各回复 <see cref="BattleRules.SupplyHealPercent"/>% 最大生命、清掉身上状态、咏唱作废，
    /// 然后下一波敌人上阵，双方从此刻重新排时间轴；回合上限按新一波重新计。不在补给回合返回 false。
    /// </summary>
    public bool Resupply()
    {
        if (!SupplyRound || Outcome != CombatOutcome.Ongoing || _waves.Count == 0)
            return false;
        SupplyRound = false;
        foreach (var m in Members.Where(m => m.Alive && m.Side == _controlled))
        {
            var before = m.Hp;
            m.Hp = Math.Min(m.MaxHp, m.Hp + Math.Max(1, m.MaxHp * BattleRules.SupplyHealPercent / 100));
            m.Statuses.Clear();
            m.Chanting = null;
            m.ChantFireAt = -1;
            m.NextActAt = _time + m.ActInterval;
            Events.Add(new BattleEvent
            {
                Kind = CombatEventKind.Supply, Round = Round, ActorId = m.Id, TargetId = m.Id,
                Amount = m.Hp - before, HpAfter = m.Hp,
            });
        }
        var wave = _waves[0];
        _waves.RemoveAt(0);
        WaveIndex++;
        foreach (var f in wave)
        {
            // 倒下的敌人留在名单里（掉落与战绩从他们身上算），新一波不受人数上限卡。
            Members.Add(f);
            f.NextActAt = _time + f.ActInterval;
        }
        AdvanceEnemyFormation();
        _waveStartRound = Round - 1;
        Events.Add(new BattleEvent { Kind = CombatEventKind.WaveArrived, Round = Round, Amount = WaveIndex });
        return true;
    }

    /// <summary>补给回合里见好就收：场上已无敌人，必定撤成，按 Fled 收场（已倒敌人的掉落照拿）。</summary>
    public bool Withdraw()
    {
        if (!SupplyRound || Outcome != CombatOutcome.Ongoing)
            return false;
        SupplyRound = false;
        SetOutcome(CombatOutcome.Fled);
        return true;
    }

    private void SetOutcome(CombatOutcome outcome)
    {
        Outcome = outcome;
        Events.Add(new BattleEvent { Kind = CombatEventKind.End, Round = Round });
    }

    private int Roll() => _d100 != null ? _d100() : System.Random.Shared.Next(100);

    private static int AvgDodge(List<Combatant> list)
    {
        if (list.Count == 0)
            return 0;
        var sum = 0;
        foreach (var c in list)
            sum += c.EffDodge;
        return sum / list.Count;
    }

    // ---- 内置 AI（不在控制侧的行动者由它代打） ----

    private void AiTurn(Combatant actor)
    {
        var foes = AliveFoes(actor);
        if (foes.Count == 0)
            return;

        SkillDef? best = null;
        var bestScore = -1;
        foreach (var id in actor.Skills)
        {
            var def = Lookup(id);
            if (def == null)
                continue;
            var score = def.Kind switch
            {
                SkillKind.Strike or SkillKind.Spell => ResolveDamage(actor, foes[0], def),
                SkillKind.Heal => HealScore(actor, def),
                _ => 0,
            };
            if (score > bestScore)
            {
                bestScore = score;
                best = def;
            }
        }

        var action = best ?? Lookup(BattleSkills.AttackId)!;
        var targetId = 0;
        if (action.Kind == SkillKind.Heal)
        {
            var hurt = WeakestHurt(actor.Side);
            targetId = hurt?.Id ?? actor.Id;
        }
        else
        {
            var targets = ResolveTargets(actor, action, 0);
            if (targets.Count > 0)
                targetId = targets[0].Id;
        }

        Perform(actor, action, targetId);
    }

    /// <summary>有半血以下的同伴才考虑治疗，分给伤得最重的。</summary>
    private int HealScore(Combatant actor, SkillDef def)
    {
        return WeakestHurt(actor.Side) == null
            ? 0
            : Math.Max(1, actor.EffSpellPower * def.Power / 100 * BattleRules.HealScale);
    }

    private Combatant? WeakestHurt(CombatSide side)
    {
        Combatant? weakest = null;
        foreach (var m in Members)
        {
            if (!m.Alive || m.Side != side || m.Hp * 2 >= m.MaxHp)
                continue;
            if (weakest == null || m.Hp < weakest.Hp)
                weakest = m;
        }
        return weakest;
    }
}
