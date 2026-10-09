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
    private readonly record struct CodexEntry(string Label, string Summary, string Detail);

    private static readonly string[] CodexSegmentLabels = { "怪物表", "装备表", "物品表" };
    private int _codexSegment;

    private void OpenCodexPage()
    {
        LeaveTradeIfOpen();
        CloseTransient();
        _codexSegment = 0;
        _pan.Remove("codex");
        _push = PushPage.Codex;
        ResetListDrag();
        QueueRedraw();
    }

    private bool ExecuteCodex(PortraitWidget widget)
    {
        switch (widget.Action)
        {
            case PortraitAction.CodexSegment:
                _codexSegment = widget.Index;
                _pan.Remove("codex");
                return true;
            case PortraitAction.CodexEntry:
                var entry = BuildCodexEntries(_codexSegment)[widget.Index];
                ModalWanted!(new InkModalPage { Title = entry.Label, Body = entry.Detail });
                return true;
            default:
                return false;
        }
    }

    private void DrawCodexPage()
    {
        var viewport = PortraitLayout.CodexViewport;
        var entries = BuildCodexEntries(_codexSegment);
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
        var segments = PortraitLayout.CodexSegments;
        PortraitFrame.Segmented(this, segments, CodexSegmentLabels, _codexSegment);
        for (var i = 0; i < CodexSegmentLabels.Length; i++)
            _widgets.Add(new PortraitWidget(PortraitFrame.SegmentRect(segments, CodexSegmentLabels.Length, i),
                PortraitAction.CodexSegment, i, true, CodexSegmentLabels[i]));
        DrawPageTop("图鉴");
    }

    private static IReadOnlyList<CodexEntry> BuildCodexEntries(int segment) => segment switch
    {
        0 => BuildMonsterEntries(),
        1 => BuildEquipmentEntries(),
        2 => BuildItemEntries(),
        _ => BuildMonsterEntries(),
    };

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
                var lines = new List<string>
                {
                    $"血量 {Range(variants.Select(enemy => enemy.MaxHp))}",
                    $"攻击 {Range(variants.Select(enemy => enemy.Attack))}",
                    $"防御 {Range(variants.Select(enemy => enemy.Defence))}",
                    $"闪避 {Range(variants.Select(enemy => enemy.Dodge))}",
                    $"法强 {Range(variants.Select(enemy => enemy.SpellPower))}",
                    $"速度 {Range(variants.Select(enemy => enemy.Speed))}",
                    $"护甲 {Range(variants.Select(enemy => enemy.Armour))}",
                    $"行动点 {Range(variants.Select(enemy => enemy.ActionPoints))}",
                    $"金钱 {Range(variants.Select(enemy => enemy.Money))}G",
                };
                var skills = variants.SelectMany(enemy => enemy.Skills).Distinct(StringComparer.Ordinal)
                    .Select(id => DefDatabase<SkillDef>.Get(id)!.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                if (skills.Length > 0)
                    lines.Add($"技能　{string.Join("、", skills)}");

                var loot = variants.SelectMany(enemy => enemy.Loot).GroupBy(drop => drop.ItemId)
                    .OrderBy(grouping => Items.Get(grouping.Key)!.Label, StringComparer.Ordinal);
                var drops = loot.Select(grouping =>
                {
                    var item = Items.Get(grouping.Key)!;
                    return $"{item.Label} ×{Range(grouping.Select(drop => drop.Min))}–{Range(grouping.Select(drop => drop.Max))} · {Range(grouping.Select(drop => drop.RatePercent))}%";
                }).ToArray();
                if (drops.Length > 0)
                {
                    lines.Add("掉落");
                    lines.AddRange(drops);
                }

                return new CodexEntry(group.Key.Name, summary, string.Join("\n", lines));
            }).ToArray();
    }

    private static IReadOnlyList<CodexEntry> BuildEquipmentEntries()
    {
        var entries = new List<CodexEntry>();
        entries.AddRange(DefDatabase<WeaponTypeDef>.All.OrderBy(def => def.Label, StringComparer.Ordinal).Select(def =>
            new CodexEntry(def.Label,
                $"武器 · {InkText.Style(def.Style)} · 面板 {def.BasePanel}",
                JoinLines(def.Description, $"风格 {InkText.Style(def.Style)}", $"主属性 {InkText.CoreStat(def.Core)}",
                    $"基础面板 {def.BasePanel}", $"基础价值 {def.BaseValue}"))));
        entries.AddRange(DefDatabase<ArmorSlotDef>.All.OrderBy(def => def.Label, StringComparer.Ordinal).Select(def =>
            new CodexEntry(def.Label,
                $"防具 · 防御 {def.BaseDefence}",
                JoinLines(def.Description, $"基础防御 {def.BaseDefence}", $"物品名词 {def.Noun}"))));
        entries.AddRange(DefDatabase<AccessoryDef>.All.OrderBy(def => def.Label, StringComparer.Ordinal).Select(def =>
            new CodexEntry(def.Label,
                $"饰品 · {InkText.CoreStat(def.Core)} +{def.BaseBonus}",
                JoinLines(def.Description, $"{InkText.CoreStat(def.Core)} +{def.BaseBonus}"))));
        entries.AddRange(DefDatabase<EnchantDef>.All.OrderBy(def => def.Label, StringComparer.Ordinal).Select(def =>
            new CodexEntry(def.Label,
                $"附魔 · {def.Effect}",
                JoinLines(def.Effect, def.Core.HasValue ? $"{InkText.CoreStat(def.Core.Value)} +{def.CoreBonus}" : ""))));
        return entries;
    }

    private static IReadOnlyList<CodexEntry> BuildItemEntries() => Items.All()
        .OrderBy(def => def.Label, StringComparer.Ordinal)
        .Select(def =>
        {
            var category = DefDatabase<ThingCategoryDef>.Get(def.Category)?.Label ?? "";
            var summaryParts = new List<string>();
            if (category.Length > 0)
                summaryParts.Add(category);
            if (def.MarketValue > 0)
                summaryParts.Add($"{def.MarketValue}G");
            if (def.IsFood)
                summaryParts.Add(InkText.FoodTier(def.FoodTier));

            var lines = new List<string>();
            if (category.Length > 0)
                lines.Add(category);
            if (def.MarketValue > 0)
                lines.Add($"{def.MarketValue}G");
            if (def.IsFood)
                lines.AddRange(new[] { InkText.FoodTier(def.FoodTier), $"营养+{def.Nutrition}　心情{def.MoodBonus:+0;-0}" });
            if (def is MaterialDef material)
            {
                lines.Add($"材料等级 {material.Tier}");
                lines.Add($"伤害/防御加成 {material.DamageBonus}");
                lines.Add($"价值倍率 {material.ValueFactor}%");
                lines.Add($"兵器 { (material.WeaponUsable ? "可用" : "不可用") } · 防具 { (material.ArmorUsable ? "可用" : "不可用") }");
            }
            if (def.Description.Length > 0)
                lines.AddRange(new[] { "", def.Description });

            return new CodexEntry(def.Label, string.Join(" · ", summaryParts), string.Join("\n", lines));
        }).ToArray();

    private static string JoinLines(params string[] lines) => string.Join("\n", lines.Where(line => line.Length > 0));

    private static string Range(IEnumerable<int> values) => Range(values.Select(value => (long)value));

    private static string Range(IEnumerable<long> values)
    {
        var sorted = values.Distinct().Order().ToArray();
        return sorted.Length == 1 ? $"{sorted[0]}" : $"{sorted[0]}–{sorted[^1]}";
    }
}
