using System.Collections.Generic;
using System.Linq;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Combat;

namespace Rimisekai.Portrait;

/// <summary>技能网上的一式：格位（见 <see cref="SkillTree"/>）；对这个角色作不作数（没有池全作数，有池只算抽到的）、已不已激活。</summary>
public readonly record struct ChartSkill(SkillDef Def, int Row, float Column, bool Eligible, bool Active);

/// <summary>
/// 竖屏技能网的数据：一式一格，按等阶自上而下由弱到强，核心技能一行居中；
/// 连线按门槛高低现算（<see cref="SkillTree.Links"/>）。门槛达到即自动激活，不必学习。
/// </summary>
public static class PortraitSkillChart
{
    public const int Columns = SkillTree.Columns;

    public static List<ChartSkill> Build(CharacterState who)
    {
        var active = SkillTree.Active(who).Select(s => s.Id).ToHashSet();
        return SkillTree.Layout(who)
            .Select(slot => new ChartSkill(slot.Def, slot.Row, slot.Column, SkillTree.Eligible(who, slot.Def), active.Contains(slot.Def.Id)))
            .ToList();
    }

    /// <summary>激活条件逐条：标签、要求、现值、是否达成。通用技能返回空表。</summary>
    public static List<(string Label, string Need, string Have, bool Met)> Requirements(CharacterState who, SkillDef s)
    {
        var rows = new List<(string, string, string, bool)>();
        var g = s.Gate;
        if (g.Style.HasValue)
        {
            var holding = SkillGate.StyleOf(who);
            rows.Add(("持用流派", Ink.InkText.Style(g.Style.Value), Ink.InkText.Style(holding), holding == g.Style));
            if (g.StyleLevel > 0)
            {
                var have = who.Styles[(int)g.Style.Value].Level;
                rows.Add(($"{Ink.InkText.Style(g.Style.Value)}熟练", $"Lv{g.StyleLevel}", $"Lv{have}", have >= g.StyleLevel));
            }
        }
        if (g.Weapon.HasValue && g.WeaponLevel > 0)
        {
            var have = who.Weapons[(int)g.Weapon.Value].Level;
            rows.Add(($"{Ink.InkText.Weapon(g.Weapon.Value)}熟练", $"Lv{g.WeaponLevel}", $"Lv{have}", have >= g.WeaponLevel));
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
        {
            var active = SkillTree.Active(who).Select(x => x.Id).ToHashSet();
            foreach (var id in g.Prerequisites)
            {
                var ok = active.Contains(id);
                rows.Add(("前置技能", SkillTable.Get(id)!.Name, ok ? "已激活" : "未激活", ok));
            }
        }
        return rows;
    }
}
