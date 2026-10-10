using System.Collections.Generic;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;
using Rimisekai.Command;

namespace Rimisekai.Session;

/// <summary>
/// 战斗会话：把 Battle 挂进通用指令循环。UI 读 Battle 的成员与事件渲染，
/// 按当前行动者提交攻击/防御/技能/逃跑；不在控制侧的行动者由 Battle 内置 AI 代打。
/// 战斗分出胜负后指令返回 ExitSession。
/// </summary>
public sealed class BattleSession : Session
{
    public const int CmdAttack = 1;
    public const int CmdGuard = 2;
    public const int CmdFlee = 3;
    private const int CmdSkillBase = 100;

    public Battle Battle { get; }
    public Rimisekai.Quest.QuestRun? QuestRun { get; set; }

    public override SessionKind Kind => SessionKind.Combat;

    protected override CommandContext Context => CommandContext.Combat;

    public BattleSession(Roster roster, Battle battle) : base(roster)
    {
        Battle = battle;
        AddCommand(CmdAttack, BattleSkills.AttackId);
        AddCommand(CmdGuard, BattleSkills.GuardId);
        Register(new FleeEffect(this));
        Register(new CommandDef
        {
            Id = CmdFlee, Context = CommandContext.Combat, EffectId = "battle:flee",
        });

        var next = CmdSkillBase;
        foreach (var skillId in PartySkills(battle))
        {
            if (skillId == BattleSkills.AttackId || skillId == BattleSkills.GuardId)
                continue;
            if (battle.Lookup(skillId) != null)
                AddCommand(next++, skillId);
        }
    }

    private void AddCommand(int id, string skillId)
    {
        var effect = new ActEffect(this, id, skillId);
        Register(new CommandDef
        {
            Id = id, Context = CommandContext.Combat, EffectId = effect.EffectId,
        });
        Register(effect);
    }

    /// <summary>控制方全会用的技能 Id 去重列表，供指令注册与 UI 排菜单。</summary>
    public List<string> PartySkills(Battle battle)
    {
        var ids = new List<string>();
        foreach (var m in battle.Members)
        {
            if (m.Side != battle.ControlledSide)
                continue;
            foreach (var id in m.Skills)
                if (!ids.Contains(id))
                    ids.Add(id);
        }
        return ids;
    }

    /// <summary>逃跑是整体动作，不带目标。</summary>
    public CommandResult Flee() => Battle.TryFlee()
        ? CommandResult.ExitSession
        : CommandResult.Executed;

    private sealed class ActEffect : ICommandEffect
    {
        private readonly BattleSession _host;
        private readonly string _skillId;

        public ActEffect(BattleSession host, int commandId, string skillId)
        {
            _host = host;
            _skillId = skillId;
            EffectId = $"battle:{commandId}";
        }

        public string EffectId { get; }

        public CommandResult Apply(CommandRequest request, ICommandHost host)
        {
            var pending = _host.Battle.PendingActor;
            if (pending == null)
                return CommandResult.Rejected;
            var action = new CombatAction
            {
                ActorId = pending.Id,
                SkillId = _skillId,
                TargetId = request.TargetIds.Count > 0 ? request.TargetIds[0] : pending.Id,
            };
            if (!_host.Battle.Act(action))
                return CommandResult.Rejected;
            return _host.Battle.Outcome == CombatOutcome.Ongoing
                ? CommandResult.Executed
                : CommandResult.ExitSession;
        }
    }

    private sealed class FleeEffect : ICommandEffect
    {
        private readonly BattleSession _host;

        public FleeEffect(BattleSession host)
        {
            _host = host;
            EffectId = "battle:flee";
        }

        public string EffectId { get; }

        public CommandResult Apply(CommandRequest request, ICommandHost host) => _host.Flee();
    }
}
