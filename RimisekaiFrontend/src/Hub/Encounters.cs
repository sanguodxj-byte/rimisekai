using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;


using Rimisekai.Save;
using Rimisekai.Session;

namespace Rimisekai.Hub;

/// <summary>
/// 遭遇入口：把"遭遇了什么"变成一场可以指挥的战斗。
/// 三个来源共用这一个入口——
/// 副本/任务探索里房间与设施上声明的事件、任务步骤的直接战斗、
/// 世界地图上与敌对势力敌人的遭遇。差别只在敌人清单由谁给。
/// 团灭（我方全倒）由战斗结果自查，后果（回据点/损失）由调用方接。
/// </summary>
public static class Encounters
{
    private static int _nextEnemyId = 1000;

    /// <summary>
    /// 把主人背包里的消耗品（能在战斗里用的那几样，如药剂，见 ItemActions）点数带进战斗；
    /// 用掉多少由 <see cref="CombatSettlement.Settle"/> 从背包扣。
    /// </summary>
    public static void Pack(GameState state, Battle battle)
    {
        var bag = state.Roster.Master?.Bag;
        if (bag == null)
            return;
        foreach (var skill in ItemActions.All)
            if (bag.Get(skill.Item) > 0)
                battle.Supplies[skill.Item] = bag.Get(skill.Item);
    }

    /// <summary>
    /// 从敌人定义成军，与名册中全体非主人成员开战。
    /// 返回战斗会话（前端据此切入战斗页）；敌人清单为空或没有可用成员返回 null。
    /// <paramref name="partyIds"/> 传任务编成时，只让名单里的人上阵（玩家本人始终随行）；
    /// 不传或为空则按名册全员出动（据点遭遇战用）。
    /// <paramref name="waves"/> 为连战在首波之后依次补上的各波（每清一波有补给回合）；不传即一波打完。
    /// </summary>
    public static BattleSession? Start(
        GameState state, IReadOnlyList<EnemyDef> enemies, GameCatalog? catalog = null,
        string placeName = "迷宫地下城", IReadOnlyList<int>? partyIds = null,
        IReadOnlyList<IReadOnlyList<EnemyDef>>? waves = null)
    {
        if (enemies.Count == 0)
            return null;

        var pool = state.Roster.Members;
        if (partyIds is { Count: > 0 })
        {
            var chosen = new List<CharacterState>();
            foreach (var id in partyIds)
            {
                var member = state.Roster.Find(id);
                if (member != null && !chosen.Contains(member))
                    chosen.Add(member);
            }
            // 玩家本人必须随行：任务编成里左 1 槽锁死玩家，不进 partyIds。
            var master = state.Roster.Master;
            if (master != null && !chosen.Contains(master))
                chosen.Add(master);
            if (chosen.Count > 0)
                pool = chosen;
        }

        // 按威胁等级降序排序：前 4 名首发，后 2 名进入后备位
        var sorted = pool
            .OrderByDescending(m => m.ThreatTier)
            .ThenBy(m => m.Id)
            .ToList();
        if (sorted.Count == 0)
            return null;

        var starters = sorted.Take(4).ToList();
        var reserves = sorted.Skip(4).Take(2).ToList();

        var battle = new Battle(catalog) { PlaceName = placeName };
        foreach (var c in starters)
            battle.Add(Deploy.FromCharacter(c, CombatSide.Attacker, state.Weapons, state.Equips));
        foreach (var c in reserves)
            battle.AddReserve(Deploy.FromCharacter(c, CombatSide.Attacker, state.Weapons, state.Equips));
        Pack(state, battle);

        foreach (var def in enemies)
            battle.Add(Deploy.FromEnemy(def, _nextEnemyId++, CombatSide.Defender));
        if (waves != null)
            foreach (var wave in waves)
                battle.QueueWave(wave.Select(def => Deploy.FromEnemy(def, _nextEnemyId++, CombatSide.Defender)).ToList());

        battle.StartBattle();
        return new BattleSession(state.Roster, battle);
    }

    /// <summary>我方是否全倒（团灭）。调用方据此接战败后果。</summary>
    public static bool PartyWiped(BattleSession session) =>
        session.Battle.Outcome == CombatOutcome.DefenderWin
        && !session.Battle.Members.Any(m => m.Side == CombatSide.Attacker && m.Alive);
}
