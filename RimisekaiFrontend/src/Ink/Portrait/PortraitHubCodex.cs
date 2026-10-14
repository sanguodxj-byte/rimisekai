using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Character;
using Rimisekai.Defs;
using Rimisekai.Ink;
using Rimisekai.Quest;
using Rimisekai.WorldMap;

namespace Rimisekai.Portrait;

public partial class PortraitHubScreen
{
    private readonly record struct CodexEntry(string Label, IReadOnlyList<(string Label, string Value)> Summary, InkModalMonsterCodexData Detail);

    private void OpenCodexPage()
    {
        LeaveSearchIfOpen();
        CloseTransient();
        _pan.Remove("codex");
        _push = PushPage.Codex;
        ResetListDrag();
        QueueRedraw();
    }

    private bool ExecuteCodex(PortraitWidget widget)
    {
        if (widget.Action != PortraitAction.CodexEntry)
            return false;

        var entry = BuildMonsterEntries()[widget.Index];
        ModalWanted!(new InkModalPage { Title = entry.Label, MonsterCodex = entry.Detail });
        return true;
    }

    private void DrawCodexPage()
    {
        var viewport = PortraitLayout.CodexViewport;
        var entries = BuildMonsterEntries();
        var total = entries.Count * PortraitLayout.CodexRowStep + 16f;
        var offset = Pan("codex", (int)total, (int)viewport.Size.Y);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rect = new Rect2(PortraitLayout.Pad,
                viewport.Position.Y + 16f + i * PortraitLayout.CodexRowStep - offset,
                PortraitLayout.FullWidth, PortraitLayout.CodexRowHeight);
            PortraitFrame.Card(this, rect);
            var emblem = new Rect2(rect.Position.X + 22f, rect.Position.Y + 26f, 118f, 122f);
            PortraitFrame.Brackets(this, emblem, InkStyle.Dim);
            InkDraw.TextBounded(this, new Rect2(emblem.Position.X + 14f, emblem.Position.Y,
                    emblem.Size.X - 28f, emblem.Size.Y), entry.Label[..1],
                PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "cm");
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 166f, rect.Position.Y + 22f,
                    rect.Size.X - 196f, 62f), entry.Label,
                PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
            PortraitFrame.CountTags(this, rect.Position.X + 166f, rect.Position.Y + 119f, entry.Summary, rect.End.X - 30f);
            AddClipped(rect, viewport, PortraitAction.CodexEntry, i, true, entry.Label);
        }

        RegisterScroll("codex", viewport, (int)total, (int)viewport.Size.Y, offset,
            value => _pan["codex"] = value, 1f);
        MaskAbove(viewport);
        DrawPageTop("怪物图鉴");
    }

    /// <summary>图鉴一条一个怪物模板（同名同立绘的合成一条）：武器、挑战等级与对应等级、站位、掉落。</summary>
    private static IReadOnlyList<CodexEntry> BuildMonsterEntries()
    {
        var rules = ChallengeDef.Standard;
        return DefDatabase<MonsterDef>.All.GroupBy(monster => (monster.Name, monster.Portrait))
            .OrderBy(group => group.Key.Name, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Portrait, StringComparer.Ordinal)
            .Select(group =>
            {
                var variants = group.ToArray();
                var cr = Range(variants.Select(monster => monster.Cr.ToString()));
                var level = Range(variants.Select(monster => rules.LevelOf(monster.Cr)));
                var summary = new[]
                {
                    ("武器", Range(variants.Select(monster => WeaponName(monster.Weapon)))),
                    ("挑战等级", cr),
                    ("等级", level),
                };
                var skills = variants.SelectMany(monster => monster.Skills).Distinct(StringComparer.Ordinal)
                    .Select(id => DefDatabase<SkillDef>.Get(id)!.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                var loot = variants.SelectMany(monster => monster.Loot).GroupBy(drop => drop.ItemId)
                    .OrderBy(grouping => Items.Get(grouping.Key)!.Label, StringComparer.Ordinal);
                var drops = loot.Select(grouping =>
                {
                    var item = Items.Get(grouping.Key)!;
                    return new InkModalMonsterCodexData.Drop
                    {
                        Name = item.Label,
                        Quantity = $"×{Range(grouping.Select(drop => drop.Min))}–{Range(grouping.Select(drop => drop.Max))}",
                        Chance = $"{Range(grouping.Select(drop => drop.RatePercent))}%",
                    };
                }).ToArray();

                return new CodexEntry(group.Key.Name, summary, new InkModalMonsterCodexData
                {
                    RecordCount = variants.Length,
                    Attributes = new List<InkModalMonsterCodexData.Metric>
                    {
                        new() { Label = "武器", Value = Range(variants.Select(monster => WeaponName(monster.Weapon))) },
                        new() { Label = "主天资", Value = Range(variants.Select(monster => StatName(monster.Primary))) },
                        new() { Label = "副天资", Value = Range(variants.Select(monster => StatName(monster.Secondary))) },
                        new() { Label = "挑战等级", Value = cr },
                        new() { Label = "等级", Value = level },
                        new() { Label = "威胁层", Value = Range(variants.Select(monster => monster.ThreatTier)) },
                        new() { Label = "定位", Value = Range(variants.Select(RoleName)) },
                        new() { Label = "占格", Value = Range(variants.Select(monster => monster.Size)) },
                        new() { Label = "护甲", Value = Range(variants.Select(monster => monster.Armour)) },
                        new() { Label = "行动点", Value = Range(variants.Select(monster => monster.ActionPoints)) },
                        new() { Label = "金币", Value = $"{Range(variants.Select(monster => monster.Money))} G" },
                    },
                    Skills = skills.ToList(),
                    Drops = drops.ToList(),
                });
            }).ToArray();
    }

    private static string RoleName(MonsterDef monster) =>
        monster.CanLead ? "首领" : "随从";

    private static string WeaponName(WeaponType? weapon) => weapon switch
    {
        null or WeaponType.Unarmed => "徒手",
        WeaponType.Sword => "剑",
        WeaponType.Axe => "斧",
        WeaponType.Spear => "枪",
        WeaponType.Bow => "弓",
        WeaponType.Staff => "法杖",
        WeaponType.Dagger => "匕首",
        WeaponType.Crossbow => "弩",
        _ => weapon.Value.ToString(),
    };

    private static string StatName(CoreStat? stat) => stat switch
    {
        null => "随机",
        CoreStat.Constitution => "体质",
        CoreStat.Dexterity => "灵巧",
        CoreStat.Intellect => "智力",
        CoreStat.Charm => "魅力",
        CoreStat.Perception => "感知",
        CoreStat.Strength => "力量",
        CoreStat.Speed => "速度",
        _ => stat.Value.ToString(),
    };

    private static string Range(IEnumerable<string> values)
    {
        var sorted = values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return sorted.Length == 1 ? sorted[0] : string.Join(" / ", sorted);
    }

    private static string Range(IEnumerable<int> values) => Range(values.Select(value => (long)value));

    private static string Range(IEnumerable<long> values)
    {
        var sorted = values.Distinct().Order().ToArray();
        return sorted.Length == 1 ? $"{sorted[0]}" : $"{sorted[0]}–{sorted[^1]}";
    }
}
