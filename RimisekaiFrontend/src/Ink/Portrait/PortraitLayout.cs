using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 竖屏版式：基准画布 1080×2340。
///
/// 一切尺寸由物理毫米反推（20:9 手机上 1 画布 px = 0.0644mm）：
/// 触控下限 7.6mm(48dp)、字号下限 2.83mm、细线下限 0.32mm。
///
/// 骨架（2026-10-07 重设计）：
/// 根页签＝顶部 HUD（安全区之下 210 高，两行：地名＋金钱 / 季节·天气·时刻＋系统钮）
///        ＋内容区＋底部五页签（领地 / 角色 / 委托 / 仓储 / 日志，210 高）；
/// 推入页（角色详情、建造、系统、技能星盘、对话）＝顶栏（返回＋标题，150 高）＋整页内容，不带页签；
/// 设施、编成、排班、存取、交互一律走底部抽屉（压暗上层＋圆顶面板）。
/// </summary>
public static partial class PortraitLayout
{
    public const float CanvasWidth = 1080f;
    public const float CanvasHeight = 2340f;

    /// <summary>20:9 手机（2400×1080、393ppi）等比缩放 1.0 时，一个画布像素的物理毫米数。</summary>
    public const float MmPerPx = 0.0644f;

    /// <summary>48dp＝7.6mm 最小可点边长。</summary>
    public const int TouchMin = 118;

    /// <summary>60dp＝9.5mm 舒适边长。</summary>
    public const int TouchComfort = 148;

    /// <summary>细线 0.32mm、主线 0.52mm。低于 5px 的线在手机上是灰影而非线。</summary>
    public const float LineHair = 5f;
    public const float LineBold = 8f;

    /// <summary>装饰档：角花边长 88px、笔画 4.5px、珠径 6px（标题画面与星盘沿用）。</summary>
    public const float Flourish = 88f;
    public const float OrnamentWidth = 4.5f;
    public const float OrnamentJewel = 6f;

    /// <summary>贴角排线：区深 300px、线间隔 16px。</summary>
    public const float EtchZone = 300f;
    public const float EtchSpacing = 16f;

    /// <summary>字号：大标题 60、正文 50、下限 44（2.83mm）；地名 56、结算大字 110。</summary>
    public const int FontTitle = 60;
    public const int FontBody = 50;
    public const int FontMeta = 44;

    /// <summary>提示签行距：次级字号 44 加 10。</summary>
    public const float ToastLine = 54f;
    public const int FontPlace = 56;
    public const int FontDisplay = 110;

    /// <summary>网格格内角色小圆标的首字（圆标直径 48，图标级，不算正文）。</summary>

    /// <summary>屏边留白与通用标题带。</summary>
    public const int Pad = 40;
    public const int TitleBand = 118;

    /// <summary>列表行高＝触控下限，一行一个手指位。</summary>
    public const int RowHeight = TouchMin;

    public const int GridCols = 5;
    public const int GridRows = 5;

    // ---------- 安全区 ----------

    /// <summary>刘海/状态栏占去的画布高度；由根画面在启动时按显示器安全区写入，桌面预览为 0。</summary>
    public static float SafeTop { get; set; }

    // ---------- 根页签骨架：HUD / 内容 / 页签带 ----------

    public const float HudHeight = 210f;
    public const float TabBarHeight = 210f;
    public const int TabCount = 5;
    public static readonly string[] TabLabels = { "领地", "角色", "委托", "仓储", "日志" };

    public static Rect2 Hud => new(0, SafeTop, CanvasWidth, HudHeight);

    /// <summary>HUD 第一行：地名（可点改名），命中块整行高 118。</summary>
    public static Rect2 HudPlace => new(Pad, SafeTop + 4f, 640f, TouchMin);
    public static float HudLine1 => SafeTop + 63f;
    public static float HudLine2 => SafeTop + 150f;

