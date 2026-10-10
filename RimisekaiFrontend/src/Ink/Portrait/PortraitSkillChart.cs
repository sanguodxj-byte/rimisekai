using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;

namespace Rimisekai.Portrait;

/// <summary>技能网上的一式：格位（内容表 chartRow / chartColumn），学没学会、能不能用、此刻的学习率。</summary>
public readonly record struct ChartSkill(SkillDef Def, int Row, int Column, bool Learned, bool Usable, int Chance);

/// <summary>
/// 竖屏技能网的数据：一式一格，自上而下由浅入深；格与格按 <see cref="SkillDef.DeriveFrom"/> 连线（可跨流派），
/// 用上方的来源技能，就有机会学会下方与它相连的技能。
/// </summary>
public static class PortraitSkillChart
{
    public const int Columns = 5;

    public static List<ChartSkill> Build(CharacterState who) => SkillTable.All
        .Select(s =>
        {
            var learned = SkillLearning.Learned(who, s);
            var usable = learned && (!s.Gate.Style.HasValue || who.EquippedStyle == s.Gate.Style);
            return new ChartSkill(s, s.ChartRow, s.ChartColumn, learned, usable, SkillLearning.Chance(who, s));
        })
        .OrderBy(s => s.Row).ThenBy(s => s.Column).ToList();

    public static int Rows => SkillTable.All.Max(s => s.ChartRow) + 1;

    /// <summary>学习条件逐条：标签、要求、现值、是否达成。通用技能返回空表。</summary>
    public static List<(string Label, string Need, string Have, bool Met)> Requirements(CharacterState who, SkillDef s)
    {
        var rows = new List<(string, string, string, bool)>();
        foreach (var id in s.DeriveFrom)
        {
            var source = SkillTable.Get(id)!;
            var ok = SkillLearning.Learned(who, source);
            rows.Add(("来源技能", source.Name, ok ? "已学习" : "未学习", ok));
        }
        var g = s.Gate;
        if (g.Style.HasValue && g.StyleLevel > 0)
        {
            var have = who.Styles[(int)g.Style.Value].Level;
            rows.Add(($"{Ink.InkText.Style(g.Style.Value)}熟练", $"Lv{g.StyleLevel}", $"Lv{have}", have >= g.StyleLevel));
        }
        if (g.Core != null)
            foreach (var r in g.Core)
                rows.Add((Ink.InkText.CoreStat(r.Stat), $"{r.Min}", $"{who[r.Stat]}", who[r.Stat] >= r.Min));
        if (g.Life != null)
            foreach (var r in g.Life)
                rows.Add((Ink.InkText.LifeSkill(r.Skill), $"{r.Min}", $"{who.Life(r.Skill)}", who.Life(r.Skill) >= r.Min));
        if (g.Traits != null)
            foreach (var t in g.Traits)
                rows.Add(("素质", Ink.InkText.TraitName(t), who.Has(t) ? "具备" : "未具备", who.Has(t)));
        if (g.Prerequisites != null)
            foreach (var id in g.Prerequisites)
            {
                var pre = SkillTable.Get(id)!;
                var ok = SkillLearning.Learned(who, pre);
                rows.Add(("前置技能", pre.Name, ok ? "已学习" : "未学习", ok));
            }
        return rows;
    }
}
