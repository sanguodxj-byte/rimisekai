using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 弹窗排版（2026-10-07 重设计）：整屏压暗 → 居中缺角双线框 → 标题（居中大字＋渐隐线）→ 正文 →
/// 原生输入框（圆角框＋字数）→ 药丸钮（两钮并排，确定＝实心；多钮竖排）。
/// 底部一律一枚呼吸的实心 ▼；战后结算单独排（大字胜负 / 轮数 / 战利品条 / 各人经验）；
/// 物品详情单独排（标题 / 副题 / 逐行「暗标签＋亮数值」条）。
/// 标题底下一律压一幅代码画的窗花底纹（<see cref="PortraitTracery"/>），框外不再立冠饰。
/// </summary>
public partial class PortraitModalLayer
{
    private Rect2 _modalPanel;
    private Rect2 _modalBody;
    private Rect2 _modalInput;
    private int _modalFirst;
    private int _modalTotal;
    private int _modalVisible;
    private float _modalTime;
    private bool _modalPressed;
    private bool _modalDragged;
    private float _modalPressY;
    private int _modalPressFirst;
    /// <summary>稀有度雾：结算战利品条（左→右吹）与物品详情（上飘）。</summary>
    private PortraitRarityFog? _fogPool;
    private PortraitRarityFog _fog => _fogPool ??= new PortraitRarityFog(this);

    /// <summary>可滚区一格的高度：正文按行（ModalLineHeight），结算战利品按条。</summary>
    private float _modalStep = PortraitLayout.ModalLineHeight;

