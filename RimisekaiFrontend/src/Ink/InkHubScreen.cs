using System;
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

    /// <summary>界面自身的视图状态（分页、打开的页面、改名输入等），不进 Core。</summary>
    private readonly InkUiState _ui = new();

    /// <summary>请求打开系统页（settings/save/load），路由监听。</summary>
    public event Action<string>? SystemRequested;

    /// <summary>请求打开任务页，路由监听。</summary>
    public event Action? QuestRequested;

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
        Refresh();
    }

    /// <summary>打开一个页面，并把该页的查询与选中状态复位。</summary>
    private void OpenPage(InkPage page)
    {
        _ui.OpenPage = page;
        _ui.PageSelected = -1;
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
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // 尺寸与位置由 project.godot 的 stretch 统一处理，这里不设锚点。
    }

    /// <summary>
    /// 打字机推进。只有对话层有文字、且当前句还没显示完时才逐帧补字，
    /// 其余时间什么都不做，不产生无谓重绘。
    /// </summary>
    public override void _Process(double delta)
    {
        var overlay = _vm?.Hub.Overlay;
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
        var overlay = _vm?.Hub.Overlay;
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
        var overlay = _vm?.Hub.Overlay;
        return overlay == null || _ui.TypeRevealed >= overlay.Text.Length;
    }

    /// <summary>重建模型并请求重绘。任何状态变更后都走这里。</summary>
    private void Refresh()
    {
        if (_vm == null)
            return;
        _model = InkHubModel.Build(_vm, _ui);
        QueueRedraw();
    }

    // ---------- 输入 ----------

    public override void _GuiInput(InputEvent @event)
    {
        if (_vm == null || _model == null)
            return;

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
            var hit = _model.Hit(motion.Position);
            var index = hit == null ? -1 : IndexOf(hit.Value);
            if (index != _ui.Hovered)
            {
                _ui.Hovered = index;
                Refresh();
            }
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            return;

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

        Dispatch(target.Value);
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
    private void Dispatch(InkWidget widget)
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
            case InkAction.OverlayAdvance:
                // 打字机还没走完就先补完这一句；已显示完才推进下一句。
                if (!TypingDone())
                    SkipTyping();
                else
                    vm.Hub.AdvanceOverlay();
                break;

            case InkAction.OverlayChoice:
                var overlay = vm.Hub.Overlay;
                if (overlay != null && widget.Index < overlay.Choices.Count)
                    vm.Hub.Choose(overlay.Choices[widget.Index].Id);
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

            // ---- 交流 / 行动 ----
            case InkAction.Social:
            {
                var social = InkViewModel.SocialActions[widget.Index];
                _ui.Notice = vm.Hub.Social(social.Action)
                    ? $"你{social.Label}了对方。"
                    : $"无法{social.Label}。";
                break;
            }

            case InkAction.Place:
            {
                var place = InkViewModel.PlaceActions[widget.Index];
                _ui.Notice = vm.Hub.Act(place.Action) ? "" : $"无法{place.Label}。";
                break;
            }

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
                OpenPage(InkPage.Work);
                _ui.Notice = "";
                break;

            case InkAction.PageEntry:
            {
                var entries = _model?.PageEntries ?? System.Array.Empty<InkPage>();
                if (widget.Index >= entries.Count)
                    break;
                if (entries[widget.Index] == InkPage.Develop)
                {
                    // 开发是主界面的编辑模式（四面板容器不变），不是全屏页。
                    _ui.DevMode = true;
                    _ui.OpenPage = InkPage.None;
                    _ui.PageSelected = -1;
                    _ui.DevSelectedFacility = -1;
                    _ui.DevPlacingRoom = -1;
                }
                else
                {
                    OpenPage(entries[widget.Index]);
                }
                _ui.Notice = "";
                break;
            }

            case InkAction.PageTab:
                if (widget.Index < InkPageTabs.All.Length)
                    OpenPage(InkPageTabs.All[widget.Index]);
                break;

            case InkAction.WorkCell:
            {
                // 点格子：选中它，并把优先级循环到下一档（空白 → 1 → 2 → 3 → 4 → 空白）。
                var r = widget.Index / 1000;
                var c = widget.Index % 1000;
                _ui.WorkRow = r;
                _ui.WorkColumn = c;
                _ui.Notice = CycleWorkPriority(vm, r, c);
                break;
            }

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
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.RoomRows.Count)
                {
                    // 点房间=选中（右下显示它的详情）；点空房间保持房间列表。
                    _ui.PageSelected = widget.Index;
                    _ui.DevSelectedFacility = -1;
                }
                break;
            }

            case InkAction.DevPickFacility:
            {
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.FacilityRows.Count)
                {
                    // 存行下标：构建器按下标回选（详情/放置都以它定位）。
                    _ui.DevSelectedFacility = widget.Index;
                }
                break;
            }

            case InkAction.DevDetailAction:
            {
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.DetailActions.Count)
                {
                    var row = dev.DetailActions[widget.Index];
                    switch (row.Action)
                    {
                        case InkPageAction.RemoveFacility:
                            // 按钮名带动词前缀，反馈里的对象名取 Note（裸设施名）。
                            _ui.Notice = vm.Hub.RemoveFacility(row.TargetNumber)
                                ? $"拆除了{(row.Note.Length > 0 ? row.Note : row.Name)}。"
                                : "无法拆除。";
                            break;

                        case InkPageAction.PlaceFacility:
                            if (dev.RoomId < 0)
                            {
                                _ui.Notice = "先在上方网格里选中一个房间。";
                                break;
                            }
                            _ui.Notice = vm.Hub.PlaceFacility(row.TargetNumber, dev.RoomId)
                                ? $"把{(row.Note.Length > 0 ? row.Note : row.Name)}放进了{dev.RoomDetailTitle}。"
                                : "无法放置。";
                            break;
                    }
                }
                break;
            }

            case InkAction.DevBuildFacility:
            {
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.FacilityCatalog.Count)
                {
                    var row = dev.FacilityCatalog[widget.Index];
                    _ui.Notice = vm.Hub.BuildFacilityDef(row.TargetNumber)
                        ? $"建造了{row.Name}（未放置）。"
                        : "材料不足。";
                }
                break;
            }

            case InkAction.DevBuildRoom:
            {
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.RoomCatalog.Count)
                {
                    var row = dev.RoomCatalog[widget.Index];
                    _ui.Notice = vm.Hub.BuildRoomDef(row.TargetNumber)
                        ? $"建造了{row.Name}（未放置）。"
                        : "材料不足。";
                }
                break;
            }

            case InkAction.DevPlaceRoomAt:
            {
                var dev = _model?.Dev;
                if (dev != null && _ui.DevPlacingRoom >= 0)
                {
                    var x = widget.Index % InkLayout.GridCols;
                    var y = widget.Index / InkLayout.GridCols;
                    if (vm.Hub.PlaceRoom(_ui.DevPlacingRoom, x, y))
                    {
                        _ui.Notice = "房间已放到网格上。";
                        _ui.DevPlacingRoom = -1;
                    }
                }
                break;
            }

            case InkAction.DevDemolishRoom:
            {
                var dev = _model?.Dev;
                if (dev != null && widget.Index < dev.Rooms.Count)
                {
                    var cell = dev.Rooms[widget.Index];
                    _ui.Notice = vm.Hub.RemoveRoom(cell.Id)
                        ? $"拆除了{cell.Name}。"
                        : "无法拆除（当前所在房间不能拆除）。";
                    if (_ui.Notice.StartsWith("拆除了"))
                    {
                        _ui.PageSelected = -1;
                    }
                }
                break;
            }

            // ---- 设施交互 / 存储配置 ----
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

            case InkAction.StorageClose:
                vm.Hub.CloseStorage();
                _ui.Notice = "";
                break;

            case InkAction.PageClose:
                _ui.OpenPage = InkPage.None;
                _ui.DevMode = false;
                break;

            case InkAction.DevExit:
                _ui.DevMode = false;
                _ui.PageSelected = -1;
                _ui.DevPlacingFacility = -1;
                _ui.DevPlacingRoom = -1;
                break;

            case InkAction.PageRow:
                RunPageRow(vm, widget);
                break;

            case InkAction.PageNext:
                _ui.CardPage++;
                _ui.Notice = "";
                break;
        }

        Refresh();
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
            InkPageAction.Buy => vm.Hub.MarketTrade(row.TargetId, 1, selling: false)
                ? $"买入了{target}。"
                : "买不起或没有货。",
            InkPageAction.Sell => vm.Hub.MarketTrade(row.TargetId, 1, selling: true)
                ? $"卖出了{target}。"
                : "没有可卖的货。",
            InkPageAction.Craft => vm.Hub.Craft(row.TargetId) ? $"制作了{target}。" : "材料不足。",
            InkPageAction.Develop => vm.Hub.Develop(row.TargetNumber) ? $"开拓了{target}。" : "钱不够。",
            InkPageAction.Build => vm.Hub.BuildFacility(row.TargetNumber)
                ? $"在{target}建造了{(row.Note.Length > 0 ? row.Note : row.Name)}。"
                : "钱不够。",
            InkPageAction.RemoveFacility => vm.Hub.RemoveFacility(row.TargetNumber)
                ? $"拆除了{(row.Note.Length > 0 ? row.Note : target)}。"
                : "无法拆除。",
            InkPageAction.AssignTask => AssignTask(vm, row),
            InkPageAction.SetWorkPriority => SetWorkPriority(vm, row),
            _ => "",
        };
    }

    /// <summary>
    /// 工作页：把选中格的优先级循环到下一档（空白 → 1 → 2 → 3 → 4 → 空白）。
    /// 点格子直接循环，与 RimWorld 的手感一致；校验交给 Core。
    /// </summary>
    private static string CycleWorkPriority(InkViewModel vm, int row, int column)
    {
        if (row < 0 || row >= ActionKindMap.WorkOrdered.Length)
            return "";
        var columns = vm.WorkColumns();
        if (column < 0 || column >= columns.Count)
            return "";
        var characterId = columns[column].CharacterId;
        var task = ActionKindMap.WorkOrdered[row];
        var next = InkWorkPageBuilder.NextPriority(vm.Hub.PriorityOf(characterId, task));
        if (!vm.Hub.SetPriority(characterId, task, next))
            return "无法安排这项工作。";
        var who = vm.NameOf(characterId);
        return next <= 0
            ? $"{who}不再做{InkText.ActionKind(task)}。"
            : $"安排{who}做{InkText.ActionKind(task)}，优先级 {next}。";
    }

    /// <summary>
    /// 工作页：把选中格的优先级循环到下一档（空白 → 1 → 2 → 3 → 4 → 空白）。
    /// TargetNumber 是工作类型下标，TargetId 是角色 Id。
    /// </summary>
    private static string SetWorkPriority(InkViewModel vm, InkPageRow row)
    {
        if (!int.TryParse(row.TargetId, out var characterId))
            return "找不到这个人。";
        if (row.TargetNumber < 0 || row.TargetNumber >= ActionKindMap.WorkOrdered.Length)
            return "不认识这项工作。";
        var task = ActionKindMap.WorkOrdered[row.TargetNumber];
        var next = InkWorkPageBuilder.NextPriority(vm.Hub.PriorityOf(characterId, task));
        if (!vm.Hub.SetPriority(characterId, task, next))
            return "无法安排这项工作。";
        var who = vm.NameOf(characterId);
        return next <= 0
            ? $"{who}不再做{InkText.ActionKind(task)}。"
            : $"安排{who}做{InkText.ActionKind(task)}，优先级 {next}。";
    }

    /// <summary>
    /// 日程页：把当前角色的某一段改成点中的开关（空闲 / 工作 / 不干活）。
    /// 校验交给 Core（<see cref="HubSession.Assign"/>），这里只负责把结果说清楚。
    /// </summary>
    private static string AssignTask(InkViewModel vm, InkPageRow row)
    {
        var who = vm.ChatPartner();
        if (who == null)
            return "没有可安排的人。";
        if (!Enum.TryParse<SlotMode>(row.TargetId, out var mode))
            return "不认识这段时间的用法。";
        if (!vm.Hub.Assign(who.Id, row.TargetNumber, mode))
            return "无法安排这段时间。";
        var slot = InkText.WorkSlot(row.TargetNumber);
        return mode == SlotMode.Free
            ? $"把{slot}这段留空了。"
            : $"安排{who.Name}在{slot}{InkText.SlotMode(mode)}。";
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

    /// <summary>点地图格：开拓、移动，或给出为什么走不了。</summary>
    private static string Visit(InkViewModel vm, Room room)
    {
        if (!room.Open)
        {
            return vm.Hub.Develop(room.Id)
                ? $"开拓了{room.Name}。"
                : $"{room.Name}需要 ${room.OpenCost:N0}。";
        }

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

    /// <summary>遮盖层（对话）是否开着，供开发期核对读取。</summary>
    public bool DebugOverlayOpen => _vm != null && _vm.Hub.Overlay != null;

    /// <summary>当前反馈文本，供开发期核对读取。</summary>
    public string DebugNotice => _ui.Notice;

    /// <summary>某坐标命中的元素描述，供开发期核对定位问题。</summary>
    public string DebugHitAt(Vector2 at)
    {
        var hit = _model?.Hit(at);
        return hit == null ? "(无)" : $"{hit.Value.Action}#{hit.Value.Index} \"{hit.Value.Label}\"";
    }

    /// <summary>按名字打开子页面，可指定选中行与搜索词，供开发期核对出图。</summary>
    public void DebugOpenPage(string page, int selected = -1, string search = "")
    {
        _ui.OpenPage = page switch
        {
            "stock" => InkPage.Stock,
            "trade" => InkPage.Trade,
            "craft" => InkPage.Craft,
            "develop" => InkPage.Develop,
            // 角色三页从聊天层右上角进入，出图时也要能直接开到。
            "status" => InkPage.Status,
            "skills" => InkPage.Skills,
            "schedule" => InkPage.Schedule,
            "work" => InkPage.Work,
            _ => InkPage.None,
        };
        ResetPageQuery();
        _ui.PageSelected = selected;
        _ui.PageSearch = search;
        _ui.Hovered = -1;
        Refresh();
    }

    /// <summary>进入开发模式，供开发期核对出图。</summary>
    public void DebugDevMode()
    {
        _ui.DevMode = true;
        _ui.PageSelected = -1;
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

    /// <summary>打开改名弹窗，供开发期核对出图。</summary>
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

        InkFrame.Panel(this, InkLayout.MapPanel, corner: 24f, fill: InkStyle.Panel);
        InkFrame.Panel(this, InkLayout.LogPanel, corner: 24f, fill: InkStyle.Panel);
        InkFrame.Panel(this, InkLayout.CharPanel, corner: 24f, fill: InkStyle.Panel);
        InkFrame.Panel(this, InkLayout.ActPanel, corner: 24f, fill: InkStyle.Panel);
        // 工作面板只在有多个可派角色时占位；单角色时那块空位留白。
        if (model.ShowWorkPanel)
            InkFrame.Panel(this, InkLayout.WorkPanel, corner: 24f, fill: InkStyle.Panel);

        DrawTopBar(model);

        // 开发模式：四面板容器不变，内容切成领地编辑；否则画据点四块。
        if (model.DevMode)
            InkPageRenderer.DrawDevPanels(this, model);
        else
        {
            InkMapRenderer.Draw(this, model);
            InkLogRenderer.Draw(this, model);
            InkCharRenderer.Draw(this, model);
            InkActionRenderer.Draw(this, model);
            if (model.ShowWorkPanel)
                DrawWorkPanel(model);
        }

        // 全屏子页面盖住整个主界面。
        if (model.OpenPage != InkPage.None)
            InkPageRenderer.Draw(this, model);

        // 遮盖层（对话）盖住地图区；再往上才是改名弹窗。
        InkOverlayRenderer.Draw(this, model);

        if (model.Renaming)
            InkRenameRenderer.Draw(this, model);

        DrawHover(model);
    }

    private void DrawTopBar(InkHubModel model)
    {
        // 左上角标题：[图层·区域·房间] 的完整地点串；领地等级达标时点它可改名。
        InkDraw.Text(this, new Vector2(64, 44), model.PlaceTitle, 30, InkStyle.Line);

        // 顶栏左右文字共用同一条基线：不同字号也能底部对齐。
        var baseline = 44f + InkStyle.Font.GetAscent(30);

        InkDraw.Text(this, new Vector2(InkLayout.CanvasWidth - 64,
            baseline - InkStyle.Font.GetAscent(22)), model.HeaderRight, 22, InkStyle.Line, "rt");
        InkFrame.HeaderRule(this, 56f, InkLayout.CanvasWidth - 56f, 100f);

        // 底部右侧的世界层切换与任务/系统入口，只在普通主界面显示。
        if (!model.DevMode && model.OpenPage == InkPage.None && !model.StorageOpen)
        {
            InkFrame.Button(this, InkLayout.HubWorldEntry,
                model.WorldLayer ? "领地" : "世界", fontSize: 18, centered: true);
            InkFrame.Button(this, InkLayout.HubQuestEntry, "任务", fontSize: 18, centered: true);
            InkFrame.Button(this, InkLayout.HubSystemEntry, "设置", fontSize: 18, centered: true);
        }
    }

    /// <summary>工作入口面板：角色面板与行动面板之间那块空位，点开工作页。</summary>
    private void DrawWorkPanel(InkHubModel model)
    {
        InkFrame.Title(this, InkLayout.WorkPanel, "工作", 26);
        InkFrame.Button(this, InkLayout.WorkEntry, "工作安排", fontSize: 22, centered: true);
        InkDraw.Text(this, new Vector2(InkLayout.WorkEntry.GetCenter().X,
            InkLayout.WorkEntry.End.Y + 26f), "按优先级派人干活", 18, InkStyle.Dim, "cm");
    }

    private void DrawHover(InkHubModel model)
    {
        if (model.Hovered < 0 || model.Hovered >= model.Widgets.Count)
            return;

        var rect = model.Widgets[model.Hovered].Rect.Grow(-1f);
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
