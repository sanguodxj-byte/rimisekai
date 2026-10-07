using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public partial class PortraitModalLayer
{
    private readonly record struct ModalLine(string Label, string Value, int Size, bool Rule = false, string Icon = "");
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
        var textWidth = PortraitLayout.ModalWidth - PortraitLayout.Pad * 2f;
        var lines = ModalLines(page, textWidth);
        var controls = page.Choices.Count + (page.Input == null ? 0 : 1);
        var controlsHeight = controls * (PortraitLayout.TouchComfort + PortraitLayout.ModalGap);
        var bodyHeight = lines.Count * PortraitLayout.ModalLineHeight;
        _modalPanel = PortraitLayout.ModalBounds(bodyHeight, controlsHeight, page.Title.Length > 0);
        DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), new Color(InkStyle.Bg, 0.72f));
        // 弹窗只有 800 宽：排线区铺到 300 时整块内腔都是纹理，读成方格纸。
        // 弹窗要的是"框＋角花"，纹样留给整屏花框（TitleFrame）。
        PortraitFrame.Panel(this, _modalPanel);
        var top = _modalPanel.Position.Y + PortraitLayout.Pad;
        if (page.Title.Length > 0)
        {
            var title = PortraitLayout.ModalTitle(_modalPanel);
            InkDraw.TextBounded(this, title, page.Title, PortraitLayout.FontTitle, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            PortraitFrame.FadingRule(this, title.Position.X, title.End.X, title.End.Y);
            top += PortraitLayout.TitleBand;
        }
        var available = _modalPanel.End.Y - PortraitLayout.ModalArrowBand - PortraitLayout.Pad - top;
        var shownBody = Mathf.Min(bodyHeight, Math.Max(0f, available - controlsHeight));
        var start = top + Math.Max(0f, (available - shownBody - controlsHeight) / 2f);
        _modalBody = new Rect2(_modalPanel.Position.X + PortraitLayout.Pad, start, textWidth, shownBody);
        _modalVisible = Math.Max(0, (int)(shownBody / PortraitLayout.ModalLineHeight));
        _modalTotal = lines.Count;
        _modalFirst = Math.Clamp(_modalFirst, 0, Math.Max(0, lines.Count - _modalVisible));
        for (var i = 0; i < _modalVisible && i + _modalFirst < lines.Count; i++)
        {
            var line = lines[i + _modalFirst];
            var rect = new Rect2(_modalBody.Position.X, start + i * PortraitLayout.ModalLineHeight,
                _modalBody.Size.X, PortraitLayout.ModalLineHeight);
            if (line.Rule)
            {
                PortraitFrame.FadingRule(this, rect.Position.X + 8f, rect.End.X - 8f, rect.Position.Y);
                rect = new Rect2(rect.Position.X, rect.Position.Y + 14f, rect.Size.X, rect.Size.Y - 14f);
            }
            if (line.Icon.Length > 0 && InkIcon.Has(line.Icon))
                DrawModalIcon(rect, line.Icon, line.Value, line.Size);
            else if (line.Value.Length == 0)
                InkDraw.TextBounded(this, rect, line.Label, line.Size, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            else
                DrawModalValue(rect, line.Label, line.Value, line.Size);
        }
        if (_modalTotal > _modalVisible && _modalVisible > 0)
        {
            var track = new Rect2(_modalPanel.End.X - 36f, _modalBody.Position.Y, 12f, _modalBody.Size.Y);
            InkDraw.InkLine(this, new Vector2(track.GetCenter().X, track.Position.Y),
                new Vector2(track.GetCenter().X, track.End.Y), InkStyle.Dim, PortraitLayout.LineHair);
            DrawRect(PortraitLayout.ListThumb(track, _modalTotal, _modalVisible, _modalFirst), InkStyle.Line);
        }
        var y = start + shownBody;
        if (page.Input != null)
        {
            _modalInput = new Rect2(_modalBody.Position.X, y, textWidth, PortraitLayout.TouchComfort);
            PortraitFrame.Button(this, _modalInput, page.Input.Text.Length > 0 ? page.Input.Text : page.Input.Placeholder);
            _hits.Add(new PortraitWidget(_modalInput, PortraitAction.ModalInput, 0, true, ""));
            y += PortraitLayout.TouchComfort + PortraitLayout.ModalGap;
        }
        for (var i = 0; i < page.Choices.Count; i++)
        {
            var choice = page.Choices[i];
            var rect = new Rect2(_modalBody.Position.X, y, textWidth, PortraitLayout.TouchComfort);
            PortraitFrame.Button(this, rect, choice.Label, enabled: choice.Enabled);
            _hits.Add(new PortraitWidget(rect, PortraitAction.ModalChoice, i, choice.Enabled, choice.Id));
            y += PortraitLayout.TouchComfort + PortraitLayout.ModalGap;
        }
        var alpha = 0.28f + 0.72f * (0.5f + 0.5f * Mathf.Sin(_modalTime * Mathf.Tau / 1.8f));
        DrawColoredPolygon(PortraitLayout.ModalArrow(_modalPanel), new Color(InkStyle.Line, alpha));
    }

    private static List<ModalLine> ModalLines(InkModalPage page, float width)
    {
        var lines = new List<ModalLine>();
        if (page.Settlement is not { } data)
        {
            foreach (var text in InkDraw.WrapLines(page.Body, width, PortraitLayout.FontBody))
                lines.Add(new ModalLine(text, "", PortraitLayout.FontBody));
            return lines;
        }
        lines.Add(new ModalLine($"历经 {data.Rounds} 轮战斗", "", PortraitLayout.FontBody));
        foreach (var row in data.Rows)
        {
            lines.Add(new ModalLine(row.Name, $"Lv.{row.Level}", PortraitLayout.FontTitle, true));
            lines.Add(new ModalLine("造成伤害", row.DamageDealt.ToString(), PortraitLayout.FontBody));
            lines.Add(new ModalLine(row.WeaponName, $"Lv.{row.WeaponLevel}  +{row.WeaponExp}", PortraitLayout.FontBody,
                Icon: row.WeaponName));
            lines.Add(new ModalLine(row.StyleName, $"Lv.{row.StyleLevel}  +{row.StyleExp}", PortraitLayout.FontBody,
                Icon: row.StyleName));
        }
        if (data.Money > 0 || data.Items.Count > 0)
            lines.Add(new ModalLine("缴获战利品", "", PortraitLayout.FontBody, true));
        if (data.Money > 0)
            lines.Add(new ModalLine("金钱", $"+{data.Money}G", PortraitLayout.FontBody));
        foreach (var item in data.Items)
            lines.Add(new ModalLine(item.ItemId, $"×{item.Count}", PortraitLayout.FontBody, Icon: item.ItemId));
        return lines;
    }

    /// <summary>带图标的一行：实心图标替代名称文字，与数值组成一组在行内居中；图标缺失时回退文字。</summary>
    private void DrawModalIcon(Rect2 rect, string icon, string value, int size)
    {
        const float iconSize = 56f;
        const float gap = 20f;
        var valueSize = InkDraw.FitSize(value, rect.Size.X * 0.6f, size, PortraitLayout.FontMeta);
        var valueWidth = InkDraw.Measure(value, valueSize).X;
        var total = iconSize + gap + valueWidth;
        var x0 = rect.GetCenter().X - total / 2f;
        var cy = rect.GetCenter().Y;
        InkIcon.Draw(this, icon, new Rect2(x0, cy - iconSize / 2f, iconSize, iconSize));
        InkDraw.Text(this, new Vector2(x0 + iconSize + gap + valueWidth, cy), value, valueSize, InkStyle.Line, "rm");
    }

    private void DrawModalValue(Rect2 rect, string label, string value, int size)
    {
        var labelSize = PortraitLayout.FontMeta;
        var valueSize = InkDraw.FitSize(value, rect.Size.X * 0.55f, size, PortraitLayout.FontMeta);
        var labelWidth = InkDraw.Measure(label, labelSize).X;
        var valueWidth = InkDraw.Measure(value, valueSize).X;
        var x = rect.GetCenter().X - (labelWidth + PortraitLayout.ModalGap + valueWidth) / 2f;
        InkDraw.Text(this, new Vector2(x, rect.GetCenter().Y), label, labelSize, InkStyle.Dim, "lm");
        InkDraw.Text(this, new Vector2(x + labelWidth + PortraitLayout.ModalGap, rect.GetCenter().Y),
            value, valueSize, InkStyle.Line, "lm");
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
