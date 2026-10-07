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
    // ---- 2026-10-07 重设计新增 ----
    Rename,
    OpenSystem,
    Build,
    SheetClose,
    RoomGo,
    RoomDemolish,
    NowAvatar,
    CharacterSegment,
    SkillCard,
    TraitInfo,
    EquipInfo,
    OpenDisc,
    StoreSegment,
    StockCategory,
    StockSearch,
    StockItem,
    TradeSegment,
    TradeMinus,
    TradePlus,
    CraftStation,
    CraftRecipe,
    CraftToggle,
    QuestTake,
    SystemSegment,
    VolumeSet,
    DevelopmentTab,
}

/// <summary>一个可点块。命中判定按注册逆序（后注册者画在上、先命中）。</summary>
public readonly record struct PortraitWidget(Rect2 Rect, PortraitAction Action, int Index, bool Enabled,
    string Label, Vector2[]? Polygon = null)
{
    public bool Contains(Vector2 point) => Rect.HasPoint(point)
        && (Polygon == null || Geometry2D.IsPointInPolygon(point, Polygon));
}

/// <summary>
/// 竖屏据点界面（2026-10-07 重设计）：
/// 根页签＝HUD＋内容＋五页签（领地 / 角色 / 委托 / 仓储 / 日志）；
/// 推入页＝顶栏＋整页（角色详情 / 技能星盘 / 建造 / 系统）；
/// 设施、编成、排班、存取、交互走底部抽屉；对话铺满整屏。
///
/// 触摸取向：点击在**松开**时才派发，列表靠**拖动**滚动，一切可点块不低于 118px＝48dp。
/// 滚动内容先画、HUD/页签/固定条后画（不透明底），越界的部分被盖住——不另做裁剪。
/// 抽屉打开时先清掉下层全部命中块，下层只作压暗的背景，点不到。
/// </summary>
public partial class PortraitHubScreen : Control
{
    private enum PushPage
    {
        None,
        Character,
        Disc,
        Build,
        System,
    }

    private enum SheetKind
    {
        None,
        Room,
        Party,
        Slot,
        Item,
    }

