using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Hub;
using Rimisekai.Save;

namespace Rimisekai.Ink;

/// <summary>
/// 系统页：设置 / 保存进度 / 读取进度，盖在当前画面之上的模态整页。
///
/// 结构（2026-09-30 重写定稿）：
/// —— 每帧把全部可点件摊成一份 <see cref="_items"/>，绘制与命中只吃这一份；
/// —— 输入全部走 <see cref="_Input"/>（节点级、先于 GUI 分发、与本 Control 矩形无关），
///    本页是模态页，所有点击就地吞掉，绝不穿透到下层画面；
/// —— 悬停按 AGENTS 规范只做浅填与线宽加深，不改线色。
/// </summary>
public partial class InkSystemScreen : Control
{
    public const string PageSettings = "settings";
    public const string PageSave = "save";
    public const string PageLoad = "load";

    private const int SavesPerPage = 7;

    /// <summary>请求关闭本页并返回上一层（标题画面或当前据点）。</summary>
    public event Action? CloseRequested;

    /// <summary>读档成功，请求切入据点。</summary>
    public event Action<GameState, HubSession>? LoadRequested;

    private enum EntryKind
    {
        Button,   // 标准按钮：浅填边框 + 居中标签
        Row,      // 存档行：整行可选中，视觉是选中框 + 行文本
    }

    private sealed class Entry
    {
        public Rect2 Rect;
        public string Label = "";
        public EntryKind Kind = EntryKind.Button;
        public bool Enabled = true;
        public bool Selected;
        public Action? OnClick;
    }

    private readonly List<Entry> _items = new();
    private readonly List<SaveSlotInfo> _saves = new();

    private string _page = PageSettings;
    private GameState? _state;
    private HubSession? _hub;
    private ContentPack? _pack;
    private bool _canSave;
    private bool _hasSaves;                 // 存档列表是否非空（决定翻页与行渲染）
    private int _savePage;
    private int _selectedSave = -1;
    private int _hovered = -1;
    private string _notice = "";

    public InkSystemScreen()
    {
        Name = "SystemScreen";
    }

    public InkSystemScreen(string initialPage, GameState? state = null, HubSession? hub = null,
        ContentPack? pack = null)
    {
        Name = "SystemScreen";
        _state = state;
        _hub = hub;
        _pack = pack;
        _canSave = _state != null;
        _page = initialPage;
    }

    /// <summary>切到某子页。保存进度仅在据点内（有状态可存）时可用。</summary>
    public void ShowPage(string page)
    {
        _page = page switch
        {
            PageSave when _canSave => PageSave,
            PageLoad => PageLoad,
            _ => PageSettings,
        };
        _notice = "";
        _selectedSave = -1;
        _savePage = 0;
        _hovered = -1;
        if (_page is PageSave or PageLoad)
            ReloadSaves();
        QueueRedraw();
    }

    /// <summary>兼容旧调用的装配入口。</summary>
    public void Setup(GameState? state, HubSession? hub, ContentPack? pack,
        string initialPage = PageSettings)
    {
        _state = state;
        _hub = hub;
        _pack = pack;
        _canSave = _state != null;
        ShowPage(initialPage);
    }

    public override void _Ready()
    {
        // 矩形由路由挂载前的锚点决定；这里只保证悬停初始态干净。
        MouseFilter = MouseFilterEnum.Stop;
        InkSettings.EnsureLoaded();
        if (_page is PageSave or PageLoad)
            ReloadSaves();
    }

    private void ReloadSaves()
    {
        _saves.Clear();
        _saves.AddRange(InkSaveStore.ListSaves());
        if (_selectedSave >= _saves.Count)
            _selectedSave = _saves.Count - 1;
    }

