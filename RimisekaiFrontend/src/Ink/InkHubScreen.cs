using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;

namespace Rimisekai.Ink;

/// <summary>
/// 据点主界面。只做三件事：组装模型、把输入分派给 HubSession、按模型绘制。
/// 版式在 InkLayout，内容在 InkHubModel，画法在各 Renderer，本类不承载其中任何一项。
/// </summary>
public partial class InkHubScreen : Control
{
    private InkViewModel? _vm;
    private InkHubModel? _model;

    /// <summary>天气特效层：盖在地图面板上，只在纯地图视图出现。</summary>
    private readonly InkWeatherFx _weatherFx = new();
    private readonly InkCombatFxLayer _combatFx = new();

    /// <summary>界面自身的视图状态（分页、打开的页面、改名输入等），不进 Core。</summary>
    private readonly InkUiState _ui = new();

    /// <summary>请求打开系统页（settings/save/load），路由监听。</summary>
    public event Action<string>? SystemRequested;

    /// <summary>请求打开任务页，路由监听。</summary>
    public event Action? QuestRequested;

    /// <summary>退出战斗，通知路由回到据点阶段（结算由路由在此时机落账）。</summary>
    public event Action? CombatExited;

    private bool _isExitingCombat;
    private bool _combatSettled;
    private float _visualAwakeningRatio;
    private int _lastEventCount;
    private float _combatTurnCooldown;
    private float _modalAnimTime;

    /// <summary>通用居中弹窗会话，供外部或各系统直接压入弹窗。</summary>
    public InkModalSession ModalSession => _ui.ModalSession ??= new InkModalSession();

    /// <summary>弹出一个单页弹窗。</summary>
    public void ShowModal(InkModalPage page)
    {
        ModalSession.Enqueue(page);
        Refresh();
    }

    /// <summary>弹出连续多页弹窗序列。</summary>
    public void ShowModals(IEnumerable<InkModalPage> pages)
    {
        ModalSession.EnqueueRange(pages);
        Refresh();
    }

    public void ExitCombat()
    {
        if (_vm == null || _isExitingCombat)
            return;
        // 彻底杜绝瞬移跳变：平滑动画逆向回放（下沉面板平滑浮回、消融层逆向显影）
        _isExitingCombat = true;
        _combatTurnCooldown = 0f;
        QueueRedraw();
    }

    /// <summary>
    /// 战斗在战斗场景末尾触发结算：汇总战绩与掉落，落账到名册与背包，自动在战斗场景中弹出战后结算弹窗。
    /// 玩家点击推进结算弹窗后，才平滑退出战斗返回据点。
    /// </summary>
    public void TriggerCombatSettlement()
    {
        if (_vm?.Combat == null)
            return;

        var battle = _vm.Combat.Battle;
        var state = _vm.Hub.State;
        var questRun = _vm.Combat.QuestRun;
        var outcome = CombatSettlement.Settle(state, battle, questRun);
        if (outcome != null)
        {
            var modal = InkModalFactory.CreateCombatSettlement(
                outcome.Result,
                outcome.Loot,
                onFinished: () =>
                {
                    ExitCombat(); // 玩家点击结算弹窗推进关闭后，平滑退出战斗返回据点！
                });
            ShowModal(modal);
        }
    }

    public void Bind(InkViewModel vm)
    {
        _vm = vm;
        _ui.CardPage = 0;
        _ui.OpenPage = InkPage.None;
        ResetPageQuery();
        _ui.PageSelected = -1;
        _ui.Renaming = false;
        _ui.RenameText = "";
        _ui.Notice = "";
        _combatSettled = false;
        Refresh();
    }

    /// <summary>打开一个页面，并把该页的查询与选中状态复位。</summary>
    private void OpenPage(InkPage page)
    {
        _ui.OpenPage = page;
        _ui.PageSelected = -1;
        _ui.SkillSelectedId = "";
        _ui.SkillFocusedSector = -1;
        _ui.SkillDiscZoom = 1.0f;
        _ui.SkillDiscPivot = InkLayout.SkillDiscCenter;
        _ui.SkillDiscRotation = 0.0f;
        // 交易页进出即开集/散集：页内才能买卖（Core 的 AtMarket 闸门）。
        // 从页签切到交易页也要开集，否则成交全被 Core 拒掉。
        if (_vm != null)
        {
            if (page == InkPage.Trade)
                _vm.Hub.OpenTrade();
            else
                _vm.Hub.LeaveMarket();
        }
        ResetPageQuery();
        _ui.Notice = "";
    }

    /// <summary>把列表的搜索/筛选/排序恢复默认。打开或换页时调用。</summary>
    private void ResetPageQuery()
    {
        _ui.PageSearch = "";
        _ui.SearchFocus = false;
        _ui.PageFilter = 0;
        _ui.PageSort = 0;
        _ui.PageSortDesc = false;
        _ui.PageFirst = 0;
        _ui.TradeSelectedHeld = -1;
        _ui.TradeSelectedMarket = -1;
        _ui.TradeHeldFirst = 0;
        _ui.TradeMarketFirst = 0;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // 尺寸与位置由 project.godot 的 stretch 统一处理，这里不设锚点。
        AddChild(_weatherFx);
        AddChild(_combatFx);
    }

    /// <summary>
    /// 打字机推进。只有对话层有文字、且当前句还没显示完时才逐帧补字，
    /// 其余时间什么都不做，不产生无谓重绘。
    /// </summary>
    public override void _Process(double delta)
    {
        // 全局动态进度条（血量、经验等）：驱动绿延白进、暗红消减、平滑生长
        InkDynamicMeter.Update((float)delta);
        if (InkDynamicMeter.HasActiveAnimations)
            QueueRedraw();

        // 通用居中弹窗：驱动底部向下的实心三角箭头呼吸动效
        if (_ui.ModalSession?.IsActive == true)
        {
            _modalAnimTime += (float)delta;
            QueueRedraw();
        }

        // 战斗形态双向平滑动画与位移驱动（全量消除跳变）：
        if (_vm?.Combat != null)
        {
            if (!_isExitingCombat && _vm.CombatT < 1f)
            {
                _vm.CombatT = Math.Min(1f, _vm.CombatT + (float)delta / 0.45f);
                QueueRedraw();
            }
            else if (_isExitingCombat)
            {
                _vm.CombatT = Math.Max(0f, _vm.CombatT - (float)delta / 0.35f);
                QueueRedraw();
                if (_vm.CombatT <= 0f)
                {
                    _vm.Combat = null;
                    _isExitingCombat = false;
                    _ui.SelectedTargetId = -1;
                    _ui.SelectedTargetColumn = 0;
                    InkCombatFx.Clear();
                    _lastEventCount = 0;
                    Refresh();
                    CombatExited?.Invoke();
                }
            }
            else
            {
                // 累计头顶大钝角指示三角形的缓慢呼吸时间戳
                InkCombatRenderer.IndicatorTime += (float)delta;

                // 更新攻击光效、受击震颤与伤害飘字
                InkCombatFx.Update((float)delta);

                // 战斗时间轴自动推进与队友/敌人回合驱动：
                var battle = _vm.Combat.Battle;
                if (battle.Outcome != Rimisekai.Combat.CombatOutcome.Ongoing)
                {
                    // 主人定：击杀完敌人自动在战斗场景末尾弹出战后结算。
                    // 必须等待死亡切片消散动画播放完毕后，自动弹出战后结算单！
                    if (!InkCombatFx.HasActiveDeathAnimations && !_combatSettled && !ModalSession.IsActive)
                    {
                        _combatSettled = true;
                        TriggerCombatSettlement();
                    }
                }
                else if (battle.BattleBegan)
                {
                    // 冷却倒计时每帧无条件递减，杜绝因动画状态导致计时停滞
                    if (_combatTurnCooldown > 0f)
                    {
                        _combatTurnCooldown -= (float)delta;
                    }

                    // 死亡切片动画播放期间，仅锁定背景敌方 AI 自动回合的时钟步进，但不锁死冷却倒计时
                    if (InkCombatFx.HasActiveDeathAnimations)
                    {
                        QueueRedraw();
                        return;
                    }

                    if (_combatTurnCooldown <= 0f)
                    {
                        // 驱动单步时钟流转：StepTurn 负责将时间轴平滑前推到下一个事件点。
                        // 若是敌方或自动战斗我方出手、法术施放、回合结算，返回 true 并产生事件流水；
                        // 若到达我方非自动回合（actedUnit 属于控制方），StepTurn 对齐时钟到该单位出手点并返回 false 等待玩家决策！
                        if (battle.StepTurn(out var actedUnit))
                        {
                            ConsumeBattleEvents();
                            _combatTurnCooldown = 0.35f;
                            Refresh();
                        }
                        else if (actedUnit != null && actedUnit.Side == battle.ControlledSide && !battle.AutoBattle)
                        {
                            // 时间已平滑流转到我方单位的回合（actedUnit）！等待玩家操作
                        }
                    }
                }

                // 战斗期间持续驱动指示三角形呼吸、跑条滑行、血条缩减、头像位移以及全套攻击/受击特效
                QueueRedraw();
            }
        }

        // 按住推进区：压满阈值触发整段快进（跳过本句已在按下时处理）。
        if (_advancePressing)
        {
            _advanceHold += delta;
            if (!_advanceHoldFired && _advanceHold >= AdvanceHoldSeconds)
            {
                _advanceHoldFired = true;
                DrainOverlayParagraph();
            }
        }

        // 技能盘扇区聚焦平滑动画：视角移动、缩放与对齐旋转
        if (_ui.OpenPage == InkPage.Skills)
        {
            var isFocused = _ui.SkillFocusedSector >= 0;
            var targetZoom = isFocused ? InkLayout.SkillDiscFocusedZoom : 1.0f;
            var targetOrigin = isFocused ? InkLayout.SkillDiscPanelCornerBL : InkLayout.SkillDiscCenter;
            var targetRotation = isFocused ? InkLayout.SkillDiscSectorRotation(_ui.SkillFocusedSector) : 0.0f;

            var zoomDiff = Math.Abs(_ui.SkillDiscZoom - targetZoom);
            var pivotDistSq = _ui.SkillDiscPivot.DistanceSquaredTo(targetOrigin);
            var rotDiff = Math.Abs(Mathf.AngleDifference(_ui.SkillDiscRotation, targetRotation));

            if (zoomDiff > 0.002f || pivotDistSq > 1f || rotDiff > 0.002f)
            {
                var fDelta = (float)delta;
                _ui.SkillDiscZoom = Mathf.Lerp(_ui.SkillDiscZoom, targetZoom, fDelta * 10f);
                _ui.SkillDiscPivot = _ui.SkillDiscPivot.Lerp(targetOrigin, fDelta * 10f);
                _ui.SkillDiscRotation = Mathf.LerpAngle(_ui.SkillDiscRotation, targetRotation, fDelta * 10f);

                if (Math.Abs(_ui.SkillDiscZoom - targetZoom) < 0.002f &&
                    _ui.SkillDiscPivot.DistanceSquaredTo(targetOrigin) < 1f &&
                    Math.Abs(Mathf.AngleDifference(_ui.SkillDiscRotation, targetRotation)) < 0.002f)
                {
                    _ui.SkillDiscZoom = targetZoom;
                    _ui.SkillDiscPivot = targetOrigin;
                    _ui.SkillDiscRotation = targetRotation;
                }
                Refresh();
            }
        }

        // 打字机跟着界面快照走：对话与场景演出共用同一份 OverlayView。
        var overlay = _model?.Overlay;
        if (overlay == null)
            return;

        // 换句了：进度归零，重新开始逐字显示。
        if (_ui.TypeLine != overlay.Text)
        {
            _ui.TypeLine = overlay.Text;
            _ui.TypeRevealed = 0;
        }

        if (_ui.TypeRevealed >= overlay.Text.Length)
            return;

        _typeAccumulator += delta * CharsPerSecond;
        var add = (int)_typeAccumulator;
        if (add <= 0)
            return;

        _typeAccumulator -= add;
        _ui.TypeRevealed = Math.Min(overlay.Text.Length, _ui.TypeRevealed + add);
        Refresh();
    }

