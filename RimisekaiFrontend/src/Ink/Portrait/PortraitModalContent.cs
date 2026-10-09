using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 弹窗排版（2026-10-07 重设计）：整屏压暗 → 居中缺角双线框 → 标题（居中大字＋渐隐线）→ 正文 →
/// 原生输入框（圆角框＋字数）→ 药丸钮（两钮并排，确定＝实心；多钮竖排）。
/// 底部一律一枚呼吸的实心 ▼；战后结算单独排（大字胜负 / 轮数 / 战利品菱块 / 各人经验）。
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
        PortraitFrame.GothicFrame(this, _modalPanel, new Color(InkStyle.Panel, 1f), crest: true);

        var top = _modalPanel.Position.Y + PortraitLayout.ModalPad;
        if (page.Title.Length > 0)
        {
            InkDraw.TextBounded(this, new Rect2(_modalPanel.Position.X + PortraitLayout.ModalPad, top, textWidth, 80f), page.Title,
                PortraitLayout.FontTitle, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            PortraitFrame.FadingRule(this, _modalPanel.Position.X + 160f, _modalPanel.End.X - 160f, top + 110f);
            top += heading;
        }
        var available = _modalPanel.End.Y - PortraitLayout.ModalPad - arrow - top - controls;
        var shownBody = Mathf.Min(bodyHeight, Mathf.Max(0f, available));
        _modalBody = new Rect2(_modalPanel.Position.X + PortraitLayout.ModalPad, top, textWidth, shownBody);
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
        PortraitFrame.GothicFrame(this, _modalPanel, new Color(InkStyle.Panel, 1f), crest: true);

        var textWidth = PortraitLayout.ModalWidth - PortraitLayout.ModalPad * 2f;
        var top = _modalPanel.Position.Y + PortraitLayout.ModalPad;
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

    private static string ItemLabel(string itemId)
    {
        var thing = Rimisekai.Defs.Items.Get(itemId);
        return thing != null && thing.Label.Length > 0 ? thing.Label : itemId;
    }

    /// <summary>战后结算：大字胜负、轮数、战利品菱块（金钱在前）、各人经验行；点任意处返回。</summary>
    private void DrawSettlement(InkModalPage page, InkModalSettlementData data)
    {
        var loot = new List<(string Glyph, string Name, string Count)>();
        if (data.Money > 0)
            loot.Add(("金", "金钱", $"+{data.Money}G"));
        foreach (var item in data.Items)
        {
            var label = ItemLabel(item.ItemId);
            loot.Add((label[..1], label, $"×{item.Count}"));
        }
        var lootRows = (loot.Count + 3) / 4;
        var height = 300f + (loot.Count > 0 ? 90f + lootRows * 260f : 0f) + 90f + data.Rows.Count * 150f + 220f;
        _modalPanel = PortraitLayout.ModalBounds(height);
        _modalBody = new Rect2();
        _modalTotal = _modalVisible = 0;
        PortraitFrame.GothicFrame(this, _modalPanel, new Color(InkStyle.Panel, 1f), crest: true);
        var cx = _modalPanel.GetCenter().X;
        var y = _modalPanel.Position.Y + 120f;
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
            var cell = (right - left) / 4f;
            for (var i = 0; i < loot.Count; i++)
            {
                var c = new Vector2(left + (i % 4 + 0.5f) * cell, y + i / 4 * 260f + 70f);
                InkDraw.Jewel(this, c, 60f, InkStyle.Line);
                InkDraw.Jewel(this, c, 55f, InkStyle.Bg);
                InkDraw.Text(this, c, loot[i].Glyph, PortraitLayout.FontBody, InkStyle.Line, "cm");
                InkDraw.TextBounded(this, new Rect2(c.X - cell / 2f + 8f, c.Y + 76f, cell - 16f, 52f), loot[i].Name,
                    PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
                InkDraw.Text(this, new Vector2(c.X, c.Y + 156f), loot[i].Count, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            }
            y += lootRows * 260f + 30f;
        }
        PortraitFrame.SectionRule(this, left, right, y, "经验");
        y += 50f;
        foreach (var row in data.Rows)
        {
            InkDraw.Text(this, new Vector2(left + 10f, y + 36f), row.Name, PortraitLayout.FontBody, InkStyle.Line, "lm");
            InkDraw.Text(this, new Vector2(left + 30f + InkDraw.Measure(row.Name, PortraitLayout.FontBody).X, y + 38f),
                $"Lv {row.Level}", PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            InkDraw.Text(this, new Vector2(right - 10f, y + 38f), $"伤害 {row.DamageDealt}", PortraitLayout.FontMeta, InkStyle.Dim, "rm");
            InkDraw.TextBounded(this, new Rect2(left + 10f, y + 72f, right - left - 20f, 56f),
                $"{row.WeaponName} Lv{row.WeaponLevel} +{row.WeaponExp} · {row.StyleName} Lv{row.StyleLevel} +{row.StyleExp}",
                PortraitLayout.FontMeta, 36, InkStyle.Line, "lm");
            y += 150f;
        }
        var go = new Rect2(_modalPanel.Position.X + 80f, _modalPanel.End.Y - 70f - 128f, _modalPanel.Size.X - 160f, 128f);
        PortraitFrame.Plaque(this, go, "返回领地", primary: true);
        // 结算的下一步是一枚真能点的钮（点别处照样推进）。
        _hits.Add(new PortraitWidget(go, PortraitAction.ModalChoice, 0, true, "settle"));
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
                    _modalFirst = Math.Clamp(_modalPressFirst - (int)(dy / PortraitLayout.ModalLineHeight),
                        0, _modalTotal - _modalVisible);
                QueueRedraw();
                return true;
            default:
                return false;
        }
    }
}
