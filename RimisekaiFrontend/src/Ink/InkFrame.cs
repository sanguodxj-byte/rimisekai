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
    /// <summary>面板留白：框线到内容区的距离。与 InkLayout.Pad 一致。</summary>
    public const float Pad = 30f;

    /// <summary>
    /// 主面板：填色 + 纸纹 + 双线矩形 + 四角卷草 + 上下压条。
    /// corner 同时决定角花尺寸。
    /// </summary>
    public static void Panel(CanvasItem ci, Rect2 r, Color? color = null,
        float corner = 26f, bool rails = true, Color? fill = null)
    {
        var c = color ?? InkStyle.Line;
        var seed = Seed(r);

        ci.DrawRect(r, fill ?? InkStyle.Panel);
        InkDraw.Paper(ci, r.Grow(-4f), seed: seed + 40);

        InkDraw.Ink(ci, RectLoop(r), c, 2f, 0.45f, seed);
        InkDraw.Ink(ci, RectLoop(r.Grow(-6f)), InkStyle.Dim, 1f, 0.35f, seed + 1);

        // 小面板（详情栏之类）只留双线，角花留给外框，避免套框时花纹打架。
        if (corner >= 20f)
        {
            var vine = VineSize(r, corner);
            CornerFlourishes(ci, r, c, seed, vine);
            if (rails)
                OrnamentRails(ci, r, c, seed, vine + 22f);
        }
        else if (rails)
        {
            OrnamentRails(ci, r, c, seed, 48f);
        }
    }

    /// <summary>角花只按请求尺寸画，大面板不再按比例放大，避免压到标题。</summary>
    private static float VineSize(Rect2 r, float requested) =>
        Mathf.Clamp(requested, 14f, Mathf.Min(48f, Mathf.Min(r.Size.X, r.Size.Y) * 0.12f));

    /// <summary>
    /// 木框面板：漂白木条围一圈，内部再铺纸纹。交易/库存这类货架感用它。
    /// </summary>
    public static void WoodPanel(CanvasItem ci, Rect2 r, Color? fill = null, float thickness = 18f)
    {
        var seed = Seed(r);
        ci.DrawRect(r, fill ?? InkStyle.Panel);
        InkDraw.WoodBand(ci, r, thickness, seed);
        var inner = r.Grow(-thickness);
        if (inner.Size.X > 8f && inner.Size.Y > 8f)
            InkDraw.Paper(ci, inner.Grow(-2f), seed: seed + 7);
    }

    /// <summary>
    /// 整屏底：青绿底加稀疏纸纹，没有外框。
    /// 据点界面用它，避免外框角花压到顶栏。
    /// </summary>
    public static void Backdrop(CanvasItem ci, Rect2 canvas)
    {
        ci.DrawRect(canvas, InkStyle.Bg);
        InkDraw.Paper(ci, canvas.Grow(-20f), new Color(InkStyle.Line, 0.035f), 9001);
    }

    /// <summary>
    /// 整屏舞台：底 + 一圈收进画布的细框与角花。标题画面用它。
    /// 四角先铺交叉排线底纹（v1 参考图的手绘质感），角花压在纹理上。
    /// </summary>
    public static void Stage(CanvasItem ci, Rect2 canvas)
    {
        Backdrop(ci, canvas);
        var frame = canvas.Grow(-22f);
        InkDraw.Ink(ci, RectLoop(frame), new Color(InkStyle.Dim, 0.5f), 1.1f, 0.22f, 9010);
        InkDraw.CornerEtching(ci, frame);
        CornerFlourishes(ci, frame, new Color(InkStyle.Line, 0.75f), 9020, 46f);
    }

    /// <summary>对话气泡：内嵌矩形，文字居中。标题画面和对话层都能用。</summary>
    public static void Balloon(CanvasItem ci, Rect2 r, string text, int fontSize = 24)
    {
        var seed = Seed(r);
        ci.DrawRect(r, InkStyle.Inset);
        InkDraw.Ink(ci, RectLoop(r), InkStyle.Line, 1.6f, 0.3f, seed);
        InkDraw.Ink(ci, RectLoop(r.Grow(-4f)), InkStyle.Dim, 0.9f, 0.22f, seed + 1);
        InkDraw.TextFitted(ci, r.GetCenter(), text, r.Size.X - 24f, fontSize, 16, InkStyle.Line, "cm");
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
    /// 上下压条：细双线 + 正中菱形珠，端点收在角花之外。
    /// 中间每隔一段嵌一颗小珠，模拟参考稿的花边压条。
    /// </summary>
    private static void OrnamentRails(CanvasItem ci, Rect2 r, Color c, int seed, float inset)
    {
        var left = r.Position.X + inset;
        var right = r.End.X - inset;
        if (right <= left + 20f)
            return;

        DrawRail(ci, left, right, r.Position.Y + 8f, c, seed + 21);
        DrawRail(ci, left, right, r.End.Y - 8f, c, seed + 31);
    }

    private static void DrawRail(CanvasItem ci, float left, float right, float y, Color c, int seed)
    {
        InkDraw.InkLine(ci, new Vector2(left, y), new Vector2(right, y), c, 1.4f, 0.28f, seed);
        InkDraw.InkLine(ci, new Vector2(left, y + 4f), new Vector2(right, y + 4f),
            InkStyle.Dim, 1f, 0.22f, seed + 1);

        var mid = (left + right) / 2f;
        InkDraw.Jewel(ci, new Vector2(mid, y + 2f), 3.2f, c);

        var span = right - left;
        var n = Mathf.Clamp((int)(span / 90f), 2, 8);
        for (var i = 1; i < n; i++)
        {
            var x = left + span * (i / (float)n);
            if (Mathf.Abs(x - mid) < 18f)
                continue;
            InkDraw.Jewel(ci, new Vector2(x, y + 2f), 1.6f, InkStyle.Dim, filled: false);
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
        InkDraw.Paper(ci, r.Grow(-3f), seed: seed + 5);

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
    /// </summary>
    public static void Button(CanvasItem ci, Rect2 r, string label, bool selected = false,
        bool enabled = true, int fontSize = 22, bool centered = false, Color? border = null,
        bool beads = false)
    {
        var c = border ?? (selected || enabled ? InkStyle.Line : InkStyle.Dim);
        var seed = Seed(r);

        ci.DrawRect(r, selected ? InkStyle.Hover : InkStyle.Inset);
        InkDraw.Ink(ci, RectLoop(r), c, selected ? 1.8f : 1.2f, 0.3f, seed);
        InkDraw.Ink(ci, RectLoop(r.Grow(-3f)), selected ? c : InkStyle.Dim, 0.9f, 0.22f, seed + 1);

        if (beads)
        {
            var cy = r.GetCenter().Y;
            InkDraw.Jewel(ci, new Vector2(r.Position.X - 7f, cy), 3.2f, c);
            InkDraw.Jewel(ci, new Vector2(r.End.X + 7f, cy), 3.2f, c);
        }

        if (centered)
            InkDraw.Text(ci, r.GetCenter(), label, fontSize, c, "cm");
        else
            InkDraw.Text(ci, new Vector2(r.Position.X + 16f, r.GetCenter().Y), label, fontSize, c, "lm");
    }

    /// <summary>
    /// 折角货架框：购买栏/出售栏那种切掉一角的内嵌矩形。
    /// </summary>
    public static void Shelf(CanvasItem ci, Rect2 r, string title, int fontSize = 22)
    {
        InkDraw.FoldedBox(ci, r, InkStyle.Line, fold: 20f, fill: true, seed: Seed(r));
        InkDraw.Text(ci, new Vector2(r.GetCenter().X, r.Position.Y + 16f),
            title, fontSize, InkStyle.Line, "ct");
    }

    /// <summary>竖分隔线，两端带空心珠。</summary>
    public static void Divider(CanvasItem ci, Vector2 top, Vector2 bottom, Color? color = null)
    {
        var c = color ?? InkStyle.Dim;
        InkDraw.InkLine(ci, top, bottom, c, 1f, 0.4f, 5150);
        InkDraw.Jewel(ci, top, 2.4f, c, false);
        InkDraw.Jewel(ci, bottom, 2.4f, c, false);
    }

    /// <summary>
    /// 面板标题：文字左对齐放在托角右侧的“安全区”，
    /// 下面一条三段渐隐的细线，长度不触及两侧托角。
    /// </summary>
    public static void Title(CanvasItem ci, Rect2 r, string text, int size = 26)
    {
        var x = r.Position.X + Pad + 12f;
        var y = r.Position.Y + 22f;
        InkDraw.Text(ci, new Vector2(x, y), text, size, InkStyle.Line, "lt");

        var lineY = y + 34f;
        var left = x;
        var right = r.End.X - Pad - 12f;
        if (right <= left)
            return;

        const int segs = 3;
        var seg = (right - left) / segs;
        for (var i = 0; i < segs; i++)
        {
            var a = left + seg * i;
            var b = a + seg;
            var alpha = 0.5f - i * 0.14f;
            InkDraw.InkLine(ci, new Vector2(a, lineY), new Vector2(b, lineY),
                new Color(InkStyle.Dim, alpha), 1f, 0.3f, 6200 + i);
        }
    }

    /// <summary>
    /// 自适应版面板标题：与 Title 同一套几何（起点、下饰线），
    /// 文字超长时在 [min, max] 内缩字号，给动态标题用（交流 · 某某）。
    /// </summary>
    public static void TitleFitted(CanvasItem ci, Rect2 r, string text,
        float maxWidth, int max, int min)
    {
        var x = r.Position.X + Pad + 12f;
        var y = r.Position.Y + 22f;
        InkDraw.TextFitted(ci, new Vector2(x, y), text, maxWidth, max, min, InkStyle.Line);

        var lineY = y + 34f;
        var left = x;
        var right = r.End.X - Pad - 12f;
        if (right <= left)
            return;

        const int segs = 3;
        var seg = (right - left) / segs;
        for (var i = 0; i < segs; i++)
        {
            var a = left + seg * i;
            var b = a + seg;
            var alpha = 0.5f - i * 0.14f;
            InkDraw.InkLine(ci, new Vector2(a, lineY), new Vector2(b, lineY),
                new Color(InkStyle.Dim, alpha), 1f, 0.3f, 6200 + i);
        }
    }

    /// <summary>
    /// 带珠横规：亮线在上、暗线在下，两端与正中缀珠。
    /// 顶栏分隔与版面横隔断都用它，取代裸直线。
    /// </summary>
    public static void HeaderRule(CanvasItem ci, float left, float right, float y,
        Color? color = null)
    {
        if (right <= left + 40f)
            return;

        var c = color ?? InkStyle.Line;
        InkDraw.InkLine(ci, new Vector2(left, y), new Vector2(right, y), c, 1.3f, 0.32f, 6300);
        InkDraw.InkLine(ci, new Vector2(left, y + 4f), new Vector2(right, y + 4f),
            InkStyle.Dim, 0.9f, 0.24f, 6301);
        InkDraw.Jewel(ci, new Vector2(left, y + 2f), 2.2f, c);
        InkDraw.Jewel(ci, new Vector2(right, y + 2f), 2.2f, c);
        InkDraw.Jewel(ci, new Vector2((left + right) / 2f, y + 2f), 3f, c);
    }

    /// <summary>卡片边框不填底，用于压在内容插画上方。</summary>
    public static void CardOutline(CanvasItem ci, Rect2 rect, bool highlighted = false)
    {
        InkDraw.Ink(ci, RectLoop(rect), InkStyle.Line, highlighted ? 2f : 1.3f, 0.35f, Seed(rect));
        InkDraw.Ink(ci, RectLoop(rect.Grow(-4f)), InkStyle.Dim, 0.9f, 0.22f, Seed(rect) + 1);
    }

    public static void RowRule(CanvasItem ci, Rect2 rect) =>
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
            Panel(ci, InkLayout.FullPagePanel, corner: 22f, rails: false);
        InkDraw.TextBounded(ci, InkLayout.PageTitleRect, title, 26, 18);
        InkDraw.TextBounded(ci, InkLayout.PageSubtitleRect, subtitle, 18, 14, InkStyle.Dim, "rm");
        Button(ci, InkLayout.FullPageClose, "关闭", fontSize: 20, centered: true);
        RowRule(ci, InkLayout.PageHeaderRule);
    }

    public static void PageNavigation(CanvasItem ci, Rect2 area, int first, int visible, int total)
    {
        var pages = Math.Max(1, (total + visible - 1) / visible);
        var current = total == 0 ? 1 : first / visible + 1;
        Button(ci, InkLayout.PagePager(area, -1), "‹", enabled: first > 0, centered: true);
        Button(ci, InkLayout.PagePager(area, 1), "›", enabled: first + visible < total, centered: true);
        InkDraw.TextBounded(ci, InkLayout.PageCounter(area), $"{current} / {pages}", 18, 14, InkStyle.Dim, "cm");
    }

    private static int Seed(Rect2 r) =>
        ((int)r.Position.X * 31 + (int)r.Position.Y * 17 + (int)r.Size.X * 7) & 0x7FFFFFF;
}
