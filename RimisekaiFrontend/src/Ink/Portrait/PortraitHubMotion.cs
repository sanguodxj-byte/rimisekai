using System;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 据点界面的动效：抽屉滑入 / 收起（压暗同步淡入淡出）、推入页自右滑入 / 返回滑出、
/// 页签尖拱窗原位升起、松手后按压浅填淡出、领地格长按。
///
/// 一律 0.22 秒三次缓出，只在过渡进行中逐帧重画（对话打字机、提示签计时、星盘视角也在这里推进）。命中块始终按终态布局登记，
/// 抽屉与推入页过渡期间不收输入（<see cref="InputLocked"/>）。收起与返回是「先演后改」：
/// 动画走完才真正改状态，抽屉 / 页面内容在滑出过程中仍按原状态画。
/// </summary>
public partial class PortraitHubScreen
{
    private readonly PortraitTransition _sheetMotion = new();
    private readonly PortraitTransition _pushMotion = new();
    private readonly PortraitTransition _tabMotion = new();
    private readonly PortraitTransition _flashMotion = new(PortraitMotion.PressFade);

    private bool _sheetWasOpen;
    private bool _sheetClosing;
    private float _lastSheetTop;

    private PushPage _pushShown;
    private PushPage _pushUnder;
    private bool _popping;


    private Rect2? _flashRect;

    private PortraitWidget? _pressWidget;
    private float _pressHeld;

    /// <summary>抽屉或推入页正在过渡：不收输入，免得点到半路上的东西。</summary>
    private bool InputLocked => _sheetMotion.Running || _pushMotion.Running;

    /// <summary>核对用：是否有过渡在走。</summary>
    public bool DebugAnimating => _sheetMotion.Running || _pushMotion.Running || _tabMotion.Running;

    public override void _Process(double delta)
    {
        var d = (float)delta;
        var redraw = false;
        // 对话打字机逐字显现。
        if (Visible && ConversationActive && _conversationReveal < _conversationText.Length)
        {
            _conversationReveal = Math.Min(_conversationText.Length, _conversationReveal + d * 30f);
            redraw = true;
        }
        HoldTick(delta);
        // 提示签 3 秒后淡出。
        if (_notice.Length > 0 && _noticeAge <= 3f)
        {
            _noticeAge += d;
            redraw |= _noticeAge > 3f;
        }
        // 技能星盘视角动效。
        redraw |= _push == PushPage.Disc && AdvanceSkillView(delta);

        redraw |= _sheetMotion.Step(d);
        if (_sheetClosing && !_sheetMotion.Running)
        {
            _sheetClosing = false;
            CloseSheet();
            redraw = true;
        }

        redraw |= _pushMotion.Step(d);
        if (_popping && !_pushMotion.Running)
        {
            _popping = false;
            Back();
            _pushShown = _push;
            redraw = true;
        }

        redraw |= _tabMotion.Step(d);
        redraw |= _flashMotion.Step(d);
        redraw |= StepLongPress(d);
        if (redraw)
            QueueRedraw();
    }

    // ---------- 按压 ----------

    /// <summary>本帧的按压态：按住＝满档浅填；松手后同一块浅填淡出。</summary>
    private void ApplyPress()
    {
        if (_pressed && !_dragging)
            PortraitFrame.SetPress(_pressRect);
        else if (_flashMotion.Running && _flashRect != null)
            PortraitFrame.SetPress(_flashRect, 1f - _flashMotion.Eased);
        else
            PortraitFrame.SetPress(null);
    }

    private void Flash(Rect2 rect)
    {
        _flashRect = rect;
        _flashMotion.Start();
    }

    /// <summary>领地格长按：按住不拖满 0.45 秒即打开该房间的设施抽屉，松手不再派发。</summary>
    private bool StepLongPress(float d)
    {
        if (!_pressed || _dragging || _pressWidget is not { Action: PortraitAction.Cell, Enabled: true } w || WorldLayer)
            return false;
        _pressHeld += d;
        if (_pressHeld < PortraitMotion.LongPress)
            return false;
        _pressed = false;
        _pressRect = null;
        _pressWidget = null;
        OpenRoomSheet(w.Index);
        return true;
    }

    // ---------- 抽屉 ----------

