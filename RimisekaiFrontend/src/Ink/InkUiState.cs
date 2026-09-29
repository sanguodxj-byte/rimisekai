namespace Rimisekai.Ink;

/// <summary>
/// 界面自身持有的纯视图状态（不进 Core，不进存档）。
/// 模型构建时读它，输入处理时改它。
/// </summary>
public sealed class InkUiState
{
    /// <summary>角色栏当前页，从 0 起。</summary>
    public int CardPage { get; set; }

    /// <summary>当前打开的页面；None 表示没开。</summary>
    public InkPage OpenPage { get; set; } = InkPage.None;

    /// <summary>子页面左列表选中的行下标；-1 表示未选（构建时取第一行）。</summary>
    public int PageSelected { get; set; } = -1;

    /// <summary>子页面列表的页首行下标（滚动窗口起点）。</summary>
    public int PageFirst { get; set; }

    /// <summary>工作页：选中格的行下标（工作类型）；-1 未选。</summary>
    public int WorkRow { get; set; } = -1;

    /// <summary>工作页：选中格的列下标（角色）；-1 未选。</summary>
    public int WorkColumn { get; set; } = -1;


    /// <summary>子页面列表的搜索词；空表示不过滤。</summary>
    public string PageSearch { get; set; } = "";

    /// <summary>子页面搜索框是否聚焦；聚焦时键盘输入进搜索词。</summary>
    public bool SearchFocus { get; set; }

    /// <summary>子页面筛选档位下标；0 恒为“全部”。</summary>
    public int PageFilter { get; set; }

    /// <summary>子页面排序字段下标。</summary>
    public int PageSort { get; set; }

    /// <summary>子页面排序方向；false 升序，true 降序。</summary>
    public bool PageSortDesc { get; set; }

    /// <summary>开发模式：四面板容器不变，内容切成领地编辑。</summary>
    public bool DevMode { get; set; }

    /// <summary>开发模式：选中的设施 Id（-1 未选）。</summary>
    public int DevSelectedFacility { get; set; } = -1;

    /// <summary>开发模式：选中的房间 Id（-1 未选）。</summary>
    public int DevSelectedRoom { get; set; } = -1;

    /// <summary>开发模式：待放进选中房间的设施 Id（-1 无）。</summary>
    public int DevPlacingFacility { get; set; } = -1;

    /// <summary>开发模式：待放到网格上的房间 Id（-1 无）。</summary>
    public int DevPlacingRoom { get; set; } = -1;

    /// <summary>改名弹窗是否打开。</summary>
    public bool Renaming { get; set; }

    /// <summary>改名输入框里的文本。</summary>
    public string RenameText { get; set; } = "";

    /// <summary>打字机已显示的字符数。换句时由界面归零。</summary>
    public int TypeRevealed { get; set; }

    /// <summary>打字机正在播的那句话，用来判断是否需要重置进度。</summary>
    public string TypeLine { get; set; } = "";

    /// <summary>上一次操作的反馈文本。</summary>
    public string Notice { get; set; } = "";

    /// <summary>鼠标当前悬停的元素下标，-1 表示无。</summary>
    public int Hovered { get; set; } = -1;
}
