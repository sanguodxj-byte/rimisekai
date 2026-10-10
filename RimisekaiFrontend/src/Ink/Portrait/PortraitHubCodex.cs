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
        LeaveTradeIfOpen();
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

    private static IReadOnlyList<CodexEntry> BuildMonsterEntries()
    {
        var map = MapCatalog.Default;
        var enemies = DefDatabase<QuestDef>.All.SelectMany(quest => quest.Foes)
            .Concat(map.Wilds.Events.SelectMany(encounter => encounter.Foes))
            .Concat(map.Dungeon.Guards.SelectMany(encounter => encounter.Foes))
            .Concat(map.Dungeon.Bosses.SelectMany(encounter => encounter.Foes));

        return enemies.GroupBy(enemy => (enemy.Name, enemy.Portrait))
            .OrderBy(group => group.Key.Name, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Portrait, StringComparer.Ordinal)
            .Select(group =>
            {
                var variants = group.ToArray();
                var summary = new[]
                {
                    ("武器", Range(variants.Select(enemy => WeaponName(enemy.Weapon)))),
                    ("属性池", Range(variants.Select(enemy => enemy.CorePool))),
                    ("经验池", Range(variants.Select(enemy => enemy.ExpPool))),
                };
                var skills = variants.SelectMany(enemy => enemy.Skills).Distinct(StringComparer.Ordinal)
                    .Select(id => DefDatabase<SkillDef>.Get(id)!.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                var loot = variants.SelectMany(enemy => enemy.Loot).GroupBy(drop => drop.ItemId)
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
                        new() { Label = "武器", Value = Range(variants.Select(enemy => WeaponName(enemy.Weapon))) },
                        new() { Label = "主天资", Value = Range(variants.Select(enemy => StatName(enemy.Primary))) },
                        new() { Label = "副天资", Value = Range(variants.Select(enemy => StatName(enemy.Secondary))) },
                        new() { Label = "属性池", Value = $"{Range(variants.Select(enemy => enemy.CorePool))} 点" },
                        new() { Label = "经验池", Value = $"{Range(variants.Select(enemy => enemy.ExpPool))} 点" },
                        new() { Label = "威胁层", Value = Range(variants.Select(enemy => enemy.ThreatTier)) },
                        new() { Label = "站位列", Value = Range(variants.Select(enemy => enemy.Column)) },
                        new() { Label = "占格", Value = Range(variants.Select(enemy => enemy.Size)) },
                        new() { Label = "护甲", Value = Range(variants.Select(enemy => enemy.Armour)) },
                        new() { Label = "行动点", Value = Range(variants.Select(enemy => enemy.ActionPoints)) },
                        new() { Label = "金币", Value = $"{Range(variants.Select(enemy => enemy.Money))} G" },
                    },
                    Skills = skills.ToList(),
                    Drops = drops.ToList(),
                });
            }).ToArray();
    }

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
