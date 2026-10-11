using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Defs;

namespace Rimisekai.Combat;

/// <summary>阵中一只怪：模板、落位（列、站位层）、是不是正主、按哪档 CR 生成。</summary>
public sealed record MonsterSlot(MonsterDef Monster, int Column, int ThreatTier, bool Boss, ChallengeRating Cr)
{
    public EnemyDef ToEnemy() => EnemyDef.Place(Monster, Column, ThreatTier, Boss, Cr);
}

/// <summary>
/// 4 列 × 4 层战场的占格：单位从 column 向右、从站位层向纵深延展 size 格，
/// 必须满足 size ≤ 站位层、column＋size−1 ≤ 4，且不压别人的格。
/// </summary>
public sealed class Formation
{
    public const int Columns = 4;
    public const int Layers = 4;

    private readonly bool[,] _taken = new bool[Columns, Layers];

    public bool Fits(int column, int layer, int size)
    {
        if (size < 1 || size > layer || layer > Layers || column < 1 || column + size - 1 > Columns)
            return false;
        for (var c = column; c < column + size; c++)
            for (var l = layer - size + 1; l <= layer; l++)
                if (_taken[c - 1, l - 1])
                    return false;
        return true;
    }

    /// <summary>
    /// 给一只怪找位置：先试默认站位层，再往后排（层号小）挪，最后往前排挪；每层从左到右取第一个空位。
    /// 放不下返回 null。
    /// </summary>
    public (int Column, int Layer)? Find(int preferredLayer, int size)
    {
        var layers = new List<int> { preferredLayer };
        for (var l = preferredLayer - 1; l >= 1; l--)
            layers.Add(l);
        for (var l = preferredLayer + 1; l <= Layers; l++)
            layers.Add(l);
        foreach (var layer in layers)
            for (var column = 1; column <= Columns; column++)
                if (Fits(column, layer, size))
                    return (column, layer);
        return null;
    }

    public void Take(int column, int layer, int size)
    {
        for (var c = column; c < column + size; c++)
            for (var l = layer - size + 1; l <= layer; l++)
                _taken[c - 1, l - 1] = true;
    }
}

/// <summary>
/// 遭遇生成：遭遇 → 危险等级取 CR 窗口与预算（<see cref="ChallengeDef.DangerTiers"/>）→ 索引 A（CR）∩ 索引 B（栖息地）
/// ∩ 遭遇条目的族群 → 按构成规则（<see cref="ChallengeDef.Compositions"/>）组队、自动分列。
/// 全部骰子由 <paramref name="seed"/> 驱动，同种子同阵容。
/// </summary>
public static class EncounterGenerator
{
    /// <summary>这里能不能组出这一类遭遇（遭遇条目据此筛：组不出的条目不参与抽选）。</summary>
    public static bool CanCompose(EncounterKind kind, int dangerTier, Habitat habitat, IReadOnlyCollection<string> families)
    {
        var rules = ChallengeDef.Standard;
        var band = rules.Tier(dangerTier);
        var index = MonsterIndex.Build();
        if (rules.Composition(kind).Leader)
            return Leaders(index, band, habitat, families).Count > 0;
        return Followers(index, band.CrMin, band.CrMax, habitat, families)
            .Any(m => rules.XpOf(m.Cr) <= band.BudgetXp);
    }

    /// <summary>
    /// 组一队。有正主的规则：正主从正主窗口里等概率抽（<paramref name="leaderCrBonus"/> 档整数 CR 加码，委托板用），
    /// 小兵 CR 低于正主模板、经验值合计不超过正主经验值的规定百分比。无正主的规则：从随从窗口里抽，合计不超过预算。
    /// 一队几只在规则区间里掷；放不下或预算不够就少放。组不出（条目本不该被抽中）直接抛。
    /// </summary>
    public static List<MonsterSlot> Compose(EncounterKind kind, int dangerTier, Habitat habitat,
        IReadOnlyCollection<string> families, int seed, int leaderCrBonus = 0)
    {
        var rules = ChallengeDef.Standard;
        var rule = rules.Composition(kind);
        var band = rules.Tier(dangerTier);
        var index = MonsterIndex.Build();
        var rng = new Random(seed);
        var formation = new Formation();
        var lineup = new List<MonsterSlot>();
        var count = rng.Next(rule.MinCount, rule.MaxCount + 1);

        List<MonsterDef> pool;
        int budget;
        if (rule.Leader)
        {
            var leaders = Leaders(index, band, habitat, families);
            if (leaders.Count == 0)
                throw new InvalidOperationException($"危险等级 {dangerTier} 的 {habitat} 组不出 {string.Join("/", families)} 的正主");
            var leader = leaders[rng.Next(leaders.Count)];
            var cr = leader.Cr.Plus(leaderCrBonus);
            lineup.Add(Place(formation, leader, boss: true, cr)!);
            budget = rules.XpOf(cr) * rule.MinionBudgetPercent / 100;
            pool = Followers(index, ChallengeRating.Zero, leader.Cr, habitat, families)
                .Where(m => m.Cr < leader.Cr).ToList();
        }
        else
        {
            budget = band.BudgetXp;
            pool = Followers(index, band.CrMin, band.CrMax, habitat, families);
        }

        while (lineup.Count < count)
        {
            var options = pool.Where(m => rules.XpOf(m.Cr) <= budget
                && formation.Find(m.ThreatTier, m.Size) != null).ToList();
            if (options.Count == 0)
                break;
            var pick = options[rng.Next(options.Count)];
            budget -= rules.XpOf(pick.Cr);
            lineup.Add(Place(formation, pick, boss: false, pick.Cr)!);
        }
        if (lineup.Count == 0)
            throw new InvalidOperationException($"危险等级 {dangerTier} 的 {habitat} 组不出 {string.Join("/", families)} 的遭遇");
        return lineup;
    }

    private static List<MonsterDef> Leaders(MonsterIndex index, DangerChallenge band, Habitat habitat,
        IReadOnlyCollection<string> families) =>
        index.Draw(band.BossCrMin, band.BossCrMax, habitat).Where(m => m.CanLead && families.Contains(m.Family)).ToList();

    private static List<MonsterDef> Followers(MonsterIndex index, ChallengeRating min, ChallengeRating max, Habitat habitat,
        IReadOnlyCollection<string> families) =>
        index.Draw(min, max, habitat).Where(m => m.CanFollow && families.Contains(m.Family)).ToList();

    private static MonsterSlot? Place(Formation formation, MonsterDef monster, bool boss, ChallengeRating cr)
    {
        if (formation.Find(monster.ThreatTier, monster.Size) is not { } spot)
            return null;
        formation.Take(spot.Column, spot.Layer, monster.Size);
        return new MonsterSlot(monster, spot.Column, spot.Layer, boss, cr);
    }
}
