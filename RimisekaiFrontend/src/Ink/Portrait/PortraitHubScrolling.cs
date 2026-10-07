using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public partial class PortraitHubScreen
{
    private sealed record ScrollArea(string Id, Rect2 Area, Rect2 Track, int Total, int Visible,
        int First, Action<int> SetFirst, float RowHeight)
    {
        public int Maximum => Math.Max(0, Total - Visible);
    }

    private readonly List<ScrollArea> _scrollAreas = new();
    private string _scrollActive = "";
    private int _scrollPointer = -2;
    private Vector2 _scrollPressPosition;
    private int _scrollPressFirst;
    private bool _scrollMoved;
    private bool _scrollOnTrack;
    private float _scrollGrab;

    private void RegisterScroll(string id, Rect2 area, int total, int visible, int first,
        Action<int> setFirst, float rowHeight = PortraitLayout.RowHeight, Rect2? track = null)
    {
        var bar = track.HasValue ? track.Value : PortraitLayout.ListTrack(area);
        var scroll = new ScrollArea(id, area, bar, total, visible, first, setFirst, rowHeight);
        _scrollAreas.Add(scroll);
        if (scroll.Maximum == 0)
            return;
        InkDraw.InkLine(this, new Vector2(bar.GetCenter().X, bar.Position.Y),
            new Vector2(bar.GetCenter().X, bar.End.Y), InkStyle.Dim, PortraitLayout.LineHair);
        DrawRect(PortraitLayout.ListThumb(bar, total, visible, first), InkStyle.Line);
        _widgets.Add(new PortraitWidget(bar, PortraitAction.ScrollTrack, _scrollAreas.Count - 1, true, id));
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
                SetScroll(wheelArea, wheelArea.First + (mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1));
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
            if (_scrollAreas[i].Area.HasPoint(point))
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
        _scrollOnTrack = area.Maximum > 0 && area.Track.HasPoint(point);
        if (_scrollOnTrack)
        {
            var thumb = PortraitLayout.ListThumb(area.Track, area.Total, area.Visible, area.First);
            _scrollGrab = point.Y >= thumb.Position.Y && point.Y <= thumb.End.Y
                ? point.Y - thumb.Position.Y : thumb.Size.Y / 2f;
            DragListThumb(area, point.Y);
        }
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
        if (_scrollOnTrack)
        {
            DragListThumb(area, point.Y);
            return true;
        }
        var delta = point.Y - _scrollPressPosition.Y;
        if (Mathf.Abs(delta) >= PortraitLayout.ListDragThreshold)
            _scrollMoved = true;
        if (_scrollMoved)
            SetScroll(area, _scrollPressFirst - (int)(delta / area.RowHeight));
        return true;
    }

    private bool EndListDrag(Vector2 point, int pointer)
    {
        if (_scrollPointer != pointer)
            return false;
        var click = !_scrollMoved && !_scrollOnTrack;
        var pressedAt = _scrollPressPosition;
        ResetListDrag();
        if (click)
        {
            var hit = Hit(point);
            if (hit != null && hit.Value.Widget.Contains(pressedAt))
                Dispatch(hit);
        }
        return true;
    }

    private void DragListThumb(ScrollArea area, float y)
    {
        var thumb = PortraitLayout.ListThumb(area.Track, area.Total, area.Visible, area.First);
        var travel = area.Track.Size.Y - thumb.Size.Y;
        var ratio = Mathf.Clamp((y - area.Track.Position.Y - _scrollGrab) / travel, 0f, 1f);
        SetScroll(area, (int)Mathf.Round(ratio * area.Maximum));
    }

    private void SetScroll(ScrollArea area, int first)
    {
        area.SetFirst(Math.Clamp(first, 0, area.Maximum));
        QueueRedraw();
    }

    private void ResetListDrag()
    {
        _scrollActive = "";
        _scrollPointer = -2;
        _scrollMoved = false;
        _scrollOnTrack = false;
    }
}