    private void DrawModalPage(InkModalPage page)
    {
        DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), new Color(InkStyle.Bg, 0.78f));
        if (page.Settlement is { } data)
        {
            DrawSettlement(page, data);
            return;
        }
        if (page.MonsterCodex is { } codex)
        {
            DrawMonsterCodex(page, codex);
            return;
        }
        if (page.Item is { } item)
        {
            DrawItemDetail(page, item);
            return;
        }
        var textWidth = PortraitLayout.ModalWidth - PortraitLayout.ModalPad * 2f;
        var lines = InkDraw.WrapLines(page.Body, textWidth, PortraitLayout.FontBody).ToList();
        if (page.Body.Length == 0)
            lines.Clear();
        var sideBySide = page.Choices.Count == 2;
        var choiceRows = sideBySide ? 1 : page.Choices.Count;
        var controls = choiceRows * (PortraitLayout.ModalButtonHeight + PortraitLayout.ModalGap)
            + (page.Input == null ? 0f : PortraitLayout.TouchComfort + PortraitLayout.ModalGap);
        var heading = page.Title.Length > 0 ? 170f : 0f;
        var bodyHeight = lines.Count * PortraitLayout.ModalLineHeight + (lines.Count > 0 ? 30f : 0f);
        // 底端永远有一枚呼吸的实心 ▼（AGENTS「弹窗通用规格」），带选项 / 输入框的页同样留出这一带。
        var arrow = PortraitLayout.ModalArrowBand;
        _modalPanel = PortraitLayout.ModalBounds(PortraitLayout.ModalPad * 2f + heading + bodyHeight + controls + arrow);
        PortraitFrame.GothicFrame(this, _modalPanel, new Color(InkStyle.Panel, 1f));

        var top = _modalPanel.Position.Y + PortraitLayout.ModalPad;
        if (page.Title.Length > 0)
        {
            TitleBackdrop(top + 40f, 140f);
            InkDraw.TextBounded(this, new Rect2(_modalPanel.Position.X + PortraitLayout.ModalPad, top, textWidth, 80f), page.Title,
                PortraitLayout.FontTitle, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            PortraitFrame.FadingRule(this, _modalPanel.Position.X + 160f, _modalPanel.End.X - 160f, top + 110f);
            top += heading;
        }
        var available = _modalPanel.End.Y - PortraitLayout.ModalPad - arrow - top - controls;
        var shownBody = Mathf.Min(bodyHeight, Mathf.Max(0f, available));
        _modalBody = new Rect2(_modalPanel.Position.X + PortraitLayout.ModalPad, top, textWidth, shownBody);
        _modalStep = PortraitLayout.ModalLineHeight;
        _modalVisible = Math.Max(0, (int)(shownBody / PortraitLayout.ModalLineHeight));
        _modalTotal = lines.Count;
        _modalFirst = Math.Clamp(_modalFirst, 0, Math.Max(0, lines.Count - _modalVisible));
        for (var i = 0; i < _modalVisible && i + _modalFirst < lines.Count; i++)
            InkDraw.Text(this, new Vector2(_modalBody.GetCenter().X, top + i * PortraitLayout.ModalLineHeight + PortraitLayout.ModalLineHeight / 2f),
                lines[i + _modalFirst], PortraitLayout.FontBody, page.Choices.Count > 0 || page.Input != null ? InkStyle.Dim : InkStyle.Line, "cm");
        if (_modalTotal > _modalVisible && _modalVisible > 0)
        {
            var track = new Rect2(_modalPanel.End.X - 36f, _modalBody.Position.Y, 6f, _modalBody.Size.Y);
            PortraitFrame.Bevel(this, track, 3f, InkStyle.Hover);
            var h = Mathf.Max(48f, track.Size.Y * _modalVisible / _modalTotal);
            var y0 = track.Position.Y + (track.Size.Y - h) * _modalFirst / (_modalTotal - _modalVisible);
            PortraitFrame.Bevel(this, new Rect2(track.Position.X, y0, 6f, h), 3f, InkStyle.Dim);
        }

        var y = top + shownBody;
        if (page.Input != null)
        {
            _modalInput = new Rect2(_modalBody.Position.X, y, textWidth, PortraitLayout.TouchComfort);
            PortraitFrame.Bevel(this, _modalInput, 22f, InkStyle.Panel, InkStyle.Line, 4f);
            var text = page.Input.Text.Length > 0 ? page.Input.Text : page.Input.Placeholder;
            InkDraw.TextBounded(this, new Rect2(_modalInput.Position.X + 40f, _modalInput.Position.Y, textWidth - 220f, _modalInput.Size.Y),
                text, PortraitLayout.FontBody, PortraitLayout.FontMeta, page.Input.Text.Length > 0 ? InkStyle.Line : InkStyle.Dim, "lm");
            InkDraw.Text(this, new Vector2(_modalInput.End.X - 40f, _modalInput.GetCenter().Y),
                $"{page.Input.Text.Length} / {page.Input.MaxChars}", PortraitLayout.FontMeta, InkStyle.Dim, "rm");
            _hits.Add(new PortraitWidget(_modalInput, PortraitAction.ModalInput, 0, true, ""));
            y += PortraitLayout.TouchComfort + PortraitLayout.ModalGap;
        }
        for (var i = 0; i < page.Choices.Count; i++)
        {
            var choice = page.Choices[i];
            Rect2 rect;
            if (sideBySide)
            {
                var w = (textWidth - PortraitLayout.ModalGap) / 2f;
                rect = new Rect2(_modalBody.Position.X + i * (w + PortraitLayout.ModalGap), y, w, PortraitLayout.ModalButtonHeight);
            }
            else
            {
                rect = new Rect2(_modalBody.Position.X, y, textWidth, PortraitLayout.ModalButtonHeight);
                y += PortraitLayout.ModalButtonHeight + PortraitLayout.ModalGap;
            }
            var primary = choice.Id == "confirm" || page.Choices.Count == 1;
            PortraitFrame.Plaque(this, rect, choice.Label, primary: primary, enabled: choice.Enabled);
            _hits.Add(new PortraitWidget(rect, PortraitAction.ModalChoice, i, choice.Enabled, choice.Id));
        }
        DrawBreathingArrow();
    }

    private void DrawMonsterCodex(InkModalPage page, InkModalMonsterCodexData data)
    {
        const float heading = 170f;
        // Keep the long-standing 9-line modal rectangle; only reorganize what is drawn inside it.
        const float bodyHeight = PortraitLayout.ModalLineHeight * 9f + 30f;
        const float introHeight = 48f;
        const float sectionHeight = 48f;
        const float statHeight = 70f;
        const float statGap = 8f;
        const float contentGap = 12f;
        const float infoHeight = 92f;
        var statRows = (data.Attributes.Count + 1) / 2;
        var statsHeight = statRows * statHeight + (statRows - 1) * statGap;
        var contentHeight = introHeight + 8f + sectionHeight + statsHeight + contentGap
            + sectionHeight + 8f + infoHeight;
        var totalHeight = PortraitLayout.ModalPad * 2f + heading + bodyHeight + PortraitLayout.ModalArrowBand;

        _modalBody = new Rect2();
        _modalTotal = 0;
        _modalVisible = 0;
        _modalPanel = PortraitLayout.ModalBounds(totalHeight);
        PortraitFrame.GothicFrame(this, _modalPanel, new Color(InkStyle.Panel, 1f));

        var textWidth = PortraitLayout.ModalWidth - PortraitLayout.ModalPad * 2f;
        var top = _modalPanel.Position.Y + PortraitLayout.ModalPad;
        TitleBackdrop(top + 40f, 140f);
        InkDraw.TextBounded(this,
            new Rect2(_modalPanel.Position.X + PortraitLayout.ModalPad, top, textWidth, 80f), page.Title,
            PortraitLayout.FontTitle, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        PortraitFrame.FadingRule(this, _modalPanel.Position.X + 160f, _modalPanel.End.X - 160f, top + 110f);
        top += heading;

        var innerX = _modalPanel.Position.X + PortraitLayout.ModalPad;
        var innerWidth = textWidth;
        top += (bodyHeight - contentHeight) / 2f;
        InkDraw.TextBounded(this, new Rect2(innerX, top, innerWidth, introHeight),
            $"汇总 {data.RecordCount} 条配置 · 数值以范围表示", PortraitLayout.FontMeta,
            PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        top += introHeight + 8f;

        PortraitFrame.SectionRule(this, innerX + 24f, innerX + innerWidth - 24f, top + sectionHeight / 2f, "战斗属性");
        top += sectionHeight;
        var statWidth = (innerWidth - 16f) / 2f;
        for (var row = 0; row < statRows; row++)
        {
            var first = row * 2;
            var remaining = data.Attributes.Count - first;
            var fullWidth = remaining == 1;
            var rowY = top + row * (statHeight + statGap);
            DrawCodexMetric(this, new Rect2(innerX, rowY, fullWidth ? innerWidth : statWidth, statHeight), data.Attributes[first]);
            if (!fullWidth)
                DrawCodexMetric(this, new Rect2(innerX + statWidth + 16f, rowY, statWidth, statHeight), data.Attributes[first + 1]);
        }
        top += statsHeight + contentGap;

        PortraitFrame.SectionRule(this, innerX + 24f, innerX + innerWidth - 24f, top + sectionHeight / 2f, "能力与掉落");
        top += sectionHeight + 8f;
        var infoWidth = (innerWidth - 16f) / 2f;
        DrawCodexSkills(this, new Rect2(innerX, top, infoWidth, infoHeight), data.Skills);
        DrawCodexDrops(this, new Rect2(innerX + infoWidth + 16f, top, infoWidth, infoHeight), data.Drops);
        DrawBreathingArrow();
    }

    private static void DrawCodexMetric(CanvasItem ci, Rect2 rect, InkModalMonsterCodexData.Metric metric)
    {
        PortraitFrame.Card(ci, rect, radius: 22f);
        var labelWidth = rect.Size.X * 0.46f;
        InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 22f, rect.Position.Y + 6f, labelWidth - 30f, rect.Size.Y - 12f),
            metric.Label, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        InkDraw.TextBounded(ci, new Rect2(rect.Position.X + labelWidth, rect.Position.Y + 6f,
                rect.Size.X - labelWidth - 22f, rect.Size.Y - 12f),
            metric.Value, PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "rm");
    }

    private static void DrawCodexSkills(CanvasItem ci, Rect2 rect, IReadOnlyList<string> skills)
    {
        PortraitFrame.Card(ci, rect, radius: 22f);
        InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 18f, rect.Position.Y + 4f, rect.Size.X - 36f, 38f),
            "额外技能", PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        if (skills.Count == 0)
        {
            InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 18f, rect.Position.Y + 42f, rect.Size.X - 36f, 42f),
                "暂无记录", PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            return;
        }

        for (var i = 0; i < skills.Count; i++)
        {
            var y = rect.Position.Y + 42f + i * 44f;
            InkDraw.Jewel(ci, new Vector2(rect.Position.X + 22f, y + 21f), 8f, InkStyle.Line);
            InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 40f, y, rect.Size.X - 56f, 42f),
                skills[i], PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        }
    }

    private static void DrawCodexDrops(CanvasItem ci, Rect2 rect, IReadOnlyList<InkModalMonsterCodexData.Drop> drops)
    {
        PortraitFrame.Card(ci, rect, radius: 22f);
        InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 18f, rect.Position.Y + 4f, rect.Size.X - 36f, 38f),
            "掉落", PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        if (drops.Count == 0)
        {
            InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 18f, rect.Position.Y + 42f, rect.Size.X - 36f, 42f),
                "暂无记录", PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            return;
        }

        for (var i = 0; i < drops.Count; i++)
        {
            var y = rect.Position.Y + 42f + i * 44f;
            var drop = drops[i];
            var text = $"{drop.Name} {drop.Quantity} · {drop.Chance}";
            InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 18f, y, rect.Size.X - 36f, 42f),
                text, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            if (i > 0)
                ci.DrawLine(new Vector2(rect.Position.X + 16f, y - 4f),
                    new Vector2(rect.End.X - 16f, y - 4f), InkStyle.Dim, 2f);
        }
    }

    private void DrawBreathingArrow()
    {
        var alpha = 0.28f + 0.72f * (0.5f + 0.5f * Mathf.Sin(_modalTime * Mathf.Tau / 1.8f));
        DrawColoredPolygon(PortraitLayout.ModalArrow(_modalPanel), new Color(InkStyle.Line, alpha));
    }

    /// <summary>标题底纹：以 centerY 为中线、高 height，横向让开四角角花。</summary>
    private void TitleBackdrop(float centerY, float height)
    {
        const float inset = 150f;
        PortraitTracery.TitleBackdrop(this,
            new Rect2(_modalPanel.Position.X + inset, centerY - height / 2f, _modalPanel.Size.X - inset * 2f, height));
    }

    private const float StripHeight = 120f;
    private const float StripGap = 14f;

    /// <summary>条状行（与仓储存取行同一套：缺角细框＋菱形首字）。</summary>
    private void DrawStrip(Rect2 rect, string glyph, string name, string value)
    {
        PortraitFrame.Bevel(this, rect, 22f, null, InkStyle.WoodDark, 3f);
        var icon = new Vector2(rect.Position.X + 62f, rect.GetCenter().Y);
        InkDraw.Jewel(this, icon, 34f, InkStyle.Dim);
        InkDraw.Jewel(this, icon, 30f, InkStyle.Bg);
        InkDraw.Text(this, icon, glyph, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        var valueWidth = Mathf.Min(260f, InkDraw.Measure(value, PortraitLayout.FontBody).X + 8f);
        InkDraw.TextBounded(this, new Rect2(rect.Position.X + 116f, rect.Position.Y, rect.Size.X - 116f - valueWidth - 60f, rect.Size.Y),
            name, PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        InkDraw.TextBounded(this, new Rect2(rect.End.X - 32f - valueWidth, rect.Position.Y, valueWidth, rect.Size.Y),
            value, PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "rm");
    }

    /// <summary>
    /// 结算经验一格：上行「名 Lv n ▲ … +经验」，升了级就在等级后挂一枚实心上升三角；下行尖头进度条（本级进度）。
    /// </summary>
    private void DrawExpMeter(Rect2 r, string name, int level, int gained, float ratio, bool levelUp)
    {
        var mid = r.Position.Y + 20f;
        InkDraw.Text(this, new Vector2(r.Position.X, mid), name, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        var x = r.Position.X + InkDraw.Measure(name, PortraitLayout.FontMeta).X + 14f;
        var lv = $"Lv{level}";
        InkDraw.Text(this, new Vector2(x, mid + 2f), lv, 36, InkStyle.Dim, "lm");
        x += InkDraw.Measure(lv, 36).X + 10f;
        if (levelUp)
            DrawColoredPolygon(new[] { new Vector2(x, mid + 10f), new Vector2(x + 22f, mid + 10f), new Vector2(x + 11f, mid - 12f) }, InkStyle.Line);
        InkDraw.Text(this, new Vector2(r.End.X, mid + 2f), $"+{gained}", 36, InkStyle.Line, "rm");
        PortraitFrame.Bar(this, new Rect2(r.Position.X, r.Position.Y + 50f, r.Size.X, 14f), ratio);
    }

    /// <summary>战利品可滚时右下角那枚 ▼ 与滑条占的额外高度。</summary>
    private const float LootScrollBand = 34f;

    /// <summary>
    /// 战后结算版面里除战利品条以外的固定高度（标题带 300、经验节头 90、每人 150、返回钮带 220、战利品节头与尾距 76）。
    /// </summary>
    private static float SettlementFixedHeight(int lootCount, int partyRows) =>
        300f + (lootCount > 0 ? 76f : 0f) + 90f + partyRows * 150f + 220f;

    /// <summary>弹窗最高时战利品条最多能完整排下几条；超出就转为滚动（右下 ▼＋滑条）。</summary>
    public static int SettlementLootCapacity(int partyRows)
    {
        var room = PortraitLayout.ModalBounds(float.MaxValue).Size.Y - SettlementFixedHeight(1, partyRows) - LootScrollBand;
        return Math.Max(1, (int)((room + StripGap) / (StripHeight + StripGap)));
    }

    /// <summary>战后结算：大字胜负（压窗花底纹）、轮数、战利品条（金钱在前，物品按价值从高到低）、各人经验行；点任意处返回。</summary>
    private void DrawSettlement(InkModalPage page, InkModalSettlementData data)
    {
        var loot = new List<(string Glyph, string Name, string Count, Rimisekai.Defs.Quality? Quality)>();
        if (data.Money > 0)
            loot.Add(("金", "金钱", $"+{data.Money}G", null));
        foreach (var item in data.Items)
            loot.Add((item.Label[..1], item.Label, $"×{item.Count}", item.Quality));
        var capacity = SettlementLootCapacity(data.Rows.Count);
        var scrolls = loot.Count > capacity;
        var shown = scrolls ? capacity : loot.Count;
        var lootHeight = shown * (StripHeight + StripGap) + (scrolls ? LootScrollBand : 0f);
        _modalPanel = PortraitLayout.ModalBounds(SettlementFixedHeight(loot.Count, data.Rows.Count) + lootHeight);
        _modalBody = new Rect2();
        _modalTotal = _modalVisible = 0;
        PortraitFrame.GothicFrame(this, _modalPanel, new Color(InkStyle.Panel, 1f));
        var cx = _modalPanel.GetCenter().X;
        var y = _modalPanel.Position.Y + 120f;
        TitleBackdrop(y, 176f);
        InkDraw.TextBounded(this, new Rect2(_modalPanel.Position.X + 60f, y - 70f, _modalPanel.Size.X - 120f, 140f), page.Title,
            PortraitLayout.FontDisplay, PortraitLayout.FontTitle, InkStyle.Line, "cm");
        PortraitFrame.FadingRule(this, _modalPanel.Position.X + 140f, _modalPanel.End.X - 140f, y + 90f);
        InkDraw.Text(this, new Vector2(cx, y + 150f), $"历经 {data.Rounds} 回合", PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        y += 230f;
        var left = _modalPanel.Position.X + 60f;
        var right = _modalPanel.End.X - 60f;
        if (loot.Count > 0)
        {
            PortraitFrame.SectionRule(this, left, right, y, "战利品");
            y += 60f;
            var listRight = scrolls ? right - 24f : right;
            var listHeight = shown * (StripHeight + StripGap) - StripGap;
            if (scrolls)
            {
                // 战利品区接进弹窗通用的拖动 / 滚轮：一格＝一条。
                _modalStep = StripHeight + StripGap;
                _modalBody = new Rect2(left, y, right - left, listHeight);
                _modalTotal = loot.Count;
                _modalVisible = shown;
                _modalFirst = Math.Clamp(_modalFirst, 0, loot.Count - shown);
            }
            var first = scrolls ? _modalFirst : 0;
            for (var i = 0; i < shown; i++)
            {
                var (glyph, name, count, quality) = loot[first + i];
                var strip = new Rect2(left, y + i * (StripHeight + StripGap), listRight - left, StripHeight);
                DrawStrip(strip, glyph, name, count);
                if (quality is { } q)
                    _fog.Place(strip.Grow(-4f), q);
            }
            if (scrolls)
            {
                var track = new Rect2(right - 8f, y, 6f, listHeight);
                PortraitFrame.Bevel(this, track, 3f, InkStyle.Hover);
                var h = Mathf.Max(48f, track.Size.Y * shown / loot.Count);
                var y0 = track.Position.Y + (track.Size.Y - h) * first / (loot.Count - shown);
                PortraitFrame.Bevel(this, new Rect2(track.Position.X, y0, 6f, h), 3f, InkStyle.Dim);
                if (first + shown < loot.Count)
                {
                    // 右下角 ▼：下面还有，往上拖可看；拖到底即隐去。
                    var alpha = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(_modalTime * Mathf.Tau / 1.8f));
                    var tip = new Vector2(track.GetCenter().X - 4f, y + listHeight + LootScrollBand - 6f);
                    DrawColoredPolygon(new[] { tip + new Vector2(-14f, -16f), tip + new Vector2(14f, -16f), tip },
                        new Color(InkStyle.Line, alpha));
                }
            }
            y += lootHeight + 16f;
        }
        PortraitFrame.SectionRule(this, left, right, y, "经验");
        y += 50f;
        foreach (var row in data.Rows)
        {
            InkDraw.Text(this, new Vector2(left + 10f, y + 36f), row.Name, PortraitLayout.FontBody, InkStyle.Line, "lm");
            InkDraw.Text(this, new Vector2(left + 30f + InkDraw.Measure(row.Name, PortraitLayout.FontBody).X, y + 38f),
                $"Lv {row.Level}", PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            InkDraw.Text(this, new Vector2(right - 10f, y + 38f), $"伤害 {row.DamageDealt}", PortraitLayout.FontMeta, InkStyle.Dim, "rm");
            var half = (right - left - 20f - 40f) / 2f;
            DrawExpMeter(new Rect2(left + 10f, y + 66f, half, 72f), row.WeaponName, row.WeaponLevel, row.WeaponExp, row.WeaponRatio, row.WeaponLevelUp);
            DrawExpMeter(new Rect2(left + 10f + half + 40f, y + 66f, half, 72f), row.StyleName, row.StyleLevel, row.StyleExp, row.StyleRatio, row.StyleLevelUp);
            y += 150f;
        }
        var go = new Rect2(_modalPanel.Position.X + 80f, _modalPanel.End.Y - 70f - 128f, _modalPanel.Size.X - 160f, 128f);
        PortraitFrame.Plaque(this, go, "返回领地", primary: true);
        // 结算的下一步是一枚真能点的钮（点别处照样推进）。
        _hits.Add(new PortraitWidget(go, PortraitAction.ModalChoice, 0, true, "settle"));
        DrawBreathingArrow();
    }

    private const float DetailRow = 104f;
    private const float DetailNoteRow = 150f;
    private const float DetailGap = 12f;

    /// <summary>
    /// 物品详情：标题（压窗花底纹）→ 副题（谁的哪一槽 / 背包件数）→ 渐隐线 →
    /// 逐行条：左暗标签、右亮数值；带注解的行（附魔）数值下再挂一行暗字注解。
    /// </summary>
    private void DrawItemDetail(InkModalPage page, InkModalItemData data)
    {
        var heading = data.Subtitle.Length > 0 ? 210f : 170f;
        var rows = 0f;
        foreach (var line in data.Lines)
            rows += (line.Note.Length > 0 ? DetailNoteRow : DetailRow) + DetailGap;
        _modalPanel = PortraitLayout.ModalBounds(PortraitLayout.ModalPad * 2f + heading + rows + PortraitLayout.ModalArrowBand);
        _modalBody = new Rect2();
        _modalTotal = _modalVisible = 0;
        PortraitFrame.GothicFrame(this, _modalPanel, new Color(InkStyle.Panel, 1f));
        if (data.Quality is { } quality)
            _fog.Place(_modalPanel.Grow(-18f), quality, rising: true);

        var textWidth = PortraitLayout.ModalWidth - PortraitLayout.ModalPad * 2f;
        var left = _modalPanel.Position.X + PortraitLayout.ModalPad;
        var top = _modalPanel.Position.Y + PortraitLayout.ModalPad;
        TitleBackdrop(top + 40f, 140f);
        InkDraw.TextBounded(this, new Rect2(left, top, textWidth, 80f), page.Title,
            PortraitLayout.FontTitle, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        if (data.Subtitle.Length > 0)
            InkDraw.TextBounded(this, new Rect2(left, top + 104f, textWidth, 52f), data.Subtitle,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        PortraitFrame.FadingRule(this, _modalPanel.Position.X + 160f, _modalPanel.End.X - 160f, top + (data.Subtitle.Length > 0 ? 176f : 110f));
        var y = top + heading;
        foreach (var line in data.Lines)
        {
            var h = line.Note.Length > 0 ? DetailNoteRow : DetailRow;
            var rect = new Rect2(left, y, textWidth, h);
            PortraitFrame.Bevel(this, rect, 22f, null, InkStyle.WoodDark, 3f);
            var mid = line.Note.Length > 0 ? rect.Position.Y + 54f : rect.GetCenter().Y;
            InkDraw.Jewel(this, new Vector2(rect.Position.X + 34f, mid), 7f, InkStyle.Dim);
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 58f, mid - 30f, 160f, 60f), line.Label,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + 220f, mid - 32f, rect.Size.X - 252f, 64f), line.Value,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "rm");
            if (line.Note.Length > 0)
                InkDraw.TextBounded(this, new Rect2(rect.Position.X + 220f, rect.Position.Y + 88f, rect.Size.X - 252f, 50f), line.Note,
                    PortraitLayout.FontMeta, 36, InkStyle.Dim, "rm");
            y += h + DetailGap;
        }
        DrawBreathingArrow();
    }

    private bool HandleModalScroll(InputEvent input)
    {
        if (_modalTotal <= _modalVisible)
            return false;
        switch (input)
        {
            case InputEventMouseButton mouse when mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                if (!mouse.Pressed || !_modalBody.HasPoint(mouse.Position))
                    return false;
                _modalFirst = Math.Clamp(_modalFirst + (mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1),
                    0, _modalTotal - _modalVisible);
                QueueRedraw();
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse:
                if (mouse.Pressed && _modalBody.HasPoint(mouse.Position))
                {
                    _modalPressed = true;
                    _modalDragged = false;
                    _modalPressY = mouse.Position.Y;
                    _modalPressFirst = _modalFirst;
                    return true;
                }
                if (!mouse.Pressed && _modalPressed)
                {
                    _modalPressed = false;
                    return _modalDragged;
                }
                return false;
            case InputEventMouseMotion mouse when _modalPressed:
                var dy = mouse.Position.Y - _modalPressY;
                if (Mathf.Abs(dy) >= PortraitLayout.ListDragThreshold)
                    _modalDragged = true;
                if (_modalDragged)
                    _modalFirst = Math.Clamp(_modalPressFirst - (int)(dy / _modalStep),
                        0, _modalTotal - _modalVisible);
                QueueRedraw();
                return true;
            default:
                return false;
        }
    }
}
