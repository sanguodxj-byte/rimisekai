using System;
using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 边框与装饰。
///
/// 版式约束：所有装饰都必须收在框线以内，不得外溢——
/// 相邻面板之间的空隙很窄，外溢的角花会互相打架。
/// 角花从框内向内长，压条也收在托角之间。
/// </summary>
public static class InkFrame
{
    /// <summary>面板留白：框线到内容区的距离。**单一来源**——直接用 InkLayout.Pad，禁止再手抄一份。</summary>
    public const float Pad = InkLayout.Pad;

    /// <summary>
    /// 主面板：填色 + 双线矩形 + 四角角花。corner 同时决定角花尺寸。
    ///
    /// 2026-10-01 主人定：**回到最初的边框形态**——细双线框＋四角小角花。
    /// 此前所有「厚白框／骨板框／骨脊框／玫瑰花带雕花框」的尝试全部否掉，
    /// `BorderBand` 整支已删除，禁止回退。
    /// </summary>
    public static void Panel(CanvasItem ci, Rect2 r, Color? color = null,
        float corner = 26f, Color? fill = null)
    {
        var c = color ?? InkStyle.Line;
        var seed = Seed(r);

        ci.DrawRect(r, fill ?? InkStyle.Panel);

        InkDraw.Ink(ci, RectLoop(r), c, 2f, 0.45f, seed);
        InkDraw.Ink(ci, RectLoop(r.Grow(-6f)), InkStyle.Dim, 1f, 0.35f, seed + 1);
        if (corner >= 20f)
            CornerFlourishes(ci, r, c, seed, VineSize(r, corner));
    }

    /// <summary>
    /// 分区容器（2026-10-01 主人定）：几块相邻面板合成一个视觉整体——
    /// 外面只画一圈**双线框＋四角角花**（与 `Panel` 同一套画法），
    /// 内部用细竖线划分成各面板，**不再给每块面板各画一圈外框**
    /// （面板不是彼此独立的视觉元素）。splitXs 是内部竖分隔线的 x 坐标，可传 0..n 条。
    /// </summary>
    public static void Zone(CanvasItem ci, Rect2 r, params float[] splitXs)
        => Zone(ci, r, null, splitXs);

    public static void Zone(CanvasItem ci, Rect2 r, Color? fill, params float[] splitXs)
    {
        var seed = Seed(r);
        ci.DrawRect(r, fill ?? InkStyle.Panel);

        InkDraw.Ink(ci, RectLoop(r), InkStyle.Line, 2f, 0.45f, seed);
        InkDraw.Ink(ci, RectLoop(r.Grow(-6f)), InkStyle.Dim, 1f, 0.35f, seed + 1);
        CornerFlourishes(ci, r, InkStyle.Line, seed, VineSize(r, 26f));

        // 内部竖分隔：一条极细的暗线，自框线内沿贯到内沿——
        // 读作「同一块板上划的界」，不是又一圈边框。
        foreach (var x in splitXs)
        {
            if (x <= r.Position.X + 12f || x >= r.End.X - 12f)
                continue;
            InkDraw.InkLine(ci, new Vector2(x, r.Position.Y + 8f),
                new Vector2(x, r.End.Y - 8f), new Color(InkStyle.Dim, 0.5f), 1f);
        }
    }

    /// <summary>
    /// 只画框：双线矩形 + 四角卷草，不铺底不铺纸纹。
    /// 整面内容（插画）嵌进面板时用——底被内容盖掉后把框线压回内容之上。
    /// </summary>
    public static void PanelFrame(CanvasItem ci, Rect2 r, Color? color = null,
        float corner = 26f)
    {
        var c = color ?? InkStyle.Line;
        var seed = Seed(r);

        InkDraw.Ink(ci, RectLoop(r), c, 2f, 0.45f, seed);
        InkDraw.Ink(ci, RectLoop(r.Grow(-6f)), InkStyle.Dim, 1f, 0.35f, seed + 1);

        if (corner >= 20f)
        {
            var vine = VineSize(r, corner);
            CornerFlourishes(ci, r, c, seed, vine);
        }
    }