    /// <summary>HUD 第二行右端：系统钮（存档与设置）。</summary>
    public static Rect2 HudSystem => new(CanvasWidth - Pad - TouchMin, SafeTop + 91f, TouchMin, TouchMin);

    public static float TabTop => CanvasHeight - TabBarHeight;
    public static Rect2 TabBar => new(0, TabTop, CanvasWidth, TabBarHeight);
    public static Rect2 Tab(int i) => new(i * (CanvasWidth / TabCount), TabTop, CanvasWidth / TabCount, TabBarHeight);

    /// <summary>根页签内容区：HUD 之下、页签带之上。</summary>
    public static Rect2 Body => new(0, Hud.End.Y, CanvasWidth, TabTop - Hud.End.Y);

    // ---------- 推入页骨架：顶栏 / 整页 ----------

    public const float PageTopHeight = 150f;
    public static Rect2 PageTop => new(0, SafeTop, CanvasWidth, PageTopHeight);
    public static Rect2 PageBack => new(16f, SafeTop + 16f, 150f, TouchMin);
    public static Rect2 PageAction => new(CanvasWidth - 16f - 200f, SafeTop + 16f, 200f, TouchMin);
    public static Rect2 PageBody => new(0, PageTop.End.Y, CanvasWidth, CanvasHeight - PageTop.End.Y);

    // ---------- 通用件 ----------

    public static float FullWidth => CanvasWidth - Pad * 2f;

    /// <summary>根页签内容区顶部的分段控件（角色筛选、仓储三段等）。</summary>
    public static Rect2 BodySegment => new(Pad, Body.Position.Y + 24f, FullWidth, TouchMin);

    /// <summary>分段控件之下的滚动区。</summary>
    public static Rect2 BodyBelowSegment => new(0, BodySegment.End.Y + 24f, CanvasWidth, TabTop - BodySegment.End.Y - 24f);

    /// <summary>抽屉面板内容的左右边距与把手下的标题基线。</summary>
    public const float SheetTitleOffset = 110f;
    public const float SheetContentOffset = 180f;

    /// <summary>抽屉底部固定操作条（两钮或单主钮）。</summary>
    public static Rect2 SheetFooter => new(Pad, CanvasHeight - 40f - 136f, FullWidth, 136f);
    public static Rect2 SheetFooterLeft => new(Pad, SheetFooter.Position.Y, (FullWidth - 24f) / 2f, SheetFooter.Size.Y);
    public static Rect2 SheetFooterRight => new(Pad + (FullWidth + 24f) / 2f, SheetFooter.Position.Y,
        (FullWidth - 24f) / 2f, SheetFooter.Size.Y);
    public static Rect2 SheetClose(float top) => new(CanvasWidth - Pad - TouchMin, top + 50f, TouchMin, TouchMin);

    // ---------- 标题画面 ----------

    // 只取参考图中的标志，不把其中的横屏按钮再画一遍。
    public static readonly Rect2 TitleArtSource = new(310, 80, 1060, 390);
    public static Rect2 TitleArt => new(Pad, 700f, CanvasWidth - Pad * 2f,
        (CanvasWidth - Pad * 2f) * 390f / 1060f);

    /// <summary>
    /// 标题画面入口：0 开始 / 1 继续 / 2 设置 / 3 退出。
    /// 继续＝主钮（大，带存档副行）；开始与设置并排两枚描边钮；退出是底部一枚窄描边钮。
    /// </summary>
    public static Rect2 TitleButton(int i) => i switch
    {
        1 => new Rect2(140f, 1700f, CanvasWidth - 280f, 160f),
        0 => new Rect2(140f, 1900f, (CanvasWidth - 300f) / 2f, 128f),
        2 => new Rect2(160f + (CanvasWidth - 300f) / 2f, 1900f, (CanvasWidth - 300f) / 2f, 128f),
        _ => new Rect2((CanvasWidth - 320f) / 2f, 2080f, 320f, TouchMin),
    };
}
