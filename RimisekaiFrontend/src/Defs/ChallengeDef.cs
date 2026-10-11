using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;

namespace Rimisekai.Defs;

/// <summary>遭遇的来路，决定用哪条构成规则：野外、地城守卫房、首领房。</summary>
public enum EncounterKind
{
    Wild,
    Guard,
    Boss,
}

/// <summary>
/// 挑战等级规则表（content/defs/challenge.json，全项目一行 <see cref="StandardDefName"/>）：
/// CR → 我方等级 → 属性池/经验池；CR → 经验值（遭遇预算）；危险等级 → CR 窗口与预算；遭遇构成规则。
/// 数值全在表里，代码只读表。
/// </summary>
public sealed class ChallengeDef : Def
{
    public const string StandardDefName = "Challenge_Standard";

    public static ChallengeDef Standard => DefDatabase<ChallengeDef>.Get(StandardDefName)!;

    /// <summary>每档 CR 一行：对应我方等级（1–100）与经验值（D&amp;D 的 CR 经验值，作遭遇预算）。</summary>
    public List<ChallengeRow> Ratings { get; init; } = new();

    /// <summary>等级 → 属性池、经验池的线性式。</summary>
    public ChallengeStats Stats { get; init; } = new();

    /// <summary>危险等级 1–4 → CR 窗口、经验值预算、正主 CR 窗口。</summary>
    public List<DangerChallenge> DangerTiers { get; init; } = new();

    /// <summary>野外 / 守卫房 / 首领房各一条构成规则。</summary>
    public List<CompositionRule> Compositions { get; init; } = new();

    public ChallengeRow Row(ChallengeRating cr) => Ratings.Single(r => r.Cr == cr);

    public int LevelOf(ChallengeRating cr) => Row(cr).Level;

    public int XpOf(ChallengeRating cr) => Row(cr).Xp;

    /// <summary>该等级的怪物属性池：底线 6 之上再分多少点（交给角色生成器）。</summary>
    public int CorePool(int level) =>
        (int)Math.Round(Stats.CoreBase + Stats.CorePerLevel * (level - 1), MidpointRounding.AwayFromZero);

    /// <summary>该等级的怪物经验池（交给角色生成器）。</summary>
    public int ExpPool(int level) =>
        (int)Math.Round(Stats.ExpBase + Stats.ExpPerLevel * (level - 1), MidpointRounding.AwayFromZero);

    public DangerChallenge Tier(int dangerTier) => DangerTiers.Single(t => t.Tier == dangerTier);

    public CompositionRule Composition(EncounterKind kind) => Compositions.Single(c => c.Kind == kind);
}

public sealed class ChallengeRow
{
    public ChallengeRating Cr { get; init; }
    public int Level { get; init; }
    public int Xp { get; init; }
}

/// <summary>属性池＝coreBase＋corePerLevel×(等级−1)，经验池＝expBase＋expPerLevel×(等级−1)，四舍五入。</summary>
public sealed class ChallengeStats
{
    public double CoreBase { get; init; }
    public double CorePerLevel { get; init; }
    public double ExpBase { get; init; }
    public double ExpPerLevel { get; init; }
}

/// <summary>
/// 一档危险等级：随从（野外、守卫房、首领身边的小兵除外）只从 [<see cref="CrMin"/>, <see cref="CrMax"/>] 里抽，
/// 一队经验值合计不超过 <see cref="BudgetXp"/>；首领房的正主从 [<see cref="BossCrMin"/>, <see cref="BossCrMax"/>] 里抽。
/// </summary>
public sealed class DangerChallenge
{
    public int Tier { get; init; }
    public ChallengeRating CrMin { get; init; }
    public ChallengeRating CrMax { get; init; }
    public int BudgetXp { get; init; }
    public ChallengeRating BossCrMin { get; init; }
    public ChallengeRating BossCrMax { get; init; }
}

/// <summary>
/// 构成规则：一队几只（[<see cref="MinCount"/>, <see cref="MaxCount"/>] 里掷，预算或格子不够就少放，至少一只）、
/// 有没有正主；有正主时身边小兵 CR 低于正主，经验值合计不超过正主经验值的 <see cref="MinionBudgetPercent"/>%。
/// </summary>
public sealed class CompositionRule
{
    public EncounterKind Kind { get; init; }
    public int MinCount { get; init; }
    public int MaxCount { get; init; }
    public bool Leader { get; init; }
    public int MinionBudgetPercent { get; init; }
}