    /// <summary>打字机速度，字/秒。</summary>
    private const double CharsPerSecond = 30.0;

    private double _typeAccumulator;

    /// <summary>把当前句直接显示完（点击跳过打字机）。</summary>
    private void SkipTyping()
    {
        var overlay = _model?.Overlay;
        if (overlay == null)
            return;
        _ui.TypeLine = overlay.Text;
        _ui.TypeRevealed = overlay.Text.Length;
        _typeAccumulator = 0;
        Refresh();
    }

    /// <summary>当前句是否已显示完。</summary>
    private bool TypingDone()
    {
        var overlay = _model?.Overlay;
        return overlay == null || _ui.TypeRevealed >= overlay.Text.Length;
    }

    // ---------- 推进区按住/整段快进 ----------

    /// <summary>按住行动面板的整段快进阈值（秒）：压满才触发，点一下只算单击。</summary>
    private const double AdvanceHoldSeconds = 0.4;

    // ---------- 列表滑条 ----------

    /// <summary>正在拖拽的滑块代号（ScrollList 枚举值）；-1 未拖。</summary>
    private int _scrollDragging = -1;

    /// <summary>按下滑块时鼠标 y 与滑块顶的偏移（像素）。</summary>
    private float _scrollGrabOffset;

    /// <summary>本界面里可滚动的列表。代号即 ScrollThumb widget 的 Index。</summary>
    public enum ScrollList
    {
        None = -1,
        Main,      // 库存/交易/制作 左列表
        Storage,   // 设施交互页存储行
        DevFacility, DevRoom, DevAction, ScheduleFacility,
        TradeHeld, TradeMarket,   // 交易页：左栏领地库存 / 右栏市场库存
        StatusAbility,            // 状态页：能力条（展开态）
        ScheduleMember,           // 日程页：左侧成员列表
    }

    private bool _advancePressing;
    private double _advanceHold;
    private bool _advanceHoldFired;

    // ---------- 滑条滚动 ----------

    /// <summary>一个可滚动列表的实时信息。从元素表里的轨道/滑块 widget 反推。</summary>
    private sealed record ScrollGeom(Rect2 Track, Rect2 Thumb, int Total, int Visible)
    {
        public float ThumbRatio => Track.Size.Y <= 0 ? 1f : Mathf.Clamp(Thumb.Size.Y / Track.Size.Y, 0f, 1f);
        public int Max => Mathf.Max(0, Total - Visible);
        public float Ratio => Max <= 0 ? 0f : Mathf.Clamp(First / (float)Max, 0f, 1f);
        public int First { get; init; }
    }

    /// <summary>按代号取列表的滚动几何；该列表当前不存在时返回 null。</summary>
    private ScrollGeom? ScrollGeomOf(int list)
    {
        Rect2? track = null;
        Rect2? thumb = null;
        foreach (var w in _model!.Widgets)
        {
            if (w.Action == InkAction.ScrollJump && w.Index == list) track = w.Rect;
            if (w.Action == InkAction.ScrollThumb && w.Index == list) thumb = w.Rect;
        }
        if (track == null)
            return null;

        int total, visible, first;
        switch ((ScrollList)list)
        {
            case ScrollList.Main:
                total = _model.Page?.Rows.Count ?? 0;
                visible = InkLayout.ListVisibleRows(InkLayout.FullListArea);
                first = _ui.PageFirst;
                break;
            case ScrollList.Storage:
                total = _model.StorageRows.Count;
                visible = InkLayout.FixtureVisibleRows;
                first = _ui.StorageScrollRows;
                break;
            case ScrollList.DevFacility:
                total = _model.Dev?.FacilityRows.Count ?? 0;
                visible = InkLayout.DevFacilityVisibleRows;
                first = _ui.DevFacilityScrollRows;
                break;
            case ScrollList.DevRoom:
                total = _model.Dev?.RoomRows.Count ?? 0;
                visible = InkLayout.DevRoomVisibleRows;
                first = _ui.DevRoomScrollRows;
                break;
            case ScrollList.DevAction:
                total = _model.Dev?.ActionRows.Count ?? 0;
                visible = InkLayout.DevActionVisibleRows;
                first = _ui.DevActionScrollRows;
                break;
            case ScrollList.ScheduleFacility:
                total = _model.Page?.Work?.Facilities.Count ?? 0;
                visible = InkLayout.ScheduleFacilityVisibleRows;
                first = _ui.ScheduleFacilityScrollRows;
                break;
            case ScrollList.TradeHeld:
                total = _model.Page?.Trade?.Held.Count ?? 0;
                visible = InkLayout.TradeVisibleRows(InkLayout.TradePlayerPanel);
                first = _ui.TradeHeldFirst;
                break;
            case ScrollList.TradeMarket:
                total = _model.Page?.Trade?.Market.Count ?? 0;
                visible = InkLayout.TradeVisibleRows(InkLayout.TradeMarketPanel);
                first = _ui.TradeMarketFirst;
                break;
            case ScrollList.StatusAbility:
                total = InkLayout.StatusAbilityBarCount(_model.Page);
                visible = InkLayout.AbilityVisibleBars;
                first = _ui.StatusAbilityScrollRows;
                break;
            case ScrollList.ScheduleMember:
                total = _model.Page?.Work?.Members.Count ?? 0;
                visible = InkLayout.ScheduleMemberVisibleRows;
                first = _ui.ScheduleMemberScrollRows;
                break;
            default:
                return null;
        }
        return new ScrollGeom(track.Value, thumb ?? track.Value, total, visible) { First = first };
    }

    /// <summary>按代号写回滚动首行（夹在合法范围）。</summary>
    private void SetScroll(int list, int first)
    {
        var geom = ScrollGeomOf(list);
        if (geom == null)
            return;
        var clamped = Mathf.Clamp(first, 0, geom.Max);
        switch ((ScrollList)list)
        {
            case ScrollList.Main: _ui.PageFirst = clamped; break;
            case ScrollList.Storage: _ui.StorageScrollRows = clamped; break;
            case ScrollList.DevFacility: _ui.DevFacilityScrollRows = clamped; break;
            case ScrollList.DevRoom: _ui.DevRoomScrollRows = clamped; break;
            case ScrollList.DevAction: _ui.DevActionScrollRows = clamped; break;
            case ScrollList.ScheduleFacility: _ui.ScheduleFacilityScrollRows = clamped; break;
            case ScrollList.TradeHeld: _ui.TradeHeldFirst = clamped; break;
            case ScrollList.TradeMarket: _ui.TradeMarketFirst = clamped; break;
            case ScrollList.StatusAbility: _ui.StatusAbilityScrollRows = clamped; break;
            case ScrollList.ScheduleMember: _ui.ScheduleMemberScrollRows = clamped; break;
        }
        Refresh();
    }


    /// <summary>点轨道：把滑块中心挪到鼠标 y（点轨自动计算比率）。</summary>
    private void DragThumbCenterTo(int list, float mouseY)
    {
        var geom = ScrollGeomOf(list);
        if (geom == null)
            return;
        var ratio = InkLayout.ScrollRatioAt(geom.Track, mouseY, geom.ThumbRatio);
        SetScroll(list, Mathf.RoundToInt(ratio * geom.Max));
    }

    /// <summary>拖拽中：把滑块中心跟到鼠标 y（由分派处把代号存进 _scrollDragging）。</summary>
    private void DragScrollTo(float mouseY)
    {
        var geom = ScrollGeomOf(_scrollDragging);
        if (geom == null)
            return;
        // 抓取点跟着鼠标走：滑块顶 = 鼠标y - 抓取偏移，反推比率。
        var ratio = Mathf.Clamp(
            (mouseY - _scrollGrabOffset - geom.Track.Position.Y - geom.Thumb.Size.Y / 2f)
            / Mathf.Max(1f, geom.Track.Size.Y - geom.Thumb.Size.Y), 0f, 1f);
        SetScroll(_scrollDragging, Mathf.RoundToInt(ratio * geom.Max));
    }

    /// <summary>滚轮：鼠标落在哪个轨道附近就滚哪个列表。滚动了返回 true。</summary>
    private bool ScrollListAt(Vector2 pos, int delta)
    {
        if (_model == null)
            return false;
        foreach (var w in _model.Widgets)
        {
            if (w.Action != InkAction.ScrollJump || !w.Enabled)
                continue;
            var zone = w.Rect.GrowIndividual(60f, 12f, 60f, 12f);
            if (!zone.HasPoint(pos))
                continue;
            var geom = ScrollGeomOf(w.Index);
            if (geom == null)
                return false;
            SetScroll(w.Index, geom.First + delta);
            return true;
        }
        return false;
    }

    /// <summary>按下推进区：跳过本句打字机；本句已显示完则推进一句。</summary>
    private void OnAdvancePress()
    {
        if (!TypingDone())
            SkipTyping();
        else
            RunAdvanceStep();
    }

    /// <summary>推进一句。场景演出推进场景状态机，对话推进遮盖层自己的行队列。</summary>
    private void RunAdvanceStep()
    {
        if (_model!.SceneMode)
            _vm!.Hub.SceneContinue();
        else
            _vm!.Hub.AdvanceOverlay();
        Refresh();
    }

    /// <summary>整段快进：把当前遮罩剩余的台词句全部立即完整显示（按住行动面板触发）。</summary>
    private void DrainOverlayParagraph()
    {
        var overlay = _vm?.Hub.Overlay;
        if (overlay == null)
            return;
        if (!TypingDone())
            SkipTyping();
        for (var guard = 0; guard < 32; guard++)
        {
            var before = overlay.Text;
            if (_model!.SceneMode)
                _vm!.Hub.SceneContinue();
            else
                _vm!.Hub.AdvanceOverlay();
            overlay = _vm.Hub.Overlay;
            if (overlay == null || overlay.Waiting || overlay.Text == before)
                break;
            _ui.TypeLine = overlay.Text;
            _ui.TypeRevealed = overlay.Text.Length;
        }
        Refresh();
    }

