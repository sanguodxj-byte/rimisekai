using System;
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
    /// <summary>落了一个增益/减益，Amount 记百分比。</summary>
    Status,
    /// <summary>尝试逃跑，Amount 1=成功 0=失败。</summary>
    Flee,
    End,
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
}

/// <summary>一条进行中的增益/减益。幅度按目标基础值的百分比记，回合结束掉一格。</summary>
public sealed class BattleStatus
{
    public string Token { get; init; } = "";
    public StatusStat Stat { get; init; }
    public int Percent { get; init; }
    public int RoundsLeft { get; set; }
}

/// <summary>战斗中的一个参战者。数值在开战时从角色/敌人行快照进来，不回写角色表。</summary>
public sealed class Combatant
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public CombatSide Side { get; init; }
    public int Hp { get; set; }
    public int MaxHp { get; init; }
    public int Mp { get; set; }
    public int MaxMp { get; init; }
    public int Attack { get; init; }
    public int Defence { get; init; }
    public int Dodge { get; init; }
    public int SpellPower { get; init; }
    public int Threat { get; init; }

    /// <summary>护甲：实数减挡，物理全额、法术减半。</summary>
    public int Armour { get; init; }
    /// <summary>护盾点数：只挡物理，法术穿透；被增益类技能与部分效果叠加。</summary>
    public int Shield { get; set; }
    /// <summary>暴击率（百分点）与暴击倍率（百分比）。角色按感知折算，敌人默认不暴击。</summary>
    public int CritRate { get; init; }
    public int CritMultiplier { get; init; } = BattleRules.CritMultiplier;
    /// <summary>索敌取向，只在本方行动者由 AI 代打或自动索敌时生效。</summary>
    public TargetMode Targeting { get; init; } = TargetMode.Balanced;
    public bool Alive => Hp > 0;

    /// <summary>出手结算快照：武器与流派（熟练回写用）、面板总量、基础命中。</summary>
    public WeaponType Weapon { get; init; } = WeaponType.Unarmed;
    public StyleType Style { get; init; } = StyleType.Unarmed;
    public int StrikePower { get; init; }
    public int BaseHit { get; init; } = BattleRules.BaseHit;

    /// <summary>会用的技能。普通攻击与防御架势人人自带。</summary>
    public List<string> Skills { get; } = new() { BattleSkills.AttackId, BattleSkills.GuardId };
    public List<BattleStatus> Statuses { get; } = new();
    public Dictionary<string, int> Cooldowns { get; } = new();

    /// <summary>奖励结算用的战绩。</summary>
    public int DamageDealt { get; set; }
    public int Kills { get; set; }

    /// <summary>击坠后的金钱与掉落，由目录行带入；掉落只从倒下者身上掷。</summary>
    public long MoneyReward { get; init; }
    public List<EnemyLoot> Loot { get; } = new();

    public int EffAttack => Mod(Attack, StatusStat.Attack);
    /// <summary>物理出手总量吃 Attack 类状态修正。</summary>
    public int EffStrikePower => Mod(StrikePower, StatusStat.Attack);
    public int EffDefence => Mod(Defence, StatusStat.Defence);
    public int EffDodge => Mod(Dodge, StatusStat.Dodge);
    public int EffSpellPower => Mod(SpellPower, StatusStat.SpellPower);
    /// <summary>敌视权重吃 Threat 类状态修正（挑衅/隐匿），下限 1。</summary>
    public int EffThreat => Math.Max(1, Mod(Threat, StatusStat.Threat));

    /// <summary>防御折成的减伤百分比（已含状态与封顶）。</summary>
    public int DefPercent => Math.Min(
        EffDefence * BattleRules.DefPercentPerPoint, BattleRules.DefCapPercent);

    private int Mod(int baseValue, StatusStat stat)
    {
        var percent = 0;
        foreach (var s in Statuses)
            if (s.Stat == stat)
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
    /// <summary>防御架势：防御提升幅度（基础值百分比）。</summary>
    public const int GuardPercent = 50;
    /// <summary>逃跑基准率，按双方均躲闪差修正，夹在 FleeMin~FleeMax。</summary>
    public const int FleeBase = 60;
    public const int FleeMin = 20;
    public const int FleeMax = 95;
}

