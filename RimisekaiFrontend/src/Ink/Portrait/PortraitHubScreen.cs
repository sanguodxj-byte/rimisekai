using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public enum PortraitAction
{
    Cell,
    Fixture,
    Tab,
    Entry,
    Back,
    PageRow,
    PageAction,
    StoragePick,
    StoreIn,
    StoreOut,
    TradeHeld,
    TradeMarket,
    TradeRun,
    QuestPick,
    PartyPick,
    QuestStart,
    SystemToggle,
    SavePick,
    SaveNow,
    ModalInput,
    ModalChoice,
    CombatAct,
    CombatTarget,
    CombatSkill,
    CombatFlee,
    CombatColumn,
    CombatMenu,
    CombatSettings,
    SkillSector,
    SkillNode,
    SkillReset,
    SkillPrevious,
    SkillNext,
    ScrollTrack,
    RosterPick,
    ScheduleMember,
    ScheduleSlot,
    ScheduleRoom,
    ScheduleFacility,
    ScheduleCancel,
    SystemPage,
    InteractionBack,
    SocialCategory,
    SocialRun,
    HubWorld,
    GiftItem,
    FixtureRun,
    ObserveRoom,
    ConversationAdvance,
    ConversationChoice,
    CharacterPage,
    StatusAbilityToggle,
    DevelopmentCell,
    DevelopmentFacility,
    DevelopmentRoom,
    DevelopmentAction,
}

/// <summary>一个可点块。命中判定按注册逆序（后注册者画在上、先命中），与横屏同一套约定。</summary>
public readonly record struct PortraitWidget(Rect2 Rect, PortraitAction Action, int Index, bool Enabled,
    string Label, Vector2[]? Polygon = null)
{
    public bool Contains(Vector2 point) => Rect.HasPoint(point)
        && (Polygon == null || Geometry2D.IsPointInPolygon(point, Polygon));
}

/// <summary>
/// 竖屏据点界面：顶栏 + 内容区 + 底部页签带。与横屏 InkHubScreen 平行存在，互不引用。
///
/// 触摸取向的三处与横屏不同：点击在**松开**时才派发（按下即走会误触），
/// 列表靠**拖动**滚动（没有滚轮），一切可点块不低于 118px＝48dp。
/// </summary>
public partial class PortraitHubScreen : Control
{
    private static readonly string[] TabLabels = { "地图", "日志", "角色", "操作" };

    private readonly List<PortraitWidget> _widgets = new();
    private InkViewModel _vm = null!;
    private string _notice = "";
    private int _tab;
    private int _listFirst;
    private int _fixtureFirst;
    private int _selectedCell = -1;
    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPos;
    private Rect2? _pressRect;

    public Func<int, Texture2D?>? RoomImageProvider { get; set; }
    public IReadOnlyList<PortraitWidget> DebugWidgets => _widgets;
    public string DebugNotice => _notice;
    public int DebugTab => _tab;

