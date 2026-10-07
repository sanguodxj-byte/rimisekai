using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 领地页签：提示条（最新操作反馈）、5×5 领地网格、「此刻」头像带、出行 / 建造两枚浮动药丸；
/// 点房间格弹设施抽屉（设施、在场的人、可做的事、拆除 / 前往）。
/// 世界层（出行后）网格换成兴趣点格，点格即进入该地点。
/// </summary>
public partial class PortraitHubScreen
{
    private int _sheetRoom = -1;

    private bool WorldLayer => _vm.Hub.Layer == MapLayer.World;

    private void DrawTerritory()
    {
        if (_notice.Length > 0)
        {
            var strip = PortraitLayout.AlertStrip;
            PortraitFrame.Card(this, strip);
            PortraitGlyph.Bell(this, strip.Position.X + 60f, strip.GetCenter().Y, 24f, InkStyle.Line);
            InkDraw.TextBounded(this, new Rect2(strip.Position.X + 110f, strip.Position.Y, strip.Size.X - 140f, strip.Size.Y),
                _notice, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        }

        PortraitFrame.NotchedFrame(this, PortraitLayout.MapFrame, InkStyle.Bg);
        for (var y = 0; y < PortraitLayout.GridRows; y++)
            for (var x = 0; x < PortraitLayout.GridCols; x++)
                DrawCell(x, y);

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad,
            PortraitLayout.NowRuleY, "此刻");
        DrawNowStrip();

        var travel = PortraitLayout.TravelButton;
        var travelLabel = WorldLayer ? "返回领地" : "出行";
        PortraitFrame.Pill(this, travel, travelLabel, glyph: WorldLayer ? PortraitGlyph.Castle : PortraitGlyph.Map);
        _widgets.Add(new PortraitWidget(travel, PortraitAction.HubWorld, 0, true, travelLabel));
        var build = PortraitLayout.BuildButton;
        PortraitFrame.Pill(this, build, "建造", primary: true, enabled: !WorldLayer, glyph: PortraitGlyph.Hammer);
        _widgets.Add(new PortraitWidget(build, PortraitAction.Build, 0, !WorldLayer, "建造"));
    }

    private void DrawCell(int x, int y)
    {
        var r = PortraitLayout.Cell(x, y);
        var room = _vm.RoomAt(x, y);
        var inner = r.Grow(-6f);
        var c = r.GetCenter();

        if (room == null)
        {
            // 未开拓的空格：暗木细框，中间一点。
            InkDraw.Ink(this, RectLoop(inner), InkStyle.WoodDark, 2.5f);
            InkDraw.Jewel(this, c, 4f, InkStyle.WoodDark);
            return;
        }

        if (!room.Open)
        {
            InkDraw.Ink(this, RectLoop(inner), InkStyle.WoodDark, 2.5f);
            InkDraw.TextBounded(this, inner.Grow(-10f), room.Name, PortraitLayout.FontMeta, PortraitLayout.FontMeta,
                InkStyle.Dim, "cm");
            _widgets.Add(new PortraitWidget(r, PortraitAction.Cell, room.Id, false, room.Name));
            return;
        }

        var pressed = PortraitFrame.IsPressed(r);
        var picked = _sheet == SheetKind.Room && _sheetRoom == room.Id;
        DrawRect(inner, pressed ? PortraitFrame.PressFill : picked ? InkStyle.Hover : InkStyle.Panel);
        var picture = RoomImageProvider?.Invoke(room.Id);
        if (picture != null)
        {
            PortraitFrame.Cover(this, picture, inner.Grow(-4f), 0.3f);
            PortraitFrame.Fade(this, new Rect2(inner.Position.X, c.Y - 30f, inner.Size.X, inner.End.Y - c.Y + 30f), 0f, 0.85f);
        }
        InkDraw.Ink(this, RectLoop(inner), room.Vacant ? InkStyle.WoodDark : InkStyle.Dim, room.Vacant ? 2.5f : 3.5f);
        // 左上角三道短斜线：已开放房间的标记（空房不画）。
        if (!room.Vacant)
            for (var k = 0; k < 3; k++)
                InkDraw.InkLine(this, new Vector2(inner.Position.X + 12f + k * 14f, inner.Position.Y + 12f),
                    new Vector2(inner.Position.X + 12f, inner.Position.Y + 12f + k * 14f), InkStyle.WoodDark, 2.5f);
        InkDraw.TextBounded(this, new Rect2(inner.Position.X + 8f, c.Y - 46f, inner.Size.X - 16f, 64f), room.Name,
            PortraitLayout.FontMeta, PortraitLayout.FontMeta, room.Vacant ? InkStyle.Dim : InkStyle.Line, "cm");

        var here = _vm.Cards().Where(card => card.RoomId == room.Id).OrderByDescending(card => card.IsPlayer).ToArray();
        for (var i = 0; i < here.Length && i < 3; i++)
        {
            var at = PortraitLayout.CellToken(r, i);
            if (i == 2 && here.Length > 3)
            {
                InkDraw.Text(this, at, $"+{here.Length - 2}", PortraitLayout.FontMeta, InkStyle.Dim, "cm");
                break;
            }
            DrawToken(at, here[i]);
        }

        if (picked || _vm.IsPlayerRoom(room.Id))
        {
            InkDraw.Ink(this, RectLoop(r.Grow(-1f)), InkStyle.Line, picked ? 7f : 4f);
            if (picked)
                foreach (var corner in new[] { r.Position, new Vector2(r.End.X, r.Position.Y), new Vector2(r.Position.X, r.End.Y), r.End })
                    InkDraw.Jewel(this, corner, 12f, InkStyle.Line);
        }

        _widgets.Add(new PortraitWidget(r, PortraitAction.Cell, room.Id, true, room.Name));
    }

