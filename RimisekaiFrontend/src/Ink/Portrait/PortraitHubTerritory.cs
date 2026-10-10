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
        // 先定「此刻」翻到哪一页（有人开口就翻到说话人那页），日志面板与头像栏用同一页。
        ShowChatterSpeakerPage();
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

        if (!WorldLayer)
            DrawFacilityStrip();
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
        if (!Crossing)
            DrawChatter();
    }

    /// <summary>「此刻」里与主角同处的其他人（不含主角，按名册顺序）：每页 <see cref="PortraitLayout.NowOthersPerPage"/> 人。</summary>
    private CharacterCard[] NowOthers() => _vm.Cards().Where(c => !c.IsPlayer && SameRoomAsPlayer(c)).ToArray();

    private int NowPages() => Math.Max(1, (NowOthers().Length + PortraitLayout.NowOthersPerPage - 1) / PortraitLayout.NowOthersPerPage);

    /// <summary>
    /// 「此刻」当前页：主角固定在第 1 格，后面跟当前页的至多 3 人。
    /// 日志面板的角色档只显示这几个人的行——与头像一一对应，翻页即同步换（2026-10-10 主人定）。
    /// </summary>
    private CharacterCard[] NowPageCards()
    {
        _nowPage %= NowPages();
        var list = new List<CharacterCard>();
        foreach (var c in _vm.Cards())
            if (c.IsPlayer)
            {
                list.Add(c);
                break;
            }
        list.AddRange(NowOthers().Skip(_nowPage * PortraitLayout.NowOthersPerPage).Take(PortraitLayout.NowOthersPerPage));
        return list.ToArray();
    }

    /// <summary>有人主动开口时，「此刻」翻到说话人那一页，气泡才指得到她的头像。</summary>
    private void ShowChatterSpeakerPage()
    {
        if (_vm.Hub.PendingChatter is not { } chat)
            return;
        var at = Array.FindIndex(NowOthers(), c => c.Id == chat.Speaker.Id);
        if (at >= 0)
            _nowPage = at / PortraitLayout.NowOthersPerPage;
    }

    /// <summary>
    /// 场景内的主动对话：半透明黑底气泡盖住地图网格下半部分，白字。
    /// 气泡是一整条圆角轮廓，底边顺势收出一道尖角指向「此刻」里说话人的头像（轮廓与尖角同一笔，不断线）；
    /// 内缘一道淡细线作衬框。顶上说话人名牌：菱珠＋名字＋向右淡出的细线；正文白字折行；
    /// 右下一枚呼吸的下指折角提示点按，多句时左下以菱珠记第几句。整只气泡是一个命中块：点一下推进一句，说完收起。
    /// </summary>
    private void DrawChatter()
    {
        if (_vm.Hub.PendingChatter is not { } chat)
            return;
        var at = Array.FindIndex(NowPageCards(), c => c.Id == chat.Speaker.Id);
        if (at < 0)
            return;
        var grid = PortraitLayout.MapGrid;
        var bubble = new Rect2(grid.Position.X + 18f, grid.GetCenter().Y + 6f, grid.Size.X - 36f, grid.Size.Y / 2f - 30f);
        var white = new Color(1f, 1f, 1f);
        var fill = new Color(0.02f, 0.02f, 0.03f, 0.9f);

        var card = PortraitLayout.NowCard(at);
        var tipX = card.GetCenter().X;
        var tip = new Vector2(tipX, card.Position.Y + 84f - 66f - 14f);
        var outline = BubblePath(bubble, 28f, tip, 26f);
        DrawColoredPolygon(outline, fill);
        var loop = new Vector2[outline.Length + 1];
        outline.CopyTo(loop, 0);
        loop[^1] = outline[0];
        DrawPolyline(loop, new Color(white, 0.62f), 2.5f, true);
        var inner = BubblePath(bubble.Grow(-12f), 18f, null, 0f);
        var innerLoop = new Vector2[inner.Length + 1];
        inner.CopyTo(innerLoop, 0);
        innerLoop[^1] = inner[0];
        DrawPolyline(innerLoop, new Color(white, 0.16f), 1.5f, true);

        var pad = 54f;
        var nameAt = new Vector2(bubble.Position.X + pad, bubble.Position.Y + 58f);
        InkDraw.Jewel(this, nameAt, 9f, white);
        InkDraw.Text(this, nameAt + new Vector2(26f, 0f), chat.Speaker.Name, PortraitLayout.FontMeta, white, "lm");
        var nameEnd = nameAt.X + 26f + InkDraw.Measure(chat.Speaker.Name, PortraitLayout.FontMeta).X + 22f;
        PortraitFrame.GradLine(this, nameEnd, bubble.End.X - pad, nameAt.Y, 2f, new Color(white, 0.45f), new Color(white, 0f));

        var body = new Rect2(bubble.Position.X + pad, bubble.Position.Y + 112f, bubble.Size.X - pad * 2f, bubble.Size.Y - 112f - 70f);
        InkDraw.Wrapped(this, body, chat.Text, PortraitLayout.FontBody, white, PortraitLayout.FontBody * 1.6f);

        // 多句：左下菱珠记第几句（说过的实心、未说的空心）。
        if (chat.Lines.Count > 1)
            for (var k = 0; k < chat.Lines.Count; k++)
            {
                var c = new Vector2(bubble.Position.X + pad + 8f + k * 34f, bubble.End.Y - 42f);
                InkDraw.Jewel(this, c, 9f, new Color(white, k <= chat.Index ? 0.9f : 0.35f));
                if (k > chat.Index)
                    InkDraw.Jewel(this, c, 4.5f, fill);
            }

        // 右下的下指折角，上下轻轻浮动提示「点一下」。
        var bob = (float)Math.Sin(Time.GetTicksMsec() / 260.0) * 5f;
        var m = new Vector2(bubble.End.X - pad, bubble.End.Y - 46f + bob);
        DrawPolyline(new[] { m + new Vector2(-15f, -8f), m + new Vector2(0f, 6f), m + new Vector2(15f, -8f) }, white, 3f, true);

        _widgets.Add(new PortraitWidget(bubble, PortraitAction.ChatterAdvance, chat.Speaker.Id, true, chat.Text));
    }

    /// <summary>
    /// 圆角矩形的轮廓点（顺时针）。给了尖点就在底边对准尖点横坐标处收出一道尖角（两侧以弧线过渡进底边），
    /// 尖角根部夹在两侧圆角之内。
    /// </summary>
    private static Vector2[] BubblePath(Rect2 r, float radius, Vector2? tip, float halfBase)
    {
        var pts = new List<Vector2>();
        void Arc(Vector2 c, float from, float to)
        {
            const int steps = 8;
            for (var k = 0; k <= steps; k++)
            {
                var a = Mathf.Lerp(from, to, k / (float)steps);
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }
        Arc(new Vector2(r.Position.X + radius, r.Position.Y + radius), Mathf.Pi, Mathf.Pi * 1.5f);
        Arc(new Vector2(r.End.X - radius, r.Position.Y + radius), Mathf.Pi * 1.5f, Mathf.Pi * 2f);
        Arc(new Vector2(r.End.X - radius, r.End.Y - radius), 0f, Mathf.Pi * 0.5f);
        if (tip is { } t)
        {
            var bx = Math.Clamp(t.X, r.Position.X + radius + halfBase + 12f, r.End.X - radius - halfBase - 12f);
            var y = r.End.Y;
            // 右侧：底边→弧线收向尖点；左侧对称。用二次贝塞尔让根部圆润。
            Vector2 Q(Vector2 a, Vector2 c, Vector2 b, float u) => a * (1 - u) * (1 - u) + c * 2 * u * (1 - u) + b * u * u;
            var right = new Vector2(bx + halfBase, y);
            var left = new Vector2(bx - halfBase, y);
            for (var k = 0; k <= 6; k++)
                pts.Add(Q(right, new Vector2(bx + halfBase * 0.25f, y + 4f), t, k / 6f));
            for (var k = 1; k <= 6; k++)
                pts.Add(Q(t, new Vector2(bx - halfBase * 0.25f, y + 4f), left, k / 6f));
        }
        Arc(new Vector2(r.Position.X + radius, r.End.Y - radius), Mathf.Pi * 0.5f, Mathf.Pi);
        return pts.ToArray();
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
        // 房名整名显示：一行放不下就拆两行。格里有人时房名上移到棋子上方那一截里居中，不与棋子相压；没人时在格内居中。
        var present = _vm.Cards().Where(card => card.RoomId == room.Id && !(card.IsPlayer && Walking)).OrderByDescending(card => card.IsPlayer).ToArray();
        var pieces = PortraitLayout.CellPieces(r);
        var nameBox = present.Length == 0 ? inner.Grow(-10f)
            : new Rect2(inner.Position.X + 10f, inner.Position.Y + 10f, inner.Size.X - 20f,
                pieces.End.Y - PortraitLayout.PieceHeight - 6f - (inner.Position.Y + 10f));
        // 有人时房名那一截只有棋子上方的高度：拆两行的长房名字号收到两行放得下，不压到王棋的十字顶。
        var nameSize = present.Length > 0 && InkDraw.Measure(room.Name, PortraitLayout.FontMeta).X > nameBox.Size.X
            ? Math.Min(PortraitLayout.FontMeta, (int)(nameBox.Size.Y / 2f) - 4)
            : PortraitLayout.FontMeta;
        InkDraw.TextStacked(this, nameBox, nameBox, room.Name, nameSize,
            room.Vacant ? InkStyle.Dim : InkStyle.Line);

        DrawCellPieces(present, pieces);

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

    /// <summary>某人此刻的安排：工作写设施名，空闲写「空闲」。</summary>
    private string ActivityOf(int characterId)
    {
        var a = _vm.Hub.AssignmentOf(characterId, CurrentSlot);
        if (a.Mode == SlotMode.Free || a.FacilityId < 0)
            return "空闲";
        return $"工作 · {_vm.Hub.FacilityName(a.FacilityId)}";
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
        var path = _vm.Hub.State.Territory.Route(from, roomId, barred: _vm.Hub.PlayerBarred);
        if (_vm.Hub.Arrive(roomId))
            // 地城里半路撞上东西会停在那一间：棋子只走到人实际站的地方。
            StartWalk(from, path.Take(path.IndexOf(_vm.Hub.PlayerRoomId) + 1).ToList());
        else
            SetNotice(_vm.Hub.LockedOut(roomId) ? $"{RoomNameOf(roomId)}的门锁着，进不去。" : $"{RoomNameOf(roomId)}与这里不连通，过不去。");
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
    /// 「此刻」：主角固定第 1 格，其后是与主角同房的其他人，每页至多 3 人；不在同一房间的人不显示。
    /// 点别人即交流，点主角看角色详情。多于 3 人时第 4 格右侧画一枚实心右指三角钮，点了翻到下 3 人，末页再点回首页；
    /// 日志面板跟着换成这一页的人（见 <see cref="NowPageCards"/>）。
    /// </summary>
    private void DrawNowStrip()
    {
        var cards = NowPageCards();
        for (var i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            var r = PortraitLayout.NowCard(i);
            var cx = r.GetCenter().X;
            if (PortraitFrame.IsPressed(r))
                PortraitFrame.PressMark(this, r);
            PortraitFrame.Avatar(this, new Vector2(cx, r.Position.Y + 84f), 66f,
                PortraitAvatars.Resolve(_vm.FindById(card.Id)), card.Name, ring: true);
            DrawPieceBadge(new Vector2(cx + 56f, r.Position.Y + 130f), card);
            InkDraw.TextBounded(this, new Rect2(r.Position.X, r.Position.Y + 168f, r.Size.X, 56f), card.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            _widgets.Add(new PortraitWidget(r, PortraitAction.NowAvatar, card.Id, true, card.Name));
        }
        if (NowPages() <= 1)
            return;
        var pager = PortraitLayout.NowPager;
        if (PortraitFrame.IsPressed(pager))
            PortraitFrame.PressMark(this, pager);
        var c = new Vector2(pager.GetCenter().X + 8f, PortraitLayout.NowStrip.Position.Y + 84f);
        DrawColoredPolygon(new[] { c + new Vector2(-18f, -30f), c + new Vector2(26f, 0f), c + new Vector2(-18f, 30f) }, InkStyle.Line);
        _widgets.Add(new PortraitWidget(pager, PortraitAction.NowPage, 0, true, "下一页"));
    }

    /// <summary>
    /// 日志面板＝本次操作的快照（Core 的 <c>HubSession.Log</c>，按写入先后即按时间排）：
    /// 每次有新输出的操作都整份清空换新，不与旧日志混排。每条一句「a，b」，自上而下排，全部骨白。
    /// 字号自 50 往下收到恰好放下，下限 36；角色档只显示「此刻」当前页的至多 3 人，翻页同步换。
    /// 整块点一下即进日志页签（那里按时间看全部历史）。
    /// </summary>
    private void DrawLogPanel(bool register = true)
    {
        var panel = PortraitLayout.LogPanel;
        var pressed = register && PortraitFrame.IsPressed(panel);
        PortraitFrame.GothicFrame(this, panel, InkStyle.Panel, ornate: false);
        PortraitFrame.CornerRivets(this, panel.Grow(-24f), InkStyle.Dim);
        if (pressed)
            PortraitFrame.PressMark(this, panel);
        if (register)
            _widgets.Add(new PortraitWidget(panel, PortraitAction.Tab, 4, true, "日志"));
        var area = PortraitLayout.LogPanelText;
        // 角色档只留「此刻」当前页上的人（至多 3 人），与头像对齐；玩家动作与环境变化照常全显。
        // 门外搭话的人不在同房名单里，照常显示（声音隔着门听得见）。
        var onPage = NowPageCards().Select(c => c.Id).ToHashSet();
        var inRoom = NowOthers().Select(c => c.Id).ToHashSet();
        var entries = _vm.Hub.Log.Where(e => e.Kind != LogKind.Activity || e.Who < 0 || onPage.Contains(e.Who) || !inRoom.Contains(e.Who)).ToList();
        if (entries.Count == 0)
            return;
        // 字号自 50 往下逐级试到下限 36：先求整段放得下，再求折行最少——差一两个字就折出半行孤字时，
        // 宁可收小一号排成整行。同样行数取较大的字号。行距与条间距随字号同比缩。
        var size = PortraitLayout.LogFontMin;
        var best = (Fits: false, Lines: int.MaxValue);
        for (var fs = PortraitLayout.LogFontMax; fs >= PortraitLayout.LogFontMin; fs -= 2)
        {
            var need = -LogGap(fs);
            var lines = 0;
            foreach (var e in entries)
            {
                var n = InkDraw.WrapLines(e.Text, area.Size.X, fs).Count;
                lines += n;
                need += n * LogLineHeight(fs) + LogGap(fs);
            }
            var fits = need <= area.Size.Y + 0.5f;
            // 都放不下时取下限字号，好让尽量多的条目露出来。
            if ((fits && !best.Fits) || (fits && lines < best.Lines) || (!fits && !best.Fits))
            {
                best = (fits, lines);
                size = fs;
            }
        }
        var lineH = LogLineHeight(size);
        var gap = LogGap(size);
        var top = area.Position.Y;
        var drawn = 0;
        foreach (var e in entries)
        {
            var lines = InkDraw.WrapLines(e.Text, area.Size.X, size);
            if (top + lines.Count * lineH > area.End.Y + 0.5f)
            {
                // 规范：最小字号下必须完整显示全部日志（docs/日志规范.md）。走到这里是 Core 写多了，报出来而不是悄悄吞。
                GD.PushWarning($"日志面板放不下：{entries.Count} 条里只画得下 {drawn} 条（字号 {size}）");
                break;
            }
            foreach (var line in lines)
            {
                InkDraw.Text(this, new Vector2(area.Position.X, top + lineH / 2f), line, size, InkStyle.Line, "lm");
                top += lineH;
            }
            top += gap;
            drawn++;
        }
    }

    /// <summary>行距＝字号 ×1.28，条间距＝字号 ×0.16：50 号 64＋8，36 号 46＋6。</summary>
    private static float LogLineHeight(int size) => Mathf.Round(size * 1.28f);

    private static float LogGap(int size) => Mathf.Round(size * 0.16f);

    // ---------- 设施牌 ----------

    /// <summary>
    /// 网格与「此刻」之间一排设施牌：只摆主角此刻所在房间的设施，按房里的顺序，最多 Room.MaxFacilities 块；
    /// 空位只描一道暗框，让人看得出这间还能放几件。点牌＝使用该设施（与抽屉里的「使用」同一动作）。
    /// </summary>
    private void DrawFacilityStrip()
    {
        // 走动 / 过界期间设施栏不空置：照旧摆出发那间的设施（只画不登记），落定后才换成新房间的（2026-10-10 主人定）。
        var moving = Walking || Crossing;
        if (!moving)
            _stripRoom = _vm.Hub.PlayerRoomId;
        var fixtures = _vm.Hub.FixturesIn(_stripRoom);
        for (var i = 0; i < Rimisekai.Housing.Room.MaxFacilities; i++)
        {
            var r = PortraitLayout.FacilityPlaque(i);
            if (i >= fixtures.Count)
            {
                PortraitFrame.Bevel(this, r, 22f, null, new Color(InkStyle.WoodDark, 0.6f), 2f);
                continue;
            }
            var f = fixtures[i];
            var pressed = PortraitFrame.IsPressed(r);
            PortraitFrame.Bevel(this, r, 22f, pressed ? PortraitFrame.PressFill : new Color(InkStyle.Inset, 0.85f), InkStyle.Line, 3f);
            PortraitFrame.Bevel(this, r.Grow(-9f), 16f, null, new Color(InkStyle.Dim, 0.6f), 1.5f);
            var cx = r.GetCenter().X;
            // 正在用这件设施的人：与领地格、「此刻」同款的棋子，一人占一角（2026-10-10 主人定，不另造标记）。
            // 下两角有人时图标收到上两角之间、名字挪到上下两排角印正中的空带里，谁也不压谁。
            var workers = _vm.WorkersAtFixture(f.Id);
            var bottomTaken = workers.Count >= 3;
            FixtureGlyph(f)(this, cx, r.Position.Y + (bottomTaken ? 28f : 46f), bottomTaken ? 18f : 22f, InkStyle.Line);
            InkDraw.Text(this, new Vector2(cx, bottomTaken ? r.GetCenter().Y : r.End.Y - 40f), f.Name, PortraitLayout.FontMeta, InkStyle.Line, "cm");
            DrawFixturePieces(workers, r);
            if (!moving)
                _widgets.Add(new PortraitWidget(r, PortraitAction.Fixture, f.Id, true, f.Name));
        }
    }

    /// <summary>设施栏此刻摆的是哪间房的设施：主角落定时跟着主角，走动 / 过界期间停在出发那间。</summary>
    private int _stripRoom = -1;

    private const float FixtureSealSize = 44f;
    private const float FixtureSealInset = 6f;

    /// <summary>
    /// 设施牌的使用者棋子：一人一角，依次左上、右上、左下、右下；每枚坐在贴角的切角小印里（黑底暗线，
    /// 朝牌角一侧加一道骨白折角线）。多于 4 人时右下角换成实心「+」。
    /// </summary>
    private void DrawFixturePieces(IReadOnlyList<CharacterCard> cards, Rect2 plaque)
    {
        var slots = System.Math.Min(cards.Count, 4);
        var overflow = cards.Count > 4;
        for (var i = 0; i < slots; i++)
        {
            var right = i % 2 == 1;
            var bottom = i >= 2;
            var x = right ? plaque.End.X - FixtureSealInset - FixtureSealSize : plaque.Position.X + FixtureSealInset;
            var y = bottom ? plaque.End.Y - FixtureSealInset - FixtureSealSize : plaque.Position.Y + FixtureSealInset;
            var seal = new Rect2(x, y, FixtureSealSize, FixtureSealSize);
            PortraitFrame.Poly(this, PortraitFrame.ChamferPoints(seal, 9f), InkStyle.Bg, new Color(InkStyle.Dim, 0.9f), 2f);
            // 折角线：沿牌角两边各走 20，把小印「钉」在角上。
            var corner = new Vector2(right ? seal.End.X : seal.Position.X, bottom ? seal.End.Y : seal.Position.Y);
            var dx = right ? -20f : 20f;
            var dy = bottom ? -20f : 20f;
            DrawPolyline(new[] { corner + new Vector2(dx, 0f), corner, corner + new Vector2(0f, dy) }, InkStyle.Line, 3f);
            var c = seal.GetCenter();
            if (overflow && i == 3)
                PortraitGlyph.Plus(this, c.X, c.Y, 13f, InkStyle.Line);
            else
                InkDraw.Chess(this, new Vector2(c.X, seal.End.Y - 3f), 40f, InkDraw.PieceFor(cards[i]));
        }
    }

    /// <summary>设施牌上的小图标：按设施能做的事挑一枚，挑不出就是一颗菱。</summary>
    private static System.Action<CanvasItem, float, float, float, Color> FixtureGlyph(Rimisekai.Housing.Facility f)
    {
        if (f.CanStore)
            return PortraitGlyph.Chest;
        if (f.Supports(Rimisekai.Housing.ActionKind.Till) || f.Supports(Rimisekai.Housing.ActionKind.Tend))
            return PortraitGlyph.Leaf;
        if (f.Supports(Rimisekai.Housing.ActionKind.Forge) || f.Supports(Rimisekai.Housing.ActionKind.Woodwork))
            return PortraitGlyph.Hammer;
        if (f.Supports(Rimisekai.Housing.ActionKind.Read))
            return PortraitGlyph.Book;
        if (f.Supports(Rimisekai.Housing.ActionKind.Pray))
            return PortraitGlyph.Bell;
        return PortraitGlyph.Diamond;
    }

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
        var subY = top + 188f;
        var sx = PortraitFrame.CountTag(this, icon.End.X + 40f, subY, "设施", $"{fixtures.Count}", fixtures.Count > 0);
        InkDraw.Jewel(this, new Vector2(sx + 24f, subY), 6f, new Color(InkStyle.Dim, 0.8f));
        sx = PortraitFrame.CountTag(this, sx + 48f, subY, "在场", $"{people.Length}", people.Length > 0);
        if (here)
            PortraitFrame.TagLine(this, sx, subY, new[] { "你在这里" }, icon.End.X + 640f, InkStyle.Line, continues: true);

        var close = PortraitLayout.SheetClose(top);
        PortraitGlyph.Close(this, close.GetCenter().X, close.GetCenter().Y, 26f, InkStyle.Dim);
        _widgets.Add(new PortraitWidget(close, PortraitAction.SheetClose, 0, true, "收起"));

        PortraitFrame.SectionRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, top + 290f, "在场");
        // 在场头像整排居中（间距 136）。
        var avatarCount = Math.Min(people.Length, 7);
        var avatarStart = PortraitLayout.CanvasWidth / 2f - (avatarCount - 1) * 136f / 2f;
        for (var i = 0; i < avatarCount; i++)
            PortraitFrame.Avatar(this, new Vector2(avatarStart + i * 136f, top + 382f), 46f,
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
        // 只有自家领地的房能拆：兴趣点与地城（含委托地城）的石室不归你。
        var canDemolish = _vm.Hub.Layer == MapLayer.Territory && !here;
        // 站在自己的房间里：左钮换成门锁（自己待的房间本就拆不了），点一下换一档。
        if (here && _vm.Hub.Layer == MapLayer.Territory && _vm.Hub.CurrentRoomLockable())
        {
            var lockLabel = _vm.Hub.RoomLockLabel();
            PortraitFrame.Plaque(this, PortraitLayout.SheetFooterLeft, lockLabel);
            _widgets.Add(new PortraitWidget(PortraitLayout.SheetFooterLeft, PortraitAction.RoomLock, room.Id, true, lockLabel));
        }
        else
        {
            PortraitFrame.Plaque(this, PortraitLayout.SheetFooterLeft, "拆除", enabled: canDemolish);
            _widgets.Add(new PortraitWidget(PortraitLayout.SheetFooterLeft, PortraitAction.RoomDemolish, room.Id, canDemolish, "拆除"));
        }
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
            case PortraitAction.ChatterAdvance:
                _vm.Hub.AdvanceChatter();
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
                        QueueRedraw();
                    });
                    return true;
                }
                if (hub.Layer == MapLayer.World && !hub.State.Party.AtHome)
                {
                    // 快速返回（2026-10-09 主人要求）：先问一声，写明走回去要多久——返回照常沿最省时的路逐格走、推进时间、掷遭遇。
                    var map = hub.State.World;
                    var minutes = hub.WorldTravelMinutes(map.HomeX, map.HomeY);
                    var span = minutes >= 60 ? $"{minutes / 60}时{minutes % 60:00}分" : $"{minutes}分";
                    Confirm(hub.TravelLabel, $"走回{TerritoryName()}要 {span}，\n途中可能遇上状况。", () =>
                    {
                        hub.ToggleWorldLayer();
                        _sheet = SheetKind.None;
                        if (WorldLayer)
                            CenterWorldOnParty();
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
            case PortraitAction.RoomLock:
                hub.BeginOperation();
                hub.ToggleRoomLock();
                SetNotice(hub.RoomLockLabel() switch
                {
                    "门·锁着" => "门锁上了：你在屋里时谁都进不来，出门就回到自动。",
                    "门·敞开" => "门敞开着：谁都进得来。",
                    _ => "门回到自动：你不在或睡下时上锁，只放女仆进来。",
                });
                QueueRedraw();
                return true;
            case PortraitAction.RoomDemolish:
                var roomName = RoomNameOf(w.Index);
                var roomId = w.Index;
                Confirm("拆除", roomName, () =>
                {
                    hub.BeginOperation();
                    if (hub.RemoveRoom(roomId))
                        _sheet = SheetKind.None;
                    QueueRedraw();
                });
                return true;
            case PortraitAction.Fixture:
                var fixture = hub.State.Territory.Facilities.Find(f => f.Id == w.Index);
                if (fixture == null)
                    return true;
                if (fixture.RoomId != hub.PlayerRoomId)
                    hub.Enter(fixture.RoomId);
                // 走到设施跟前只是移动，不放过渡小动画（2026-10-10 主人定）；在设施里真正做事（FixtureRun）才放。
                if (hub.Use(w.Index))
                {
                    hub.ClearSelection();
                    _sheet = SheetKind.None;
                    OpenInteraction();
                }
                else if (hub.UseRefusal.Length > 0)
                    SetNotice(hub.UseRefusal);
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
