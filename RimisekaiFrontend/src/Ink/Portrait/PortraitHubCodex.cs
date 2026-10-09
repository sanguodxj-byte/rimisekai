using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Catalog;
using Rimisekai.Defs;
using Rimisekai.Ink;
using Rimisekai.Quest;
using Rimisekai.WorldMap;

namespace Rimisekai.Portrait;

public partial class PortraitHubScreen
{
    private readonly record struct CodexEntry(string Label, string Summary, InkModalMonsterCodexData Detail);

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
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 166f, rect.Position.Y + 92f,
                    rect.Size.X - 196f, 54f), entry.Summary,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
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
                var summary = $"血量 {Range(variants.Select(enemy => enemy.MaxHp))} · 攻击 {Range(variants.Select(enemy => enemy.Attack))}";
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
                        new() { Label = "血量", Value = Range(variants.Select(enemy => enemy.MaxHp)) },
                        new() { Label = "攻击", Value = Range(variants.Select(enemy => enemy.Attack)) },
                        new() { Label = "防御", Value = Range(variants.Select(enemy => enemy.Defence)) },
                        new() { Label = "闪避", Value = Range(variants.Select(enemy => enemy.Dodge)) },
                        new() { Label = "法强", Value = Range(variants.Select(enemy => enemy.SpellPower)) },
                        new() { Label = "速度", Value = Range(variants.Select(enemy => enemy.Speed)) },
                        new() { Label = "护甲", Value = Range(variants.Select(enemy => enemy.Armour)) },
                        new() { Label = "行动点", Value = Range(variants.Select(enemy => enemy.ActionPoints)) },
                        new() { Label = "金币", Value = $"{Range(variants.Select(enemy => enemy.Money))} G" },
                    },
                    Skills = skills.ToList(),
                    Drops = drops.ToList(),
                });
            }).ToArray();
    }

    private static string Range(IEnumerable<int> values) => Range(values.Select(value => (long)value));

    private static string Range(IEnumerable<long> values)
    {
        var sorted = values.Distinct().Order().ToArray();
        return sorted.Length == 1 ? $"{sorted[0]}" : $"{sorted[0]}–{sorted[^1]}";
    }
}