    /// <summary>格内角色小圆标：主角实心骨白黑字，其余黑底骨白环。</summary>
    private void DrawToken(Vector2 at, CharacterCard card)
    {
        if (card.IsPlayer)
        {
            DrawCircle(at, 24f, InkStyle.Line);
            InkDraw.Text(this, at, card.Name[..1], PortraitLayout.FontToken, InkStyle.Bg, "cm");
        }
        else
        {
            DrawCircle(at, 24f, InkStyle.Bg);
            DrawArc(at, 24f, 0f, Mathf.Tau, 32, InkStyle.Line, 3f, true);
            InkDraw.Text(this, at, card.Name[..1], PortraitLayout.FontToken, InkStyle.Line, "cm");
        }
    }

    /// <summary>当前时段（0/6/12/18 时起各 6 小时）。</summary>
    private int CurrentSlot => Math.Clamp(_vm.Hub.Header().Hour / 6, 0, WorkSlot.Count - 1);

    /// <summary>某人此刻的安排：工作/娱乐写设施名，空闲写「空闲」。</summary>
    private string ActivityOf(int characterId)
    {
        var a = _vm.Hub.AssignmentOf(characterId, CurrentSlot);
        if (a.Mode == SlotMode.Free || a.FacilityId < 0)
            return "空闲";
        return $"{(a.Mode == SlotMode.Work ? "工作" : "娱乐")} · {_vm.Hub.FacilityName(a.FacilityId)}";
    }

    private string RoomNameOf(int roomId)
    {
        foreach (var room in _vm.Rooms())
            if (room.Id == roomId)
                return room.Name;
        return "";
    }

