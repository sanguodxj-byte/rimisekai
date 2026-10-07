using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 建造推入页（原开发页）：模型、开拓/安装/拆除逻辑不变，只换版式——
/// 上＝缺角双线框里的 5×5 开发网格（已开放实底、可开拓虚线＋加号＋花费、空房暗、安装中高亮空房），
/// 下＝常驻目录面板，分段 操作（拆/房/设/安）/ 设施（选中房间里的）/ 待安装（已建未装的房间）。
/// </summary>
public partial class PortraitHubScreen
{
    private int _developmentCell = -1;
    private int _developmentFacility = -1;
    private int _developmentRoom = -1;
    private int _developmentPlacing = -1;
    private int _developmentFacilityFirst;
    private int _developmentRoomFirst;
    private int _developmentActionFirst;
    private int _developmentTab;

    private static readonly string[] DevelopmentTabs = { "操作", "设施", "待安装" };

    private InkDevModel DevelopmentModel(int confirmCell = -1)
    {
        var query = new InkPageQuery(_developmentCell, _developmentRoom, _developmentFacility,
            "", false, 0, 0, false, -1, _developmentPlacing, ConfirmCell: confirmCell,
            ActionFirst: _developmentActionFirst);
        return InkPageBuilder.Build(_vm, InkPage.Develop, in query).Dev!;
    }