/// <summary>
/// 回合制战斗。对应 CLOSE_COMBAT：双方列表、按有效闪避排序行动、一次行动、判定胜负。
/// 控制方（默认进攻方）由 <see cref="Act"/>/<see cref="TryFlee"/> 逐个下指令，
/// 另一侧由内置 AI 代打；一整轮走完结算状态与冷却，打满 <see cref="BattleRules.RoundLimit"/> 判 Draw。
/// </summary>
public sealed class Battle
{
    public const int MaxMembers = 12;

    private readonly GameCatalog? _catalog;
    private readonly Func<int>? _d100;
    private readonly CombatSide _controlled;
    private readonly List<Combatant> _order = new();
    private int _cursor;
    private bool _started;

    public List<Combatant> Members { get; } = new();
    public int Round { get; private set; } = 1;
    public CombatOutcome Outcome { get; private set; } = CombatOutcome.Ongoing;
    public List<BattleEvent> Events { get; } = new();

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

    public Combatant? Find(int id) => Members.Find(m => m.Id == id);

    /// <summary>
    /// 当前轮到谁。非控制方的行动者由 AI 立即代打并顺延；一整轮走完后结算回合
    /// （状态掉格、冷却回落）进入下一轮。战斗结束返回 null。
    /// </summary>
    public Combatant? PendingActor
    {
        get
        {
            if (Outcome != CombatOutcome.Ongoing)
                return null;
            if (!_started)
                StartRound();
            while (true)
            {
                while (_cursor < _order.Count)
                {
                    var actor = _order[_cursor];
                    if (!actor.Alive)
                    {
                        _cursor++;
                        continue;
                    }
                    if (actor.Side == _controlled)
                        return actor;
                    AiTurn(actor);
                    _cursor++;
                    if (Outcome != CombatOutcome.Ongoing)
                        return null;
                }
                EndRound();
                if (Outcome != CombatOutcome.Ongoing)
                    return null;
                StartRound();
            }
        }
    }

    /// <summary>当前行动者用某技能是否合法（只校验，不消耗、不落子）。</summary>
    public bool CanAct(CombatAction action)
    {
        var actor = PendingActor;
        if (actor == null || action.ActorId != actor.Id)
            return false;
        var def = SkillOf(actor, action.SkillId);
        if (def == null || ResolveTargets(actor, def, action.TargetId).Count == 0)
            return false;
        return actor.Mp >= def.MpCost && CooldownLeft(actor, def.Id) <= 0;
    }

    /// <summary>当前行动者出一次手。非法动作返回 false，不产生任何变化。</summary>
    public bool Act(CombatAction action)
    {
        if (!CanAct(action))
            return false;
        var actor = Members.Find(m => m.Id == action.ActorId)!;
        var def = SkillOf(actor, action.SkillId)!;
        Perform(actor, def, action.TargetId);
        _cursor++;
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
        _cursor++;
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
            if (def != null && actor.Mp >= def.MpCost && CooldownLeft(actor, id) <= 0)
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
        return null;
    }

    private SkillDef? SkillOf(Combatant actor, string skillId) =>
        actor.Skills.Contains(skillId) ? Lookup(skillId) : null;

    private int CooldownLeft(Combatant actor, string skillId) =>
        actor.Cooldowns.TryGetValue(skillId, out var left) ? left : 0;

