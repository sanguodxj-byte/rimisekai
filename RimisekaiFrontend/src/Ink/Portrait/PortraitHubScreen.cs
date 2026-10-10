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
    ChatterAdvance,
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
    DevelopmentDoor,
    RoomDemolish,
    RoomLock,
    NowAvatar,
    NowPage,
    WorldZoomIn,
    WorldZoomOut,
    WorldHome,
    WorldGo,
    WorldStep,
    CharacterSegment,
    SkillCard,
    TraitInfo,
    EquipInfo,
    EquipSlotPick,
    EquipOption,
    EquipRemove,
    OpenDisc,
    CodexOpen,
    CodexEntry,
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
    CrossGate,
    OpenPortraitPicker,
    PickPortraitDiff,
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
        Equip,
        Codex,
    }

    private enum SheetKind
    {
        None,
        Room,
        Party,
        Slot,
        Item,
        World,
        PortraitPicker,
    }

    private readonly List<PortraitWidget> _widgets = new();
    private InkViewModel _vm = null!;
    private string _notice = "";
    private float _noticeAge;
    private (int, PushPage) _noticeView;
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
        PortraitAvatars.Bind(vm.Hub.State.Roster);
        _tab = 0;
        _push = PushPage.None;
        _sheet = SheetKind.None;
        _notice = "";
        _interactionOpen = _giftOpen = _observing = false;
        _socialCategory = -1;
        _sheetRoom = -1;
        _nowPage = 0;
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
        SetProcess(true);
    }

    /// <summary>切到某个根页签：收起推入页与抽屉，离开交易段即散集。</summary>
    public void ShowTab(int i)
    {
        LeaveTradeIfOpen();
        CloseTransient();
        _push = PushPage.None;
        if (i != _tab)
            StartTabSlide(_tab);
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
        _crossArrowShown = false;
        // 日志、对白等折行时人名不从中间断开。
        InkDraw.SetUnbreakable(_vm.Hub.State.Roster.Members.Select(m => m.Name));
        // 触摸没有悬停：按下当场把那一块画成选中态，松开才派发（之后浅填淡出）。
        ApplyPress();
        PortraitFrame.Backdrop(this);

        if (_push != PushPage.None)
            DrawPushLayer();
        else if (ConversationActive)
        {
            _pushShown = PushPage.None;
            _sheetWasOpen = false;
            DrawScene();
            DrawToast();
            return;
        }
        else
        {
            _pushShown = PushPage.None;
            DrawRootTab();
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
        else if (_sheet == SheetKind.World)
            OpenSheetLayer(DrawWorldSheet);
        else if (_sheet == SheetKind.PortraitPicker)
            OpenSheetLayer(DrawPortraitPickerSheet);
        else
            _sheetWasOpen = false;

        DrawToast();
    }

    /// <summary>根页签：内容＋HUD＋五页签。</summary>
    private void DrawRootTab()
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

    private void DrawPushed(PushPage page)
    {
        switch (page)
        {
            case PushPage.Character: DrawCharacterPage(); break;
            case PushPage.Disc: DrawSkillPage(); break;
            case PushPage.Build: DrawDevelopment(); break;
            case PushPage.Equip: DrawEquipmentPage(); break;
            case PushPage.Codex: DrawCodexPage(); break;
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
            PortraitFrame.PressMark(this, back.Grow(-8f));
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
                PortraitFrame.PressMark(this, r.Grow(-8f));
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
            PortraitFrame.PressMark(this, place);
        var title = header.Place;
        var money = items[3].Value;
        var moneyWidth = InkDraw.Measure(money, PortraitLayout.FontMeta).X + 120f;
        var titleMax = PortraitLayout.CanvasWidth - PortraitLayout.Pad * 2f - moneyWidth - 90f;
        var titleSize = InkDraw.FitSize(title, titleMax, PortraitLayout.FontPlace, PortraitLayout.FontMeta);
        var shown = InkDraw.Ellipsize(title, titleMax, titleSize);
        InkDraw.Text(this, new Vector2(place.Position.X + 20f, PortraitLayout.HudLine1), shown, titleSize, InkStyle.Line, "lm");
        if (_vm.CanRenameTerritory)
        {
            // 2026-10-10 主人定：地名右侧的笔形小图标去掉；点地名照样改名。
            _widgets.Add(new PortraitWidget(new Rect2(place.Position, new Vector2(
                    Mathf.Min(place.Size.X, InkDraw.Measure(shown, titleSize).X + 100f), place.Size.Y)),
                PortraitAction.Rename, 0, true, "改名"));
        }

        var pill = new Rect2(PortraitLayout.CanvasWidth - PortraitLayout.Pad - moneyWidth, PortraitLayout.HudLine1 - 38f,
            moneyWidth, 76f);
        PortraitFrame.RoundRect(this, pill, 38f, new Color(InkStyle.Inset, 0.85f), InkStyle.Dim, 3f);
        // 金币＋数字作为一组在签内水平居中（2026-10-10 主人定）。
        const float coinR = 20f, coinGap = 14f;
        var groupW = coinR * 2f + coinGap + InkDraw.Measure(money, PortraitLayout.FontMeta).X;
        var gx0 = pill.GetCenter().X - groupW / 2f;
        PortraitGlyph.Coin(this, gx0 + coinR, pill.GetCenter().Y, coinR, InkStyle.Line);
        InkDraw.Text(this, new Vector2(gx0 + coinR * 2f + coinGap, pill.GetCenter().Y), money, PortraitLayout.FontMeta, InkStyle.Line, "lm");

        // 第二行＝一条状态缎带：季节日子 · 天气 · 时刻三段，段与段之间等距、正中各一枚小菱隔开；
        // 段宽按内容，剩下的空隙均分，所以无论字长短，三段的间距都一样齐。时刻是此刻最常看的，用亮字。
        var y = PortraitLayout.HudLine2;
        var sysLeft = PortraitLayout.HudSystem.Position.X;
        var left = PortraitLayout.Pad + 20f;
        var right = sysLeft - 28f;
        var segments = new (System.Action<float> Glyph, string Text, Color Color)[]
        {
            (gx => PortraitGlyph.Leaf(this, gx, y, 18f, InkStyle.Dim),
                $"{items[0].Value} 第{_vm.Hub.State.Clock.Week}日", InkStyle.Dim),
            (gx => PortraitGlyph.Weather(this, header.Weather, gx, y, 18f, InkStyle.Dim), items[1].Value, InkStyle.Dim),
            (gx => PortraitGlyph.Clock(this, gx, y, 18f, InkStyle.Line), items[2].Value, InkStyle.Line),
        };
        const float glyphSpan = 36f + 14f;
        var widths = new float[segments.Length];
        var used = 0f;
        for (var i = 0; i < segments.Length; i++)
        {
            widths[i] = glyphSpan + InkDraw.Measure(segments[i].Text, PortraitLayout.FontMeta).X;
            used += widths[i];
        }
        // 2026-10-10 主人定：状态行对称——中段（天气）正对画面中线、与上下两道缎带线的菱形对齐；
        // 首段贴左、末段贴系统钮，两枚隔菱各落在相邻两段空隙的正中。
        var xs = new float[segments.Length];
        xs[0] = left;
        xs[^1] = right - widths[^1];
        if (segments.Length == 3)
            xs[1] = PortraitLayout.CanvasWidth / 2f - widths[1] / 2f;
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i].Glyph(xs[i] + 18f);
            InkDraw.Text(this, new Vector2(xs[i] + glyphSpan, y), segments[i].Text, PortraitLayout.FontMeta, segments[i].Color, "lm");
            if (i < segments.Length - 1)
                InkDraw.Jewel(this, new Vector2((xs[i] + widths[i] + xs[i + 1]) / 2f, y), 5f, new Color(InkStyle.Dim, 0.9f));
        }
        // 缎带上沿一道淡出细线，把状态行和地名行分开。
        // 两端对称，菱形落在画面正中（2026-10-10 主人定）。
        PortraitFrame.FadingRule(this, PortraitLayout.Pad, PortraitLayout.CanvasWidth - PortraitLayout.Pad, y - 40f);

        var sys = PortraitLayout.HudSystem;
        if (PortraitFrame.IsPressed(sys))
            PortraitFrame.PressMark(this, sys.Grow(-10f));
        PortraitGlyph.Gear(this, sys.GetCenter().X, sys.GetCenter().Y, 26f, InkStyle.Line);
        _widgets.Add(new PortraitWidget(sys, PortraitAction.OpenSystem, 0, true, "系统"));

        DrawRect(new Rect2(0, hud.End.Y - 4f, PortraitLayout.CanvasWidth, 3f), InkStyle.Dim);
        PortraitFrame.FadingRule(this, 0f, PortraitLayout.CanvasWidth, hud.End.Y - 2f);
    }

    /// <summary>
    /// 底部五页签＝一条雕花檐壁：上沿双线，五个页签各占一龛（龛与龛之间一根细柱、柱头一枚小菱），
    /// 当前页签＝一扇骨白实心尖拱窗托住图标（图标反黑），切页签时新龛的窗自下升起、旧龛的窗淡去（不做横向滑动指示条）。
    /// </summary>
    private void DrawTabBar()
    {
        var bar = PortraitLayout.TabBar;
        DrawRect(new Rect2(0, bar.Position.Y, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - bar.Position.Y), InkStyle.Bg);
        var y0 = bar.Position.Y;
        DrawLine(new Vector2(0, y0), new Vector2(PortraitLayout.CanvasWidth, y0), InkStyle.Dim, 3f, true);
        DrawLine(new Vector2(0, y0 + 10f), new Vector2(PortraitLayout.CanvasWidth, y0 + 10f), new Color(InkStyle.WoodDark, 0.9f), 2f, true);
        var cy = PortraitLayout.Tab(0).Position.Y + 62f;
        // 龛：每个页签一道压暗尖拱轮廓（自檐下垂到字下），柱：页签缝上一根细柱＋柱头菱
        for (var i = 0; i < PortraitLayout.TabCount; i++)
        {
            var r = PortraitLayout.Tab(i);
            if (i > 0)
            {
                var x = r.Position.X;
                DrawLine(new Vector2(x, y0 + 30f), new Vector2(x, bar.End.Y - 30f), new Color(InkStyle.WoodDark, 0.8f), 2f, true);
                InkDraw.Jewel(this, new Vector2(x, y0 + 30f), 6f, InkStyle.WoodDark);
            }
            var cx = r.GetCenter().X;
            if (i != _tab && PortraitFrame.IsPressed(r))
                PortraitFrame.PressMark(this, new Rect2(cx - 70f, cy - 64f, 140f, 110f));
        }
        // 当前页签的尖拱窗：不横向滑动（安卓式指示条），切换时新龛里的窗自檐下升起、旧龛的窗淡去。
        var rise = _tabMotion.Running ? _tabMotion.Eased : 1f;
        var full = new Rect2(PortraitLayout.Tab(_tab).GetCenter().X - 64f, cy - 62f, 128f, 102f);
        if (_tabMotion.Running && _tabFrom != _tab)
        {
            var old = new Rect2(PortraitLayout.Tab(_tabFrom).GetCenter().X - 64f, cy - 62f, 128f, 102f);
            PortraitFrame.Arch(this, old, 46f, new Color(InkStyle.Line, 1f - rise));
        }
        var h = Mathf.Max(1f, full.Size.Y * rise);
        var win = new Rect2(full.Position.X, full.End.Y - h, full.Size.X, h);
        PortraitFrame.Arch(this, win, Mathf.Min(46f, h), InkStyle.Line);
        if (rise >= 1f)
        {
            PortraitFrame.Arch(this, full.Grow(8f), 52f, null, new Color(InkStyle.Line, 0.55f), 2f);
            InkDraw.Jewel(this, new Vector2(full.GetCenter().X, full.Position.Y - 22f), 6f, InkStyle.Line);
        }
        for (var i = 0; i < PortraitLayout.TabCount; i++)
        {
            var r = PortraitLayout.Tab(i);
            var on = i == _tab;
            var cx = r.GetCenter().X;
            var covered = on ? rise >= 0.6f : _tabMotion.Running && i == _tabFrom && rise < 0.4f;
            PortraitGlyph.TabIcons[i](this, cx, cy + 6f, 26f, covered ? InkStyle.Bg : InkStyle.Dim);
            InkDraw.Text(this, new Vector2(cx, cy + 82f), PortraitLayout.TabLabels[i], PortraitLayout.FontMeta,
                on ? InkStyle.Line : InkStyle.Dim, "cm");
            _widgets.Add(new PortraitWidget(r, PortraitAction.Tab, i, true, PortraitLayout.TabLabels[i]));
        }
    }

    /// <summary>
    /// 操作反馈：在底部弹一枚 3 秒的浅填签，不拦输入。只弹没进日志的话（「已保存。」「这里去不了。」之类）——
    /// 进了日志的反馈由日志面板 / 日志页签显示，一律不弹。签只属于弹出它的那一页：换页签、推入或返回即撤掉。
    /// 字不截断不缩：一行放不下就按字宽换行，签往上长。
    /// </summary>
    private void DrawToast()
    {
        if (_notice.Length > 0 && NoticeView != _noticeView)
            _notice = "";
        if (_notice.Length == 0 || _noticeAge > 3f || (_veil?.Running ?? false))
            return;
        var bottom = _sheetTop >= 0f ? _sheetTop - 30f
            : _push == PushPage.None ? PortraitLayout.TabTop - 24f : PortraitLayout.CanvasHeight - 80f;
        var lines = InkDraw.WrapLines(_notice, PortraitLayout.FullWidth - 96f, PortraitLayout.FontMeta);
        var widest = lines.Max(l => InkDraw.Measure(l, PortraitLayout.FontMeta).X);
        var width = Mathf.Min(PortraitLayout.FullWidth, widest + 96f);
        var height = 48f + lines.Count * PortraitLayout.ToastLine;
        var r = new Rect2((PortraitLayout.CanvasWidth - width) / 2f, bottom - height, width, height);
        // 倒角签（不用圆头药丸）：外线银白、内收一道暗线，两端各一粒小菱。
        PortraitFrame.Bevel(this, r, 40f, new Color(InkStyle.Hover, 0.97f), InkStyle.Line, 3f);
        PortraitFrame.Bevel(this, r.Grow(-9f), 26f, null, new Color(InkStyle.WoodDark, 0.9f), 2f);
        foreach (var side in new[] { r.Position.X, r.End.X })
            InkDraw.Jewel(this, new Vector2(side, r.GetCenter().Y), 9f, InkStyle.Line);
        for (var i = 0; i < lines.Count; i++)
            InkDraw.Text(this, new Vector2(r.GetCenter().X, r.Position.Y + 24f + (i + 0.5f) * PortraitLayout.ToastLine), lines[i],
                PortraitLayout.FontMeta, InkStyle.Line, "cm");
    }

    /// <summary>提示签所属的页：根页签＋推入页。</summary>
    private (int, PushPage) NoticeView => (_tab, _push);

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
        if (InputLocked)
        {
            _pressed = _dragging = false;
            _pressRect = null;
            _pressWidget = null;
            ResetListDrag();
            return;
        }
        if (HandleWorldInput(e) || HandleSkillInput(e) || HandleListInput(e))
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
            _holdFired = false;
            _holdAge = 0f;
            _pressed = true;
            _dragging = false;
            _pressPos = mb.Position;
            _pressWidget = Hit(mb.Position)?.Widget;
            _pressRect = _pressWidget?.Rect;
            _pressHeld = 0f;
            QueueRedraw();
            return;
        }

        if (!_pressed)
            return;
        _pressed = false;
        _pressRect = null;
        _pressWidget = null;
        if (_dragging || _holdFired)
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
        Flash(w.Rect);
        _vm.Hub.BeginOperation();
        switch (w.Action)
        {
            case PortraitAction.Tab:
                ShowTab(w.Index);
                return;
            case PortraitAction.CodexOpen:
                OpenCodexPage();
                return;
            case PortraitAction.OpenSystem:
                OpenSystemPage(InkSystemScreen.PageSave);
                return;
            case PortraitAction.Rename:
                OpenRename();
                return;
            case PortraitAction.SheetClose:
                RequestSheetClose();
                break;
            case PortraitAction.Back:
                RequestBack();
                break;
            default:
                Execute(w);
                break;
        }
        QueueRedraw();
    }

    private void Execute(PortraitWidget w)
    {
        if (ExecuteWorld(w) || ExecuteTerritory(w) || ExecuteInteraction(w) || ExecuteEquipment(w) || ExecuteCharacter(w) || ExecuteSchedule(w)
            || ExecuteStore(w) || ExecutePages(w) || ExecuteDevelopment(w) || ExecuteCodex(w))
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
        if (_push is PushPage.Disc or PushPage.Equip)
        {
            _push = PushPage.Character;
            return;
        }
        // 图鉴从系统页「设置」段进来，返回回到那里。
        if (_push == PushPage.Codex)
        {
            _push = PushPage.System;
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
        _noticeView = NoticeView;
    }

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