    private readonly List<PortraitWidget> _widgets = new();
    private InkViewModel _vm = null!;
    private string _notice = "";
    private float _noticeAge;
    private int _tab;
    private PushPage _push;
    private SheetKind _sheet;
    private float _sheetTop = -1f;
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
        _push = PushPage.None;
        _sheet = SheetKind.None;
        _notice = "";
        _interactionOpen = _giftOpen = _observing = false;
        _socialCategory = -1;
        _sheetRoom = -1;
        _developmentCell = _developmentFacility = _developmentRoom = _developmentPlacing = -1;
        _developmentFacilityFirst = _developmentRoomFirst = _developmentActionFirst = 0;
        _tradeQty.Clear();
        _party.Clear();
        _questSel = -1;
        _storeMode = 0;
        _pan.Clear();
        ResetListDrag();
        ResetSkillView();
        Size = new Vector2(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
        CustomMinimumSize = Size;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>切到某个根页签：收起推入页与抽屉，离开交易段即散集。</summary>
    public void ShowTab(int i)
    {
        LeaveTradeIfOpen();
        CloseTransient();
        _push = PushPage.None;
        _tab = i;
        if (_tab == 3 && _storeMode == 1)
            _vm.Hub.OpenTrade();
        ResetListDrag();
        QueueRedraw();
    }

    /// <summary>收起抽屉与交互（不动根页签）。</summary>
    private void CloseTransient()
    {
        _sheet = SheetKind.None;
        _interactionOpen = false;
        _giftOpen = false;
        _socialCategory = -1;
        if (_vm.StorageOpen)
            _vm.Hub.CloseStorage();
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
        _sheetTop = -1f;
        // 触摸没有悬停：按下当场把那一块画成选中态，松开才派发。
        PortraitFrame.SetPress(_pressed && !_dragging ? _pressRect : null);
        PortraitFrame.Backdrop(this);

        if (_push != PushPage.None)
            DrawPushed();
        else if (ConversationActive)
        {
            DrawScene();
            DrawToast();
            return;
        }
        else
        {
            switch (_tab)
            {
                case 0: DrawTerritory(); break;
                case 1: DrawRoster(); break;
                case 2: DrawQuestBoard(); break;
                case 3: DrawStore(); break;
                default: DrawLog(); break;
            }
            DrawHud();
            DrawTabBar();
        }

        if (_vm.StorageOpen)
            OpenSheetLayer(DrawStorageSheet);
        else if (_interactionOpen)
            OpenSheetLayer(DrawInteractionSheet);
        else if (_sheet == SheetKind.Room)
            OpenSheetLayer(DrawRoomSheet);
        else if (_sheet == SheetKind.Party)
            OpenSheetLayer(DrawPartySheet);
        else if (_sheet == SheetKind.Slot)
            OpenSheetLayer(DrawSlotSheet);
        else if (_sheet == SheetKind.Item)
            OpenSheetLayer(DrawItemSheet);

        DrawToast();
    }

    /// <summary>
    /// 抽屉层：下层画面已画完，这里把它的命中块与滚动区整个清掉（只剩压暗的背景），
    /// 再由 draw 画面板并注册抽屉自己的命中块；draw 返回面板上沿。
    /// 抽屉之上的压暗区是一整块「收起」命中块。
    /// </summary>
    private void OpenSheetLayer(Func<float> draw)
    {
        _widgets.Clear();
        _scrollAreas.Clear();
        _sheetTop = draw();
        _widgets.Insert(0, new PortraitWidget(new Rect2(0, 0, PortraitLayout.CanvasWidth, _sheetTop),
            PortraitAction.SheetClose, 0, true, "收起"));
    }

    private void DrawPushed()
    {
        switch (_push)
        {
            case PushPage.Character: DrawCharacterPage(); break;
            case PushPage.Disc: DrawSkillPage(); break;
            case PushPage.Build: DrawDevelopment(); break;
            default: DrawSystem(); break;
        }
    }

    /// <summary>推入页顶栏：左返回、中标题（可带副行）、右侧可选一枚文字钮。不透明底，盖住滚上来的内容。</summary>
    private void DrawPageTop(string title, string sub = "", string action = "", PortraitAction actionKind = PortraitAction.Back)
    {
        var top = PortraitLayout.PageTop;
        DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, top.End.Y), InkStyle.Bg);
        GothicArt.Tile(this, new Rect2(0, 0, PortraitLayout.CanvasWidth, top.End.Y), 0.7f);
        var back = PortraitLayout.PageBack;
        if (PortraitFrame.IsPressed(back))
            PortraitFrame.RoundRect(this, back.Grow(-8f), 40f, PortraitFrame.PressFill);
        PortraitGlyph.Back(this, back.Position.X + 64f, back.GetCenter().Y, 30f, InkStyle.Line);
        _widgets.Add(new PortraitWidget(back, PortraitAction.Back, 0, true, "返回"));
        var cy = top.GetCenter().Y;
        var titleRect = new Rect2(200f, cy - (sub.Length > 0 ? 62f : 40f), PortraitLayout.CanvasWidth - 400f, 80f);
        InkDraw.TextBounded(this, titleRect, title, PortraitLayout.FontPlace, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        if (sub.Length > 0)
            InkDraw.TextBounded(this, new Rect2(160f, cy + 14f, PortraitLayout.CanvasWidth - 320f, 52f), sub,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Dim, "cm");
        if (action.Length > 0)
        {
            var r = PortraitLayout.PageAction;
            if (PortraitFrame.IsPressed(r))
                PortraitFrame.RoundRect(this, r.Grow(-8f), 40f, PortraitFrame.PressFill);
            InkDraw.Text(this, new Vector2(r.End.X - 40f, r.GetCenter().Y), action, PortraitLayout.FontBody, InkStyle.Line, "rm");
            _widgets.Add(new PortraitWidget(r, actionKind, 0, true, action));
        }
        DrawRect(new Rect2(0, top.End.Y - 4f, PortraitLayout.CanvasWidth, 3f), InkStyle.Dim);
        PortraitFrame.FadingRule(this, 0f, PortraitLayout.CanvasWidth, top.End.Y - 2f);
    }