    /// <summary>角花只按请求尺寸画，大面板不再按比例放大，避免压到标题。</summary>
    private static float VineSize(Rect2 r, float requested) =>
        Mathf.Clamp(requested, 14f, Mathf.Min(48f, Mathf.Min(r.Size.X, r.Size.Y) * 0.12f));

    /// <summary>
    /// 木框面板：漂白木条围一圈。交易/库存这类货架感用它。
    /// </summary>
    public static void WoodPanel(CanvasItem ci, Rect2 r, Color? fill = null, float thickness = 18f)
    {
        var seed = Seed(r);
        ci.DrawRect(r, fill ?? InkStyle.Panel);
        InkDraw.WoodBand(ci, r, thickness, seed);
    }

    /// <summary>
    /// 整屏底：黑底，没有外框。
    /// 据点界面用它，避免外框角花压到顶栏。
    /// </summary>
    public static void Backdrop(CanvasItem ci, Rect2 canvas)
    {
        ci.DrawRect(canvas, InkStyle.Bg);
    }

    private static Vector2[] RectLoop(Rect2 r) => new[]
    {
        r.Position,
        new Vector2(r.End.X, r.Position.Y),
        r.End,
        new Vector2(r.Position.X, r.End.Y),
        r.Position,
    };

    /// <summary>
    /// 四角卷轴：沿框边向内卷，不斜着穿过标题。
    /// </summary>
    private static void CornerFlourishes(CanvasItem ci, Rect2 r, Color c, int seed, float size)
    {
        var k = Mathf.Clamp(size, 16f, Mathf.Min(r.Size.X, r.Size.Y) * 0.16f);
        var corners = new[]
        {
            (r.Position.X, r.Position.Y, 1f, 1f),
            (r.End.X, r.Position.Y, -1f, 1f),
            (r.End.X, r.End.Y, -1f, -1f),
            (r.Position.X, r.End.Y, 1f, -1f),
        };
        for (var i = 0; i < corners.Length; i++)
        {
            var (cx, cy, dx, dy) = corners[i];
            InkDraw.CornerFlourish(ci, new Vector2(cx, cy), dx, dy, c, k, seed + i * 11);
        }
    }

    /// <summary>
    /// 卡片框（角色头像）：细双线 + 四角短刻。选中时浅填，不改线色。
    /// </summary>
    public static void Card(CanvasItem ci, Rect2 r, Color? color = null,
        bool highlighted = false, Color? fill = null)
    {
        var c = color ?? InkStyle.Line;
        var seed = Seed(r);

        ci.DrawRect(r, fill ?? (highlighted ? InkStyle.Hover : InkStyle.Inset));

        InkDraw.Ink(ci, RectLoop(r), c, highlighted ? 2f : 1.3f, 0.4f, seed);
        InkDraw.Ink(ci, RectLoop(r.Grow(-4f)), InkStyle.Dim, 1f, 0.3f, seed + 1);

        const float arm = 12f;
        foreach (var (cx, cy, dx, dy) in new[]
                 {
                     (r.Position.X, r.Position.Y, 1f, 1f),
                     (r.End.X, r.Position.Y, -1f, 1f),
                     (r.End.X, r.End.Y, -1f, -1f),
                     (r.Position.X, r.End.Y, 1f, -1f),
                 })
        {
            var p = new Vector2(cx + dx * 8f, cy + dy * 8f);
            InkDraw.InkLine(ci, p, p + new Vector2(dx * arm, 0), c, 1f, 0.25f, seed + 2);
            InkDraw.InkLine(ci, p, p + new Vector2(0, dy * arm), c, 1f, 0.25f, seed + 3);
        }
    }