    private void StartRound()
    {
        _started = true;
        _order.Clear();
        foreach (var m in Members)
            if (m.Alive)
                _order.Add(m);
        _order.Sort((a, b) =>
        {
            var byDodge = b.EffDodge.CompareTo(a.EffDodge);
            if (byDodge != 0)
                return byDodge;
            var bySide = a.Side.CompareTo(b.Side);
            return bySide != 0 ? bySide : a.Id.CompareTo(b.Id);
        });
        _cursor = 0;
        Events.Add(new BattleEvent { Kind = CombatEventKind.Round, Round = Round });
    }

    private void EndRound()
    {
        foreach (var m in Members)
        {
            for (var i = m.Statuses.Count - 1; i >= 0; i--)
            {
                m.Statuses[i].RoundsLeft--;
                if (m.Statuses[i].RoundsLeft <= 0)
                    m.Statuses.RemoveAt(i);
            }
            var keys = new List<string>(m.Cooldowns.Keys);
            foreach (var key in keys)
            {
                m.Cooldowns[key]--;
                if (m.Cooldowns[key] <= 0)
                    m.Cooldowns.Remove(key);
            }
        }
        Round++;
        if (Round > BattleRules.RoundLimit)
            SetOutcome(CombatOutcome.Draw);
    }

    private void Perform(Combatant actor, SkillDef def, int targetId)
    {
        actor.Mp -= def.MpCost;
        if (def.Cooldown > 0)
            actor.Cooldowns[def.Id] = def.Cooldown;
        foreach (var target in ResolveTargets(actor, def, targetId))
            ApplySkill(actor, def, target);
    }

    public List<Combatant> ResolveTargets(Combatant actor, SkillDef def, int targetId)
    {
        var list = new List<Combatant>();
        switch (def.Target)
        {
            case SkillTarget.Enemy:
                var enemy = Members.Find(m => m.Id == targetId && m.Alive && m.Side != actor.Side);
                if (enemy != null)
                    list.Add(enemy);
                break;
            case SkillTarget.Ally:
                var ally = Members.Find(m => m.Id == targetId && m.Alive && m.Side == actor.Side);
                if (ally != null)
                    list.Add(ally);
                break;
            case SkillTarget.Self:
                list.Add(actor);
                break;
            case SkillTarget.AllEnemies:
                foreach (var m in Members)
                    if (m.Alive && m.Side != actor.Side)
                        list.Add(m);
                break;
            case SkillTarget.AllAllies:
                foreach (var m in Members)
                    if (m.Alive && m.Side == actor.Side)
                        list.Add(m);
                break;
        }
        return list;
    }