    /// <summary>
    /// HUD：第一行地名（领地内可点改名）＋金钱签；第二行季节·天气·时刻＋系统钮。
    /// 时间随操作推进（Core 无倍速），所以这里没有暂停/倍速控件。
    /// </summary>
    private void DrawHud()
    {
        var hud = PortraitLayout.Hud;
        DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, hud.End.Y), InkStyle.Bg);
        GothicArt.Tile(this, new Rect2(0, 0, PortraitLayout.CanvasWidth, hud.End.Y), 0.7f);
        var header = _vm.Hub.Header();
        var items = _vm.HeaderItems();

        var place = PortraitLayout.HudPlace;
        if (PortraitFrame.IsPressed(place))
            PortraitFrame.RoundRect(this, place, 30f, PortraitFrame.PressFill);
        var title = _vm.MapTitle();
        var money = items[3].Value;
        var moneyWidth = InkDraw.Measure(money, PortraitLayout.FontMeta).X + 120f;
        var titleMax = PortraitLayout.CanvasWidth - PortraitLayout.Pad * 2f - moneyWidth - 90f;
        var titleSize = InkDraw.FitSize(title, titleMax, PortraitLayout.FontPlace, PortraitLayout.FontMeta);
        var shown = InkDraw.Ellipsize(title, titleMax, titleSize);
        InkDraw.Text(this, new Vector2(place.Position.X + 20f, PortraitLayout.HudLine1), shown, titleSize, InkStyle.Line, "lm");
        if (_vm.CanRenameTerritory)
        {
            PortraitGlyph.Pen(this, place.Position.X + 20f + InkDraw.Measure(shown, titleSize).X + 40f,
                PortraitLayout.HudLine1, 16f, InkStyle.Dim);
            _widgets.Add(new PortraitWidget(new Rect2(place.Position, new Vector2(
                    Mathf.Min(place.Size.X, InkDraw.Measure(shown, titleSize).X + 100f), place.Size.Y)),
                PortraitAction.Rename, 0, true, "改名"));
        }

        var pill = new Rect2(PortraitLayout.CanvasWidth - PortraitLayout.Pad - moneyWidth, PortraitLayout.HudLine1 - 38f,
            moneyWidth, 76f);
        PortraitFrame.RoundRect(this, pill, 38f, new Color(InkStyle.Inset, 0.85f), InkStyle.Dim, 3f);
        PortraitGlyph.Coin(this, pill.Position.X + 46f, pill.GetCenter().Y, 20f, InkStyle.Line);
        InkDraw.Text(this, new Vector2(pill.End.X - 32f, pill.GetCenter().Y), money, PortraitLayout.FontMeta, InkStyle.Line, "rm");

        var y = PortraitLayout.HudLine2;
        var x = PortraitLayout.Pad + 20f;
        var season = $"{items[0].Value} 第{_vm.Hub.State.Clock.Week}日";
        PortraitGlyph.Leaf(this, x + 18f, y, 18f, InkStyle.Dim);
        InkDraw.Text(this, new Vector2(x + 50f, y), season, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        x += 50f + InkDraw.Measure(season, PortraitLayout.FontMeta).X + 36f;
        PortraitGlyph.Weather(this, header.Weather, x + 18f, y, 18f, InkStyle.Dim);
        InkDraw.Text(this, new Vector2(x + 50f, y), items[1].Value, PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        x += 50f + InkDraw.Measure(items[1].Value, PortraitLayout.FontMeta).X + 36f;
        PortraitGlyph.Clock(this, x + 18f, y, 18f, InkStyle.Dim);
        InkDraw.Text(this, new Vector2(x + 50f, y), items[2].Value, PortraitLayout.FontMeta, InkStyle.Line, "lm");

        var sys = PortraitLayout.HudSystem;
        if (PortraitFrame.IsPressed(sys))
            PortraitFrame.RoundRect(this, sys.Grow(-10f), 49f, PortraitFrame.PressFill);
        PortraitGlyph.Gear(this, sys.GetCenter().X, sys.GetCenter().Y, 26f, InkStyle.Line);
        _widgets.Add(new PortraitWidget(sys, PortraitAction.OpenSystem, 0, true, "系统"));

        DrawRect(new Rect2(0, hud.End.Y - 4f, PortraitLayout.CanvasWidth, 3f), InkStyle.Dim);
        PortraitFrame.FadingRule(this, 0f, PortraitLayout.CanvasWidth, hud.End.Y - 2f);
    }

    /// <summary>
    /// 底部五页签：图标＋字。哥特版：暗纹石板底、顶沿双线；当前页签＝骨白尖拱牌托底、
    /// 图标反黑，页签顶沿正上方一枚菱珠。命中块与旧版一致（整格）。
    /// </summary>
    private void DrawTabBar()
    {
        var bar = PortraitLayout.TabBar;
        var back = new Rect2(0, bar.Position.Y, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - bar.Position.Y);
        DrawRect(back, InkStyle.Bg);
        GothicArt.Tile(this, back, 0.7f);
        DrawRect(new Rect2(0, bar.Position.Y, PortraitLayout.CanvasWidth, 4f), InkStyle.Line);
        DrawLine(new Vector2(0, bar.Position.Y + 12f), new Vector2(PortraitLayout.CanvasWidth, bar.Position.Y + 12f),
            InkStyle.Dim, 2f);
        for (var i = 1; i < PortraitLayout.TabCount; i++)
        {
            var x = PortraitLayout.Tab(i).Position.X;
            DrawLine(new Vector2(x, bar.Position.Y + 48f), new Vector2(x, bar.Position.Y + 160f), new Color(InkStyle.WoodDark, 0.6f), 2f);
        }
        for (var i = 0; i < PortraitLayout.TabCount; i++)
        {
            var r = PortraitLayout.Tab(i);
            var on = i == _tab && _push == PushPage.None;
            var cx = r.GetCenter().X;
            var cy = r.Position.Y + 70f;
            var pill = new Rect2(cx - 76f, cy - 40f, 152f, 80f);
            if (on)
            {
                PortraitFrame.RoundRect(this, pill, 40f, InkStyle.Line, InkStyle.Line, 3f);
                PortraitFrame.RoundRect(this, pill.Grow(-8f), 32f, null, new Color(InkStyle.Bg, 0.4f), 2f);
                InkDraw.Jewel(this, new Vector2(cx, r.Position.Y + 8f), 10f, InkStyle.Line);
                InkDraw.Jewel(this, new Vector2(cx, r.Position.Y + 8f), 4f, InkStyle.Bg);
            }
            else if (PortraitFrame.IsPressed(r))
                PortraitFrame.RoundRect(this, pill, 40f, PortraitFrame.PressFill);
            PortraitGlyph.TabIcons[i](this, cx, cy, 26f, on ? InkStyle.Bg : InkStyle.Dim);
            InkDraw.Text(this, new Vector2(cx, cy + 84f), PortraitLayout.TabLabels[i], PortraitLayout.FontMeta,
                on ? InkStyle.Line : InkStyle.Dim, "cm");
            _widgets.Add(new PortraitWidget(r, PortraitAction.Tab, i, true, PortraitLayout.TabLabels[i]));
        }
    }

    /// <summary>
    /// 操作反馈：领地页签上由提示条常显；其余画面在底部弹一枚 3 秒的浅填签，不拦输入。
    /// </summary>
    private void DrawToast()
    {
        if (_notice.Length == 0 || _noticeAge > 3f || (_push == PushPage.None && _tab == 0 && _sheetTop < 0f))
            return;
        var bottom = _sheetTop >= 0f ? _sheetTop - 30f
            : _push == PushPage.None ? PortraitLayout.TabTop - 24f : PortraitLayout.CanvasHeight - 80f;
        var width = Mathf.Min(PortraitLayout.FullWidth, InkDraw.Measure(_notice, PortraitLayout.FontMeta).X + 96f);
        var r = new Rect2((PortraitLayout.CanvasWidth - width) / 2f, bottom - 96f, width, 96f);
        PortraitFrame.RoundRect(this, r, 48f, new Color(InkStyle.Hover, 0.96f), InkStyle.Line, 3f);
        InkDraw.TextBounded(this, r.Grow(-24f), _notice, PortraitLayout.FontMeta, PortraitLayout.FontMeta, InkStyle.Line, "cm");
    }

    /// <summary>在滚动视口内登记命中块：只登记露在视口里的部分，露出的短边不足触控下限就不登记。</summary>
    private void AddClipped(Rect2 rect, Rect2 viewport, PortraitAction action, int index, bool enabled, string label)
    {
        var shown = rect.Intersection(viewport);
        if (shown.Size.X < PortraitLayout.TouchMin || shown.Size.Y < PortraitLayout.TouchMin)
            return;
        _widgets.Add(new PortraitWidget(shown, action, index, enabled, label));
    }

    /// <summary>滚动视口上方的遮罩：把滚出视口顶的内容盖掉（之后再画固定控件）。</summary>
    private void MaskAbove(Rect2 viewport, Color? color = null) =>
        DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, viewport.Position.Y), color ?? InkStyle.Bg);

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
        var before = _vm.Hub.Log.Count > 0 ? _vm.Hub.Log[^1].Text : "";
        switch (w.Action)
        {
            case PortraitAction.Tab:
                ShowTab(w.Index);
                return;
            case PortraitAction.OpenSystem:
                OpenSystemPage(InkSystemScreen.PageSave);
                return;
            case PortraitAction.Rename:
                OpenRename();
                return;
            case PortraitAction.SheetClose:
                CloseSheet();
                break;
            case PortraitAction.Back:
                Back();
                break;
            default:
                Execute(w);
                break;
        }
        var after = _vm.Hub.Log.Count > 0 ? _vm.Hub.Log[^1].Text : "";
        if (after.Length > 0 && after != before)
            SetNotice(after);
        QueueRedraw();
    }

    private void Execute(PortraitWidget w)
    {
        if (ExecuteTerritory(w) || ExecuteInteraction(w) || ExecuteCharacter(w) || ExecuteSchedule(w)
            || ExecuteStore(w) || ExecutePages(w) || ExecuteDevelopment(w))
            return;
        switch (w.Action)
        {
            case PortraitAction.SkillSector:
            case PortraitAction.SkillNode:
            case PortraitAction.SkillReset:
            case PortraitAction.SkillPrevious:
            case PortraitAction.SkillNext:
                ExecuteSkillWidget(w);
                break;
        }
    }

    /// <summary>抽屉的收起：交互抽屉逐级退（赠礼 → 类别 → 关闭），存取抽屉关设施，其余直接收。</summary>
    private void CloseSheet()
    {
        if (_vm.StorageOpen)
            _vm.Hub.CloseStorage();
        else if (_interactionOpen)
            InteractionBack();
        else
            _sheet = SheetKind.None;
    }

    /// <summary>顶栏返回：星盘回角色详情，其余推入页回根页签。</summary>
    private void Back()
    {
        ResetSkillView();
        if (_push == PushPage.Disc)
        {
            _push = PushPage.Character;
            return;
        }
        if (_push == PushPage.Build)
            _developmentCell = _developmentFacility = _developmentRoom = _developmentPlacing = -1;
        _push = PushPage.None;
        _sheet = SheetKind.None;
        if (_tab == 3 && _storeMode == 1)
            _vm.Hub.OpenTrade();
    }

    private void SetNotice(string text)
    {
        _notice = text;
        _noticeAge = 0f;
    }

    private void Notice() =>
        SetNotice(_vm.Hub.Log.Count > 0 ? _vm.Hub.Log[^1].Text : "");

    /// <summary>领地改名：原生输入框弹窗（软键盘中文输入法只认 LineEdit）。</summary>
    private void OpenRename()
    {
        var page = InkModalFactory.CreateInputQuestion("领地命名", "", _vm.TerritoryName, 8, name =>
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0)
                SetNotice("名字不能为空。");
            else
            {
                _vm.Hub.State.Territory.Name = trimmed;
                SetNotice($"领地改名为「{trimmed}」。");
            }
            QueueRedraw();
        });
        page.Choices.Insert(0, new InkModalChoice { Id = "cancel", Label = "取消", OnSelected = () => { } });
        ModalWanted!(page);
    }

    /// <summary>注册逆序命中：后注册的画在上层，先命中。</summary>
    private (int Index, PortraitWidget Widget)? Hit(Vector2 at)
    {
        for (var i = _widgets.Count - 1; i >= 0; i--)
            if (_widgets[i].Contains(at))
                return (i, _widgets[i]);
        return null;
    }

    private static IReadOnlyList<Vector2> RectLoop(Rect2 r) => new[]
    {
        r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), r.Position,
    };

    /// <summary>虚线矩形。</summary>
    private void DashedLoop(Rect2 r, Color color)
    {
        var corners = new[]
        {
            r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), r.Position,
        };
        for (var i = 0; i < 4; i++)
            InkDraw.Dashed(this, corners[i], corners[i + 1], color, PortraitLayout.LineHair - 2f, dash: 22f, gap: 16f);
    }
}