    /// <summary>
    /// 按钮：细双线矩形，文字居中或靠左。
    /// selected 时浅填加粗，enabled 为假时改用次级色。
    /// border 传入时整体改用它描边——鼠标悬浮／手机触碰的暗框反馈。
    /// beads：两端各缀一枚菱珠，标题菜单这类参考稿带珠按钮用。
    /// 约定（2026-10-02 主人定）：最小字号为 26，低于 26 自动提升至 26。
    /// </summary>
    public static void Button(CanvasItem ci, Rect2 r, string label, bool selected = false,
        bool enabled = true, int fontSize = 26, bool centered = false, Color? border = null,
        bool beads = false, bool hovered = false, string value = "", Color? fill = null)
    {
        fontSize = Math.Max(26, fontSize);
        var c = border ?? (selected || enabled ? InkStyle.Line : InkStyle.Dim);
        var seed = Seed(r);

        // 悬停：浅填 + 线宽加深，不改线色（与选中同款浅填，按 AGENTS 悬停规范）。
        var defaultFill = selected || hovered ? InkStyle.Hover : InkStyle.Inset;
        ci.DrawRect(r, fill ?? defaultFill);
        InkDraw.Ink(ci, RectLoop(r), c, selected || hovered ? 1.8f : 1.2f, 0.3f, seed);
        InkDraw.Ink(ci, RectLoop(r.Grow(-3f)), selected ? c : InkStyle.Dim, 0.9f, 0.22f, seed + 1);

        if (beads)
        {
            var cy = r.GetCenter().Y;
            InkDraw.Jewel(ci, new Vector2(r.Position.X - 7f, cy), 3.2f, c);
            InkDraw.Jewel(ci, new Vector2(r.End.X + 7f, cy), 3.2f, c);
        }

        // value 非空 = 左右分区：名称贴左内缘、数值贴右内缘，
        // 两侧各自自动缩字号（下限 26），任一侧变长都只吃自己那半。
        if (value.Length > 0)
        {
            const float gap = 8f;
            var innerW = r.Size.X - 32f - gap;
            var nameMax = innerW * 0.56f;
            var valueMax = innerW - nameMax;
            var nameSize = InkDraw.FitSize(label, nameMax, fontSize, 26);
            var valueSize = InkDraw.FitSize(value, valueMax, fontSize, 26);
            var cy = r.GetCenter().Y;
            InkDraw.Text(ci, new Vector2(r.Position.X + 16f, cy), label, nameSize, c, "lm");
            InkDraw.Text(ci, new Vector2(r.End.X - 16f, cy), value, valueSize, c, "rm");
        }
        else if (centered)
        {
            // 小文本框放不下就自动缩字号（下限 26），不截断不溢出。
            var fitted = InkDraw.FitSize(label, r.Size.X - 10f, fontSize, 26);
            InkDraw.Text(ci, r.GetCenter(), label, fitted, c, "cm");
        }
        else
        {
            var fitted = InkDraw.FitSize(label, r.Size.X - 32f, fontSize, 26);
            InkDraw.Text(ci, new Vector2(r.Position.X + 16f, r.GetCenter().Y), label, fitted, c, "lm");
        }
    }

    /// <summary>
    /// 面板标题：文字左对齐放在托角右侧的“安全区”，
    /// 下面一条三段渐隐的细线，长度不触及两侧托角。
    /// 🚨 2026-10-01 主人定：**不要标题牌**——标题字外面不许再套矩形框、
    /// 不许缀端珠。就是「一行字＋一条渐隐线」，与基线一致。
    /// </summary>
    public static void Title(CanvasItem ci, Rect2 r, string text, int size = InkLayout.TitleFontSize)
    {
        var x = r.Position.X + Pad + InkLayout.TitleInset;
        var y = r.Position.Y + 22f;
        InkDraw.Text(ci, new Vector2(x, y), text, size, InkStyle.Line, "lt");

        var lineY = y + InkLayout.TitleRuleGap;
        var left = x;
        var right = r.End.X - Pad - InkLayout.TitleInset;
        FadingRule(ci, left, right, lineY);
    }

    // 🚨 标题牌 `TitlePlate` 已于 2026-10-01 被主人点名删除，禁止再引入。
    // 主人原话：「日志角色的框体和白点是谁添加的？需要删掉」。
    // 那个「框体」＝标题字外的矩形牌面，「白点」＝牌面两端的实心菱珠。
    // **基线提交里本来就没有这东西**，是后来照参考稿的「铜版书名牌」加的，
    // 属自造语汇，主人不要。标题一律只有「一行字 ＋ 一条三段渐隐线」。

