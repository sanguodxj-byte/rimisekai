using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Flow;
using Rimisekai.Hub;
using Rimisekai.Save;

namespace Rimisekai.Ink;

/// <summary>
/// 系统设置、存档、读档管理全屏界面。
/// 严格使用线稿单色风格与 InkLayout 提供的几何坐标，禁止 emoji 与彩色。
/// </summary>
public partial class InkSystemScreen : Control
{
    public const string PageSettings = "settings";
    public const string PageSave = "save";
    public const string PageLoad = "load";

    /// <summary>请求关闭当前系统页面并返回上一层（标题画面或当前据点）。</summary>
    public event Action? CloseRequested;

    /// <summary>读档成功请求切入据点。</summary>
    public event Action<GameState, HubSession>? LoadRequested;

    private string _currentPage = PageSettings;
    private GameState? _state;
    private HubSession? _hub;
    private ContentPack? _pack;
    private bool _canSave;

    // 读档/存档列表状态
    private List<SaveSlotInfo> _saves = new();
    private int _selectedSaveIndex = -1;
    private int _savePageIndex = 0;
    private const int SavesPerPage = 7;
    private string _statusNotice = "";

    // 侧边栏项配置
    private readonly struct SidebarTab
    {
        public readonly string Id;
        public readonly string Label;
        public readonly bool Enabled;

        public SidebarTab(string id, string label, bool enabled)
        {
            Id = id;
            Label = label;
            Enabled = enabled;
        }
    }

    private List<SidebarTab> GetSidebarTabs() => new()
    {
        new(PageSettings, "设置", true),
        new(PageSave, "保存进度", _canSave),
        new(PageLoad, "读取进度", true),
    };

    public InkSystemScreen()
    {
        Name = "SystemScreen";
        _canSave = false;
    }

    public InkSystemScreen(string initialPage, GameState? state = null, HubSession? hub = null, ContentPack? pack = null)
    {
        Name = "SystemScreen";
        _state = state;
        _hub = hub;
        _pack = pack;
        _canSave = _state != null;
        _currentPage = initialPage;
    }

    public void Setup(GameState? state, HubSession? hub, ContentPack? pack, string initialPage = PageSettings)
    {
        _state = state;
        _hub = hub;
        _pack = pack;
        _canSave = _state != null;
        ShowPage(initialPage);
    }

    public void ShowPage(string page)
    {
        _currentPage = page switch
        {
            PageSave when _canSave => PageSave,
            PageLoad => PageLoad,
            _ => PageSettings,
        };

        _statusNotice = "";
        _selectedSaveIndex = -1;
        _savePageIndex = 0;

        if (_currentPage is PageSave or PageLoad)
        {
            RefreshSaves();
        }
        else if (_currentPage == PageSettings)
        {
            InkSettings.EnsureLoaded();
        }

        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        InkSettings.EnsureLoaded();
        if (_currentPage is PageSave or PageLoad)
            RefreshSaves();
    }

