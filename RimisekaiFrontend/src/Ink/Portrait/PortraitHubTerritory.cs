using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 领地页签：日志面板（本次操作的日志快照，每次操作清空换新，点一下进日志页签）、5×5 领地网格（格底棋子＝在场的人，至多 4 枚）、
/// 「此刻」头像带（只列与主角同房的人，每页 4 人，三角钮翻页）、出行 / 建造两枚浮动药丸。
/// 点房间格＝沿连通的门前往（不连通则不动并提示）；格与格共用边上的门洞＝连通；再点主角所在的格、或长按任一房间格，弹设施抽屉（设施、在场的人、拆除）。
/// 世界层（出行后）网格换成兴趣点格，点格即进入该地点。
/// </summary>
public partial class PortraitHubScreen
{
    private int _sheetRoom = -1;

    private bool WorldLayer => _vm.Hub.Layer == MapLayer.World;

    private void DrawTerritory()
    {
        // 过界平移时网格先画、再把滑出网格的部分遮掉，日志面板须在遮罩之后画。
        if (!Crossing)
            DrawLogPanel();

        if (Crossing)
            DrawCrossPan();
        else
        {
            PortraitFrame.GothicFrame(this, PortraitLayout.MapFrame, InkStyle.Bg);
            if (WorldLayer)
                DrawWorldMap();
            else
            {
                for (var y = 0; y < PortraitLayout.GridRows; y++)
                    for (var x = 0; x < PortraitLayout.GridCols; x++)
                        DrawCell(x, y, _vm.RoomAt(x, y), true);
                DrawDoors(PortraitLayout.Cell, _vm.Hub.RegionId);
                DrawWalker();
                DrawCrossArrow();
            }
        }

        if (Crossing)
            DrawLogPanel();

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad,
            PortraitLayout.NowRuleY, "此刻");
        DrawNowStrip();