    private void DrawDevelopment()
    {
        var model = DevelopmentModel();
        PortraitFrame.NotchedFrame(this, PortraitLayout.DevelopmentFrame, InkStyle.Bg);
        for (var y = 0; y < PortraitLayout.GridRows; y++)
            for (var x = 0; x < PortraitLayout.GridCols; x++)
                InkDraw.Ink(this, RectLoop(PortraitLayout.DevelopmentCell(x, y).Grow(-6f)), new Color(InkStyle.WoodDark, 0.5f), 2f);
        for (var i = 0; i < model.Rooms.Count; i++)
        {
            var cell = model.Rooms[i];
            var rect = PortraitLayout.DevelopmentCell(cell.X, cell.Y);
            var inner = rect.Grow(-6f);
            var enabled = cell.Open || cell.CanDevelop;
            var pressed = PortraitFrame.IsPressed(rect);
            if (cell.Open)
            {
                var target = _developmentPlacing >= 0 && cell.Vacant;
                DrawRect(inner, pressed ? PortraitFrame.PressFill : cell.Selected || target ? InkStyle.Hover : InkStyle.Panel);
                if (target)
                    DashedLoop(inner, InkStyle.Line);
                else
                    InkDraw.Ink(this, RectLoop(inner), cell.Vacant ? InkStyle.WoodDark : InkStyle.Dim, 3f);
                InkDraw.TextBounded(this, inner.Grow(-10f), cell.Name, PortraitLayout.FontMeta, PortraitLayout.FontMeta,
                    cell.Vacant ? InkStyle.Dim : InkStyle.Line, "cm");
            }
            else if (cell.CanDevelop)
            {
                if (pressed)
                    DrawRect(inner, PortraitFrame.PressFill);
                DashedLoop(inner, InkStyle.Dim);
                // 花费在确认弹窗里写全；格内只放加号，避免小于 44 的字。
                PortraitGlyph.Plus(this, inner.GetCenter().X, inner.GetCenter().Y, 30f, InkStyle.Dim);
            }
            else if (cell.Name.Length > 0)
                InkDraw.TextBounded(this, inner.Grow(-10f), cell.Name, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.WoodDark, "cm");
            if (cell.Selected)
            {
                InkDraw.Ink(this, RectLoop(rect.Grow(-1f)), InkStyle.Line, 7f);
                foreach (var corner in new[] { rect.Position, new Vector2(rect.End.X, rect.Position.Y), new Vector2(rect.Position.X, rect.End.Y), rect.End })
                    InkDraw.Jewel(this, corner, 12f, InkStyle.Line);
            }
            _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentCell, i, enabled, cell.Name));
        }

        var panel = PortraitLayout.DevelopmentPanel;
        PortraitFrame.RoundRect(this, new Rect2(panel.Position, panel.Size + new Vector2(0, 80f)), 56f, InkStyle.Panel, InkStyle.Dim, 4f);
        var counts = new[] { model.ActionRows.Count, model.FacilityRows.Count, model.RoomRows.Count };
        var labels = DevelopmentTabs.Select((t, i) => counts[i] > 0 ? $"{t} {counts[i]}" : t).ToArray();
        var seg = PortraitLayout.DevelopmentSegment;
        PortraitFrame.Segmented(this, seg, labels, _developmentTab);
        for (var i = 0; i < labels.Length; i++)
            _widgets.Add(new PortraitWidget(PortraitFrame.SegmentRect(seg, labels.Length, i), PortraitAction.DevelopmentTab, i, true, DevelopmentTabs[i]));

        var visible = PortraitLayout.DevelopmentRows;
        var area = new Rect2(0, PortraitLayout.DevelopmentListTop, PortraitLayout.CanvasWidth, visible * PortraitLayout.SheetRowStep);
        if (_developmentTab == 0)
        {
            _developmentActionFirst = Math.Clamp(_developmentActionFirst, 0, Math.Max(0, model.ActionRows.Count - visible));
            for (var i = 0; i < visible && i + _developmentActionFirst < model.ActionRows.Count; i++)
            {
                var index = i + _developmentActionFirst;
                var row = model.ActionRows[index];
                var rect = PortraitLayout.DevelopmentRow(i);
                PortraitFrame.Card(this, rect, row.Selected, 22f);
                if (row.Prefix.Length > 0)
                    PortraitFrame.Tag(this, new Vector2(rect.Position.X + 30f, rect.GetCenter().Y - 33f), row.Prefix, 66f, row.Selected);
                InkDraw.TextBounded(this, new Rect2(rect.Position.X + 150f, rect.Position.Y, rect.Size.X - 190f, rect.Size.Y), row.Name,
                    PortraitLayout.FontBody, PortraitLayout.FontMeta, row.Enabled ? InkStyle.Line : InkStyle.Dim, "lm");
                _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentAction, index, row.Enabled, row.Name));
            }
            RegisterScroll("development_actions", area, model.ActionRows.Count, visible, _developmentActionFirst,
                first => _developmentActionFirst = first, PortraitLayout.SheetRowStep);
        }
        else if (_developmentTab == 1)
        {
            _developmentFacilityFirst = Math.Clamp(_developmentFacilityFirst, 0, Math.Max(0, model.FacilityRows.Count - visible));
            for (var i = 0; i < visible && i + _developmentFacilityFirst < model.FacilityRows.Count; i++)
            {
                var row = model.FacilityRows[i + _developmentFacilityFirst];
                var rect = PortraitLayout.DevelopmentRow(i);
                DrawDevRow(rect, row.Name, row.Value.Length > 0 ? row.Value : row.Note, row.Id == _developmentFacility, row.Enabled);
                _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentFacility, row.Id, row.Enabled, row.Name));
            }
            RegisterScroll("development_facilities", area, model.FacilityRows.Count, visible, _developmentFacilityFirst,
                first => _developmentFacilityFirst = first, PortraitLayout.SheetRowStep);
        }
        else
        {
            _developmentRoomFirst = Math.Clamp(_developmentRoomFirst, 0, Math.Max(0, model.RoomRows.Count - visible));
            for (var i = 0; i < visible && i + _developmentRoomFirst < model.RoomRows.Count; i++)
            {
                var index = i + _developmentRoomFirst;
                var row = model.RoomRows[index];
                var rect = PortraitLayout.DevelopmentRow(i);
                DrawDevRow(rect, row.Name, row.Value.Length > 0 ? row.Value : row.Note, row.Id == _developmentPlacing, row.Enabled);
                _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentRoom, index, row.Enabled, row.Name));
            }
            RegisterScroll("development_rooms", area, model.RoomRows.Count, visible, _developmentRoomFirst,
                first => _developmentRoomFirst = first, PortraitLayout.SheetRowStep);
        }

        DrawPageTop("建造", model.FacilityTitle, "完成", PortraitAction.Back);
    }

    private void DrawDevRow(Rect2 rect, string name, string value, bool selected, bool enabled)
    {
        PortraitFrame.Card(this, rect, selected, 22f);
        InkDraw.TextBounded(this, new Rect2(rect.Position.X + 40f, rect.Position.Y, rect.Size.X * 0.6f, rect.Size.Y), name,
            PortraitLayout.FontBody, PortraitLayout.FontMeta, enabled ? InkStyle.Line : InkStyle.Dim, "lm");
        if (value.Length > 0)
            InkDraw.TextBounded(this, new Rect2(rect.Position.X + rect.Size.X * 0.6f + 60f, rect.Position.Y, rect.Size.X * 0.4f - 100f, rect.Size.Y),
                value, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "rm");
    }

    private bool ExecuteDevelopment(PortraitWidget widget)
    {
        var hub = _vm.Hub;
        if (widget.Action == PortraitAction.DevelopmentTab)
        {
            _developmentTab = widget.Index;
            return true;
        }
        if (widget.Action == PortraitAction.DevelopmentCell)
        {
            var cell = DevelopmentModel().Rooms[widget.Index];
            _developmentCell = widget.Index;
            _developmentFacility = -1;
            _developmentFacilityFirst = 0;
            if (_developmentPlacing >= 0 && cell.Open && cell.Vacant)
            {
                if (hub.PlaceRoom(_developmentPlacing, cell.Id))
                {
                    _developmentPlacing = _developmentRoom = -1;
                    _developmentCell = -1;
                    Notice();
                }
            }
            else if (cell.CanDevelop)
            {
                var confirmation = DevelopmentModel(cell.Y * PortraitLayout.GridCols + cell.X);
                Confirm(confirmation.ConfirmTitle, confirmation.ConfirmBody, () =>
                {
                    hub.BeginOperation();
                    if (cell.Id >= 0)
                        hub.DevelopEmptyRoom(cell.Id);
                    else
                        hub.DevelopVacantCell(confirmation.RegionId, cell.X, cell.Y);
                    _developmentCell = -1;
                    Notice();
                    QueueRedraw();
                });
            }
            else if (cell.Open)
                _developmentTab = 0;
            return true;
        }
        if (widget.Action == PortraitAction.DevelopmentFacility)
        {
            _developmentFacility = widget.Index;
            _developmentTab = 0;
            return true;
        }
        if (widget.Action == PortraitAction.DevelopmentRoom)
        {
            _developmentRoom = widget.Index;
            _developmentPlacing = DevelopmentModel().RoomRows[widget.Index].Id;
            return true;
        }
        if (widget.Action != PortraitAction.DevelopmentAction)
            return false;
        var model = DevelopmentModel();
        var row = model.ActionRows[widget.Index];
        switch (row.Action)
        {
            case InkAction.DevBuildRoom:
                hub.BuildRoomDef(row.Index);
                break;
            case InkAction.DevBuildFacility:
                hub.BuildFacilityDef(row.Index, model.RoomId);
                break;
            case InkAction.DevPlaceFacility:
                hub.PlaceFacility(row.Index, model.RoomId);
                break;
            case InkAction.DevRemoveFacility:
                Confirm("拆除", row.Name, () =>
                {
                    hub.BeginOperation();
                    hub.RemoveFacility(row.Index);
                    _developmentFacility = -1;
                    Notice();
                    QueueRedraw();
                });
                break;
            case InkAction.DevDemolishRoom:
                Confirm("拆除", row.Name, () =>
                {
                    hub.BeginOperation();
                    hub.RemoveRoom(row.Index);
                    _developmentCell = _developmentFacility = -1;
                    Notice();
                    QueueRedraw();
                });
                break;
        }
        return true;
    }

    private IReadOnlyList<PortraitRegion> DevelopmentRegions() => new[]
    {
        new PortraitRegion("page_top", PortraitLayout.PageTop),
        new PortraitRegion("development_grid", PortraitLayout.DevelopmentFrame),
        new PortraitRegion("development_panel", PortraitLayout.DevelopmentPanel),
    };
}
