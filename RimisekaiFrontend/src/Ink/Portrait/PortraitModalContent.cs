using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 弹窗排版（2026-10-07 重设计）：整屏压暗 → 居中缺角双线框 → 标题（居中大字＋渐隐线）→ 正文 →
/// 原生输入框（圆角框＋字数）→ 药丸钮（两钮并排，确定＝实心；多钮竖排）。
/// 纯展示页底部一枚呼吸的实心 ▼；战后结算单独排（大字胜负 / 轮数 / 战利品菱块 / 各人经验）。
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
        var arrow = page.HasInteractiveControls ? 0f : PortraitLayout.ModalArrowBand;
        _modalPanel = PortraitLayout.ModalBounds(PortraitLayout.ModalPad * 2f + heading + bodyHeight + controls + arrow);
        PortraitFrame.NotchedFrame(this, _modalPanel, new Color(0.03f, 0.03f, 0.03f));

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
            PortraitFrame.RoundRect(this, track, 3f, InkStyle.Hover);
            var h = Mathf.Max(48f, track.Size.Y * _modalVisible / _modalTotal);
            var y0 = track.Position.Y + (track.Size.Y - h) * _modalFirst / (_modalTotal - _modalVisible);
            PortraitFrame.RoundRect(this, new Rect2(track.Position.X, y0, 6f, h), 3f, InkStyle.Dim);
        }

        var y = top + shownBody;
        if (page.Input != null)
        {
            _modalInput = new Rect2(_modalBody.Position.X, y, textWidth, PortraitLayout.TouchComfort);
            PortraitFrame.RoundRect(this, _modalInput, 22f, InkStyle.Panel, InkStyle.Line, 4f);
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
            PortraitFrame.Pill(this, rect, choice.Label, primary: primary, enabled: choice.Enabled);
            _hits.Add(new PortraitWidget(rect, PortraitAction.ModalChoice, i, choice.Enabled, choice.Id));
        }
        if (!page.HasInteractiveControls)
            DrawBreathingArrow();
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
        PortraitFrame.NotchedFrame(this, _modalPanel, new Color(0.03f, 0.03f, 0.03f));
        var cx = _modalPanel.GetCenter().X;
        var y = _modalPanel.Position.Y + 120f;
        InkDraw.TextBounded(this, new Rect2(_modalPanel.Position.X + 60f, y - 70f, _modalPanel.Size.X - 120f, 140f), page.Title,
            PortraitLayout.FontDisplay, PortraitLayout.FontTitle, InkStyle.Line, "cm");
        PortraitFrame.FadingRule(this, _modalPanel.Position.X + 140f, _modalPanel.End.X - 140f, y + 90f);
        InkDraw.Text(this, new Vector2(cx, y + 150f), $"历经 {data.Rounds} 轮", PortraitLayout.FontMeta, InkStyle.Dim, "cm");
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
        PortraitFrame.Pill(this, go, "返回领地", primary: true);
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