        var travel = PortraitLayout.TravelButton;
        var travelLabel = _vm.Hub.TravelLabel;
        var homeward = WorldLayer || _vm.Hub.InQuestDungeon;
        PortraitFrame.Plaque(this, travel, travelLabel, glyph: homeward ? PortraitGlyph.Castle : PortraitGlyph.Map);
        _widgets.Add(new PortraitWidget(travel, PortraitAction.HubWorld, 0, true, travelLabel));
        // 只有领地里能建造：大地图、兴趣点、地城里都不行。
        var buildable = _vm.Hub.Layer == MapLayer.Territory;
        var build = PortraitLayout.BuildButton;
        PortraitFrame.Plaque(this, build, "建造", primary: true, enabled: buildable, glyph: PortraitGlyph.Hammer);
        _widgets.Add(new PortraitWidget(build, PortraitAction.Build, 0, buildable, "建造"));
    }

    /// <summary>画一格。register＝登记命中块（过界平移时滑出的旧网格只画不登记）。</summary>
    private void DrawCell(int x, int y, Room? room, bool register)
    {
        var r = PortraitLayout.Cell(x, y);
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
            InkDraw.TextStacked(this, inner.Grow(-10f), inner.Grow(-10f), room.Name, PortraitLayout.FontMeta, InkStyle.Dim);
            if (register)
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
        // 房名整名显示、在格内居中：一行放不下就拆两行。
        InkDraw.TextStacked(this, inner.Grow(-10f), inner.Grow(-10f), room.Name, PortraitLayout.FontMeta,
            room.Vacant ? InkStyle.Dim : InkStyle.Line);

        DrawCellPieces(_vm.Cards().Where(card => card.RoomId == room.Id && !(card.IsPlayer && Walking)).OrderByDescending(card => card.IsPlayer).ToArray(),
            PortraitLayout.CellPieces(r));

        if (picked || _vm.IsPlayerRoom(room.Id))
        {
            InkDraw.Ink(this, RectLoop(r.Grow(-1f)), InkStyle.Line, picked ? 7f : 4f);
            if (picked)
                foreach (var corner in new[] { r.Position, new Vector2(r.End.X, r.Position.Y), new Vector2(r.Position.X, r.End.Y), r.End })
                    InkDraw.Jewel(this, corner, 12f, InkStyle.Line);
        }

        if (register)
            _widgets.Add(new PortraitWidget(r, PortraitAction.Cell, room.Id, true, room.Name));
    }

    /// <summary>
    /// 格底国际象棋棋子（主人定，自 ui-motion-territory 分支 8498768/320e7f2 移植，不得再换成方标）：
    /// 在场的人一人一枚（主角＝王，好感高者＝后，其余按好感取车 / 象 / 马 / 兵），底线对齐、居中排开，
    /// 至多 <see cref="PortraitLayout.PieceCap"/> 枚；多于此数时第 4 位换成一枚实心「+」。
    /// </summary>
    private void DrawCellPieces(IReadOnlyList<CharacterCard> cards, Rect2 area)
    {
        if (cards.Count == 0)
            return;
        var (shown, overflow) = PieceSlots(cards.Count);
        var slots = overflow ? shown + 1 : shown;
        var start = area.GetCenter().X - (slots - 1) * PortraitLayout.PieceStep / 2f;
        for (var i = 0; i < shown; i++)
            InkDraw.Chess(this, new Vector2(start + i * PortraitLayout.PieceStep, area.End.Y),
                PortraitLayout.PieceHeight, InkDraw.PieceFor(cards[i]));
        if (overflow)
            PortraitGlyph.Plus(this, start + shown * PortraitLayout.PieceStep, area.End.Y - PortraitLayout.PieceHeight * 0.4f,
                PortraitLayout.PieceHeight * 0.32f, InkStyle.Line);
    }

    /// <summary>格内 count 个人要画几枚棋子、要不要在最后补一枚「+」（多于 4 人时 3 枚棋子＋「+」）。</summary>
    public static (int Pieces, bool Plus) PieceSlots(int count) =>
        count > PortraitLayout.PieceCap ? (PortraitLayout.PieceCap - 1, true) : (count, false);

    /// <summary>「此刻」头像右下角的棋子徽：黑底骨白环里一枚与领地格同款的棋子。</summary>
    private void DrawPieceBadge(Vector2 center, CharacterCard card)
    {
        var badge = new Rect2(center - Vector2.One * PortraitLayout.BadgeRadius, Vector2.One * PortraitLayout.BadgeRadius * 2f);
        PortraitFrame.Poly(this, PortraitFrame.ChamferPoints(badge, PortraitLayout.BadgeRadius * 0.55f), InkStyle.Bg, InkStyle.Line, 3f);
        InkDraw.Chess(this, center + new Vector2(0f, PortraitLayout.BadgeRadius * 0.62f),
            PortraitLayout.BadgeRadius * 1.3f, InkDraw.PieceFor(card));
    }

    /// <summary>与主角同房：「此刻」只列这些人，不在同一房间的人直接不显示（不再压暗列出）。</summary>
    private bool SameRoomAsPlayer(CharacterCard card) =>
        card.IsPlayer || card.RoomId == _vm.Hub.PlayerRoomId;

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

    /// <summary>前往某房间：只能沿连通的门走；与这里不连通就原地不动，弹一句提示。</summary>
    private void GoTo(int roomId)
    {
        var from = _vm.Hub.PlayerRoomId;
        var path = _vm.Hub.State.Territory.Route(from, roomId, ignoreLocks: true);
        if (_vm.Hub.Arrive(roomId))
            // 地城里半路撞上东西会停在那一间：棋子只走到人实际站的地方。
            StartWalk(from, path.Take(path.IndexOf(_vm.Hub.PlayerRoomId) + 1).ToList());
        else
            SetNotice($"{RoomNameOf(roomId)}与这里不连通，过不去。");
    }

    /// <summary>
    /// 门：同区网格四邻的两间已开放房间，连通时在共用边上画一道门洞（盖住两格各自的边框、两侧各一道骨白门槛），
    /// 不连通就是墙，什么都不画。领地网格与建造网格共用。
    /// </summary>
    private void DrawDoors(Func<int, int, Rect2> cellRect, int regionId)
    {
        var territory = _vm.Hub.State.Territory;
        foreach (var room in territory.Rooms)
        {
            if (!room.Open || room.RegionId != regionId || room.X < 0 || room.Y < 0)
                continue;
            foreach (var dir in new[] { RoomDir.East, RoomDir.South })
            {
                if (territory.NeighborAt(room, dir) is not { Open: true } other || !room.Links.Contains(other.Id)
                    || !_vm.Hub.DoorShown(room.Id, other.Id))
                    continue;
                var a = cellRect(room.X, room.Y);
                if (dir == RoomDir.East)
                {
                    var x = a.End.X;
                    var cy = a.GetCenter().Y;
                    var hole = new Rect2(x - 12f, cy - 26f, 24f, 52f);
                    DrawRect(hole, InkStyle.Panel);
                    DrawLine(new Vector2(hole.Position.X, hole.Position.Y), new Vector2(hole.End.X, hole.Position.Y), InkStyle.Line, 3f);
                    DrawLine(new Vector2(hole.Position.X, hole.End.Y), new Vector2(hole.End.X, hole.End.Y), InkStyle.Line, 3f);
                }
                else
                {
                    var y = a.End.Y;
                    var cx = a.GetCenter().X;
                    var hole = new Rect2(cx - 26f, y - 12f, 52f, 24f);
                    DrawRect(hole, InkStyle.Panel);
                    DrawLine(new Vector2(hole.Position.X, hole.Position.Y), new Vector2(hole.Position.X, hole.End.Y), InkStyle.Line, 3f);
                    DrawLine(new Vector2(hole.End.X, hole.Position.Y), new Vector2(hole.End.X, hole.End.Y), InkStyle.Line, 3f);
                }
            }
        }
    }

    /// <summary>「此刻」带当前页（每页 4 人）。</summary>
    private int _nowPage;

    /// <summary>
    /// 「此刻」：与主角同房的人（头像＋棋子徽＋名字＋此刻的安排），每页至多 4 人；不在同一房间的人不显示。
    /// 点别人即交流，点主角看角色详情。多于 4 人时第 4 人右侧画一枚实心右指三角钮，点了翻到下 4 人，末页再点回首页。
    /// </summary>
    private void DrawNowStrip()
    {
        var cards = _vm.Cards().Where(SameRoomAsPlayer).ToArray();
        var size = PortraitLayout.NowPageSize;
        var pages = Math.Max(1, (cards.Length + size - 1) / size);
        _nowPage %= pages;
        for (var i = 0; i < size && _nowPage * size + i < cards.Length; i++)
        {
            var card = cards[_nowPage * size + i];
            var r = PortraitLayout.NowCard(i);
            var cx = r.GetCenter().X;
            if (PortraitFrame.IsPressed(r))
                PortraitFrame.PressMark(this, r);
            PortraitFrame.Avatar(this, new Vector2(cx, r.Position.Y + 84f), 66f,
                PortraitAvatars.Resolve(_vm.FindById(card.Id)), card.Name, ring: true);
            DrawPieceBadge(new Vector2(cx + 56f, r.Position.Y + 130f), card);
            InkDraw.TextBounded(this, new Rect2(r.Position.X, r.Position.Y + 168f, r.Size.X, 56f), card.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            InkDraw.TextBounded(this, new Rect2(r.Position.X, r.Position.Y + 226f, r.Size.X, 52f),
                ActivityOf(card.Id),
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
            _widgets.Add(new PortraitWidget(r, PortraitAction.NowAvatar, card.Id, true, card.Name));
        }
        if (cards.Length <= size)
            return;
        var pager = PortraitLayout.NowPager;
        if (PortraitFrame.IsPressed(pager))
            PortraitFrame.PressMark(this, pager);
        var c = new Vector2(pager.GetCenter().X, PortraitLayout.NowStrip.Position.Y + 84f);
        DrawColoredPolygon(new[] { c + new Vector2(-18f, -30f), c + new Vector2(26f, 0f), c + new Vector2(-18f, 30f) }, InkStyle.Line);
        _widgets.Add(new PortraitWidget(pager, PortraitAction.NowPage, 0, true, "下一页"));
    }

    /// <summary>
    /// 日志面板＝本次操作的快照（Core 的 <c>HubSession.Log</c>，固定排版 环境 → 场景 → 他人 → 自己）：
    /// 每次有新输出的操作都整份清空换新，不与旧日志混排。每条一句「a，b」，自上而下排，全部骨白。
    /// 字号自 50 往下收到恰好放下，不低于 44；收到 44 仍放不下，末尾放不下的整条不画，不溢出。
    /// 整块点一下即进日志页签（那里按时间看全部历史）。
    /// </summary>
    private void DrawLogPanel()
    {
        var panel = PortraitLayout.LogPanel;
        var pressed = PortraitFrame.IsPressed(panel);
        PortraitFrame.GothicFrame(this, panel, InkStyle.Panel, ornate: false);
        PortraitFrame.CornerRivets(this, panel.Grow(-24f), InkStyle.Dim);
        if (pressed)
            PortraitFrame.PressMark(this, panel);
        _widgets.Add(new PortraitWidget(panel, PortraitAction.Tab, 4, true, "日志"));
        var area = PortraitLayout.LogPanelText;
        var entries = _vm.Hub.Log;
        if (entries.Count == 0)
            return;
        var gap = 12f;
        var size = PortraitLayout.LogFontMin;
        for (var fs = PortraitLayout.LogFontMax; fs >= PortraitLayout.LogFontMin; fs -= 2)
        {
            var need = 0f;
            foreach (var e in entries)
                need += InkDraw.WrapLines(e.Text, area.Size.X, fs).Count * LogLineHeight(fs) + gap;
            if (need - gap <= area.Size.Y)
            {
                size = fs;
                break;
            }
        }
        var lineH = LogLineHeight(size);
        var top = area.Position.Y;
        foreach (var e in entries)
        {
            var lines = InkDraw.WrapLines(e.Text, area.Size.X, size);
            if (top + lines.Count * lineH > area.End.Y + 0.5f && top > area.Position.Y)
                break;
            foreach (var line in lines)
            {
                if (top + lineH > area.End.Y + 0.5f)
                    break;
                InkDraw.Text(this, new Vector2(area.Position.X, top + lineH / 2f), line, size, InkStyle.Line, "lm");
                top += lineH;
            }
            top += gap;
        }
    }

    private static float LogLineHeight(int size) => Mathf.Round(size * 1.36f);

    // ---------- 设施抽屉 ----------

    private void OpenRoomSheet(int roomId)
    {
        _sheetRoom = roomId;
        _sheet = SheetKind.Room;
        _pan.Remove("room_fixtures");
        QueueRedraw();
    }

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
            PortraitFrame.Bevel(this, row, 22f, null, InkStyle.WoodDark, 3f);
            var workers = _vm.WorkersAtFixture(f.Id);
            InkDraw.TextBounded(this, new Rect2(row.Position.X + 40f, row.Position.Y, 360f, row.Size.Y), f.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
            InkDraw.Text(this, new Vector2(row.Position.X + 420f, row.GetCenter().Y), $"{workers.Count}/{f.Capacity}",
                PortraitLayout.FontMeta, InkStyle.Dim, "lm");
            for (var k = 0; k < workers.Count && k < 2; k++)
                PortraitFrame.Avatar(this, new Vector2(row.Position.X + 560f + k * 70f, row.GetCenter().Y), 28f,
                    PortraitAvatars.Resolve(_vm.FindById(workers[k].Id)), workers[k].Name, ring: false);
            var use = PortraitLayout.RoomSheetUse(row);
            PortraitFrame.Plaque(this, use, "使用", enabled: !WorldLayer);
            _widgets.Add(new PortraitWidget(use, PortraitAction.Fixture, f.Id, !WorldLayer, f.Name));
        }
        RegisterScroll("room_fixtures", new Rect2(0, PortraitLayout.RoomSheetRow(0).Position.Y, PortraitLayout.CanvasWidth,
            visible * 140f), fixtures.Count, visible, first, v => _pan["room_fixtures"] = v, 140f);

        var reachable = here || WorldLayer || _vm.Hub.CanReach(room.Id);
        var canDemolish = !WorldLayer && !here;
        PortraitFrame.Plaque(this, PortraitLayout.SheetFooterLeft, "拆除", enabled: canDemolish);
        _widgets.Add(new PortraitWidget(PortraitLayout.SheetFooterLeft, PortraitAction.RoomDemolish, room.Id, canDemolish, "拆除"));
        var goLabel = here ? "已在此处" : reachable ? "前往" : "不连通";
        PortraitFrame.Plaque(this, PortraitLayout.SheetFooterRight, goLabel, primary: true, enabled: !here && reachable);
        _widgets.Add(new PortraitWidget(PortraitLayout.SheetFooterRight, PortraitAction.RoomGo, room.Id, !here && reachable, goLabel));
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
                // 点格即前往；已在这格里再点，才弹设施抽屉（长按任一格也弹，见 StepLongPress）。
                if (_vm.IsPlayerRoom(w.Index))
                    OpenRoomSheet(w.Index);
                else
                    GoTo(w.Index);
                return true;
            case PortraitAction.NowPage:
                _nowPage++;
                return true;
            case PortraitAction.CrossGate:
                StartCross(w.Index);
                return true;
            case PortraitAction.HubWorld:
                if (hub.InQuestDungeon)
                {
                    // 撤离委托地城：委托不算数，先问一声。
                    Confirm(hub.TravelLabel, hub.MapTitle(), () =>
                    {
                        hub.AbandonQuestDungeon();
                        Notice();
                        QueueRedraw();
                    });
                    return true;
                }
                hub.ToggleWorldLayer();
                _sheet = SheetKind.None;
                if (WorldLayer)
                    CenterWorldOnParty();
                return true;
            case PortraitAction.Build:
                _push = PushPage.Build;
                _developmentCell = _developmentFacility = _developmentRoom = _developmentPlacing = -1;
                return true;
            case PortraitAction.RoomGo:
                GoTo(w.Index);
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
                    PlayVeil(VeilIcon.Wait, fixture.Name);
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