    /// <summary>
    /// 分割细线：三段渐隐的墨线，无珠无框。标题下饰与面板内分段分隔共用这一种画法。
    /// </summary>
    public static void FadingRule(CanvasItem ci, float left, float right, float y, int seed = 6200)
    {
        if (right <= left)
            return;

        const int segs = 3;
        var seg = (right - left) / segs;
        for (var i = 0; i < segs; i++)
        {
            var a = left + seg * i;
            var b = a + seg;
            var alpha = 0.5f - i * 0.14f;
            InkDraw.InkLine(ci, new Vector2(a, y), new Vector2(b, y),
                new Color(InkStyle.Dim, alpha), 1f, 0.3f, seed + i);
        }
    }

    /// <summary>
    /// 自适应版面板标题：与 Title 同一套几何（起点、下饰线），
    /// 文字超长时在 [min, max] 内缩字号，给动态标题用（交流 · 某某）。
    /// </summary>
    public static void TitleFitted(CanvasItem ci, Rect2 r, string text,
        float maxWidth, int max, int min)
    {
        var x = r.Position.X + Pad + InkLayout.TitleInset;
        var y = r.Position.Y + 22f;
        // 先定字号再落笔（动态标题会缩字号）。
        var used = InkDraw.FitSize(text, maxWidth, max, min);
        InkDraw.Text(ci, new Vector2(x, y), text, used, InkStyle.Line, "lt");

        var lineY = y + InkLayout.TitleRuleGap;
        var left = x;
        var right = r.End.X - Pad - InkLayout.TitleInset;
        FadingRule(ci, left, right, lineY);
    }

    /// <summary>卡片边框不填底，用于压在内容插画上方。</summary>
    public static void CardOutline(CanvasItem ci, Rect2 rect, bool highlighted = false)
    {
        InkDraw.Ink(ci, RectLoop(rect), InkStyle.Line, highlighted ? 2f : 1.3f, 0.35f, Seed(rect));
        InkDraw.Ink(ci, RectLoop(rect.Grow(-4f)), InkStyle.Dim, 0.9f, 0.22f, Seed(rect) + 1);
    }

    public static void RowDivider(CanvasItem ci, Rect2 rect) =>
        InkDraw.InkLine(ci, new Vector2(rect.Position.X, rect.End.Y),
            new Vector2(rect.End.X, rect.End.Y), new Color(InkStyle.Dim, 0.28f), 0.8f, 0.2f, Seed(rect));

    public static void Selection(CanvasItem ci, Rect2 rect)
    {
        ci.DrawRect(rect, InkStyle.Hover);
        InkDraw.Ink(ci, RectLoop(rect), InkStyle.Line, 1.7f, 0.25f, Seed(rect));
    }

    public static void PageShell(CanvasItem ci, string title, bool wood = false, string subtitle = "")
    {
        Backdrop(ci, new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight));
        if (wood)
            WoodPanel(ci, InkLayout.FullPagePanel, thickness: 12f);
        else
            Panel(ci, InkLayout.FullPagePanel, corner: 22f);
        // 主人定（2026-10-02）：这个大页面所有的左上角标题全部移除
        InkDraw.TextBounded(ci, InkLayout.PageSubtitleRect, subtitle, 26, 26, InkStyle.Line, "rm");
        Button(ci, InkLayout.FullPageClose, "关闭", fontSize: 26, centered: true);
        RowDivider(ci, InkLayout.PageHeaderDivider);
    }

    public static void PageNavigation(CanvasItem ci, Rect2 area, int first, int visible, int total)
    {
        var pages = Math.Max(1, (total + visible - 1) / visible);
        var current = total == 0 ? 1 : first / visible + 1;
        Button(ci, InkLayout.PagePager(area, -1), "‹", fontSize: 26, enabled: first > 0, centered: true);
        Button(ci, InkLayout.PagePager(area, 1), "›", fontSize: 26, enabled: first + visible < total, centered: true);
        InkDraw.TextBounded(ci, InkLayout.PageCounter(area), $"{current} / {pages}", 26, 26, InkStyle.Line, "cm");
    }

    private static int Seed(Rect2 r) =>
        ((int)r.Position.X * 31 + (int)r.Position.Y * 17 + (int)r.Size.X * 7) & 0x7FFFFFF;
}
