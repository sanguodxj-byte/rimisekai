using Rimisekai.Catalog;

namespace Rimisekai.Combat;

/// <summary>
/// 技能等阶（拟案，待主人核定）：按数值把每式技能分成一到五阶，技能网自上而下按阶排（越往下越强）。
/// 估值＝单次出手的分量：威力 × 目标数系数 × 命中修正，加附加状态（增减益按幅度×回合，持续伤害按总量，护盾按点数），
/// 打断咏唱另加一截；咏唱每多一回合按四分之一折扣。增益 / 治疗本身没有威力，只算状态与治疗量。
/// 核心技能不分阶（网里固定居中）。
/// </summary>
public static class SkillTier
{
    public const int Count = 5;

    /// <summary>进二、三、四、五阶的估值门槛。</summary>
    private static readonly int[] Thresholds = { 75, 100, 120, 140 };

    public static double Score(SkillDef s)
    {
        var spread = s.Target switch
        {
            SkillTarget.AllEnemies or SkillTarget.AllAllies => 2.0,
            SkillTarget.FoesColumn => 1.5,
            _ => 1.0,
        };
        var value = s.Kind switch
        {
            SkillKind.Strike or SkillKind.Spell => s.Power * spread * (1 + s.HitMod / 100.0),
            SkillKind.Heal => s.Power * spread * 1.2,
            _ => 0.0,
        };
        value += s.Status switch
        {
            StatusKind.StatMod => System.Math.Abs(s.StatusPercent) * s.StatusRounds * 1.5 * (s.Kind == SkillKind.Buff ? spread : 1.0),
            StatusKind.Dot => s.StatusPower * s.StatusRounds * 2.0,
            StatusKind.Points => s.StatusPower * s.StatusRounds * 0.5,
            _ => 0.0,
        };
        if (s.Control)
            value += 20;
        return value / (1 + 0.25 * s.ChantRounds);
    }

    /// <summary>一到五阶。</summary>
    public static int Of(SkillDef s)
    {
        var score = Score(s);
        var tier = 1;
        foreach (var t in Thresholds)
            if (score >= t)
                tier++;
        return tier;
    }
}