    /// <summary>重建模型并请求重绘。任何状态变更后都走这里。</summary>
    public void Refresh()
    {
        if (_vm == null)
            return;

        // 走出房间：观察态自动收回。
        if (_ui.Observing && _ui.ObservedRoomId != _vm.Hub.PlayerRoomId)
            _ui.Observing = false;

        // 段落累积：同一说话人的连续对话台词累积成同一段；
        // 换说话人、旁白/插画或遮罩关闭才换段（每句台词各自新建遮罩实例，去重按实例+台词）。
        var overlay = _vm.Hub.Overlay;
        if (overlay == null || overlay.Kind != OverlayKind.Dialogue)
        {
            _ui.OverlayLines.Clear();
            _ui.OverlayLinesFor = null;
            _ui.OverlayLinesForText = "";
            _ui.OverlayParagraphSpeaker = "";
        }
        else if (_ui.OverlayLinesFor == null || _ui.OverlayParagraphSpeaker != overlay.Speaker)
        {
            _ui.OverlayLinesFor = overlay;
            _ui.OverlayLinesForText = overlay.Text;
            _ui.OverlayParagraphSpeaker = overlay.Speaker;
            _ui.OverlayLines.Clear();
            _ui.OverlayLines.Add(overlay.Text);
        }
        else if (_ui.OverlayLinesFor != overlay || _ui.OverlayLinesForText != overlay.Text)
        {
            _ui.OverlayLinesFor = overlay;
            _ui.OverlayLinesForText = overlay.Text;
            _ui.OverlayLines.Add(overlay.Text);
        }

        _model = InkHubModel.Build(_vm, _ui);
        _weatherFx.UpdateFrom(_model, _vm.Hub.Weather);
        QueueRedraw();
    }

    // ---------- 输入 ----------