    /// <summary>
    /// 抽屉层：下层画面已画完，这里把它的命中块与滚动区整个清掉（只剩压暗的背景），
    /// 再由 draw 画面板并注册抽屉自己的命中块；draw 返回面板上沿。
    /// 刚打开时面板自下滑入、压暗淡入；收起时反向。抽屉之间互换（设施→交互）不再演一遍。
    /// 抽屉之上的压暗区是一整块「收起」命中块。
    /// </summary>
    private void OpenSheetLayer(Func<float> draw)
    {
        if (!_sheetWasOpen)
            _sheetMotion.Start();
        _sheetWasOpen = true;
        _widgets.Clear();
        _scrollAreas.Clear();
        var shown = _sheetClosing ? 1f - _sheetMotion.Eased : _sheetMotion.Eased;
        DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight),
            new Color(InkStyle.Bg, PortraitFrame.ScrimAlpha * shown));
        var travel = PortraitLayout.CanvasHeight - _lastSheetTop;
        PortraitFrame.SetLayer(this, new Vector2(0f, (1f - shown) * travel));
        _sheetTop = draw();
        PortraitFrame.SetLayer(this, Vector2.Zero);
        _lastSheetTop = _sheetTop;
        _widgets.Insert(0, new PortraitWidget(new Rect2(0, 0, PortraitLayout.CanvasWidth, _sheetTop),
            PortraitAction.SheetClose, 0, true, "收起"));
    }

    /// <summary>这一次「收起」会不会把抽屉层整个关掉（而不是交互抽屉退一级、或露出下面另一张抽屉）。</summary>
    private bool SheetCloseEndsLayer()
    {
        if (_vm.StorageOpen)
            return !_interactionOpen && _sheet == SheetKind.None;
        if (_interactionOpen)
            return !_giftOpen && _socialCategory < 0 && _sheet == SheetKind.None;
        return true;
    }

    /// <summary>收起：整层关掉时先演滑出，演完再改状态；退一级则当场改。</summary>
    private void RequestSheetClose()
    {
        if (PortraitMotion.Instant || !SheetCloseEndsLayer())
        {
            CloseSheet();
            return;
        }
        _sheetClosing = true;
        _sheetMotion.Start();
    }

    // ---------- 推入页 ----------

    private static int Depth(PushPage page) => page switch
    {
        PushPage.None => 0,
        PushPage.Disc => 2,
        _ => 1,
    };

    /// <summary>返回：先让当前页向右滑出（下层页面跟着从左归位），演完再真正退。</summary>
    private void RequestBack()
    {
        if (PortraitMotion.Instant)
        {
            Back();
            return;
        }
        _pushUnder = _push == PushPage.Disc ? PushPage.Character : PushPage.None;
        _popping = true;
        _pushMotion.Start();
    }

    /// <summary>
    /// 推入页：新页比上一帧更深时自右滑入，下层页面向左退三成并压暗；
    /// 返回时反向。过渡结束后只画当前页。
    /// </summary>
    private void DrawPushLayer()
    {
        if (!_popping && Depth(_push) > Depth(_pushShown))
        {
            _pushUnder = _pushShown;
            _pushMotion.Start();
        }
        _pushShown = _push;
        if (!_pushMotion.Running)
        {
            DrawPushed(_push);
            return;
        }
        // offset：当前页右移的比例（1＝完全在屏外）。
        var offset = _popping ? _pushMotion.Eased : 1f - _pushMotion.Eased;
        var width = PortraitLayout.CanvasWidth;
        PortraitFrame.SetLayer(this, new Vector2(-0.3f * width * (1f - offset), 0f));
        if (_pushUnder == PushPage.None)
            DrawRootTab();
        else
            DrawPushed(_pushUnder);
        PortraitFrame.SetLayer(this, Vector2.Zero);
        DrawRect(new Rect2(0, 0, width, PortraitLayout.CanvasHeight), new Color(InkStyle.Bg, 0.5f * (1f - offset)));
        _widgets.Clear();
        _scrollAreas.Clear();
        PortraitFrame.SetLayer(this, new Vector2(width * offset, 0f));
        PortraitFrame.Backdrop(this);
        DrawPushed(_push);
        InkDraw.InkLine(this, Vector2.Zero, new Vector2(0f, PortraitLayout.CanvasHeight), InkStyle.WoodDark, 3f);
        PortraitFrame.SetLayer(this, Vector2.Zero);
    }

    // ---------- 页签药丸 ----------

    private int _tabFrom;

    /// <summary>切页签：记下旧页签，新龛的尖拱窗自下升起、旧龛的窗淡去。</summary>
    private void StartTabSlide(int from)
    {
        _tabFrom = from;
        _tabMotion.Start();
    }
}