    private void ApplySkill(Combatant actor, SkillDef def, Combatant target)
    {
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
                var damage = ResolveDamage(actor, target, def);
                if (actor.CritRate > 0 && Roll() < actor.CritRate)
                    damage = damage * actor.CritMultiplier / 100;
                damage = AbsorbShield(target, damage);
                Damage(actor, target, damage, def.Id);
                break;
            }
            case SkillKind.Spell:
            {
                // 法术必中、穿透护盾、不暴击，只吃一半减伤与护甲
                var damage = ResolveDamage(actor, target, def);
                Damage(actor, target, damage, def.Id);
                break;
            }
            case SkillKind.Heal:
            {
                var amount = Math.Max(1, actor.EffSpellPower * def.Power / 100 * BattleRules.HealScale);
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
                if (def.Stat != StatusStat.None && def.StatusRounds > 0)
                {
                    target.Statuses.Add(new BattleStatus
                    {
                        Token = def.Id, Stat = def.Stat,
                        Percent = def.StatusPercent, RoundsLeft = def.StatusRounds,
                    });
                    Events.Add(new BattleEvent
                    {
                        Kind = CombatEventKind.Status, Round = Round, ActorId = actor.Id,
                        SkillId = def.Id, TargetId = target.Id, Amount = def.StatusPercent,
                        HpAfter = target.Hp,
                    });
                }
                if (def.ShieldPoints > 0)
                {
                    target.Shield += def.ShieldPoints;
                    Events.Add(new BattleEvent
                    {
                        Kind = CombatEventKind.Status, Round = Round, ActorId = actor.Id,
                        SkillId = def.Id, TargetId = target.Id, Amount = def.ShieldPoints,
                        HpAfter = target.Hp,
                    });
                }
                break;
            }
        }
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

    /// <summary>护盾只挡物理：吃掉能吃下的部分并扣减盾值，返回穿透后的伤害。</summary>
    private static int AbsorbShield(Combatant target, int damage)
    {
        if (target.Shield <= 0 || damage <= 0)
            return damage;
        var absorbed = Math.Min(target.Shield, damage);
        target.Shield -= absorbed;
        return damage - absorbed;
    }

    private void Damage(Combatant actor, Combatant target, int damage, string skillId)
    {
        target.Hp = Math.Max(0, target.Hp - damage);
        actor.DamageDealt += damage;
        if (!target.Alive)
            actor.Kills++;
        Events.Add(new BattleEvent
        {
            Kind = CombatEventKind.Hit, Round = Round, ActorId = actor.Id,
            SkillId = skillId, TargetId = target.Id, Amount = damage, HpAfter = target.Hp,
        });
        Judge();
    }

    private void Judge()
    {
        var atk = Members.Exists(m => m.Side == CombatSide.Attacker && m.Alive);
        var def = Members.Exists(m => m.Side == CombatSide.Defender && m.Alive);
        if (atk && def)
            return;
        SetOutcome(atk ? CombatOutcome.AttackerWin : CombatOutcome.DefenderWin);
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
        // 血线危急且防御架势可用，先保命。
        var guard = SkillOf(actor, BattleSkills.GuardId);
        if (actor.Hp * 4 < actor.MaxHp && guard != null && actor.Mp >= guard.MpCost
            && CooldownLeft(actor, guard.Id) <= 0)
        {
            Perform(actor, guard, actor.Id);
            return;
        }

        var foes = Members.FindAll(m => m.Alive && m.Side != actor.Side);
        if (foes.Count == 0)
            return;
        var target = PickTarget(actor, foes);

        SkillDef? best = null;
        var bestScore = 0;
        foreach (var id in actor.Skills)
        {
            var def = Lookup(id);
            if (def == null || actor.Mp < def.MpCost || CooldownLeft(actor, id) > 0)
                continue;
            var score = def.Kind switch
            {
                SkillKind.Strike or SkillKind.Spell => ResolveDamage(actor, target, def),
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
        var aim = action.Kind == SkillKind.Heal ? WeakestHurt(actor.Side) : target;
        if (aim == null)
            aim = target;
        if (ResolveTargets(actor, action, aim.Id).Count == 0)
            action = Lookup(BattleSkills.AttackId)!;
        Perform(actor, action, aim.Id);
    }

    /// <summary>按行动者的索敌取向挑目标：噬弱咬残血，猎强打高攻，均衡按敌视权重掷骰。</summary>
    private Combatant PickTarget(Combatant actor, List<Combatant> foes)
    {
        switch (actor.Targeting)
        {
            case TargetMode.HuntWeak:
                return Weakest(foes);
            case TargetMode.HuntStrong:
            {
                var strongest = foes[0];
                foreach (var c in foes)
                    if (c.EffAttack > strongest.EffAttack
                        || (c.EffAttack == strongest.EffAttack && c.Id < strongest.Id))
                        strongest = c;
                return strongest;
            }
            default:
            {
                var total = 0;
                foreach (var c in foes)
                    total += c.EffThreat;
                var pick = Roll() * total / 100;
                foreach (var c in foes)
                {
                    pick -= c.EffThreat;
                    if (pick < 0)
                        return c;
                }
                return foes[foes.Count - 1];
            }
        }
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

    private static Combatant Weakest(List<Combatant> list)
    {
        var weakest = list[0];
        foreach (var c in list)
            if (c.Hp < weakest.Hp || (c.Hp == weakest.Hp && c.Id < weakest.Id))
                weakest = c;
        return weakest;
    }
}