    private void RefreshSaves()
    {
        _saves = InkSaveStore.ListSaves();
        if (_selectedSaveIndex >= _saves.Count)
            _selectedSaveIndex = _saves.Count - 1;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Escape)
            {
                CloseRequested?.Invoke();
                AcceptEvent();
                return;
            }
        }

        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            HandleClick(mouse.Position);
            AcceptEvent();
        }
    }

    private void HandleClick(Vector2 pos)
    {
        // 1. 关闭按钮
        if (InkLayout.FullPageClose.HasPoint(pos))
        {
            CloseRequested?.Invoke();
            return;
        }

        // 2. 侧边栏点击
        var tabs = GetSidebarTabs();
        for (var i = 0; i < tabs.Count; i++)
        {
            var tabRect = InkLayout.SystemSidebarEntry(i);
            if (tabRect.HasPoint(pos))
            {
                if (tabs[i].Enabled && _currentPage != tabs[i].Id)
                {
                    ShowPage(tabs[i].Id);
                }
                return;
            }
        }

        // 3. 子页面内部点击
        switch (_currentPage)
        {
            case PageSettings:
                HandleSettingsClick(pos);
                break;
            case PageSave:
                HandleSaveClick(pos);
                break;
            case PageLoad:
                HandleLoadClick(pos);
                break;
        }
    }

    private void HandleSettingsClick(Vector2 pos)
    {
        // 行 0: 窗口模式 (3 个选项)
        var row0 = InkLayout.SystemRow(0);
        if (row0.HasPoint(pos))
        {
            for (var i = 0; i < 3; i++)
            {
                var optRect = InkLayout.SystemOption(row0, i, 3);
                if (optRect.HasPoint(pos))
                {
                    InkSettings.ApplyWindowMode((WindowModeOption)i);
                    QueueRedraw();
                    return;
                }
            }
        }

        // 行 1: 主音量 (5 档：静音, 25%, 50%, 75%, 100%)
        var row1 = InkLayout.SystemRow(1);
        if (row1.HasPoint(pos))
        {
            var vols = new[] { 0.0f, 0.25f, 0.50f, 0.75f, 1.0f };
            for (var i = 0; i < vols.Length; i++)
            {
                var optRect = InkLayout.SystemOption(row1, i, vols.Length);
                if (optRect.HasPoint(pos))
                {
                    InkSettings.ApplyMasterVolume(vols[i]);
                    QueueRedraw();
                    return;
                }
            }
        }

        // 行 2: 垂直同步 (2 个选项：开启, 关闭)
        var row2 = InkLayout.SystemRow(2);
        if (row2.HasPoint(pos))
        {
            var optOn = InkLayout.SystemOption(row2, 0, 2);
            var optOff = InkLayout.SystemOption(row2, 1, 2);
            if (optOn.HasPoint(pos))
            {
                InkSettings.ApplyVSync(true);
                QueueRedraw();
                return;
            }
            if (optOff.HasPoint(pos))
            {
                InkSettings.ApplyVSync(false);
                QueueRedraw();
                return;
            }
        }
    }

    private void HandleSaveClick(Vector2 pos)
    {
        // 顶部“新建保存”动作按钮
        var newSaveBtn = InkLayout.DetailButton(InkLayout.SystemContent, 0, 1);
        if (newSaveBtn.HasPoint(pos))
        {
            if (_state != null)
            {
                var fileName = InkSaveStore.SaveNew(_state, _hub, out var error);
                if (fileName != null)
                {
                    _statusNotice = "进度已成功保存。";
                    RefreshSaves();
                }
                else
                {
                    _statusNotice = $"保存失败：{error}";
                }
                QueueRedraw();
            }
            return;
        }

        // 翻页与列表项处理
        HandleSaveListClick(pos, canSelect: false);
    }

    private void HandleLoadClick(Vector2 pos)
    {
        // 底部动作区：“读取选中进度”
        if (_selectedSaveIndex >= 0 && _selectedSaveIndex < _saves.Count)
        {
            var loadBtn = InkLayout.DetailButton(InkLayout.SystemContent, 0, 1);
            if (loadBtn.HasPoint(pos))
            {
                PerformLoad(_saves[_selectedSaveIndex]);
                return;
            }
        }

        // 翻页与列表项处理
        HandleSaveListClick(pos, canSelect: true);
    }

    private void HandleSaveListClick(Vector2 pos, bool canSelect)
    {
        // 翻页
        var prevPager = InkLayout.PagePager(InkLayout.SystemContent, -1);
        var nextPager = InkLayout.PagePager(InkLayout.SystemContent, 1);
        var maxPage = Math.Max(0, (_saves.Count - 1) / SavesPerPage);

        if (prevPager.HasPoint(pos))
        {
            if (_savePageIndex > 0)
            {
                _savePageIndex--;
                QueueRedraw();
            }
            return;
        }

        if (nextPager.HasPoint(pos))
        {
            if (_savePageIndex < maxPage)
            {
                _savePageIndex++;
                QueueRedraw();
            }
            return;
        }

        // 槽位选择
        var startIndex = _savePageIndex * SavesPerPage;
        var endIndex = Math.Min(_saves.Count, startIndex + SavesPerPage);
        for (var i = startIndex; i < endIndex; i++)
        {
            var slotIndex = i - startIndex;
            var rowRect = InkLayout.SystemRow(slotIndex);
            if (rowRect.HasPoint(pos))
            {
                if (canSelect)
                {
                    _selectedSaveIndex = i;
                    _statusNotice = _saves[i].IsCorrupt ? $"该存档存在异常：{_saves[i].ErrorMessage}" : "";
                    QueueRedraw();
                }
                return;
            }
        }
    }

    private void PerformLoad(SaveSlotInfo slot)
    {
        if (slot.IsCorrupt)
        {
            _statusNotice = $"无法读取损坏的存档：{slot.ErrorMessage}";
            QueueRedraw();
            return;
        }

        if (_pack == null)
        {
            _statusNotice = "错误：内容包未就绪，无法加载。";
            QueueRedraw();
            return;
        }

        if (InkSaveStore.TryLoad(slot.FilePath, _pack, out var state, out var hub, out var error))
        {
            _statusNotice = "读取成功。";
            LoadRequested?.Invoke(state!, hub!);
        }
        else
        {
            _statusNotice = $"读取存档失败：{error}";
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var title = _currentPage switch
        {
            PageSave => "系统 · 保存进度",
            PageLoad => "系统 · 读取进度",
            _ => "系统 · 游戏设置",
        };

        var subtitle = _currentPage switch
        {
            PageSave => "创建全新独立快照文件",
            PageLoad => "选择已存入的进度记录",
            _ => "显示与声音设定",
        };

        InkFrame.PageShell(this, title, wood: false, subtitle: subtitle);

        DrawSidebar();

        switch (_currentPage)
        {
            case PageSettings:
                DrawSettingsContent();
                break;
            case PageSave:
                DrawSaveContent();
                break;
            case PageLoad:
                DrawLoadContent();
                break;
        }
    }

    private void DrawSidebar()
    {
        InkFrame.Panel(this, InkLayout.SystemSidebar, rails: false);
        var tabs = GetSidebarTabs();
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var rect = InkLayout.SystemSidebarEntry(i);
            var isCurrent = _currentPage == tab.Id;
            InkFrame.Button(this, rect, tab.Label, selected: isCurrent, enabled: tab.Enabled, fontSize: 20, centered: true);
        }
    }

    private void DrawSettingsContent()
    {
        InkFrame.Panel(this, InkLayout.SystemContent, rails: true);

        // 行 0: 窗口模式
        var row0 = InkLayout.SystemRow(0);
        InkDraw.TextBounded(this, new Rect2(row0.Position.X, row0.Position.Y, 240f, row0.Size.Y), "窗口模式", 22, 16);
        var windowModes = new[] { "窗口化", "无边框全屏", "独占全屏" };
        for (var i = 0; i < 3; i++)
        {
            var optRect = InkLayout.SystemOption(row0, i, 3);
            var selected = (int)InkSettings.CurrentWindowMode == i;
            InkFrame.Button(this, optRect, windowModes[i], selected: selected, fontSize: 18, centered: true);
        }
        InkFrame.RowRule(this, row0);

        // 行 1: 主音量
        var row1 = InkLayout.SystemRow(1);
        InkDraw.TextBounded(this, new Rect2(row1.Position.X, row1.Position.Y, 240f, row1.Size.Y), "主音量", 22, 16);
        var volLabels = new[] { "静音", "25%", "50%", "75%", "100%" };
        var volVals = new[] { 0.0f, 0.25f, 0.50f, 0.75f, 1.0f };
        for (var i = 0; i < volLabels.Length; i++)
        {
            var optRect = InkLayout.SystemOption(row1, i, volLabels.Length);
            var selected = Math.Abs(InkSettings.CurrentMasterVolume - volVals[i]) < 0.05f;
            InkFrame.Button(this, optRect, volLabels[i], selected: selected, fontSize: 18, centered: true);
        }
        InkFrame.RowRule(this, row1);

        // 行 2: 垂直同步
        var row2 = InkLayout.SystemRow(2);
        InkDraw.TextBounded(this, new Rect2(row2.Position.X, row2.Position.Y, 240f, row2.Size.Y), "垂直同步", 22, 16);
        var optOn = InkLayout.SystemOption(row2, 0, 2);
        var optOff = InkLayout.SystemOption(row2, 1, 2);
        InkFrame.Button(this, optOn, "开启", selected: InkSettings.CurrentVSync, fontSize: 18, centered: true);
        InkFrame.Button(this, optOff, "关闭", selected: !InkSettings.CurrentVSync, fontSize: 18, centered: true);
        InkFrame.RowRule(this, row2);

        // 底部提示
        var tipRect = new Rect2(InkLayout.SystemContent.Position.X + InkLayout.Pad,
            InkLayout.SystemContent.End.Y - 50f, InkLayout.SystemContent.Size.X - InkLayout.Pad * 2f, 32f);
        InkDraw.TextBounded(this, tipRect, "设置修改后即刻生效并自动保存。", 18, 14, InkStyle.Dim, "lm");
    }

    private void DrawSaveContent()
    {
        InkFrame.Panel(this, InkLayout.SystemContent, rails: true);

        // 底部“保存当前进度”按钮
        var saveBtn = InkLayout.DetailButton(InkLayout.SystemContent, 0, 1);
        InkFrame.Button(this, saveBtn, "保存当前进度", selected: false, enabled: _canSave, fontSize: 22, centered: true);

        DrawSaveSlotsList(canSelect: false);
    }

    private void DrawLoadContent()
    {
        InkFrame.Panel(this, InkLayout.SystemContent, rails: true);

        // 底部“读取选中进度”按钮
        var hasValidSelection = _selectedSaveIndex >= 0 && _selectedSaveIndex < _saves.Count && !_saves[_selectedSaveIndex].IsCorrupt;
        var loadBtn = InkLayout.DetailButton(InkLayout.SystemContent, 0, 1);
        InkFrame.Button(this, loadBtn, "读取选中进度", selected: false, enabled: hasValidSelection, fontSize: 22, centered: true);

        DrawSaveSlotsList(canSelect: true);
    }

    private void DrawSaveSlotsList(bool canSelect)
    {
        if (_saves.Count == 0)
        {
            var emptyRect = new Rect2(InkLayout.SystemContent.Position.X + 40f, InkLayout.SystemContent.Position.Y + 60f,
                InkLayout.SystemContent.Size.X - 80f, 100f);
            InkDraw.TextBounded(this, emptyRect, "暂无存档记录", 22, 16, InkStyle.Dim, "cm");
        }
        else
        {
            var startIndex = _savePageIndex * SavesPerPage;
            var endIndex = Math.Min(_saves.Count, startIndex + SavesPerPage);

            for (var i = startIndex; i < endIndex; i++)
            {
                var slotIndex = i - startIndex;
                var rowRect = InkLayout.SystemRow(slotIndex);
                var slot = _saves[i];
                var isSelected = canSelect && _selectedSaveIndex == i;

                if (isSelected)
                {
                    InkFrame.Selection(this, rowRect);
                }

                if (slot.IsCorrupt)
                {
                    InkDraw.TextBounded(this, new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 8f, 500f, 26f),
                        $"{slot.FileName} (文件损坏)", 20, 14, InkStyle.Dim, "lm");
                    InkDraw.TextBounded(this, new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 34f, 800f, 20f),
                        slot.ErrorMessage, 16, 12, InkStyle.Dim, "lm");
                }
                else
                {
                    InkDraw.TextBounded(this, new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 8f, 500f, 26f),
                        slot.Summary, 20, 14, InkStyle.Line, "lm");
                    InkDraw.TextBounded(this, new Rect2(rowRect.Position.X + 16f, rowRect.Position.Y + 34f, 500f, 20f),
                        slot.Timestamp, 16, 12, InkStyle.Dim, "lm");
                    InkDraw.TextBounded(this, new Rect2(rowRect.End.X - 340f, rowRect.Position.Y + 16f, 320f, 28f),
                        slot.FileName, 16, 12, InkStyle.Dim, "rm");
                }

                InkFrame.RowRule(this, rowRect);
            }

            // 导航翻页
            InkFrame.PageNavigation(this, InkLayout.SystemContent, startIndex, SavesPerPage, _saves.Count);
        }

        // 状态消息与通知
        if (!string.IsNullOrEmpty(_statusNotice))
        {
            var noticeRect = new Rect2(InkLayout.SystemContent.Position.X + InkLayout.Pad,
                InkLayout.SystemContent.End.Y - 110f, InkLayout.SystemContent.Size.X - InkLayout.Pad * 2f, 32f);
            InkDraw.TextBounded(this, noticeRect, _statusNotice, 18, 14, InkStyle.Line, "lm");
        }
    }
}