    /// <summary>
    /// 「此刻」：全员头像＋名字＋此刻的安排。与主角同房的人点了即交流；主角与不在场的人点了看角色详情。
    /// 人多时整条横拖。
    /// </summary>
    private void DrawNowStrip()
    {
        var cards = _vm.Cards();
        var strip = PortraitLayout.NowStrip;
        var total = (int)(PortraitLayout.Pad * 2f + cards.Count * PortraitLayout.NowSlot);
        var offset = Pan("now", total, (int)strip.Size.X);
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var r = PortraitLayout.NowCard(i, offset);
            if (r.End.X < 0f || r.Position.X > PortraitLayout.CanvasWidth)
                continue;
            var present = card.IsPlayer || card.RoomId == _vm.Hub.PlayerRoomId;
            var cx = r.GetCenter().X;
            if (PortraitFrame.IsPressed(r))
                PortraitFrame.RoundRect(this, r, 24f, PortraitFrame.PressFill);
            PortraitFrame.Avatar(this, new Vector2(cx, r.Position.Y + 84f), 66f,
                PortraitAvatars.Resolve(_vm.FindById(card.Id)), card.Name, ring: true, dim: card.RoomId < 0 && !card.IsPlayer);
            InkDraw.TextBounded(this, new Rect2(r.Position.X, r.Position.Y + 168f, r.Size.X, 56f), card.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            InkDraw.TextBounded(this, new Rect2(r.Position.X, r.Position.Y + 226f, r.Size.X, 52f),
                card.IsPlayer || present ? ActivityOf(card.Id) : RoomNameOf(card.RoomId).Length > 0 ? RoomNameOf(card.RoomId) : ActivityOf(card.Id),
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
            AddClipped(r, strip, PortraitAction.NowAvatar, card.Id, true, card.Name);
        }
        RegisterScroll("now", strip, total, (int)strip.Size.X, offset, v => _pan["now"] = v, 1f, horizontal: true);
    }

    // ---------- 设施抽屉 ----------

    private float DrawRoomSheet()
    {
        var room = _vm.Rooms().FirstOrDefault(r => r.Id == _sheetRoom);
        var top = PortraitLayout.RoomSheetTop;
        var sheet = PortraitFrame.Sheet(this, top);
        if (room == null)
            return top;
        var here = _vm.IsPlayerRoom(room.Id);
        var icon = new Rect2(PortraitLayout.Pad + 20f, top + 70f, 170f, 170f);
        DrawRect(icon, InkStyle.Bg);
        var picture = RoomImageProvider?.Invoke(room.Id);
        if (picture != null)
            PortraitFrame.Cover(this, picture, icon.Grow(-6f), 0.3f);
        else
            PortraitGlyph.Castle(this, icon.GetCenter().X, icon.GetCenter().Y, 50f, InkStyle.Line);
        InkDraw.Ink(this, RectLoop(icon), InkStyle.Dim, 3f);

        var fixtures = _vm.Hub.FixturesIn(room.Id);
        var people = _vm.Cards().Where(card => card.RoomId == room.Id).ToArray();
        InkDraw.TextBounded(this, new Rect2(icon.End.X + 40f, top + 76f, 600f, 76f), room.Name,
            PortraitLayout.FontTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");
        var sub = $"设施 {fixtures.Count} · 在场 {people.Length}" + (here ? " · 你在这里" : "");
        InkDraw.TextBounded(this, new Rect2(icon.End.X + 40f, top + 160f, 600f, 56f), sub,
            PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "lm");

        var close = PortraitLayout.SheetClose(top);
        PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.SheetClose, 0, true, "收起"));

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, top + 290f, "在场");
        for (var i = 0; i < people.Length && i < 7; i++)
            PortraitFrame.Avatar(this, new Vector2(PortraitLayout.Pad + 70f + i * 136f, top + 382f), 46f,
                PortraitAvatars.Resolve(_vm.FindById(people[i].Id)), people[i].Name);
        if (people.Length > 7)
            InkDraw.Text(this, new Vector2(PortraitLayout.CanvasWidth - PortraitLayout.Pad, top + 382f),
                $"+{people.Length - 7}", PortraitLayout.FontMeta, InkStyle.Dim, "rm");

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, top + 480f, "设施");
        var visible = PortraitLayout.RoomSheetRows;
        var first = Math.Clamp(Pan("room_fixtures", fixtures.Count, visible), 0, Math.Max(0, fixtures.Count - visible));
        for (var i = 0; i < visible && first + i < fixtures.Count; i++)
        {
            var f = fixtures[first + i];
            var row = PortraitLayout.RoomSheetRow(i);
            PortraitFrame.RoundRect(this, row, 22f, null, InkStyle.WoodDark, 3f);
            var workers = _vm.WorkersAtFixture(f.Id);
            InkDraw.TextBounded(this, new Rect2(row.Position.X + 40f, row.Position.Y, 360f, row.Size.Y), f.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            InkDraw.Text(this, new Vector2(row.Position.X + 420f, row.GetCenter().Y), $"{workers.Count}/{f.Capacity}",
                PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            for (var k = 0; k < workers.Count && k < 2; k++)
                PortraitFrame.Avatar(this, new Vector2(row.Position.X + 560f + k * 70f, row.GetCenter().Y), 28f,
                    PortraitAvatars.Resolve(_vm.FindById(workers[k].Id)), workers[k].Name, ring: false);
            var use = PortraitLayout.RoomSheetUse(row);
            PortraitFrame.Pill(this, use, "使用", enabled: !WorldLayer);
            _widgets.Add(new PortraitWidget(use, PortraitAction.Fixture, f.Id, !WorldLayer, f.Name));
        }
        RegisterScroll("room_fixtures", new Rect2(0, PortraitLayout.RoomSheetRow(0).Position.Y, PortraitLayout.CanvasWidth,
            visible * 140f), fixtures.Count, visible, first, v => _pan["room_fixtures"] = v, 140f);

        var canDemolish = !WorldLayer && !here;
        PortraitFrame.Pill(this, PortraitLayout.SheetFooterLeft, "拆除", enabled: canDemolish);
        _widgets.Add(new PortraitWidget(PortraitLayout.SheetFooterLeft, PortraitAction.RoomDemolish, room.Id, canDemolish, "拆除"));
        PortraitFrame.Pill(this, PortraitLayout.SheetFooterRight, here ? "已在此处" : "前往", primary: true, enabled: !here);
        _widgets.Add(new PortraitWidget(PortraitLayout.SheetFooterRight, PortraitAction.RoomGo, room.Id, !here, "前往"));
        return sheet.Position.Y;
    }

    private bool ExecuteTerritory(PortraitWidget w)
    {
        var hub = _vm.Hub;
        switch (w.Action)
        {
            case PortraitAction.Cell:
                if (WorldLayer)
                {
                    // 世界层：点兴趣点所在格进入该地点。
                    var name = RoomNameOf(w.Index);
                    var poi = hub.State.World.Pois.Find(p => p.NameZh == name || p.NameEn == name);
                    if (poi == null || !hub.EnterWorldPoi(poi.Id))
                        SetNotice("这里无法进入。");
                    return true;
                }
                _sheetRoom = w.Index;
                _sheet = SheetKind.Room;
                _pan.Remove("room_fixtures");
                return true;
            case PortraitAction.HubWorld:
                hub.ToggleWorldLayer();
                _sheet = SheetKind.None;
                return true;
            case PortraitAction.Build:
                _push = PushPage.Build;
                _developmentCell = _developmentFacility = _developmentRoom = _developmentPlacing = -1;
                return true;
            case PortraitAction.RoomGo:
                hub.Enter(w.Index);
                return true;
            case PortraitAction.RoomDemolish:
                var roomName = RoomNameOf(w.Index);
                var roomId = w.Index;
                Confirm("拆除", roomName, () =>
                {
                    hub.BeginOperation();
                    if (hub.RemoveRoom(roomId))
                        _sheet = SheetKind.None;
                    Notice();
                    QueueRedraw();
                });
                return true;
            case PortraitAction.Fixture:
                var fixture = hub.State.Territory.Facilities.Find(f => f.Id == w.Index);
                if (fixture == null)
                    return true;
                if (fixture.RoomId != hub.PlayerRoomId)
                    hub.Enter(fixture.RoomId);
                if (hub.Use(w.Index))
                {
                    hub.ClearSelection();
                    _sheet = SheetKind.None;
                    OpenInteraction();
                }
                return true;
            case PortraitAction.NowAvatar:
                var card = _vm.Cards().First(c => c.Id == w.Index);
                if (!card.IsPlayer && card.RoomId == hub.PlayerRoomId)
                {
                    hub.Select(card.Id);
                    OpenInteraction();
                }
                else
                    OpenCharacter(card.Id);
                return true;
            default:
                return false;
        }
    }
}
