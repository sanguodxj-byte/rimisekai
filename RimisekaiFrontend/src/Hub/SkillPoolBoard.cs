using System.Collections.Generic;
using System.Linq;
using Rimisekai.Combat;

namespace Rimisekai.Hub;

/// <summary>
/// 身份技能池的界面后端（拟案，待主人核定）：玩家角色特权——在领地里可随时自选身份、重抽自己的技能池；
/// 其他人的池按本身份生成后不变。
/// </summary>
public sealed partial class HubSession
{
    /// <summary>能选的身份（有技能池的那些），按内容表顺序。</summary>
    public IReadOnlyList<string> PoolIdentities => SkillPool.Pools.Select(p => p.DefName).ToList();

    /// <summary>此刻能不能重抽：只有玩家角色，只在领地里（大地图、兴趣点、地城里都不行）。</summary>
    public bool CanRerollSkillPool => Layer == MapLayer.Territory && State.Roster.Master != null;

    /// <summary>玩家按所选身份重抽技能池。不在领地里、或这个身份没有技能池，返回 false 不动。</summary>
    public bool RerollSkillPool(string identity, System.Random? rng = null)
    {
        if (!CanRerollSkillPool || SkillPool.PoolOf(identity) == null)
            return false;
        SkillPool.Assign(State.Roster.Master!, identity, rng ?? System.Random.Shared);
        return true;
    }
}
