using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 滚动：一块滚动区＝视口矩形＋总量＋可见量＋当前起点。rowHeight＝1 时按像素滚（卡片流），
/// 否则按行滚（抽屉里的定高列表）。horizontal 为横向拖（「此刻」头像带）。
/// 拖动超过阈值即滚动、不派发；没拖动的松开照常派发命中块。只画一条细指示条，不占内容宽度。
/// </summary>
public partial class PortraitHubScreen
{
    private sealed record ScrollArea(string Id, Rect2 Area, int Total, int Visible,
        int First, Action<int> SetFirst, float RowHeight, bool Horizontal)
    {
        public int Maximum => Math.Max(0, Total - Visible);
    }

    private readonly List<ScrollArea> _scrollAreas = new();
    private readonly Dictionary<string, int> _pan = new();
    private string _scrollActive = "";
    private int _scrollPointer = -2;
    private Vector2 _scrollPressPosition;
    private int _scrollPressFirst;
    private bool _scrollMoved;

    /// <summary>取某滚动区的当前起点，并夹在 [0, total-visible]。</summary>
    private int Pan(string id, int total, int visible)
    {
        var v = Math.Clamp(_pan.GetValueOrDefault(id), 0, Math.Max(0, total - visible));
        _pan[id] = v;
        return v;
    }

    private void RegisterScroll(string id, Rect2 area, int total, int visible, int first,
        Action<int> setFirst, float rowHeight = PortraitLayout.RowHeight, bool horizontal = false)
    {
        var scroll = new ScrollArea(id, area, total, visible, first, setFirst, rowHeight, horizontal);
        _scrollAreas.Add(scroll);
        if (scroll.Maximum == 0 || horizontal)
            return;
        var track = new Rect2(area.End.X - 16f, area.Position.Y + 8f, 6f, area.Size.Y - 16f);
        PortraitFrame.RoundRect(this, track, 3f, InkStyle.Hover);
        var h = Mathf.Max(60f, track.Size.Y * visible / total);
        var y = track.Position.Y + (track.Size.Y - h) * first / scroll.Maximum;
        PortraitFrame.RoundRect(this, new Rect2(track.Position.X, y, track.Size.X, h), 3f, InkStyle.Dim);
    }

    private bool HandleListInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton mouse when mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                if (!mouse.Pressed)
                    return false;
                var wheelArea = ScrollAt(mouse.Position);
                if (wheelArea == null)
                    return false;
                var step = wheelArea.RowHeight <= 1f ? 120 : 1;
                SetScroll(wheelArea, wheelArea.First + (mouse.ButtonIndex == MouseButton.WheelUp ? -step : step));
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse:
                if (_scrollPointer >= 0)
                    return true;
                return mouse.Pressed ? BeginListDrag(mouse.Position, -1) : EndListDrag(mouse.Position, -1);
            case InputEventMouseMotion mouse:
                if (_scrollPointer >= 0)
                    return true;
                return MoveListDrag(mouse.Position, -1);
            case InputEventScreenTouch touch:
                return touch.Pressed ? BeginListDrag(touch.Position, touch.Index) : EndListDrag(touch.Position, touch.Index);
            case InputEventScreenDrag touch:
                return MoveListDrag(touch.Position, touch.Index);
            default:
                return false;
        }
    }

    private ScrollArea? ScrollAt(Vector2 point)
    {
        for (var i = _scrollAreas.Count - 1; i >= 0; i--)
            if (_scrollAreas[i].Area.HasPoint(point) && _scrollAreas[i].Maximum > 0)
                return _scrollAreas[i];
        return null;
    }

    private bool BeginListDrag(Vector2 point, int pointer)
    {
        if (_scrollPointer != -2)
            return false;
        var area = ScrollAt(point);
        if (area == null)
            return false;
        _scrollActive = area.Id;
        _scrollPointer = pointer;
        _scrollPressPosition = point;
        _scrollPressFirst = area.First;
        _scrollMoved = false;
        // 按下反馈与非滚动区同一套：按住的块先画成按下态。
        _pressed = true;
        _dragging = false;
        _pressPos = point;
        _pressRect = Hit(point)?.Widget.Rect;
        QueueRedraw();
        return true;
    }

    private bool MoveListDrag(Vector2 point, int pointer)
    {
        if (_scrollPointer != pointer)
            return false;
        var area = _scrollAreas.Find(candidate => candidate.Id == _scrollActive);
        if (area == null)
        {
            ResetListDrag();
            return false;
        }
        var delta = area.Horizontal ? point.X - _scrollPressPosition.X : point.Y - _scrollPressPosition.Y;
        if (Mathf.Abs(delta) >= PortraitLayout.ListDragThreshold && !_scrollMoved)
        {
            _scrollMoved = true;
            _dragging = true;
        }
        if (_scrollMoved)
            SetScroll(area, _scrollPressFirst - (int)(delta / area.RowHeight));
        return true;
    }

    private bool EndListDrag(Vector2 point, int pointer)
    {
        if (_scrollPointer != pointer)
            return false;
        var click = !_scrollMoved;
        var pressedAt = _scrollPressPosition;
        ResetListDrag();
        _pressed = false;
        _dragging = false;
        _pressRect = null;
        if (click)
        {
            var hit = Hit(point);
            if (hit != null && hit.Value.Widget.Contains(pressedAt))
            {
                Dispatch(hit);
                return true;
            }
        }
        QueueRedraw();
        return true;
    }

    private void SetScroll(ScrollArea area, int first)
    {
        var clamped = Math.Clamp(first, 0, area.Maximum);
        area.SetFirst(clamped);
        // 同一帧内连续拖动（核对工具）也要从新起点继续，所以把登记里的起点一并更新。
        var index = _scrollAreas.IndexOf(area);
        if (index >= 0)
            _scrollAreas[index] = area with { First = clamped };
        QueueRedraw();
    }

    private void ResetListDrag()
    {
        _scrollActive = "";
        _scrollPointer = -2;
        _scrollMoved = false;
    }
}