    // ---------- 输入：标准遮盖（自身全屏矩形隔离），不主动吞事件 ----------

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo
            && key.Keycode == Key.Escape)
        {
            CloseRequested?.Invoke();
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            var hovered = HitEntry(motion.Position);
            if (hovered != _hovered)
            {
                _hovered = hovered;
                QueueRedraw();
            }
            return;
        }

        if (@event is InputEventMouseButton mouse && mouse.Pressed
            && mouse.ButtonIndex == MouseButton.Left)
        {
            // 事件位即引擎换算后的画布坐标（窗口缩放已含在内）。
            var index = HitEntry(mouse.Position);
            if (index >= 0)
            {
                var item = _items[index];
                if (item.Enabled)
                {
                    item.OnClick?.Invoke();
                    QueueRedraw();
                    AcceptEvent();
                }
            }
        }
    }

    /// <summary>命中：逆序遍历（后画者优先），返回条目下标；-1 无命中。</summary>
    private int HitEntry(Vector2 at)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (item.Enabled && item.Rect.HasPoint(at))
                return i;
        }
        return -1;
    }

    // ---------- 绘制：与命中同吃一份 _items ----------

    public override void _Draw()
    {
        _items.Clear();

        var title = _page switch
        {
            PageSave => "系统 · 保存进度",
            PageLoad => "系统 · 读取进度",
            _ => "系统 · 游戏设置",
        };
        var subtitle = _page switch
        {
            PageSave => "创建全新独立快照文件",
            PageLoad => "选择已存入的进度记录",
            _ => "显示与声音设定",
        };

        InkFrame.PageShell(this, title, wood: true, subtitle: subtitle);

        AddButton(InkLayout.FullPageClose, "关闭", () => CloseRequested?.Invoke());
        AddButton(InkLayout.SystemSidebarEntry(0), "设置", () => ShowPage(PageSettings),
            selected: _page == PageSettings);
        AddButton(InkLayout.SystemSidebarEntry(1), "保存进度", () => ShowPage(PageSave),
            enabled: _canSave, selected: _page == PageSave);
        AddButton(InkLayout.SystemSidebarEntry(2), "读取进度", () => ShowPage(PageLoad),
            selected: _page == PageLoad);

        InkFrame.Panel(this, InkLayout.SystemSidebar);

        switch (_page)
        {
            case PageSave:
                ComposeSavePage();
                break;
            case PageLoad:
                ComposeLoadPage();
                break;
            default:
                ComposeSettingsPage();
                break;
        }

        // 可点件最后统一落笔，保证盖在各自底纹上。
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.Kind != EntryKind.Button)
                continue;
            InkFrame.Button(this, item.Rect, item.Label, item.Selected, item.Enabled, 26,
                centered: true, hovered: i == _hovered);
        }
    }

    private void AddButton(Rect2 rect, string label, Action? onClick = null,
        bool enabled = true, bool selected = false)
    {
        _items.Add(new Entry
        {
            Rect = rect,
            Label = label,
            Kind = EntryKind.Button,
            Enabled = enabled,
            Selected = selected,
            OnClick = onClick,
        });
    }

    private void AddRow(Rect2 rect, Action? onClick, bool selected)
    {
        _items.Add(new Entry
        {
            Rect = rect,
            Kind = EntryKind.Row,
            Enabled = true,
            Selected = selected,
            OnClick = onClick,
        });
    }

    // ---------- 设置页 ----------

    private void ComposeSettingsPage()
    {
        InkFrame.Panel(this, InkLayout.SystemContent);

        OptionRow(0, "窗口模式", new[]
        {
            ("窗口化", InkSettings.CurrentWindowMode == WindowModeOption.Windowed,
                (Action)(() => InkSettings.ApplyWindowMode(WindowModeOption.Windowed))),
            ("无边框全屏", InkSettings.CurrentWindowMode == WindowModeOption.Borderless,
                (Action)(() => InkSettings.ApplyWindowMode(WindowModeOption.Borderless))),
            ("独占全屏", InkSettings.CurrentWindowMode == WindowModeOption.ExclusiveFullscreen,
                (Action)(() => InkSettings.ApplyWindowMode(WindowModeOption.ExclusiveFullscreen))),
        });

        OptionRow(1, "主音量", new[]
        {
            ("静音", InkSettings.CurrentMasterVolume <= 0.001f,
                (Action)(() => InkSettings.ApplyMasterVolume(0f))),
            ("25%", Math.Abs(InkSettings.CurrentMasterVolume - 0.25f) < 0.05f,
                (Action)(() => InkSettings.ApplyMasterVolume(0.25f))),
            ("50%", Math.Abs(InkSettings.CurrentMasterVolume - 0.50f) < 0.05f,
                (Action)(() => InkSettings.ApplyMasterVolume(0.50f))),
            ("75%", Math.Abs(InkSettings.CurrentMasterVolume - 0.75f) < 0.05f,
                (Action)(() => InkSettings.ApplyMasterVolume(0.75f))),
            ("100%", InkSettings.CurrentMasterVolume >= 0.95f,
                (Action)(() => InkSettings.ApplyMasterVolume(1f))),
        });

        OptionRow(2, "垂直同步", new[]
        {
            ("开启", InkSettings.CurrentVSync,
                (Action)(() => InkSettings.ApplyVSync(true))),
            ("关闭", !InkSettings.CurrentVSync,
                (Action)(() => InkSettings.ApplyVSync(false))),
        });

        var tip = new Rect2(InkLayout.SystemContent.Position.X + InkLayout.Pad,
            InkLayout.SystemContent.End.Y - 50f,
            InkLayout.SystemContent.Size.X - InkLayout.Pad * 2f, 32f);
        InkDraw.TextBounded(this, tip, "设置修改后即刻生效并自动保存。", 18, 14, InkStyle.Dim, "lm");
    }

    /// <summary>设置行：左标签，右一排互斥选项，行底一条分割细线。</summary>
    private void OptionRow(int index, string label,
        (string Label, bool Selected, Action OnClick)[] options)
    {
        var row = InkLayout.SystemRow(index);
        InkDraw.TextBounded(this,
            new Rect2(row.Position.X, row.Position.Y, 240f, row.Size.Y), label, 22, 16);

        for (var i = 0; i < options.Length; i++)
        {
            var rect = InkLayout.SystemOption(row, i, options.Length);
            AddButton(rect, options[i].Label, options[i].OnClick, selected: options[i].Selected);
        }
        InkFrame.RowDivider(this, row);
    }

    // ---------- 保存 / 读取页 ----------

    private void ComposeSavePage()
    {
        InkFrame.Panel(this, InkLayout.SystemContent);

        var saveBtn = InkLayout.DetailButton(InkLayout.SystemContent, 0, 1);
        AddButton(saveBtn, "保存当前进度", () =>
        {
            if (_state == null)
                return;
            var fileName = InkSaveStore.SaveNew(_state, _hub, out var error);
            _notice = fileName != null ? "进度已成功保存。" : $"保存失败：{error}";
            ReloadSaves();
        }, enabled: _canSave);

        ComposeSaveList(canSelect: false);
        ComposePaging();
    }

    private void ComposeLoadPage()
    {
        InkFrame.Panel(this, InkLayout.SystemContent);

        var valid = _selectedSave >= 0 && _selectedSave < _saves.Count
            && !_saves[_selectedSave].IsCorrupt;
        var loadBtn = InkLayout.DetailButton(InkLayout.SystemContent, 0, 1);
        AddButton(loadBtn, "读取选中进度", () =>
        {
            if (_selectedSave >= 0 && _selectedSave < _saves.Count)
                PerformLoad(_saves[_selectedSave]);
        }, enabled: valid);

        ComposeSaveList(canSelect: true);
        ComposePaging();
    }

    private void ComposeSaveList(bool canSelect)
    {
        if (_saves.Count == 0)
        {
            var empty = new Rect2(InkLayout.SystemContent.Position.X + 40f,
                InkLayout.SystemContent.Position.Y + 60f,
                InkLayout.SystemContent.Size.X - 80f, 100f);
            InkDraw.TextBounded(this, empty, "暂无存档记录", 22, 16, InkStyle.Dim, "cm");
            return;
        }

        var first = _savePage * SavesPerPage;
        var last = Math.Min(_saves.Count, first + SavesPerPage);

        for (var i = first; i < last; i++)
        {
            var slotIndex = i - first;
            var rowRect = InkLayout.SystemRow(slotIndex);
            var slot = _saves[i];
            var isSelected = canSelect && _selectedSave == i;

            if (isSelected)
                InkFrame.Selection(this, rowRect);

            var nameColor = slot.IsCorrupt ? InkStyle.Dim : InkStyle.Line;
            InkDraw.TextBounded(this,
                new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 8f, 500f, 26f),
                slot.IsCorrupt ? $"{slot.FileName} (文件损坏)" : slot.Summary,
                20, 14, nameColor, "lm");
            if (slot.IsCorrupt)
            {
                InkDraw.TextBounded(this,
                    new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 34f, 800f, 20f),
                    slot.ErrorMessage, 16, 12, InkStyle.Dim, "lm");
            }
            else
            {
                InkDraw.TextBounded(this,
                    new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 34f, 500f, 20f),
                    slot.Timestamp, 16, 12, InkStyle.Dim, "lm");
                InkDraw.TextBounded(this,
                    new Rect2(rowRect.End.X - 340f, rowRect.Position.Y + 16f, 320f, 28f),
                    slot.FileName, 16, 12, InkStyle.Dim, "rm");
            }

            InkFrame.RowDivider(this, rowRect);

            var captured = i;
            AddRow(rowRect, () =>
            {
                if (!canSelect)
                    return;
                _selectedSave = captured;
                _notice = _saves[captured].IsCorrupt
                    ? $"该存档存在异常：{_saves[captured].ErrorMessage}"
                    : "";
            }, selected: isSelected);
        }
    }

    private void ComposePaging()
    {
        if (_saves.Count > 0)
        {
            var maxPage = Math.Max(0, (_saves.Count - 1) / SavesPerPage);
            AddButton(InkLayout.PagePager(InkLayout.SystemContent, -1), "上一页", () =>
            {
                if (_savePage > 0)
                {
                    _savePage--;
                    QueueRedraw();
                }
            }, enabled: _savePage > 0);
            AddButton(InkLayout.PagePager(InkLayout.SystemContent, 1), "下一页", () =>
            {
                if (_savePage < maxPage)
                {
                    _savePage++;
                    QueueRedraw();
                }
            }, enabled: _savePage < maxPage);
        }

        if (!string.IsNullOrEmpty(_notice))
        {
            var notice = new Rect2(InkLayout.SystemContent.Position.X + InkLayout.Pad,
                InkLayout.SystemContent.End.Y - 110f,
                InkLayout.SystemContent.Size.X - InkLayout.Pad * 2f, 32f);
            InkDraw.TextBounded(this, notice, _notice, 18, 14, InkStyle.Line, "lm");
        }
    }

    private void PerformLoad(SaveSlotInfo slot)
    {
        if (slot.IsCorrupt)
        {
            _notice = $"无法读取损坏的存档：{slot.ErrorMessage}";
            QueueRedraw();
            return;
        }
        if (_pack == null)
        {
            _notice = "错误：内容包未就绪，无法加载。";
            QueueRedraw();
            return;
        }
        if (InkSaveStore.TryLoad(slot.FilePath, _pack, out var state, out var hub, out var error))
        {
            _notice = "读取成功。";
            LoadRequested?.Invoke(state!, hub!);
        }
        else
        {
            _notice = $"读取存档失败：{error}";
            QueueRedraw();
        }
    }
}