    public override void _GuiInput(InputEvent @event)
    {
        if (_vm == null || _model == null)
            return;

        // 战斗状态下按 Esc 随时安全退出战斗
        if (_vm.Combat != null && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            ExitCombat();
            AcceptEvent();
            return;
        }

        // 通用居中弹窗输入框聚焦时接管键盘输入。
        if (_ui.ModalSession?.Current?.Input is { Focused: true } modalInput
            && @event is InputEventKey modalKey && modalKey.Pressed)
        {
            HandleModalKey(modalInput, modalKey);
            AcceptEvent();
            return;
        }

        // 改名弹窗打开时接管键盘输入。
        if (_ui.Renaming && @event is InputEventKey key && key.Pressed)
        {
            HandleRenameKey(key);
            AcceptEvent();
            return;
        }

        // 子页面搜索框聚焦时接管键盘输入。
        if (_ui.SearchFocus && _ui.OpenPage != InkPage.None
            && @event is InputEventKey searchKey && searchKey.Pressed)
        {
            HandleSearchKey(searchKey);
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            // 拖滑块：跟随鼠标更新对应列表的滚动行号。
            if (_scrollDragging >= 0)
            {
                DragScrollTo(motion.Position.Y);
                AcceptEvent();
                return;
            }
            var hit = _model.Hit(motion.Position);
            var index = hit == null ? -1 : IndexOf(hit.Value);
            if (index != _ui.Hovered)
            {
                _ui.Hovered = index;
                Refresh();
            }
            return;
        }

        // 滚轮：滚鼠标所落的那个列表；不在任何列表上则不动。
        if (@event is InputEventMouseButton { Pressed: true } wheel
            && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            var delta = wheel.ButtonIndex == MouseButton.WheelDown ? 3 : -3;
            if (ScrollListAt(wheel.Position, delta))
                AcceptEvent();
            return;
        }

        // 推进区松开：结束按住状态即可，单击语义已在按下时处理。
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }
            && _advancePressing)
        {
            _advancePressing = false;
            AcceptEvent();
            return;
        }

        // 滑块松开：结束拖拽。
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }
            && _scrollDragging >= 0)
        {
            _scrollDragging = -1;
            AcceptEvent();
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            return;

        // 观察四周状态下点击行动面板直接退出
        if (_ui.Observing && InkLayout.ActPanel.HasPoint(click.Position))
        {
            _ui.Observing = false;
            Refresh();
            AcceptEvent();
            return;
        }

        var target = _model.Hit(click.Position);
        if (target == null)
        {
            // 点在页面空白处：让搜索框失焦。
            if (_ui.SearchFocus)
            {
                _ui.SearchFocus = false;
                Refresh();
            }
            return;
        }

        // 推进区（遮盖层）：按下即跳过本句打字机，本句已显示完则推进一句；
        // 按住不放进入整段快进，松开结束。
        if (target.Value.Action == InkAction.OverlayAdvance)
        {
            _advancePressing = true;
            _advanceHold = 0;
            _advanceHoldFired = false;
            OnAdvancePress();
            AcceptEvent();
            return;
        }

        Dispatch(target.Value, click.Position);
        AcceptEvent();
    }

    /// <summary>搜索框的键盘输入：回车/esc 退出聚焦，退格删字，可见字符追加。</summary>
    private void HandleSearchKey(InputEventKey key)
    {
        if (key.Keycode == Key.Escape || key.Keycode is Key.Enter or Key.KpEnter)
        {
            _ui.SearchFocus = false;
            Refresh();
            return;
        }

        if (key.Keycode == Key.Backspace)
        {
            if (_ui.PageSearch.Length > 0)
                _ui.PageSearch = _ui.PageSearch[..^1];
            Refresh();
            return;
        }

        var ch = key.Unicode;
        if (ch >= 32 && ch != 127 && _ui.PageSearch.Length < 16)
        {
            _ui.PageSearch += char.ConvertFromUtf32((int)ch);
            Refresh();
        }
    }

    private void HandleModalKey(InkModalInput input, InputEventKey key)
    {
        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            input.OnSubmit?.Invoke(input.Text);
            _ui.ModalSession?.Advance();
            Refresh();
            return;
        }

        if (key.Keycode == Key.Backspace)
        {
            if (input.Text.Length > 0)
            {
                input.Text = input.Text[..^1];
                Refresh();
            }
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            input.Focused = false;
            Refresh();
            return;
        }

        var unicode = (char)key.Unicode;
        if (!char.IsControl(unicode) && unicode != '\0' && input.Text.Length < input.MaxChars)
        {
            input.Text += unicode;
            Refresh();
        }
    }

    private void HandleRenameKey(InputEventKey key)
    {
        if (key.Keycode == Key.Escape)
        {
            _ui.Renaming = false;
            Refresh();
            return;
        }

        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            CommitRename();
            return;
        }

        if (key.Keycode == Key.Backspace)
        {
            if (_ui.RenameText.Length > 0)
                _ui.RenameText = _ui.RenameText[..^1];
            Refresh();
            return;
        }

        // 只接受可见字符，并限制长度，避免输入框被撑破。
        var ch = key.Unicode;
        if (ch >= 32 && ch != 127 && _ui.RenameText.Length < InkLayout.RenameMaxChars)
        {
            _ui.RenameText += char.ConvertFromUtf32((int)ch);
            Refresh();
        }
    }

    private int IndexOf(InkWidget widget)
    {
        var widgets = _model!.Widgets;
        for (var i = 0; i < widgets.Count; i++)
        {
            if (widgets[i].Equals(widget))
                return i;
        }
        return -1;
    }

    /// <summary>把一次点击翻译成 HubSession 调用。</summary>
    private void Dispatch(InkWidget widget, Vector2 clickPos)
    {
        var vm = _vm!;

        // 一次点击就是一次玩家操作。日志的更新单位是操作（见 Journal），
        // 边界只标在这里，不逐 Core 方法插桩：操作有几百种且会继续加，
        // 只要都从这一个漏斗走，新操作自动获得正确的清空时机。
        vm.Hub.BeginOperation();

        // 除点搜索框外，任何点击都让搜索框失焦；
        // 同时清掉悬停下标，模型一变旧的高亮矩形就失效了。
        if (widget.Action != InkAction.PageSearchBox && _ui.SearchFocus)
            _ui.SearchFocus = false;
        _ui.Hovered = -1;

        switch (widget.Action)
        {
            // ---- 遮盖层 ----
            case InkAction.OverlayChoice:
                if (_model!.SceneMode)
                    vm.Hub.SceneChoose(widget.Index);
                else
                {
                    var overlay = vm.Hub.Overlay;
                    if (overlay != null && widget.Index < overlay.Choices.Count)
                        vm.Hub.Choose(overlay.Choices[widget.Index].Id);
                }
                break;

            // ---- 改名 ----
            case InkAction.RenameOpen:
                _ui.Renaming = true;
                _ui.RenameText = vm.TerritoryName;
                break;

            case InkAction.RenameConfirm:
                CommitRename();
                return;

            case InkAction.RenameCancel:
                _ui.Renaming = false;
                break;

            // ---- 地图与设施 ----
            case InkAction.MapCell:
            {
                var room = vm.RoomAt(InkLayout.CellCol(widget.Index), InkLayout.CellRow(widget.Index));
                if (room == null)
                    break;
                if (vm.Hub.Layer == Rimisekai.Hub.MapLayer.World)
                {
                    // 世界层：点兴趣点所在格进入该地点。
                    var poi = vm.Hub.State.World.Pois.Find(p =>
                        p.NameZh == room.Name || p.NameEn == room.Name);
                    _ui.Notice = poi != null && vm.Hub.EnterWorldPoi(poi.Id)
                        ? ""
                        : "这里无法进入。";
                }
                else
                {
                    _ui.Notice = Visit(vm, room);
                }
                break;
            }

            case InkAction.Fixture:
                _ui.Notice = vm.Hub.Use(widget.Index) ? "你在这里安顿下来。" : "这个位置已被占用。";
                break;

            // ---- 角色：再点一次同一张卡就取消选中 ----
            case InkAction.Card:
            {
                // 玩家本人的卡点了无效：不选中、不交谈、不提示。
                var clicked = vm.FindById(widget.Index);
                if (clicked != null && clicked.IsMaster)
                    break;

                if (vm.Hub.SelectedCharacterId == widget.Index)
                {
                    vm.Hub.ClearSelection();
                    _ui.Notice = "";
                }
                else
                {
                    _ui.Notice = vm.Hub.Select(widget.Index) ? "" : "无法选中该角色。";
                }
                break;
            }

            // ---- 交流 / 行动 ----
            case InkAction.SocialCategory:
            {
                if (widget.Index == 2)
                {
                    // 观察：无子项，直接执行。
                    _ui.Notice = vm.Hub.Social(InkViewModel.SocialActions[1].Action)
                        ? "你观察了对方。"
                        : "无法观察。";
                    _ui.SocialCategory = -1;
                }
                else if (widget.Index == 3)
                {
                    // 离开：结束与当前角色的交流。
                    vm.Hub.ClearSelection();
                    _ui.SocialCategory = -1;
                }
                else
                {
                    // 再点一次已展开的类别则收回到起始态。
                    _ui.SocialCategory = _ui.SocialCategory == widget.Index ? -1 : widget.Index;
                }
                break;
            }

            case InkAction.Social:
            {
                // “邀请”位跟随中换成“分开”，点击时按当下状态解析，不靠建面板时的旧快照。
                var social = widget.Index == InkViewModel.InviteActionIndex
                    ? vm.InviteEntry()
                    : InkViewModel.SocialActions[widget.Index];
                var ok = vm.Hub.Social(social.Action);

                // Core 的返回值只表示“行动被消费”；好感不够时对方会躲开并单独记日志。
                // 躲开不算成功：通知改记日志原话。（接触解锁跟随存档旗标，在 Core 里落账。）
                // 日志是快照式：每次操作清空重写，末行即本次结果。
                var dodged = vm.Hub.Log.Count > 0
                    && vm.Hub.Log[^1].Text.Contains("躲开");
                _ui.Notice = ok
                    ? (dodged ? vm.Hub.Log[^1].Text : $"你{social.Label}了对方。")
                    : $"无法{social.Label}。";
                break;
            }

            case InkAction.CombatCategory:
                if (widget.Index == 0) // 攻击：无二级页面，点击装配攻击并激活选目标；若已装配则再次点击直接向最前排出手！
                {
                    _ui.CombatCategory = -1;
                    if (vm.Combat != null && _ui.ArmedSkillId == Rimisekai.Combat.BattleSkills.AttackId && _combatTurnCooldown <= 0.05f)
                    {
                        var battle = vm.Combat.Battle;
                        var actor = battle.PendingActor;
                        if (actor != null && actor.Side == battle.ControlledSide)
                        {
                            _ui.ArmedSkillId = "";
                            ExecuteCombatAction(battle, actor, Rimisekai.Combat.BattleSkills.AttackId, 0, 0);
                            break;
                        }
                    }
                    _ui.ArmedSkillId = Rimisekai.Combat.BattleSkills.AttackId;
                    Refresh();
                }
                else if (widget.Index == 3) // 防御：无二级页面，点击直接生效！
                {
                    _ui.CombatCategory = -1;
                    _ui.ArmedSkillId = "";
                    if (vm.Combat != null && _combatTurnCooldown <= 0.05f)
                    {
                        var battle = vm.Combat.Battle;
                        var actor = battle.PendingActor;
                        if (actor != null && actor.Side == battle.ControlledSide)
                        {
                            ExecuteCombatAction(battle, actor, Rimisekai.Combat.BattleSkills.GuardId, actor.Id, 0);
                        }
                    }
                }
                else if (widget.Index == 1) // 技能：展开技能二级页面
                {
                    _ui.CombatCategory = 1;
                    _ui.ArmedSkillId = "";
                    Refresh();
                }
                else if (widget.Index == 2) // 道具：展开道具二级页面
                {
                    _ui.CombatCategory = 2;
                    _ui.ArmedSkillId = "";
                    Refresh();
                }
                break;

            case InkAction.CombatSkill:
                if (vm.Combat != null && widget.Index >= 0)
                {
                    var battle = vm.Combat.Battle;
                    var menu = battle.Menu();
                    if (widget.Index < menu.Count)
                    {
                        var skill = menu[widget.Index];
                        var actor = battle.PendingActor;
                        if (skill.Target == Rimisekai.Catalog.SkillTarget.Self)
                        {
                            // 自身目标技能点击直接生效
                            if (actor != null && actor.Side == battle.ControlledSide && _combatTurnCooldown <= 0.05f)
                                ExecuteCombatAction(battle, actor, skill.Id, actor.Id, 0);
                        }
                        else
                        {
                            // 点技能后进入选目标模式！
                            _ui.ArmedSkillId = skill.Id;
                            battle.CurrentActionName = skill.Name;
                            Refresh();
                        }
                    }
                }
                break;

            case InkAction.CombatTarget:
                // 玩家直接点击敌人要视作攻击；装配技能时则使用该技能：
                if (vm.Combat != null)
                {
                    var battle = vm.Combat.Battle;
                    if (!battle.BattleBegan)
                        battle.BattleBegan = true;

                    // 动画冷却中暂缓接收指令，防止双击连打卡死或吞指令
                    if (_combatTurnCooldown > 0.05f)
                        break;

                    var actor = battle.PendingActor;
                    if (actor != null && actor.Side == battle.ControlledSide)
                    {
                        var skillId = !string.IsNullOrEmpty(_ui.ArmedSkillId)
                            ? _ui.ArmedSkillId
                            : Rimisekai.Combat.BattleSkills.AttackId; // 没选技能时直接视作攻击！
                        _ui.ArmedSkillId = "";
                        ExecuteCombatAction(battle, actor, skillId, widget.Index, 0);
                    }
                }
                break;

            case InkAction.CombatColumn:
                // 远程/打击格：点动作/技能后点列执行，没选技能默认普攻：
                if (vm.Combat != null)
                {
                    var battle = vm.Combat.Battle;
                    if (!battle.BattleBegan)
                        battle.BattleBegan = true;

                    if (_combatTurnCooldown > 0.05f)
                        break;

                    var actor = battle.PendingActor;
                    if (actor != null && actor.Side == battle.ControlledSide)
                    {
                        var skillId = !string.IsNullOrEmpty(_ui.ArmedSkillId)
                            ? _ui.ArmedSkillId
                            : Rimisekai.Combat.BattleSkills.AttackId;
                        _ui.ArmedSkillId = "";
                        ExecuteCombatAction(battle, actor, skillId, 0, widget.Index);
                    }
                }
                break;

            case InkAction.CombatItem:
                // 道具使用预留挂点
                Refresh();
                break;

            case InkAction.Place:
            {
                if (vm.Combat != null)
                {
                    // 战斗形态下点击行动指令：攻击/技能/魔法/防御
                    var battle = vm.Combat.Battle;
                    var pending = battle.PendingActor;
                    var menu = battle.Menu();
                    if (pending != null && widget.Index >= 0 && widget.Index < menu.Count)
                    {
                        var skill = menu[widget.Index];
                        var targetId = _ui.SelectedTargetId;
                        if (targetId <= 0)
                        {
                            var foes = battle.Members.FindAll(m => m.Alive && m.Side != battle.ControlledSide);
                            if (foes.Count > 0)
                                targetId = foes[0].Id;
                        }
                        var action = new Rimisekai.Combat.CombatAction
                        {
                            ActorId = pending.Id,
                            SkillId = skill.Id,
                            TargetId = targetId,
                            TargetColumn = _ui.SelectedTargetColumn,
                        };
                        if (battle.CanAct(action))
                        {
                            battle.Act(action);
                            // 玩家出手后，自动唤醒后续行动者与敌方 AI，直到轮到下一位玩家或战斗分出胜负
                            _ = battle.PendingActor;
                        }
                    }
                    Refresh();
                    break;
                }
                var place = InkViewModel.PlaceActions[widget.Index];
                _ui.Notice = vm.Hub.Act(place.Action) ? "" : $"无法{place.Label}。";

                // 观察四周：最短链——点下即进入观察态，地图面板换成本房间插画；再点收回。
                if (place.Action == InkViewModel.PlaceActions[0].Action)
                {
                    _ui.Observing = !_ui.Observing;
                    _ui.ObservedRoomId = vm.Hub.PlayerRoomId;
                }
                break;
            }

            case InkAction.ObserveExit:
                _ui.Observing = false;
                Refresh();
                break;

            case InkAction.FixtureAction:
            {
                // 设施行动：睡/吃/洗等。清单来自 Core，索引同源。
                var actions = vm.FixtureActions();
                if (widget.Index >= actions.Count)
                    break;
                var action = actions[widget.Index];
                var label = InkText.ActionKind(action);
                _ui.Notice = vm.Hub.ActAtFixture(action) ? "" : $"无法{label}。";
                break;
            }

            // ---- 页面 ----
            case InkAction.HubWork:
                if (vm.Combat != null)
                {
                    var battle = vm.Combat.Battle;
                    if (battle.Outcome != Rimisekai.Combat.CombatOutcome.Ongoing)
                    {
                        ExitCombat();
                        break;
                    }
                    if (!battle.BattleBegan)
                    {
                        battle.BattleBegan = true;
                        _combatTurnCooldown = 0.2f;
                        Refresh();
                        break;
                    }
                    if (battle.IsAwakeningReady && !battle.AwakeningActive)
                    {
                        battle.TriggerAwakening();
                        Refresh();
                        break;
                    }
                    Refresh();
                    break;
                }
                // 工作已并入日程页：入口照旧，落到合并后的那一页。
                OpenPage(InkPage.Schedule);
                _ui.Notice = "";
                break;

            case InkAction.PageEntry:
            {
                if (vm.Combat != null)
                {
                    // 战斗形态下的队伍级操作：0=自动, 1=状态, 2=日志, 3=逃跑
                    var battle = vm.Combat.Battle;
                    if (widget.Index == 0) // 自动：切换整队 AI 代打
                    {
                        battle.AutoBattle = !battle.AutoBattle;
                        _combatTurnCooldown = 0.35f;
                    }
                    else if (widget.Index == 1) // 状态：打开角色状态全屏页
                    {
                        OpenPage(InkPage.Status);
                    }
                    else if (widget.Index == 2) // 日志：打开战况完整流水全屏页
                    {
                        OpenPage(InkPage.CombatLog);
                    }
                    else if (widget.Index == 3) // 逃跑
                    {
                        var fled = battle.TryFlee();
                        if (fled || battle.Outcome == Rimisekai.Combat.CombatOutcome.Fled)
                        {
                            ExitCombat();
                            break;
                        }
                    }
                    Refresh();
                    break;
                }
                var entries = _model?.PageEntries ?? System.Array.Empty<InkPage>();
                if (widget.Index >= entries.Count)
                    break;
                if (entries[widget.Index] == InkPage.Quest)
                {
                    QuestRequested?.Invoke();
                    break;
                }
                if (entries[widget.Index] == InkPage.Develop)
                {
                    // 开发是主界面的编辑模式（五块容器不变），不是全屏页。
                    _ui.DevMode = true;
                    _ui.OpenPage = InkPage.None;
                    _ui.DevSelectedCell = -1;
                    _ui.DevSelectedRoom = -1;
                    _ui.DevSelectedFacility = -1;
                    _ui.DevPlacingRoom = -1;
                    _ui.DevConfirmCell = -1;
                    _ui.DevFacilityScrollRows = 0;
                    _ui.DevRoomScrollRows = 0;
                    _ui.DevActionScrollRows = 0;
                }
                else
                {
                    // 点交易即进入交易：开页免费，行程在首笔成交时结算（Core）。
                    // 今天成交过则按钮是暗的，点了也不开页。
                    if (entries[widget.Index] == InkPage.Trade)
                    {
                        if (!vm.Hub.TradeAvailable)
                            break;
                        vm.Hub.OpenTrade();
                    }
                    OpenPage(entries[widget.Index]);
                }
                _ui.Notice = "";
                break;
            }

            case InkAction.CharStatus:
                OpenPage(InkPage.Status);
                _ui.Notice = "";
                break;

            case InkAction.CharSkills:
                OpenPage(InkPage.Skills);
                _ui.Notice = "";
                break;

            case InkAction.PageTab:
            {
                var currentTabs = InkPageTabs.For(_ui.OpenPage);
                if (widget.Index < currentTabs.Length)
                    OpenPage(currentTabs[widget.Index]);
                break;
            }

            case InkAction.ScrollThumb:
                // 按住滑块：记下列表代号与抓取偏移，拖动由 Motion 分支跟随。
                _scrollDragging = widget.Index;
                _scrollGrabOffset = Mathf.Clamp(clickPos.Y - widget.Rect.Position.Y, 0f, widget.Rect.Size.Y);
                AcceptEvent();
                return;

            case InkAction.ScrollJump:
                // 点轨道：滑块中心跳到点击处。
                DragThumbCenterTo(widget.Index, clickPos.Y);
                AcceptEvent();
                return;

            case InkAction.ScheduleMember:
                // 点左侧成员列表：切换当前排班的角色。
                _ui.ScheduleMemberId = widget.Index;
                _ui.Notice = "";
                Refresh();
                break;

            case InkAction.ScheduleSlot:
                // 点时段卡：选中这段，排了工作时右上角带取消按钮。
                _ui.PageSelected = widget.Index;
                _ui.Notice = "";
                break;

            case InkAction.CancelTask:
                // 点时段卡右上角的取消按钮：清空该时段的工作安排。
                var cancelWho = (_ui.ScheduleMemberId >= 0 ? vm.Hub.State.Roster.Find(_ui.ScheduleMemberId) : null)
                    ?? vm.ChatPartner()
                    ?? vm.Hub.State.Roster.Master;
                if (cancelWho != null)
                {
                    vm.Hub.Assign(cancelWho.Id, widget.Index, SlotMode.Free, -1);
                    _ui.Notice = $"取消了{InkText.WorkSlot(widget.Index)}的工作。";
                    Refresh();
                }
                break;

            case InkAction.StatusAbilityToggle:
                // 点某段的段头条：把本级以下摊开，再点一次收回。
                _ui.StatusAbilityOpen[widget.Index] = !_ui.StatusAbilityOpen[widget.Index];
                _ui.StatusAbilityScrollRows = 0;
                _ui.Notice = "";
                break;

            case InkAction.ScheduleRoom:
                // 点房间格：选中它，右侧列出这间房的设施。
                _ui.ScheduleRoomId = widget.Index;
                _ui.Notice = "";
                break;

            case InkAction.ScheduleFacility:
                // 点设施行：把当前时段排到这件设施上。
                _ui.Notice = AssignFacility(vm, widget.Index);
                break;

            case InkAction.PageScroll:
            {
                var rows = _model?.Page;
                if (rows == null)
                    break;
                var visible = InkLayout.ListVisibleRows(InkLayout.FullListArea);
                var max = Math.Max(0, rows.Rows.Count - visible);
                _ui.PageFirst = widget.Index == 0
                    ? Math.Max(0, _ui.PageFirst - visible)
                    : Math.Min(max, _ui.PageFirst + visible);
                break;
            }

            case InkAction.HubWorld:
                // 世界没有单独页面：地图网格直接切到世界层，再点一次切回领地。
                vm.Hub.ToggleWorldLayer();
                _ui.Notice = "";
                break;

            case InkAction.HubQuest:
                QuestRequested?.Invoke();
                return;

            case InkAction.HubSystem:
                SystemRequested?.Invoke("settings");
                return;

            case InkAction.ChatEntry:
                // 聊天层右上角的入口：状态/技能/日程，针对当前说话人。
                OpenPage(InkHubModel.ChatEntries[widget.Index]);
                break;

            case InkAction.PageSelect:
                _ui.PageSelected = widget.Index;
                break;

            case InkAction.SkillPick:
                // 技能页：把点中的瓦片 Id 交给界面态，页面重建时按它展开右栏；视角平滑聚焦至该扇区
                if (_model?.Page?.Disc != null
                    && widget.Index < _model.Page.Disc.Tiles.Count)
                {
                    var clickedTile = _model.Page.Disc.Tiles[widget.Index];
                    _ui.SkillSelectedId = clickedTile.Id;
                    if (_ui.SkillFocusedSector != clickedTile.Sector)
                        _ui.SkillFocusedSector = clickedTile.Sector;
                }
                break;

            case InkAction.SkillSectorPick:
                // 点击扇区标签：视角移动并放大至对应扇区；再次点击已聚焦扇区则缩回全景
                if (_ui.SkillFocusedSector == widget.Index)
                    _ui.SkillFocusedSector = -1;
                else
                {
                    _ui.SkillFocusedSector = widget.Index;
                    _ui.SkillSelectedId = ""; // 聚焦新扇区时重置选中，让页面自动选中该扇区第一项技能
                }
                break;

            case InkAction.SkillSectorReset:
                // 返回全景星盘视图
                _ui.SkillFocusedSector = -1;
                break;

            case InkAction.SkillTilePrev:
            case InkAction.SkillTileNext:
                // 星盘右上角导航按钮：切换左/右瓦片（切换选中当前扇区内的上一个或下一个技能）
                if (_model?.Page?.Disc != null)
                {
                    var disc = _model.Page.Disc;
                    var focused = disc.FocusedSector;
                    var skillsInSector = new List<string>();
                    foreach (var t in disc.Tiles)
                    {
                        if (t.Kind == InkSkillNodeKind.Skill && (focused < 0 || t.Sector == focused))
                        {
                            if (!skillsInSector.Contains(t.Id))
                                skillsInSector.Add(t.Id);
                        }
                    }

                    if (skillsInSector.Count > 0)
                    {
                        var curIdx = skillsInSector.IndexOf(_ui.SkillSelectedId);
                        if (curIdx < 0) curIdx = 0;

                        if (widget.Action == InkAction.SkillTilePrev)
                            curIdx = (curIdx + skillsInSector.Count - 1) % skillsInSector.Count;
                        else
                            curIdx = (curIdx + 1) % skillsInSector.Count;

                        _ui.SkillSelectedId = skillsInSector[curIdx];
                        Refresh();
                    }
                }
                break;

            case InkAction.PageSearchBox:
                _ui.SearchFocus = true;
                break;

            case InkAction.PageFilterPick:
                if (_ui.PageFilter != widget.Index)
                {
                    _ui.PageFilter = widget.Index;
                    _ui.PageSelected = -1;
                }
                break;

            case InkAction.PageSortCycle:
                if (_ui.PageSort == widget.Index)
                    _ui.PageSortDesc = !_ui.PageSortDesc;
                else
                {
                    _ui.PageSort = widget.Index;
                    _ui.PageSortDesc = false;
                }
                break;

            case InkAction.DevPickRoom:
            {
                // 点已开发的房间格：选中它（右上换成这间房的设施，右下换详情）。
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.Rooms.Count)
                {
                    _ui.DevSelectedCell = widget.Index;
                    _ui.DevSelectedFacility = -1;
                }
                break;
            }

            case InkAction.DevPickFacility:
                // 设施行的 Index 就是设施 Id（构建器按 Id 回选，换房间不会串位）。
                _ui.DevSelectedFacility = widget.Index;
                break;

            case InkAction.DevPickRoomRow:
            {
                // 点「待安装的房间」里的一行：选中它，并进入网格选位。
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.RoomRows.Count)
                {
                    _ui.DevSelectedRoom = widget.Index;
                    _ui.DevPlacingRoom = dev.RoomRows[widget.Index].Id;
                    _ui.DevSelectedFacility = -1;
                    _ui.Notice = "在左上网格里点一间空房，把它装进去。";
                }
                break;
            }

            case InkAction.DevAction:
            {
                // 中下操作面板：每一行自己带着要派发的动作与下标。
                var dev = _model?.Dev;
                if (dev == null || widget.Index < 0 || widget.Index >= dev.ActionRows.Count)
                    break;
                var row = dev.ActionRows[widget.Index];
                switch (row.Action)
                {
                    case InkAction.DevBuildFacility:
                        if (dev.RoomId < 0)
                        {
                            _ui.Notice = "先在上面网格里选一间房。";
                            break;
                        }
                        _ui.Notice = vm.Hub.BuildFacilityDef(row.Index, dev.RoomId)
                            ? $"把{row.Name}建进了这间房。"
                            : "材料不够。";
                        break;

                    case InkAction.DevBuildRoom:
                        _ui.Notice = vm.Hub.BuildRoomDef(row.Index)
                            ? $"建好了{row.Name}（待安装）。"
                            : "材料不够。";
                        break;

                    case InkAction.DevRemoveFacility:
                        _ui.Notice = vm.Hub.RemoveFacility(row.Index)
                            ? $"拆除了{row.Name}。"
                            : "无法拆除。";
                        if (_ui.Notice.StartsWith("拆除了"))
                            _ui.DevSelectedFacility = -1;
                        break;

                    case InkAction.DevPlaceFacility:
                        if (dev.RoomId < 0)
                        {
                            _ui.Notice = "先在上面网格里选一间房。";
                            break;
                        }
                        _ui.Notice = vm.Hub.PlaceFacility(row.Index, dev.RoomId)
                            ? $"把{row.Name}安置好了。"
                            : "无法安置。";
                        break;

                    case InkAction.DevDemolishRoom:
                        _ui.Notice = vm.Hub.RemoveRoom(row.Index)
                            ? $"拆除了{row.Name}。"
                            : "无法拆除（当前所在房间不能拆除）。";
                        if (_ui.Notice.StartsWith("拆除了"))
                        {
                            _ui.DevSelectedCell = -1;
                            _ui.DevSelectedFacility = -1;
                        }
                        break;
                }
                break;
            }

            case InkAction.DevOpenConfirm:
            {
                // 点邻近的未开发格（含没有房间实体的「空地」）：选中它，
                // 弹「是否消耗材料和钱开发成一间空房」。确认窗按**格子下标**记。
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.Rooms.Count)
                {
                    var cell = dev.Rooms[widget.Index];
                    if (cell.CanDevelop)
                    {
                        _ui.DevSelectedCell = widget.Index;
                        _ui.DevSelectedFacility = -1;
                        _ui.DevConfirmCell = InkLayout.CellIndex(cell.X, cell.Y);
                    }
                }
                break;
            }

            case InkAction.DevConfirmAccept:
            {
                var cellIndex = _ui.DevConfirmCell;
                _ui.DevConfirmCell = -1;
                if (cellIndex >= 0)
                {
                    var x = cellIndex % InkLayout.GridCols;
                    var y = cellIndex / InkLayout.GridCols;
                    var target = vm.RoomAt(x, y);
                    // 已有房间实体（Open=false）→ 按它自己的定价开拓；
                    // 还是空地（没有实体）→ 现场开一间「空房」。
                    var ok = target is { Open: false }
                        ? vm.Hub.DevelopEmptyRoom(target.Id)
                        : vm.Hub.DevelopVacantCell(vm.Hub.RegionId, x, y);
                    _ui.Notice = ok
                        ? "开拓好了，是一间空房。"
                        : "开拓不了（材料或钱不够，或这儿挨不着已开发的地方）。";
                }
                break;
            }

            case InkAction.DevConfirmCancel:
                _ui.DevConfirmCell = -1;
                break;

            case InkAction.DevPlaceRoomAt:
            {
                // widget.Index 是目标**空房**的房间 Id——落点只注册在空房上，
                // 所以「往裸格子上放房间」这条路已经不存在了。
                if (_ui.DevPlacingRoom >= 0 && vm.Hub.PlaceRoom(_ui.DevPlacingRoom, widget.Index))
                {
                    _ui.Notice = "房间已装进空房。";
                    _ui.DevPlacingRoom = -1;
                    _ui.DevSelectedRoom = -1;
                }
                break;
            }

            case InkAction.DevDemolishRoom:
            {
                // 网格格右上角的白 X：拆掉那间房。
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.Rooms.Count)
                {
                    var cell = dev.Rooms[widget.Index];
                    _ui.Notice = vm.Hub.RemoveRoom(cell.Id)
                        ? $"拆除了{cell.Name}。"
                        : "无法拆除（当前所在房间不能拆除）。";
                    if (_ui.Notice.StartsWith("拆除了"))
                    {
                        _ui.DevSelectedCell = -1;
                        _ui.DevSelectedFacility = -1;
                    }
                }
                break;
            }

            // ---- 设施交互 / 存储配置 ----
            case InkAction.RoomLock:
                _ui.Notice = vm.Hub.ToggleRoomLock() ? "" : "这里没有门可锁。";
                break;

            case InkAction.CrossRegion:
                _ui.Notice = vm.Hub.CrossTo(widget.Index)
                    ? $"你去了{Territory.RegionName(widget.Index)}。"
                    : "过不去（对面那块地图的连接点上还没有房）。";
                break;

            case InkAction.StorageStore:
            {
                var rows = vm.StorageRows();
                if (widget.Index < rows.Count)
                {
                    var item = rows[widget.Index].ItemId;
                    if (!vm.Hub.StoreOne(item))
                        _ui.Notice = "无法放入该物品（已满或受限）。";
                    else
                        _ui.Notice = "";
                }
                break;
            }

            case InkAction.StorageTake:
            {
                var rows = vm.StorageRows();
                if (widget.Index < rows.Count)
                {
                    var item = rows[widget.Index].ItemId;
                    if (!vm.Hub.TakeOne(item))
                        _ui.Notice = "无法取出该物品。";
                    else
                        _ui.Notice = "";
                }
                break;
            }

            case InkAction.StorageFilterToggle:
            {
                var rows = vm.StorageRows();
                if (widget.Index < rows.Count)
                {
                    vm.Hub.ToggleStorageFilter(rows[widget.Index].ItemId);
                    _ui.Notice = "";
                }
                break;
            }

            case InkAction.StorageCategoryToggle:
            {
                var cats = vm.StorageCategoryRows();
                if (widget.Index < cats.Count)
                {
                    vm.Hub.ToggleStorageFilter(cats[widget.Index].DefName);
                    _ui.Notice = "";
                }
                break;
            }

            case InkAction.StorageClose:
                vm.Hub.CloseStorage();
                _ui.Notice = "";
                break;

            case InkAction.BlockClick:
                // 遮盖的遮挡矩形：吃下点击，若弹窗输入框聚焦则令其失焦。
                if (_ui.ModalSession?.Current?.Input != null && _ui.ModalSession.Current.Input.Focused)
                    _ui.ModalSession.Current.Input.Focused = false;
                break;

            case InkAction.PageClose:
                // 关交易页即结束本次交易（不耗时）。
                if (_ui.OpenPage == InkPage.Trade)
                    vm.Hub.LeaveMarket();
                _ui.OpenPage = InkPage.None;
                _ui.DevMode = false;
                break;

            case InkAction.DevExit:
                _ui.DevMode = false;
                _ui.DevSelectedCell = -1;
                _ui.DevSelectedRoom = -1;
                _ui.DevSelectedFacility = -1;
                _ui.DevPlacingFacility = -1;
                _ui.DevPlacingRoom = -1;
                _ui.DevConfirmCell = -1;
                break;

            case InkAction.PageRow:
                RunPageRow(vm, widget);
                break;

            case InkAction.PageNext:
                _ui.CardPage++;
                _ui.Notice = "";
                break;

            case InkAction.PagePrev:
                _ui.CardPage--;
                _ui.Notice = "";
                break;

            case InkAction.LogToggle:
                _ui.LogExpanded = !_ui.LogExpanded;
                break;

            // ---- 交易页：两栏选中互斥，中钮按选中栏买卖 ----
            case InkAction.TradePickHeld:
                // 左侧点击准备卖出，支持切换或再次点击取消；不影响右侧准备购买
                _ui.TradeSelectedHeld = _ui.TradeSelectedHeld == widget.Index ? -1 : widget.Index;
                _ui.Notice = "";
                break;

            case InkAction.TradePickMarket:
                // 右侧点击准备购买，支持切换或再次点击取消；不影响左侧准备卖出
                _ui.TradeSelectedMarket = _ui.TradeSelectedMarket == widget.Index ? -1 : widget.Index;
                _ui.Notice = "";
                break;

            case InkAction.TradeRun:
                // 点击后成交：支持仅卖出、仅买入、或购买和卖出一次性完成
                _ui.Notice = RunTrade(vm);
                break;

            // ---- 通用居中自适应弹窗 ----
            case InkAction.ModalAdvance:
                _ui.ModalSession?.Advance();
                break;

            case InkAction.ModalChoice:
            {
                var cur = _ui.ModalSession?.Current;
                if (cur != null && widget.Index >= 0 && widget.Index < cur.Choices.Count)
                {
                    var choice = cur.Choices[widget.Index];
                    choice.OnSelected?.Invoke();
                    _ui.ModalSession?.Advance();
                }
                break;
            }

            case InkAction.ModalInputFocus:
                if (_ui.ModalSession?.Current?.Input != null)
                {
                    _ui.ModalSession.Current.Input.Focused = true;
                }
                break;
        }

        Refresh();
    }

    /// <summary>
    /// 交易页成交：支持仅卖出、仅买入、或购买和卖出一次性完成。
    /// </summary>
    private string RunTrade(InkViewModel vm)
    {
        var trade = _model?.Page?.Trade;
        if (trade == null || !trade.CanRun)
            return "";

        var hasSell = trade.HasSell && trade.SelectedHeld >= 0 && trade.SelectedHeld < trade.Held.Count;
        var hasBuy = trade.HasBuy && trade.SelectedMarket >= 0 && trade.SelectedMarket < trade.Market.Count;

        string? sellItemId = hasSell ? trade.Held[trade.SelectedHeld].ItemId : null;
        string? sellName = hasSell ? trade.Held[trade.SelectedHeld].Name : null;

        string? buyItemId = hasBuy ? trade.Market[trade.SelectedMarket].ItemId : null;
        string? buyName = hasBuy ? trade.Market[trade.SelectedMarket].Name : null;

        if (hasSell && hasBuy)
        {
            if (vm.Hub.MarketTradeCombined(sellItemId, 1, buyItemId, 1))
            {
                _ui.TradeSelectedHeld = -1;
                _ui.TradeSelectedMarket = -1;
                return $"卖出了{sellName}，买入了{buyName}。";
            }
            return "资金不足或交易失败。";
        }
        else if (hasSell)
        {
            if (vm.Hub.MarketTrade(sellItemId!, 1, selling: true))
            {
                _ui.TradeSelectedHeld = -1;
                return $"卖出了{sellName}。";
            }
            return "没有可卖的货。";
        }
        else if (hasBuy)
        {
            if (vm.Hub.MarketTrade(buyItemId!, 1, selling: false))
            {
                _ui.TradeSelectedMarket = -1;
                return $"买入了{buyName}。";
            }
            return "买不起或没有货。";
        }
        return "";
    }

    /// <summary>
    /// 执行右栏详情里的动作按钮，语义由行 Action 决定；
    /// 反馈文本里的对象名取详情标题（当前选中的物品/配方/房间）。
    /// </summary>
    private void RunPageRow(InkViewModel vm, InkWidget widget)
    {
        var actions = _model?.Page?.DetailActions;
        if (actions == null || widget.Index >= actions.Count)
            return;

        var row = actions[widget.Index];
        var target = _model!.Page!.DetailTitle;

        _ui.Notice = row.Action switch
        {
            InkPageAction.Craft => vm.Hub.Craft(row.TargetId)
                ? (vm.Hub.Log.Count > 0 ? vm.Hub.Log[^1].Text : "已更新生产目标。")
                : "设置失败。",
            InkPageAction.AssignTask => AssignTask(vm, row),
            InkPageAction.CancelTask => CancelTask(vm, row),
            _ => "",
        };
    }

    /// <summary>
    /// 日程页：把当前时段排到点中的那件设施上。
    /// 定义为有工作就去工作，没工作就闲着：
    /// 点未排的工作设施即安排工作；点已排的设施即取消安排（恢复闲着）。
    /// </summary>
    private string AssignFacility(InkViewModel vm, int facilityId)
    {
        var who = (_ui.ScheduleMemberId >= 0 ? vm.Hub.State.Roster.Find(_ui.ScheduleMemberId) : null)
            ?? vm.ChatPartner()
            ?? vm.Hub.State.Roster.Master;
        if (who == null)
            return "没有可安排的人。";
        var slot = _ui.PageSelected < 0 ? 0 : _ui.PageSelected;
        if (slot >= WorkSlot.Count)
            return "先选一个时段。";

        var existing = vm.Hub.AssignmentOf(who.Id, slot);
        if (existing.FacilityId == facilityId)
        {
            // 点击已排设施：取消安排，没工作就空闲
            vm.Hub.Assign(who.Id, slot, SlotMode.Free, -1);
            return $"{who.Name}在{InkText.WorkSlot(slot)}空闲。";
        }

        if (!vm.Hub.FacilityIsWorkbench(facilityId))
            return "该设施没有工作。";

        if (!vm.Hub.Assign(who.Id, slot, SlotMode.Work, facilityId))
            return "无法安排到这件设施。";
        return $"安排{who.Name}在{InkText.WorkSlot(slot)}去{vm.Hub.FacilityName(facilityId)}。";
    }

    /// <summary>
    /// 日程页：把当前角色的某一段改成点中的开关（空闲 / 工作 / 娱乐）。
    /// 工作与娱乐保留已点名的设施；空闲清掉设施。校验交给 Core。
    /// </summary>
    /// <summary>取消这一段已排好的工作：清空该时段（模式回空闲、设施撤销）。</summary>
    private string CancelTask(InkViewModel vm, InkPageRow row)
    {
        var who = (_ui.ScheduleMemberId >= 0 ? vm.Hub.State.Roster.Find(_ui.ScheduleMemberId) : null)
            ?? vm.ChatPartner()
            ?? vm.Hub.State.Roster.Master;
        if (who == null)
            return "没有可安排的人。";
        if (!vm.Hub.Assign(who.Id, row.TargetNumber, SlotMode.Free, -1))
            return "无法取消这段时间。";
        return $"取消了{InkText.WorkSlot(row.TargetNumber)}的安排。";
    }

    private string AssignTask(InkViewModel vm, InkPageRow row)
    {
        var who = (_ui.ScheduleMemberId >= 0 ? vm.Hub.State.Roster.Find(_ui.ScheduleMemberId) : null)
            ?? vm.ChatPartner()
            ?? vm.Hub.State.Roster.Master;
        if (who == null)
            return "没有可安排的人。";
        if (!Enum.TryParse<SlotMode>(row.TargetId, out var mode))
            return "不认识这段时间的用法。";
        // 切到工作/娱乐时沿用这段已点名的设施；没点名则只记开关，等点设施。
        var existing = vm.Hub.AssignmentOf(who.Id, row.TargetNumber);
        var facilityId = mode == SlotMode.Free ? -1 : existing.FacilityId;
        if (!vm.Hub.Assign(who.Id, row.TargetNumber, mode, facilityId))
            return "无法安排这段时间。";
        var slot = InkText.WorkSlot(row.TargetNumber);
        return mode == SlotMode.Free
            ? $"把{slot}这段留空了。"
            : $"安排{who.Name}在{slot}{InkText.SlotMode(mode)}。";
    }

    /// <summary>捕获战斗新产生的战况事件，激活全套攻击突进、受击震颤、斩击光弧与伤害飘字。</summary>
    private void ConsumeBattleEvents()
    {
        if (_vm?.Combat == null)
            return;
        var battle = _vm.Combat.Battle;
        while (_lastEventCount < battle.Events.Count)
        {
            var ev = battle.Events[_lastEventCount++];
            InkCombatFx.SpawnFromEvent(ev, battle, id => InkCombatRenderer.GetUnitCenter(battle, id),
                unit => InkCombatRenderer.GetEnemyCardRect(battle, unit.Id), null);
        }
    }

    private void ExecuteCombatAction(Rimisekai.Combat.Battle battle, Rimisekai.Combat.Combatant pending, string skillId, int targetId = 0, int targetColumn = 0)
    {
        var def = battle.Lookup(skillId);
        if (def != null && def.Target == Rimisekai.Catalog.SkillTarget.Enemy)
        {
            var foes = battle.Members.FindAll(m => m.Alive && m.Side != battle.ControlledSide);
            // 校验 targetId 是否存活且为有效敌方；若已死亡或未指定（<=0），自动兜底修正为当前最前排活怪
            var valid = false;
            for (var i = 0; i < foes.Count; i++)
            {
                if (foes[i].Id == targetId) { valid = true; break; }
            }
            if (!valid && foes.Count > 0)
            {
                var front = foes[0];
                for (var i = 1; i < foes.Count; i++)
                {
                    if (foes[i].ThreatTier > front.ThreatTier)
                        front = foes[i];
                }
                targetId = front.Id;
            }
        }

        var action = new Rimisekai.Combat.CombatAction
        {
            ActorId = pending.Id,
            SkillId = skillId,
            TargetId = targetId,
            TargetColumn = targetColumn,
        };

        battle.BattleBegan = true;
        if (battle.CanAct(action))
        {
            battle.Act(action);
            ConsumeBattleEvents();
            // 玩家出手冷却设置 0.15s 轻量防抖，既保障刀光特效即刻呈现，又杜绝击杀后卡死连点手感
            _combatTurnCooldown = 0.15f;
        }
        Refresh();
    }

    private void CommitRename()
    {
        var vm = _vm!;
        var name = _ui.RenameText.Trim();
        if (name.Length == 0)
        {
            _ui.Notice = "名字不能为空。";
        }
        else
        {
            vm.Hub.State.Territory.Name = name;
            _ui.Notice = $"领地改名为「{name}」。";
        }
        _ui.Renaming = false;
        Refresh();
    }

    /// <summary>
    /// 点地图格：走到已开放的房间，或给出为什么走不了。
    /// **开拓不在这里**——主地图不给未开放房间任何入口，开拓一律进开发页。
    /// </summary>
    private static string Visit(InkViewModel vm, Room room)
    {
        if (vm.IsPlayerRoom(room.Id))
            return $"你已经在{room.Name}。";

        if (!vm.IsNeighbor(room))
            return $"{room.Name}不与此处相连。";

        return vm.Hub.Move(room.Id) ? $"走进了{room.Name}。" : $"无法前往{room.Name}。";
    }

    // ---------- 开发期核对钩子（仅供 Tools/InkCapture 使用） ----------

    /// <summary>改名弹窗是否打开，供开发期核对读取。</summary>
    public bool DebugRenaming => _ui.Renaming;

    /// <summary>当前打开的子页面，供开发期核对读取。</summary>
    public InkPage DebugPage => _ui.OpenPage;

    /// <summary>遮盖层（对话/演出）是否开着，供开发期核对读取。</summary>
    public bool DebugOverlayOpen => _model?.Overlay != null;

    /// <summary>当前反馈文本，供开发期核对读取。</summary>
    public string DebugNotice => _ui.Notice;

    /// <summary>开发页模型，供开发期核对（找操作行、找空房）。</summary>
    public InkDevModel? DebugDev => _model?.Dev;

    /// <summary>技能页当前选中的技能 Id，供开发期核对读取。</summary>
    public string DebugSkillSelected => _ui.SkillSelectedId;

    /// <summary>技能盘当前聚焦的扇区序号（0..5），-1 表示全景，供开发期核对读取。</summary>
    public int DebugFocusedSector => _ui.SkillFocusedSector;

    /// <summary>技能盘视角当前平滑缩放倍率，供开发期核对读取。</summary>
    public float DebugDiscZoom => _ui.SkillDiscZoom;

    /// <summary>技能盘视角当前平移中心锚点，供开发期核对读取。</summary>
    public Vector2 DebugDiscPivot => _ui.SkillDiscPivot;

    /// <summary>技能盘视角当前旋转对齐角度，供开发期核对读取。</summary>
    public float DebugDiscRotation => _ui.SkillDiscRotation;

    /// <summary>技能盘当前内容模型，供开发期核对读取（找瓦片坐标）。</summary>
    public InkSkillDiscModel? DebugDisc => _model?.Page?.Disc;

    /// <summary>进入开发模式：直接置观察态（本房间插画盖网格），供出图核对。</summary>
    public void DebugObserve()
    {
        _ui.Observing = true;
        _ui.ObservedRoomId = _vm?.Hub.PlayerRoomId ?? -1;
        _vm?.Hub.Act(PlaceAction.Observe);
        Refresh();
    }

    /// <summary>某坐标命中的元素描述，供开发期核对定位问题。</summary>
    public string DebugHitAt(Vector2 at)
    {
        var hit = _model?.Hit(at);
        return hit == null ? "(无)" : $"{hit.Value.Action}#{hit.Value.Index} \"{hit.Value.Label}\"";
    }

    /// <summary>按名字打开子页面，可指定选中行与搜索词，供开发期核对出图。</summary>
    public void DebugOpenPage(string page, int selected = -1, string search = "", string skill = "", int sector = -1, int roomId = -1)
    {
        var target = page switch
        {
            "stock" => InkPage.Stock,
            "trade" => InkPage.Trade,
            "develop" => InkPage.Develop,
            // 角色三页从聊天层右上角进入，出图时也要能直接开到。
            "status" => InkPage.Status,
            "skills" => InkPage.Skills,
            "schedule" => InkPage.Schedule,
            "combatlog" or "combat_log" => InkPage.CombatLog,
            _ => InkPage.None,
        };
        // 走与点击同一条路：闸门（交易页开集）与查询复位都在那里。
        if (target != InkPage.None)
            OpenPage(target);
        else
        {
            _ui.OpenPage = InkPage.None;
            ResetPageQuery();
        }
        _ui.PageSelected = selected;
        _ui.SkillSelectedId = skill;
        _ui.PageSearch = search;
        _ui.Hovered = -1;
        if (roomId >= 0)
            _ui.ScheduleRoomId = roomId;
        if (sector >= 0)
        {
            _ui.SkillFocusedSector = sector;
            _ui.SkillDiscZoom = InkLayout.SkillDiscFocusedZoom;
            _ui.SkillDiscPivot = InkLayout.SkillDiscPanelCornerBL;
            _ui.SkillDiscRotation = InkLayout.SkillDiscSectorRotation(sector);
            _ui.SkillSelectedId = "";
        }
        Refresh();
    }

    /// <summary>进入开发模式，供开发期核对出图。</summary>
    public void DebugDevMode()
    {
        _ui.DevMode = true;
        _ui.DevSelectedCell = -1;
        _ui.DevSelectedRoom = -1;
        _ui.DevSelectedFacility = -1;
        _ui.DevConfirmCell = -1;
        Refresh();
    }

    /// <summary>开发期核对：直接弹开拓确认窗（挑第一格可开拓的未开发格，含「空地」）。</summary>
    public void DebugDevConfirm()
    {
        _ui.DevMode = true;
        var dev = _model?.Dev;
        if (dev == null)
            return;
        for (var i = 0; i < dev.Rooms.Count; i++)
        {
            if (!dev.Rooms[i].CanDevelop)
                continue;
            _ui.DevSelectedCell = i;
            _ui.DevConfirmCell = InkLayout.CellIndex(dev.Rooms[i].X, dev.Rooms[i].Y);
            break;
        }
        Refresh();
    }

    /// <summary>跨到次日刷新集市与日期，供开发期核对交易出图。</summary>
    public void DebugNewMarketDay()
    {
        if (_vm == null)
            return;
        _vm.Hub.State.Clock.Advance(Rimisekai.Clock.GameClock.MinutesPerDay);
        _vm.Hub.State.Territory.RollMarketDay(new System.Random());
        Refresh();
    }

    /// <summary>推进时间（分钟），供开发期核对出图跨天刷新。</summary>
    public void DebugPassTime(int minutes)
    {
        if (_vm == null)
            return;
        _vm.Hub.PassTime(minutes);
        Refresh();
    }

    /// <summary>强制设置天气，供开发期核对出图。</summary>
    public void DebugSetWeather(int weather)
    {
        if (_vm == null)
            return;
        _vm.Hub.Weather = (Rimisekai.Clock.Weather)weather;
        Refresh();
    }

    /// <summary>领地与世界层来回切换，供开发期核对出图。</summary>
    public void DebugToggleWorldLayer()
    {
        if (_vm == null)
            return;
        _vm.Hub.ToggleWorldLayer();
        Refresh();
    }

    /// <summary>直接设置列表的筛选与排序，供开发期核对出图。</summary>
    public void DebugQuery(int filter, int sort, bool desc)
    {
        _ui.PageFilter = filter;
        _ui.PageSort = sort;
        _ui.PageSortDesc = desc;
        _ui.PageSelected = -1;
        Refresh();
    }

    /// <summary>强制选中子页面的指定行，供开发期核对出图。</summary>
    public void DebugSelectPageRow(int index)
    {
        _ui.PageSelected = index;
        Refresh();
    }

    /// <summary>打开改名弹窗，供开发期核对出图。</summary>
    /// <summary>设置战斗行动分类与装配技能，供开发期核对出图。</summary>
    public void DebugCombatState(int category, string armedSkillId)
    {
        _ui.CombatCategory = category;
        _ui.ArmedSkillId = armedSkillId;
        Refresh();
    }

    /// <summary>开发核对：让当前行动者对最前排敌人出手，激活伤害飘字、攻击特效与死亡切开消散动画。</summary>
    public void DebugCombatStrike(string skillId = Rimisekai.Combat.BattleSkills.AttackId)
    {
        if (_vm?.Combat == null)
            return;
        var battle = _vm.Combat.Battle;
        battle.BattleBegan = true;

        // 先把时间轴推进到我方行动者的回合（速度快的敌人可能先动），再让玩家出手。
        for (var guard = 0; guard < 40; guard++)
        {
            var pending = battle.PendingActor;
            if (pending != null && pending.Side == battle.ControlledSide)
                break;
            if (!battle.StepTurn(out _))
                break;
            ConsumeBattleEvents();
        }

        var actor = battle.PendingActor;
        if (actor == null || actor.Side != battle.ControlledSide)
            return;
        ExecuteCombatAction(battle, actor, skillId, 0, 0);

        // 推进数步让敌我双方行动，生成丰富战况流水供日志紧凑度核对
        var oldAuto = battle.AutoBattle;
        battle.AutoBattle = true;
        for (var step = 0; step < 8; step++)
        {
            if (battle.Outcome != Rimisekai.Combat.CombatOutcome.Ongoing)
                break;
            if (!battle.StepTurn(out _))
                break;
            ConsumeBattleEvents();
        }
        battle.AutoBattle = oldAuto;
    }

    /// <summary>开发核对：直接触发战斗场景末尾的战后结算弹窗。</summary>
    public void DebugCombatSettlement()
    {
        if (_vm?.Combat == null)
            return;
        TriggerCombatSettlement();
    }

    public void DebugOpenRename()
    {
        _ui.Renaming = true;
        _ui.RenameText = _vm?.TerritoryName ?? "";
        _ui.Hovered = -1;
        Refresh();
    }

    // ---------- 绘制 ----------

    public override void _Draw()
    {
        var model = _model;
        if (model == null)
            return;

        InkFrame.Backdrop(this, new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight));

        var inCombat = _vm?.Combat != null;
        var t = _vm?.CombatT ?? 0f;
        if (inCombat)
        {
            // 战斗版式：地图与日志退出（消融层化掉）；下三面板连框带内容整体下移，
            // 内部只换字与换逻辑，几何一律沿用原样。
            if (!model.InCombat)
            {
                Refresh();
                return;
            }

            // 插图层级在最底部：战斗背景插画作为最底层底图绘制，所有战斗 UI（跑条、Boss条、日志、敌阵、卡片）全部叠在上方！
            // 主人定：插画背景本身压暗（亮度降到 30%），压的是插画而非 UI 元素
            InkCombatRenderer.DrawCombatBackground(this, new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight), dimFactor: 0.30f);

            EnsureDissolveLayer(model, Smooth(t));

            var shift = InkLayout.CombatPanelSink * Smooth(t);

            // 主人定：左上角的地区名称保持显示在战斗界面
            var titleSize = InkDraw.Measure(model.PlaceTitle, 30);
            DrawRect(new Rect2(58f, 40f, titleSize.X + 12f, 38f), new Color(InkStyle.Bg, 0.85f));
            InkDraw.Text(this, new Vector2(64, 44), model.PlaceTitle, 30, InkStyle.Line);

            // 绘制战斗全新四区布局：左上竖向跑条、中上GBF式Boss血条、右侧战斗日志、中央透视敌阵
            if (_vm?.Combat == null)
                return;
            var battle = _vm.Combat.Battle;
            var armed = battle.Menu().Count > 0 ? battle.Menu()[0] : null;

            // 1. 跑条移至左上角，地图名称下方，竖着跑
            InkCombatRenderer.DrawVerticalTimeline(this, InkLayout.CombatVerticalTimeline, battle);

            // 2. 中上为 boss/精英名称 + 血量 + 行动点条（类似碧蓝幻想）
            InkCombatRenderer.DrawBossHeader(this, InkLayout.CombatBossHeader, battle);

            // 3. 右侧显示战斗日志窗口
            InkCombatRenderer.DrawCombatLog(this, InkLayout.CombatLogWindow, battle);

            // 4. 敌阵 3D 透视网格：完全保留原本 960 中轴与 1824 宽幅舞台，绝不擅自挤压篡改！
            InkCombatRenderer.DrawEnemyField(this, new Rect2(48f, 150f, 1824f, 552f), battle, _ui.SelectedTargetId, _ui.SelectedTargetColumn, armed);

            // 战斗形态下三面板下移：
            DrawSetTransform(shift);

            // 主人定：边框加回来看看效果，遮罩和边框对齐
            InkFrame.Zone(this, InkLayout.LowerZone, new Color(InkStyle.Bg, 0.90f),
                InkLayout.LowerSplitLeftX, InkLayout.LowerSplitRightX);

            InkCharRenderer.Draw(this, model);
            DrawWorkPanel(model);
            InkActionRenderer.Draw(this, model);

            DrawSetTransform(Vector2.Zero);

            // 全屏子页面盖住战斗界面（在战斗形态下点击状态/日志时展开查看，带关闭按钮返回战斗）。
            if (model.OpenPage != InkPage.None)
                InkPageRenderer.Draw(this, model);

            // 战后结算弹窗：在战斗场景末尾弹出，置于战斗界面最上层！
            if (model.ModalPage != null)
                InkModalRenderer.Draw(this, model.ModalPage, _modalAnimTime);

            return;
        }
        if (_dissolve != null)
        {
            _dissolve.QueueFree();
            _dissolve = null;
        }

        DrawTopBar(model);

        // 分区容器（主人 2026-10-01 定）：面板不是彼此独立的视觉元素——
        // 上区（地图＋日志）合成一个大框，下区（角色＋操作＋行动）合成另一个大框，
        // 各区只画一圈厚白雕花框，区内以竖分隔筋划分，不再每块面板各画一圈。
        // 操作面板常显（领地版＝工作安排＋页面入口；探索/战斗版换按钮内容）。
        InkFrame.Zone(this, InkLayout.UpperZone, InkLayout.UpperSplitX);
        InkFrame.Zone(this, InkLayout.LowerZone,
            InkLayout.LowerSplitLeftX, InkLayout.LowerSplitRightX);

        // 全屏子页面 / 开发模式整块占住画面：底层四块不再画
        //（它反正被盖住；被盖住的就不画，而不是画了再遮）。
        // 命中侧同理——BlockClick 遮挡矩形让被盖住的控件点不到。
        var fullPage = model.OpenPage != InkPage.None || model.DevMode;
        if (model.DevMode)
            InkPageRenderer.DrawDevPanels(this, model);
        else if (!fullPage)
        {
            InkMapRenderer.Draw(this, model);
            InkLogRenderer.Draw(this, model);
            InkCharRenderer.Draw(this, model);
            InkActionRenderer.Draw(this, model);
            DrawWorkPanel(model);
        }

        // 全屏子页面盖住整个主界面。
        if (model.OpenPage != InkPage.None)
            InkPageRenderer.Draw(this, model);

        // 遮盖层（对话）盖住地图区；再往上才是改名弹窗。
        InkOverlayRenderer.Draw(this, model);

        if (model.Renaming)
            InkRenameRenderer.Draw(this, model);

        if (model.ModalPage != null)
            InkModalRenderer.Draw(this, model.ModalPage, _modalAnimTime);

        DrawHover(model);
    }

    private InkCombatDissolve? _dissolve;

    /// <summary>滴墨消融层：原位的地图、日志与下三面板边框标题在它身上化掉。</summary>
    private void EnsureDissolveLayer(InkHubModel model, float progress)
    {
        if (_dissolve == null)
        {
            _dissolve = new InkCombatDissolve { Name = "CombatDissolve" };
            _dissolve.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_dissolve);
        }
        _dissolve.Model = model;
        _dissolve.ExtraDraw = null;
        _dissolve.Progress = progress;
        _dissolve.Visible = progress < 1f;
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private void DrawTopBar(InkHubModel model)
    {
        // 全屏子页 / 开发模式整块占住画面，顶栏一起让位。
        if (model.OpenPage != InkPage.None || model.DevMode)
            return;

        // 左上角地区名称：世界地图下的地区、地城、POI、任务地点与领地同级。
        InkDraw.Text(this, new Vector2(64, 44), model.PlaceTitle, 30, InkStyle.Line);
    }

    /// <summary>
    /// 中间面板：工作入口与页面入口（库存/交易/制作/开发）。
    /// 这组按钮原先在角色面板底部，现归拢到同一块里。
    /// </summary>
    private void DrawWorkPanel(InkHubModel model)
    {
        var topLabel = model.InCombat ? model.CombatTopLabel : "工作安排";
        var topSelected = model.InCombat && model.CombatTopSelected;
        var topEnabled = !model.InCombat || model.CombatTopEnabled;
        var btnFill = model.InCombat
            ? (topSelected ? new Color(InkStyle.Line, 0.12f) : (Color?)Colors.Transparent)
            : null;
        InkFrame.Button(this, InkLayout.WorkEntry, topLabel, selected: topSelected, enabled: topEnabled, fontSize: 26, centered: true, fill: btnFill);

        // 觉醒进度纯视觉绘制（严禁百分比文字）：在觉醒按钮底部内嵌细长墨线量规
        if (model.InCombat && model.CombatTopLabel is "觉醒" or "觉醒中")
        {
            var meterRect = new Rect2(
                InkLayout.WorkEntry.Position.X + 16f,
                InkLayout.WorkEntry.End.Y - 13f,
                InkLayout.WorkEntry.Size.X - 32f,
                5f);
            _visualAwakeningRatio = Mathf.Lerp(_visualAwakeningRatio, model.AwakeningRatio, 0.2f);
            if (Math.Abs(_visualAwakeningRatio - model.AwakeningRatio) > 0.005f)
                QueueRedraw();
            InkDraw.Meter(this, meterRect, _visualAwakeningRatio);
        }

        var entries = InkViewModel.PageEntries;
        var pageFill = model.InCombat ? (Color?)Colors.Transparent : null;
        for (var i = 0; i < entries.Length; i++)
        {
            string label;
            if (model.InCombat)
            {
                label = i switch
                {
                    0 => "自动",
                    1 => "状态",
                    2 => "日志",
                    3 => "逃跑",
                    _ => InkPageModel.Info(entries[i]).Label
                };
            }
            else
            {
                label = InkPageModel.Info(entries[i]).Label;
            }

            var enabled = model.InCombat
                || entries[i] != InkPage.Trade
                || _vm == null
                || _vm.Hub.TradeAvailable;

            // 战斗形态「自动」钮：开启后显示选中态（浅填）。
            var selected = model.InCombat && i == 0 && model.AutoBattle;
            InkFrame.Button(this, InkLayout.PageEntry(i), label, selected, enabled, 26, centered: true, fill: pageFill);
        }
    }

    private void DrawHover(InkHubModel model)
    {
        if (model.Hovered < 0 || model.Hovered >= model.Widgets.Count)
            return;

        var widget = model.Widgets[model.Hovered];

        // 无外观的纯热区不画悬停高亮：遮挡矩形（BlockClick）、推进区（OverlayAdvance）、
        // 通用弹窗全屏推进（ModalAdvance）、弹窗输入框聚焦（ModalInputFocus）、
        // 滑条轨道（ScrollJump）。否则鼠标落在它们上面会整块泛白——全屏遮挡矩形尤其
        // 会变成整屏闪白。滑条滑块自身有画法，由滑条渲染器负责。
        if (widget.Action is InkAction.BlockClick or InkAction.OverlayAdvance or InkAction.ScrollJump or InkAction.ModalAdvance or InkAction.ModalInputFocus)
            return;

        // 三角瓦片这类多边形热区：高亮跟着多边形走，
        // 否则相邻瓦片的外接矩形高亮会互相压边、看着像选错了。
        if (widget.Polygon is { Length: >= 3 } poly)
        {
            var loop = new Vector2[poly.Length + 1];
            System.Array.Copy(poly, loop, poly.Length);
            loop[^1] = poly[0];
            DrawColoredPolygon(poly, new Color(InkStyle.Line, 0.10f));
            InkDraw.Ink(this, loop, new Color(InkStyle.Line, 0.55f), 1f, 0.5f, 777);
            return;
        }

        var rect = widget.Rect.Grow(-1f);
        DrawRect(rect, new Color(InkStyle.Line, 0.10f));
        InkDraw.Ink(this, new[]
        {
            rect.Position,
            new Vector2(rect.End.X, rect.Position.Y),
            rect.End,
            new Vector2(rect.Position.X, rect.End.Y),
            rect.Position,
        }, new Color(InkStyle.Line, 0.55f), 1f, 0.5f, 777);
    }
}