    public void Bind(InkViewModel vm)
    {
        _vm = vm;
        _tab = 0;
        _page = InkPage.None;
        _systemPage = "";
        _questMode = false;
        _notice = "";
        _listFirst = _fixtureFirst = 0;
        _interactionOpen = _giftOpen = _observing = false;
        _socialCategory = -1;
        _selectedCell = -1;
        _developmentCell = _developmentFacility = _developmentRoom = _developmentPlacing = -1;
        _developmentFacilityFirst = _developmentRoomFirst = _developmentActionFirst = 0;
        _tradeHeld = _tradeMarket = _rowSel = -1;
        _tradeHeldFirst = _tradeMarketFirst = 0;
        _party.Clear();
        _statusAbilityOpen = false;
        _statusAbilityFirst = 0;
        _statusAbilityTotal = 0;
        ResetListDrag();
        ResetSkillView();
        Size = new Vector2(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
        CustomMinimumSize = Size;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void ShowTab(int i)
    {
        _tab = i;
        CloseOverlay();
        _interactionOpen = false;
        _giftOpen = false;
        _socialCategory = -1;
        _listFirst = 0;
        ResetListDrag();
        QueueRedraw();
    }

    /// <summary>核对用：按动作命中一次，走与点击完全相同的派发路。</summary>
    public void DebugPress(PortraitAction action, int index)
    {
        foreach (var w in _widgets)
            if (w.Action == action && w.Index == index)
            {
                Dispatch((0, w));
                return;
            }
    }

    // ---------- 绘制 ----------

    public override void _Draw()
    {
        _widgets.Clear();
        _scrollAreas.Clear();
        // 触摸没有悬停：按下当场把那一块画成选中态，松开才派发。
        PortraitFrame.SetPress(_pressed && !_dragging ? _pressRect : null);
        PortraitFrame.Backdrop(this);
        DrawHeader();

        if (DrawOverlay() || DrawInteraction())
            return;

        switch (_tab)
        {
            case 0: DrawMap(); break;
            case 1: DrawLog(); break;
            case 2: DrawRoster(); break;
            default: DrawOps(); break;
        }

        DrawTabBar();
        DrawNotice();
    }

    private void DrawHeader()
    {
        // 顶栏是通栏窄条，四角就是地点名与金钱的落脚处：不发角花，只留细双线。
        PortraitFrame.Panel(this, PortraitLayout.Header, InkStyle.Panel, flourish: 0f);

        // 四个状态格之间各缀一枚小实心菱：把四项读成四项，而不是一串挨着的数。
        for (var i = 1; i < 4; i++)
            InkDraw.Jewel(this, new Vector2(PortraitLayout.HeaderSlot(i).Position.X,
                PortraitLayout.Header.GetCenter().Y), 4f, new Color(InkStyle.Dim, 0.85f));

        InkDraw.Text(this, new Vector2(PortraitLayout.PlaceNameRect.Position.X,
                PortraitLayout.Header.GetCenter().Y),
            _vm.PlaceTitle, PortraitLayout.FontBody, InkStyle.Line, "lm");

        var items = _vm.HeaderItems();
        for (var i = 0; i < items.Count; i++)
            InkDraw.Text(this, new Vector2(PortraitLayout.HeaderSlot(i).GetCenter().X,
                    PortraitLayout.Header.GetCenter().Y),
                items[i].Value, PortraitLayout.FontMeta, InkStyle.Line, "cm");
    }

    private void DrawMap()
    {
        for (var y = 0; y < PortraitLayout.GridRows; y++)
            for (var x = 0; x < PortraitLayout.GridCols; x++)
                DrawCell(x, y);

        DrawAvatars();
        DrawFixtures();
    }

    /// <summary>
    /// 角色头像带：删掉地点插画带后空出的中部常显。
    /// 每张卡＝方形头像＋名牌＋体力细线。只画主角所在房间里的角色。纯展示，不带新的交互。
    /// </summary>
    private void DrawAvatars()
    {
        var cards = _vm.Avatars();
        for (var i = 0; i < PortraitLayout.AvatarSlots; i++)
        {
            var rect = PortraitLayout.AvatarCard(i);
            PortraitFrame.Panel(this, rect, InkStyle.Bg, flourish: 0f);
            if (i >= cards.Count)
                continue;
            var who = cards[i];
            var present = who.RoomId >= 0 || who.IsPlayer;

            var character = _vm.FindById(who.Id);
            var tex = PortraitAvatars.Resolve(character);
            if (tex != null)
            {
                var area = PortraitLayout.AvatarImage(rect);
                var size = tex.GetSize();
                var scale = Mathf.Min(area.Size.X / size.X, area.Size.Y / size.Y);
                DrawTextureRect(tex, new Rect2(area.GetCenter() - size * scale / 2f, size * scale), false);
                if (!present)
                    DrawRect(area, new Color(InkStyle.Bg, 0.55f));
            }

            InkDraw.TextBounded(this, PortraitLayout.AvatarName(rect), who.Name,
                PortraitLayout.FontBody, PortraitLayout.FontMeta, present ? InkStyle.Line : InkStyle.Dim, "cm");
            var stamina = character?.Condition.Stamina ?? 0;
            var maxStamina = character?.Condition.MaxStamina ?? 0;
            if (maxStamina > 0)
                InkDynamicMeter.Draw(this, $"portrait_hub_avatar_{who.Id}",
                    PortraitLayout.AvatarMeter(rect), Mathf.Clamp((float)stamina / maxStamina, 0f, 1f), true);
        }
    }

    private void DrawCell(int x, int y)
    {
        var r = PortraitLayout.Cell(x, y);
        var room = _vm.RoomAt(x, y);
        var c = r.GetCenter();

        if (room == null)
        {
            // 未开拓的空格：只留一圈虚线，不写任何字。
            DashedLoop(r.Grow(-8f), new Color(InkStyle.Dim, 0.5f));
            return;
        }

        if (!room.Open)
        {
            // 已有房间但未开放：压暗的实线格，只报房名。
            InkDraw.Ink(this, RectLoop(r.Grow(-4f)), new Color(InkStyle.Dim, 0.8f), PortraitLayout.LineHair);
            InkDraw.Text(this, c, room.Name, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        }
        else
        {
            var picked = _selectedCell == room.Id;
            var pressed = PortraitFrame.IsPressed(r);
            DrawRect(r.Grow(-4f), pressed ? PortraitFrame.PressFill : picked ? InkStyle.Hover : InkStyle.Inset);
            var picture = RoomImageProvider?.Invoke(room.Id);
            if (picture != null)
            {
                var area = PortraitLayout.RoomPicture(r);
                var size = picture.GetSize();
                var scale = Mathf.Min(area.Size.X / size.X, area.Size.Y / size.Y);
                DrawTextureRect(picture, new Rect2(area.GetCenter() - size * scale / 2, size * scale), false);
            }
            // 已开放：双线格（外主线＋内暗线），与面板同一套框线语汇。按下时外线加粗。
            InkDraw.Ink(this, RectLoop(r.Grow(-4f)), InkStyle.Line,
                pressed ? PortraitLayout.LineBold + 2f : PortraitLayout.LineBold);
            InkDraw.Ink(this, RectLoop(r.Grow(-12f)), new Color(InkStyle.Dim, 0.7f), PortraitLayout.LineHair);
            InkDraw.Text(this, new Vector2(c.X, r.Position.Y + 52f), room.Name,
                PortraitLayout.FontMeta, InkStyle.Line, "cm");
            DrawMarkers(_vm.Cards().Where(card => card.RoomId == room.Id).ToArray(), PortraitLayout.RoomMarkers(r));
        }

        _widgets.Add(new PortraitWidget(r, PortraitAction.Cell, room.Id, room.Open, room.Name));
    }

    /// <summary>虚线矩形：未开拓的格子只给一圈虚线，与开发页同一套语汇。</summary>
    private void DashedLoop(Rect2 r, Color color)
    {
        var corners = new[]
        {
            r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), r.Position,
        };
        for (var i = 0; i < 4; i++)
            InkDraw.Dashed(this, corners[i], corners[i + 1], color, PortraitLayout.LineHair, dash: 22f, gap: 16f);
    }

    private void DrawFixtures()
    {
        var area = PortraitLayout.FixtureArea;
        // 设施行横贯整块面板直到下沿两角，发角花必与行文打架：只留细双线。
        PortraitFrame.Panel(this, area, new Color(InkStyle.Bg, 0f), flourish: 0f);
        var fixtures = _vm.Fixtures();
        var visible = PortraitLayout.FixtureVisibleRows;
        _fixtureFirst = Math.Clamp(_fixtureFirst, 0, Math.Max(0, fixtures.Count - visible));
        for (var i = 0; i < visible && i + _fixtureFirst < fixtures.Count; i++)
        {
            var fixture = fixtures[i + _fixtureFirst];
            var seats = _vm.Seats(fixture);
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.FixtureList, i, hasScroll: fixtures.Count > visible);
            PortraitFrame.Row(this, rect, fixture.Name, $"{seats.Used}/{seats.Capacity}", fixture.PlayerHere);
            DrawMarkers(_vm.Hub.WorkersAtFixture(fixture.Id), PortraitLayout.FixtureMarkers(rect));
            _widgets.Add(new PortraitWidget(rect, PortraitAction.Fixture, fixture.Id, true, fixture.Name));
        }
        RegisterScroll("fixtures", PortraitLayout.FixtureList, fixtures.Count, visible,
            _fixtureFirst, first => _fixtureFirst = first);
    }

    private void DrawLog()
    {
        var lines = _vm.LogLines();
        _listFirst = ClampFirst(_listFirst, lines.Count);
        var hasScroll = lines.Count > PortraitLayout.VisibleRows;
        for (var i = 0; i < PortraitLayout.VisibleRows; i++)
        {
            var at = _listFirst + i;
            if (at >= lines.Count)
                break;
            PortraitFrame.Row(this, PortraitLayout.ListRow(i, hasScroll), lines[at], "", false);
        }
        DrawScroll(lines.Count);
    }

    private void DrawRoster()
    {
        var cards = _vm.CardsHere();
        _listFirst = ClampFirst(_listFirst, cards.Count);
        var hasScroll = cards.Count > PortraitLayout.VisibleRows;
        for (var i = 0; i < PortraitLayout.VisibleRows; i++)
        {
            var at = _listFirst + i;
            if (at >= cards.Count)
                break;
            var card = cards[at];
            var rect = PortraitLayout.ScrolledRow(PortraitLayout.ListArea, i, PortraitLayout.RowHeight, hasScroll);
            PortraitFrame.Row(this, rect, card.Name, card.IsPlayer ? "你" : "",
                _vm.Hub.SelectedCharacterId == card.Id);
            _widgets.Add(new PortraitWidget(rect, PortraitAction.RosterPick, card.Id, true, card.Name));
        }
        DrawScroll(cards.Count);
    }

    /// <summary>世界层格子里房名即兴趣点名（导出表按 NameZh/NameEn 落名）。</summary>
    private string RoomName(int roomId)
    {
        foreach (var room in _vm.Rooms())
            if (room.Id == roomId)
                return room.Name;
        return "";
    }

    private void DrawOps()
    {
        PortraitFrame.Title(this, new Rect2(0, PortraitLayout.Content.Position.Y,
            PortraitLayout.CanvasWidth, PortraitLayout.TitleBand), "操作");
        for (var i = 0; i < AllEntries.Length; i++)
        {
            var r = PortraitLayout.OpRow(i + 1);
            PortraitFrame.Button(this, r, InkPageModel.Info(AllEntries[i]).Label);
            _widgets.Add(new PortraitWidget(r, PortraitAction.Entry, i, true, AllEntries[i].ToString()));
        }
        var sys = PortraitLayout.OpRow(AllEntries.Length + 1);
        PortraitFrame.Button(this, sys, "设置");
        _widgets.Add(new PortraitWidget(sys, PortraitAction.Entry, AllEntries.Length, true, "设置"));
        var world = PortraitLayout.OpRow(AllEntries.Length + 2);
        var worldLabel = _vm.Hub.Layer == Rimisekai.Hub.MapLayer.World ? "返回领地" : "世界大地图";
        PortraitFrame.Button(this, world, worldLabel);
        _widgets.Add(new PortraitWidget(world, PortraitAction.HubWorld, 0, true, worldLabel));
    }

    private void DrawTabBar()
    {
        // 页签带不发角花：四个页签正好铺满整条，角花会被页签的底盖掉，白画。
        PortraitFrame.Panel(this, PortraitLayout.TabBar, InkStyle.Inset, flourish: 0f);
        for (var i = 0; i < PortraitLayout.TabCount; i++)
        {
            var r = PortraitLayout.Tab(i);
            PortraitFrame.Button(this, r, TabLabels[i], selected: i == _tab && !OverlayActive);
            _widgets.Add(new PortraitWidget(r, PortraitAction.Tab, i, true, TabLabels[i]));
        }
    }

    private void DrawScroll(int total, int? visibleRows = null)
    {
        var visible = visibleRows.HasValue ? visibleRows.Value :
            OverlayActive ? PortraitLayout.OverlayRows : PortraitLayout.VisibleRows;
        var area = OverlayActive ? PortraitLayout.PageListArea(visible) : PortraitLayout.ListArea;
        RegisterScroll("page_list", area, total, visible, _listFirst, first => _listFirst = first);
    }

    private void DrawNotice()
    {
        if (_notice.Length == 0)
            return;
        var r = new Rect2(PortraitLayout.Pad, PortraitLayout.TabBar.Position.Y - 90,
            PortraitLayout.CanvasWidth - PortraitLayout.Pad * 2f, 74);
        if (_widgets.Any(widget => widget.Rect.Intersects(r)))
            return;
        InkDraw.Text(this, r.GetCenter(), _notice, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
    }

    // ---------- 输入 ----------

    public override void _GuiInput(InputEvent e)
    {
        if (HandleSkillInput(e) || HandleListInput(e))
            return;
        if (e is InputEventMouseMotion { ButtonMask: not 0 } motion)
        {
            if (_pressed && !_dragging
                && motion.Position.DistanceTo(_pressPos) >= PortraitLayout.ListDragThreshold)
            {
                _dragging = true;
                QueueRedraw();
            }
            return;
        }

        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
            return;

        if (mb.Pressed)
        {
            _pressed = true;
            _dragging = false;
            _pressPos = mb.Position;
            _pressRect = Hit(mb.Position)?.Widget.Rect;
            QueueRedraw();
            return;
        }

        if (!_pressed)
            return;
        _pressed = false;
        _pressRect = null;
        if (_dragging)
        {
            QueueRedraw();
            return;
        }
        var hit = Hit(mb.Position);
        if (hit != null && hit.Value.Widget.Contains(_pressPos))
            Dispatch(hit);
        else
            QueueRedraw();
    }

    private void Dispatch((int Index, PortraitWidget Widget)? hit)
    {
        if (hit == null || !hit.Value.Widget.Enabled)
            return;
        var w = hit.Value.Widget;
        _vm.Hub.BeginOperation();
        switch (w.Action)
        {
            case PortraitAction.Cell:
                if (_vm.Hub.Layer == Rimisekai.Hub.MapLayer.World)
                {
                    // 世界层：点兴趣点所在格进入该地点（与横版同一出口）。
                    var poi = _vm.Hub.State.World.Pois.Find(p =>
                        p.NameZh == RoomName(w.Index) || p.NameEn == RoomName(w.Index));
                    var entered = poi != null && _vm.Hub.EnterWorldPoi(poi.Id);
                    _notice = entered ? "" : "这里无法进入。";
                    break;
                }
                _vm.Hub.Enter(w.Index);
                _selectedCell = w.Index;
                Notice();
                break;

            case PortraitAction.HubWorld:
                _vm.Hub.ToggleWorldLayer();
                _tab = 0;
                _selectedCell = -1;
                Notice();
                break;
            case PortraitAction.Fixture:
                if (_vm.Hub.Use(w.Index))
                {
                    _vm.Hub.ClearSelection();
                    OpenInteraction();
                }
                Notice();
                break;
            case PortraitAction.Tab:
                ShowTab(w.Index);
                return;
            case PortraitAction.Entry:
                OpenEntry(w.Index);
                return;
            default:
                ExecuteOverlay(w);
                break;
        }
        QueueRedraw();
    }

    private void Notice() =>
        _notice = _vm.Hub.Log.Count > 0 ? _vm.Hub.Log[^1].Text : "";

    /// <summary>注册逆序命中：后注册的画在上层，先命中。</summary>
    private (int Index, PortraitWidget Widget)? Hit(Vector2 at)
    {
        for (var i = _widgets.Count - 1; i >= 0; i--)
            if (_widgets[i].Contains(at))
                return (i, _widgets[i]);
        return null;
    }

    private static int ClampFirst(int first, int total) =>
        Mathf.Clamp(first, 0, System.Math.Max(0, total - PortraitLayout.VisibleRows));

    private static IReadOnlyList<Vector2> RectLoop(Rect2 r) => new[]
    {
        r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), r.Position,
    };

    private void DrawMarkers(IReadOnlyList<Rimisekai.Hub.CharacterCard> cards, Rect2 area)
    {
        var capacity = Math.Max(1, (int)(area.Size.X / PortraitLayout.MarkerStep));
        var shown = Math.Min(cards.Count, capacity);
        var start = area.GetCenter().X - (shown - 1) * PortraitLayout.MarkerStep / 2;
        for (var i = 0; i < shown; i++)
            InkDraw.Chess(this, new Vector2(start + i * PortraitLayout.MarkerStep, area.End.Y),
                PortraitLayout.MarkerHeight, InkDraw.PieceFor(cards[i]), isLimitCap: false);
        if (cards.Count > shown)
            InkDraw.Text(this, area.End, $"+{cards.Count - shown}", PortraitLayout.FontMeta, InkStyle.Dim, "rb");
    }
}
