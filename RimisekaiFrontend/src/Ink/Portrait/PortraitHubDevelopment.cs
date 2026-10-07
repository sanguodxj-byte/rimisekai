using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public partial class PortraitHubScreen
{
    private int _developmentCell = -1;
    private int _developmentFacility = -1;
    private int _developmentRoom = -1;
    private int _developmentPlacing = -1;
    private int _developmentFacilityFirst;
    private int _developmentRoomFirst;
    private int _developmentActionFirst;

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
        DrawPageTop(InkPageModel.Info(InkPage.Develop).Label);
        DrawBack();
        for (var y = 0; y < PortraitLayout.GridRows; y++)
            for (var x = 0; x < PortraitLayout.GridCols; x++)
            {
                var rect = PortraitLayout.DevelopmentCell(x, y);
                InkDraw.Ink(this, RectLoop(rect.Grow(-8f)), new Color(InkStyle.Dim, 0.45f), PortraitLayout.LineHair);
            }
        for (var i = 0; i < model.Rooms.Count; i++)
        {
            var cell = model.Rooms[i];
            var rect = PortraitLayout.DevelopmentCell(cell.X, cell.Y);
            var enabled = cell.Open || cell.CanDevelop;
            PortraitFrame.Button(this, rect.Grow(-8f), cell.Name, selected: cell.Selected, enabled: enabled);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentCell, i, enabled, cell.Name));
        }
        _developmentFacilityFirst = Math.Clamp(_developmentFacilityFirst, 0, Math.Max(0, model.FacilityRows.Count - 2));
        for (var i = 0; i < 2 && i + _developmentFacilityFirst < model.FacilityRows.Count; i++)
        {
            var row = model.FacilityRows[i + _developmentFacilityFirst];
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.DevelopmentFacilities, i, hasScroll: model.FacilityRows.Count > 2);
            PortraitFrame.Row(this, rect, row.Name, "", row.Id == _developmentFacility);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentFacility, row.Id, row.Enabled, row.Name));
        }
        RegisterScroll("development_facilities", PortraitLayout.DevelopmentFacilities, model.FacilityRows.Count, 2,
            _developmentFacilityFirst, first => _developmentFacilityFirst = first);
        _developmentRoomFirst = Math.Clamp(_developmentRoomFirst, 0, Math.Max(0, model.RoomRows.Count - 2));
        for (var i = 0; i < 2 && i + _developmentRoomFirst < model.RoomRows.Count; i++)
        {
            var index = i + _developmentRoomFirst;
            var row = model.RoomRows[index];
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.DevelopmentRooms, i, hasScroll: model.RoomRows.Count > 2);
            PortraitFrame.Row(this, rect, row.Name, "", row.Id == _developmentPlacing);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentRoom, index, row.Enabled, row.Name));
        }
        RegisterScroll("development_rooms", PortraitLayout.DevelopmentRooms, model.RoomRows.Count, 2,
            _developmentRoomFirst, first => _developmentRoomFirst = first);
        _developmentActionFirst = Math.Clamp(_developmentActionFirst, 0, Math.Max(0, model.ActionRows.Count - 3));
        for (var i = 0; i < 3 && i + _developmentActionFirst < model.ActionRows.Count; i++)
        {
            var index = i + _developmentActionFirst;
            var row = model.ActionRows[index];
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.DevelopmentActions, i, hasScroll: model.ActionRows.Count > 3);
            PortraitFrame.Button(this, rect, row.Name, row.Selected, row.Enabled, row.Prefix);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.DevelopmentAction, index, row.Enabled, row.Name));
        }
        RegisterScroll("development_actions", PortraitLayout.DevelopmentActions, model.ActionRows.Count, 3,
            _developmentActionFirst, first => _developmentActionFirst = first);
    }

    private bool ExecuteDevelopment(PortraitWidget widget)
    {
        var hub = _vm.Hub;
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
                    QueueRedraw();
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
            return true;
        }
        if (widget.Action == PortraitAction.DevelopmentFacility)
        {
            _developmentFacility = widget.Index;
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
        Notice();
        return true;
    }

    private IReadOnlyList<PortraitRegion> DevelopmentRegions() => new[]
    {
        new PortraitRegion("back", PortraitLayout.OverlayBack),
        new PortraitRegion("development_grid", PortraitLayout.DevelopmentGrid),
        new PortraitRegion("development_facilities", PortraitLayout.DevelopmentFacilities),
        new PortraitRegion("development_rooms", PortraitLayout.DevelopmentRooms),
        new PortraitRegion("development_actions", PortraitLayout.DevelopmentActions),
    };
}
