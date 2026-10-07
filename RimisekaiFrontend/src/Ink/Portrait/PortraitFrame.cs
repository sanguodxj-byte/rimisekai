using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 竖屏的框线语汇——哥特西幻（2026-10-07 主人令：去掉一切安卓 / Material 观感，只锁调色板）。
///
/// 形：倒角/缺角的「刻字铭牌」、尖拱（ogive）顶、双/三道细线、四角卷草（位图角花）、四叶/三叶饰、
/// 小菱与十字珠、铁艺铆钉点。禁：药丸（全圆端）钮、圆角卡、药丸签、圆头像、带把手的圆顶抽屉、
/// 水波/整块浅填式按压。
///
/// 颜色一律取 InkStyle 调色板（可调透明度），直线一律拉直不抖动；
/// 位图只做装饰（<see cref="PortraitOrnaments"/>），可点区域一律由代码登记的矩形决定。
/// </summary>
public static class PortraitFrame
{
    private static Rect2? _press;
    private static float _pressStrength = 1f;

    /// <summary>
    /// 按下态：矩形与它一致者按选中画。触摸没有悬停，按下当场不给反馈就只剩"点没点中"的疑问。
    /// 每个画面在 _Draw 开头调一次（没有按下就传 null）；后画的画面会覆盖前一个的值。
    /// strength＜1 是松手后的淡出（按压记号随之变淡）。
    /// </summary>
    public static void SetPress(Rect2? rect, float strength = 1f)
    {
        _press = rect;
        _pressStrength = strength;
    }

    private static bool Pressed(Rect2 r) =>
        _press is { } p && p.Position == r.Position && p.Size == r.Size;

    /// <summary>自绘块（地图格等）查询自己是否正被按住，用来给按压反馈。</summary>
    public static bool IsPressed(Rect2 r) => Pressed(r);

    /// <summary>按压底色：极淡的骨白（只作衬底，主反馈是 <see cref="PressMark"/> 的四角刻痕）。</summary>
    public static Color PressFill => new(InkStyle.Line, 0.09f * _pressStrength);

    /// <summary>
    /// 按压记号：倒角极淡衬底＋四角各一道 L 形刻痕（像铜牌上被按下的四枚角钉），
    /// 取代 Material 那种整块浅填 / 水波。
    /// </summary>
    public static void PressMark(CanvasItem ci, Rect2 r)
    {
        Bevel(ci, r, 18f, PressFill);
        var k = Mathf.Min(30f, Mathf.Min(r.Size.X, r.Size.Y) * 0.28f);
        var c = new Color(InkStyle.Line, 0.85f * _pressStrength);
        var g = r.Grow(-6f);
        foreach (var (o, dx, dy) in new[]
                 {
                     (g.Position, 1f, 1f), (new Vector2(g.End.X, g.Position.Y), -1f, 1f),
                     (g.End, -1f, -1f), (new Vector2(g.Position.X, g.End.Y), 1f, -1f),
                 })
            ci.DrawPolyline(new[] { o + new Vector2(dx * k, 0), o, o + new Vector2(0, dy * k) }, c, 3f, true);
    }

    private static Vector2[] Loop(Vector2[] pts)
    {
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0);
        loop[^1] = pts[0];
        return loop;
    }

    private static IReadOnlyList<Vector2> Loop(Rect2 r) => new[]
    {
        r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), r.Position,
    };

    /// <summary>封闭多边形：fill 为空不填，line 为空不描。</summary>
    public static void Poly(CanvasItem ci, Vector2[] pts, Color? fill, Color? line = null, float width = 3f)
    {
        if (fill is { } f)
            ci.DrawColoredPolygon(pts, f);
        if (line is { } l)
            ci.DrawPolyline(Loop(pts), l, width, true);
    }

    // ======================================================================
    // 几何：倒角八边形、尖拱顶、四叶饰、菱头签
    // ======================================================================

    /// <summary>倒角八边形（四角各切去 cut）。cut 自动收在短边三分之一以内。</summary>
    public static Vector2[] ChamferPoints(Rect2 r, float cut)
    {
        var c = Mathf.Clamp(cut, 0f, Mathf.Min(r.Size.X, r.Size.Y) / 3f);
        var x = r.Position.X;
        var y = r.Position.Y;
        var X = r.End.X;
        var Y = r.End.Y;
        if (c < 1f)
            return new[] { r.Position, new Vector2(X, y), r.End, new Vector2(x, Y) };
        return new[]
        {
            new Vector2(x + c, y), new Vector2(X - c, y), new Vector2(X, y + c), new Vector2(X, Y - c),
            new Vector2(X - c, Y), new Vector2(x + c, Y), new Vector2(x, Y - c), new Vector2(x, y + c),
        };
    }

    /// <summary>
    /// 倒角块（取代旧的圆角矩形）：hint 是旧圆角半径的口径，换算成倒角＝hint×0.45、至多 20px，
    /// 于是 3px 的细槽仍是直角，药丸尺寸的大圆角变成一刀利落的斜切。
    /// </summary>
    public static void Bevel(CanvasItem ci, Rect2 r, float hint, Color? fill, Color? line = null, float width = 3f) =>
        Poly(ci, ChamferPoints(r, Mathf.Min(hint * 0.45f, 20f)), fill, line, width);

    /// <summary>
    /// 尖拱轮廓：从左拱脚 (x0, ys) 经拱顶 (x0+w/2, ys-h) 到右拱脚，两段椭圆弧在拱顶相交成尖。
    /// 任意宽高都成立（矮宽的也是尖的），返回点序自左到右。
    /// </summary>
    public static List<Vector2> ArchCurve(float x0, float ys, float w, float h, int steps = 14)
    {
        const float tm = 1.05f; // 弧段止于 60°：拱顶切线不水平，故而成尖
        var pts = new List<Vector2>();
        var sm = Mathf.Sin(tm);
        var cm = 1f - Mathf.Cos(tm);
        for (var i = 0; i <= steps; i++)
        {
            var t = tm * i / steps;
            pts.Add(new Vector2(x0 + w / 2f * (1f - Mathf.Cos(t)) / cm, ys - h * Mathf.Sin(t) / sm));
        }
        for (var i = steps - 1; i >= 0; i--)
        {
            var t = tm * i / steps;
            pts.Add(new Vector2(x0 + w - w / 2f * (1f - Mathf.Cos(t)) / cm, ys - h * Mathf.Sin(t) / sm));
        }
        return pts;
    }

    /// <summary>尖拱窗：矩形顶部换成高 rise 的尖拱（拱顶即 r 的上沿），平底。</summary>
    public static Vector2[] ArchPoints(Rect2 r, float rise)
    {
        var rs = Mathf.Min(rise, r.Size.Y);
        var pts = ArchCurve(r.Position.X, r.Position.Y + rs, r.Size.X, rs);
        pts.Add(r.End);
        pts.Add(new Vector2(r.Position.X, r.End.Y));
        return pts.ToArray();
    }

    /// <summary>尖拱窗块。</summary>
    public static void Arch(CanvasItem ci, Rect2 r, float rise, Color? fill, Color? line = null, float width = 3f) =>
        Poly(ci, ArchPoints(r, rise), fill, line, width);

    /// <summary>
    /// 四叶饰（quatrefoil）轮廓：四瓣圆叶在上下左右，叶与叶之间收成尖角。r＝外接半径。
    /// 由「自圆心向外的射线与四个叶圆的最远交点」取样，故交接处是干净的尖。
    /// </summary>
    public static Vector2[] QuatrefoilPoints(Vector2 c, float r, int steps = 72)
    {
        var d = r * 0.42f;
        var rho = r - d;
        var lobes = new[] { new Vector2(d, 0), new Vector2(0, d), new Vector2(-d, 0), new Vector2(0, -d) };
        var pts = new Vector2[steps];
        for (var i = 0; i < steps; i++)
        {
            var a = Mathf.Tau * i / steps;
            var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var best = 0f;
            foreach (var l in lobes)
            {
                var b = u.Dot(l);
                var disc = rho * rho - (l.LengthSquared() - b * b);
                if (disc >= 0f)
                    best = Mathf.Max(best, b + Mathf.Sqrt(disc));
            }
            pts[i] = c + u * best;
        }
        return pts;
    }

    /// <summary>菱头签轮廓：左右两端削成尖（像一枚拉长的菱）。cap＝尖的进深。</summary>
    public static Vector2[] LozengeCapPoints(Rect2 r, float cap)
    {
        var cy = r.GetCenter().Y;
        return new[]
        {
            new Vector2(r.Position.X + cap, r.Position.Y), new Vector2(r.End.X - cap, r.Position.Y),
            new Vector2(r.End.X, cy), new Vector2(r.End.X - cap, r.End.Y),
            new Vector2(r.Position.X + cap, r.End.Y), new Vector2(r.Position.X, cy),
        };
    }

    // ======================================================================
    // 小饰件
    // ======================================================================

    /// <summary>十字珠：一枚实心小菱，四向各缀一粒细点（「·◆·」竖横皆有），作标题两旁的小饰。</summary>
    public static void CrossJewel(CanvasItem ci, Vector2 c, float s, Color color)
    {
        InkDraw.Jewel(ci, c, s, color);
        var dot = Mathf.Max(2.5f, s * 0.28f);
        foreach (var d in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
            ci.DrawCircle(c + d * (s * 1.75f), dot, color);
    }

    /// <summary>铁艺铆钉：一粒实心小圆点。</summary>
    public static void Rivet(CanvasItem ci, Vector2 c, Color color, float r = 3.5f) => ci.DrawCircle(c, r, color);

    /// <summary>三叶饰：三瓣实心小叶（上、左下、右下）＋中心菱，r＝外接半径。</summary>
    public static void Trefoil(CanvasItem ci, Vector2 c, float r, Color color)
    {
        var lr = r * 0.42f;
        for (var i = 0; i < 3; i++)
        {
            var a = -Mathf.Pi / 2f + Mathf.Tau * i / 3f;
            ci.DrawCircle(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r - lr), lr, color);
        }
        InkDraw.Jewel(ci, c, r * 0.36f, InkStyle.Bg);
    }

    /// <summary>四叶饰小件：空心双线四叶，正中一枚实心菱。</summary>
    public static void QuatrefoilMark(CanvasItem ci, Vector2 c, float r, Color color, Color? fill = null)
    {
        var pts = QuatrefoilPoints(c, r, 56);
        Poly(ci, pts, fill ?? InkStyle.Bg, color, 3f);
        InkDraw.Jewel(ci, c, r * 0.26f, color);
    }

    // ======================================================================
    // 基础容器
    // ======================================================================

    /// <summary>全屏黑底。</summary>
    public static void Backdrop(CanvasItem ci) =>
        ci.DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), InkStyle.Bg);

    /// <summary>
    /// 双线框：外线倒角主线、内线压暗，内缩 12px，框内收四角卷草。
    /// flourish 传 0 则不画角花（构件太矮、或框内整块被按钮盖住时）。
    /// </summary>
    public static void Panel(CanvasItem ci, Rect2 r, Color? fill = null,
        float flourish = PortraitLayout.Flourish)
    {
        Poly(ci, ChamferPoints(r, 18f), fill ?? InkStyle.Panel, InkStyle.Line, 4f);
        Poly(ci, ChamferPoints(r.Grow(-12f), 12f), null, InkStyle.Dim, 2f);
        if (flourish > 0f)
            PortraitOrnaments.Corners(ci, r.Grow(-14f), flourish * 1.2f, 0.55f);
    }

    /// <summary>铜版花框：Panel 的同一画法（竖版不再铺排线底纹，排线会把字压灰）。</summary>
    public static void Plate(CanvasItem ci, Rect2 r, Color? fill = null,
        float flourish = PortraitLayout.Flourish) => Panel(ci, r, fill, flourish);

    /// <summary>四角角花：位图卷草，贴框边向内长，尺寸按面板短边收敛，绝不外溢。</summary>
    public static void Flourishes(CanvasItem ci, Rect2 r, Color color, float size) =>
        PortraitOrnaments.Corners(ci, r, size * 1.2f, color.A * 0.6f);

    /// <summary>只长两只上角：下沿被内容占满的面板用。</summary>
    public static void TopFlourishes(CanvasItem ci, Rect2 r, float size) =>
        PortraitOrnaments.Corners(ci, r, size * 1.2f, 0.6f, bottom: false);

    /// <summary>竖版分割细线：与横版同一画法（<see cref="InkDraw.FadeRule"/>），最粗处取细线档 5px。</summary>
    public static void FadingRule(CanvasItem ci, float left, float right, float y) =>
        InkDraw.FadeRule(ci, left, right, y, PortraitLayout.LineHair, InkStyle.Dim);

    /// <summary>
    /// 旧式文字钮（竖版沿用层的入口按钮）：画成次级刻字铭牌，value 非空＝名称贴左、数值贴右。
    /// </summary>
    public static void Button(CanvasItem ci, Rect2 r, string label, bool selected = false,
        bool enabled = true, string value = "", bool beads = false)
    {
        var c = selected || enabled ? InkStyle.Line : InkStyle.Dim;
        PlaqueBody(ci, r, primary: false, enabled, selected || Pressed(r));
        var inner = r.Grow(-(PortraitLayout.Pad / 2f));
        if (beads)
            inner = new Rect2(inner.Position.X + 24f, inner.Position.Y, inner.Size.X - 48f, inner.Size.Y);
        if (value.Length == 0)
        {
            var size = InkDraw.FitSize(label, inner.Size.X, PortraitLayout.FontBody, PortraitLayout.FontMeta);
            InkDraw.Text(ci, r.GetCenter(), label, size, c, "cm");
            return;
        }
        var half = inner.Size.X / 2f;
        var nameSize = InkDraw.FitSize(label, half, PortraitLayout.FontBody, PortraitLayout.FontMeta);
        var valSize = InkDraw.FitSize(value, half, PortraitLayout.FontBody, PortraitLayout.FontMeta);
        InkDraw.Text(ci, new Vector2(inner.Position.X, r.GetCenter().Y), label, nameSize, c, "lm");
        InkDraw.Text(ci, new Vector2(inner.End.X, r.GetCenter().Y), value, valSize, InkStyle.Dim, "rm");
    }

    /// <summary>
    /// 战斗头像卡片框：不填底（底由调用方铺，免得盖掉插画），倒角双线，可点目标用亮线＋四角铆钉标出。
    /// </summary>
    public static void CardOutline(CanvasItem ci, Rect2 r, bool highlighted = false)
    {
        Poly(ci, ChamferPoints(r, 14f), null, highlighted ? InkStyle.Line : InkStyle.Dim, highlighted ? 4f : 3f);
        Poly(ci, ChamferPoints(r.Grow(-9f), 9f), null,
            highlighted ? new Color(InkStyle.Line, 0.7f) : new Color(InkStyle.WoodDark, 0.9f), 2f);
        if (highlighted)
            CornerRivets(ci, r.Grow(-18f), InkStyle.Line);
    }

    public static void CornerRivets(CanvasItem ci, Rect2 r, Color color, float size = 3.5f)
    {
        Rivet(ci, r.Position, color, size);
        Rivet(ci, new Vector2(r.End.X, r.Position.Y), color, size);
        Rivet(ci, r.End, color, size);
        Rivet(ci, new Vector2(r.Position.X, r.End.Y), color, size);
    }

    // ======================================================================
    // 铭牌钮（取代药丸钮）
    // ======================================================================

    private static float PlaqueCut(Rect2 r) => Mathf.Clamp(r.Size.Y * 0.24f, 12f, 26f);

    /// <summary>
    /// 铭牌底：primary＝外一道骨白细线框住一块骨白实心倒角牌（字反黑）；
    /// 否则＝双道倒角线（外骨白、内压暗），左右两条竖边正中各钉一枚小菱。
    /// lit＝选中或按住：内线提亮并在内腔压一层极淡衬底与四角刻痕。
    /// </summary>
    public static void PlaqueBody(CanvasItem ci, Rect2 r, bool primary, bool enabled, bool lit)
    {
        var cut = PlaqueCut(r);
        var edge = enabled ? InkStyle.Line : InkStyle.WoodDark;
        if (primary && enabled)
        {
            Poly(ci, ChamferPoints(r, cut), InkStyle.Bg, InkStyle.Line, 3f);
            Poly(ci, ChamferPoints(r.Grow(-9f), cut - 5f), lit ? new Color(InkStyle.Line, 0.8f) : InkStyle.Line);
            var cy = r.GetCenter().Y;
            InkDraw.Jewel(ci, new Vector2(r.Position.X + 22f, cy), 6f, InkStyle.Bg);
            InkDraw.Jewel(ci, new Vector2(r.End.X - 22f, cy), 6f, InkStyle.Bg);
            return;
        }
        Poly(ci, ChamferPoints(r, cut), lit ? PressFill : InkStyle.Bg, edge, enabled ? 3.5f : 2.5f);
        Poly(ci, ChamferPoints(r.Grow(-9f), cut - 5f), null,
            lit ? InkStyle.Line : enabled ? InkStyle.Dim : new Color(InkStyle.WoodDark, 0.7f), 2f);
        var my = r.GetCenter().Y;
        InkDraw.Jewel(ci, new Vector2(r.Position.X, my), 7f, edge);
        InkDraw.Jewel(ci, new Vector2(r.End.X, my), 7f, edge);
        if (lit && enabled)
        {
            var g = r.Grow(-16f);
            var k = Mathf.Min(22f, g.Size.Y * 0.3f);
            foreach (var (o, dx, dy) in new[]
                     {
                         (g.Position, 1f, 1f), (new Vector2(g.End.X, g.Position.Y), -1f, 1f),
                         (g.End, -1f, -1f), (new Vector2(g.Position.X, g.End.Y), 1f, -1f),
                     })
                ci.DrawPolyline(new[] { o + new Vector2(dx * k, 0), o, o + new Vector2(0, dy * k) },
                    new Color(InkStyle.Line, _pressStrength), 2.5f, true);
        }
    }

    /// <summary>
    /// 刻字铭牌钮：primary＝骨白实心牌黑字（一屏只给一个主操作），否则＝双线铭牌骨白字。
    /// 不可用时线降为暗木色、字降为银灰。glyph 非空时图标在字左。sub 是第二行小字。
    /// </summary>
    public static void Plaque(CanvasItem ci, Rect2 r, string label, bool primary = false, bool enabled = true,
        Action<CanvasItem, float, float, float, Color>? glyph = null, string sub = "", int size = PortraitLayout.FontBody)
    {
        var pressed = Pressed(r);
        PlaqueBody(ci, r, primary, enabled, pressed);
        var text = primary && enabled ? InkStyle.Bg : enabled ? InkStyle.Line : InkStyle.Dim;
        var cy = r.GetCenter().Y - (sub.Length > 0 ? 18f : 0f);
        var room = r.Size.X - 80f - (glyph != null ? 76f : 0f);
        var fit = InkDraw.FitSize(label, room, size, PortraitLayout.FontMeta);
        var tw = InkDraw.Measure(label, fit).X;
        if (glyph != null)
        {
            var start = r.GetCenter().X - (56f + 20f + tw) / 2f;
            glyph(ci, start + 28f, cy, 26f, text);
            InkDraw.Text(ci, new Vector2(start + 76f, cy), label, fit, text, "lm");
        }
        else
            InkDraw.Text(ci, new Vector2(r.GetCenter().X, cy), label, fit, text, "cm");
        if (sub.Length > 0)
            InkDraw.TextBounded(ci, new Rect2(r.Position.X + 40f, cy + 22f, r.Size.X - 80f, 48f), sub,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, primary && enabled ? InkStyle.WoodDark : InkStyle.Dim, "cm");
    }

    // ======================================================================
    // 旗幡（取代分段控件）
    // ======================================================================

    /// <summary>旗幡第 i 段的矩形（整段可点，命中块与画面同源）。</summary>
    public static Rect2 SegmentRect(Rect2 r, int count, int i)
    {
        var w = r.Size.X / count;
        return new Rect2(r.Position.X + i * w, r.Position.Y, w, r.Size.Y);
    }

    /// <summary>
    /// 旗幡：一条两端燕尾内凹的横幅（上下双线），段与段之间一道短竖线嵌小菱；
    /// 当前段＝一块骨白实心倒角铭牌、字反黑。
    /// </summary>
    public static void Segmented(CanvasItem ci, Rect2 r, IReadOnlyList<string> labels, int selected)
    {
        var x = r.Position.X;
        var y = r.Position.Y + 6f;
        var X = r.End.X;
        var Y = r.End.Y - 6f;
        var cy = (y + Y) / 2f;
        const float tail = 26f;
        var banner = new[]
        {
            new Vector2(x, y), new Vector2(X, y), new Vector2(X - tail, cy), new Vector2(X, Y),
            new Vector2(x, Y), new Vector2(x + tail, cy),
        };
        Poly(ci, banner, InkStyle.Panel, InkStyle.Line, 3f);
        ci.DrawLine(new Vector2(x + 14f, y + 9f), new Vector2(X - 14f, y + 9f), InkStyle.WoodDark, 2f, true);
        ci.DrawLine(new Vector2(x + 14f, Y - 9f), new Vector2(X - 14f, Y - 9f), InkStyle.WoodDark, 2f, true);
        for (var i = 0; i < labels.Count; i++)
        {
            var seg = SegmentRect(r, labels.Count, i);
            if (i > 0)
            {
                var sx = seg.Position.X;
                ci.DrawLine(new Vector2(sx, cy - 24f), new Vector2(sx, cy + 24f), InkStyle.WoodDark, 2f, true);
                InkDraw.Jewel(ci, new Vector2(sx, cy), 6f, InkStyle.Dim);
            }
            var inner = new Rect2(seg.Position.X + (i == 0 ? tail + 6f : 12f), y + 16f,
                seg.Size.X - (i == 0 ? tail + 18f : 24f) - (i == labels.Count - 1 ? tail : 0f), Y - y - 32f);
            var on = i == selected;
            if (on)
                Poly(ci, ChamferPoints(inner, 14f), InkStyle.Line);
            else if (Pressed(seg))
                PressMark(ci, inner);
            InkDraw.TextBounded(ci, inner.Grow(-8f), labels[i], PortraitLayout.FontBody, PortraitLayout.FontMeta,
                on ? InkStyle.Bg : InkStyle.Dim, "cm");
        }
    }

    // ======================================================================
    // 签：括号签（可点）与菱头签（只读）
    // ======================================================================

    /// <summary>签宽度：字宽＋左右各 36。</summary>
    public static float ChipWidth(string label, int size = PortraitLayout.FontMeta) =>
        InkDraw.Measure(label, size).X + 72f;

    public static void Brackets(CanvasItem ci, Rect2 r, Color color, float width = 3f)
    {
        var hook = Mathf.Min(16f, r.Size.Y * 0.2f);
        var x0 = r.Position.X + 4f;
        var x1 = r.End.X - 4f;
        var y0 = r.Position.Y;
        var y1 = r.End.Y;
        // 〔 〕：竖笔两端各折一道斜钩
        ci.DrawPolyline(new[] { new Vector2(x0 + hook + 6f, y0), new Vector2(x0 + 6f, y0 + hook),
            new Vector2(x0 + 6f, y1 - hook), new Vector2(x0 + hook + 6f, y1) }, color, width, true);
        ci.DrawPolyline(new[] { new Vector2(x1 - hook - 6f, y0), new Vector2(x1 - 6f, y0 + hook),
            new Vector2(x1 - 6f, y1 - hook), new Vector2(x1 - hook - 6f, y1) }, color, width, true);
    }

    /// <summary>
    /// 括号签：命中块是整格 r（≥118 高），画出来的签在格内垂直居中、高 visual。
    /// 选中＝骨白实心菱头签黑字；未选＝〔 〕括号银灰字。
    /// </summary>
    public static void Chip(CanvasItem ci, Rect2 r, string label, bool selected, float visual = 96f)
    {
        var body = new Rect2(r.Position.X, r.GetCenter().Y - visual / 2f, r.Size.X, visual);
        if (selected)
            Poly(ci, LozengeCapPoints(body, Mathf.Min(26f, visual * 0.3f)), InkStyle.Line);
        else
        {
            if (Pressed(r))
                PressMark(ci, body);
            Brackets(ci, body, InkStyle.Dim, 3f);
        }
        InkDraw.TextBounded(ci, body.Grow(-20f), label, PortraitLayout.FontMeta, PortraitLayout.FontMeta,
            selected ? InkStyle.Bg : InkStyle.Dim, "cm");
    }

    /// <summary>只读小签（特质等）：暗木细线菱头签，不可点；lit＝骨白实心。</summary>
    public static float Tag(CanvasItem ci, Vector2 at, string label, float height = 72f, bool lit = false)
    {
        var w = ChipWidth(label) - 16f;
        var r = new Rect2(at, new Vector2(w, height));
        var pts = LozengeCapPoints(r, Mathf.Min(20f, height * 0.3f));
        if (lit)
            Poly(ci, pts, InkStyle.Line);
        else
            Poly(ci, pts, null, InkStyle.WoodDark, 2.5f);
        InkDraw.Text(ci, r.GetCenter(), label, PortraitLayout.FontMeta, lit ? InkStyle.Bg : InkStyle.Dim, "cm");
        return w;
    }

    // ======================================================================
    // 卡片、碑板（取代底部抽屉）
    // ======================================================================

    /// <summary>
    /// 卡片：倒角石板。未选＝暗木细线；选中/按下＝骨白外线＋内一道压暗细线＋四角铆钉。
    /// hint 沿用旧圆角半径口径（换算成倒角）。
    /// </summary>
    public static void Card(CanvasItem ci, Rect2 r, bool selected = false, float hint = 24f)
    {
        var pressed = Pressed(r);
        var active = selected || pressed;
        var cut = Mathf.Clamp(hint * 0.6f, 10f, 18f);
        Poly(ci, ChamferPoints(r, cut), pressed ? new Color(InkStyle.Hover, 1f) : active ? InkStyle.Hover : InkStyle.Panel,
            active ? InkStyle.Line : InkStyle.WoodDark, active ? 3.5f : 2.5f);
        if (!active)
            return;
        Poly(ci, ChamferPoints(r.Grow(-9f), cut - 4f), null, new Color(InkStyle.Dim, 0.8f), 2f);
        if (r.Size.Y >= 90f)
            CornerRivets(ci, r.Grow(-20f), new Color(InkStyle.Line, 0.9f));
        if (pressed)
            PressMark(ci, r.Grow(-12f));
    }

    /// <summary>碑板上方压暗的满档透明度。</summary>
    public const float ScrimAlpha = 0.70f;

    /// <summary>
    /// 碑板：一块自屏底升起的石碑（取代带把手的圆顶抽屉），上沿三道细线，正中一枚尖拱冠饰
    /// （位图卷草冠），上两角各一丛卷草角花。不带把手。返回面板矩形。
    /// </summary>
    public static Rect2 Sheet(CanvasItem ci, float top)
    {
        var r = new Rect2(0, top, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - top);
        Tablet(ci, r);
        return r;
    }

    /// <summary>碑板本体（Sheet 与战斗行动板共用）：crest＝是否画正中冠饰。</summary>
    public static void Tablet(CanvasItem ci, Rect2 r, bool crest = true)
    {
        var top = r.Position.Y;
        var w = r.Size.X;
        var x = r.Position.X;
        ci.DrawRect(new Rect2(x, top, w, r.Size.Y + 80f), InkStyle.Panel);
        // 上沿：骨白主线＋两道压暗细线（三道线读作石碑的线脚）
        ci.DrawLine(new Vector2(x, top), new Vector2(x + w, top), InkStyle.Line, 4f, true);
        ci.DrawLine(new Vector2(x + 18f, top + 12f), new Vector2(x + w - 18f, top + 12f), InkStyle.Dim, 2f, true);
        ci.DrawLine(new Vector2(x + 30f, top + 20f), new Vector2(x + w - 30f, top + 20f), new Color(InkStyle.WoodDark, 0.9f), 1.5f, true);
        // 两侧竖线脚
        var bottom = r.End.Y + 80f;
        ci.DrawLine(new Vector2(x + 18f, top + 12f), new Vector2(x + 18f, bottom), new Color(InkStyle.WoodDark, 0.9f), 2f, true);
        ci.DrawLine(new Vector2(x + w - 18f, top + 12f), new Vector2(x + w - 18f, bottom), new Color(InkStyle.WoodDark, 0.9f), 2f, true);
        // 两上角：卷草角花（贴线脚内侧）
        PortraitOrnaments.Corners(ci, new Rect2(x + 22f, top + 24f, w - 44f, 400f), 118f, 0.42f, bottom: false);
        if (!crest)
        {
            QuatrefoilMark(ci, new Vector2(x + w / 2f, top), 17f, InkStyle.Line, InkStyle.Panel);
            return;
        }
        // 冠饰：上沿正中立一枚位图尖拱冠（底边骑在上沿，主体升在压暗区里，只是装饰，不收点击）
        PortraitOrnaments.Crest(ci, new Vector2(x + w / 2f, top), 600f, 0.9f, above: true);
    }

    // ======================================================================
    // 分节线、刻度、量规
    // ======================================================================

    /// <summary>
    /// 分节线「──·◆· 标题 ·◆·──」：标题两旁各一枚十字珠，两翼各一截渐隐线（靠标题一端最亮）。
    /// label 空则画一条正中嵌四叶小饰的渐隐线。
    /// </summary>
    public static void SectionRule(CanvasItem ci, float x1, float x2, float y, string label = "")
    {
        var cx = (x1 + x2) / 2f;
        if (label.Length == 0)
        {
            InkDraw.FadeRule(ci, x1, cx - 30f, y, PortraitLayout.LineHair, InkStyle.Dim, InkDraw.FadeTaper.Left);
            InkDraw.FadeRule(ci, cx + 30f, x2, y, PortraitLayout.LineHair, InkStyle.Dim, InkDraw.FadeTaper.Right);
            QuatrefoilMark(ci, new Vector2(cx, y), 15f, InkStyle.Dim);
            return;
        }
        var w = InkDraw.Measure(label, PortraitLayout.FontMeta).X;
        InkDraw.Text(ci, new Vector2(cx, y), label, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        CrossJewel(ci, new Vector2(cx - w / 2f - 40f, y), 7f, InkStyle.Line);
        CrossJewel(ci, new Vector2(cx + w / 2f + 40f, y), 7f, InkStyle.Line);
        InkDraw.FadeRule(ci, x1, cx - w / 2f - 64f, y, PortraitLayout.LineHair, InkStyle.Dim, InkDraw.FadeTaper.Left);
        InkDraw.FadeRule(ci, cx + w / 2f + 64f, x2, y, PortraitLayout.LineHair, InkStyle.Dim, InkDraw.FadeTaper.Right);
    }

    /// <summary>菱形刻度：n 枚，前 k 枚实心骨白，其余暗木小菱。k 的小数部分把下一枚画成半亮。</summary>
    public static void Ticks(CanvasItem ci, float x, float y, int n, float k, float size = 30f, float gap = 20f)
    {
        for (var i = 0; i < n; i++)
        {
            var cx = x + i * (size + gap) + size / 2f;
            var c = new Vector2(cx, y);
            if (i + 1 <= k)
                InkDraw.Jewel(ci, c, size / 2f, InkStyle.Line);
            else if (i < k)
            {
                InkDraw.Jewel(ci, c, size / 2f, InkStyle.WoodDark);
                ci.DrawColoredPolygon(new[] { c + new Vector2(0, -size / 2f), c, c + new Vector2(0, size / 2f),
                    c + new Vector2(-size / 2f, 0) }, InkStyle.Line);
            }
            else
                InkDraw.Jewel(ci, c, size / 2f, InkStyle.WoodDark);
        }
    }

    /// <summary>
    /// 刻度量规（取代圆头进度条）：两端各一枚菱形端饰，中间一道压暗细槽线；
    /// 填充＝骨白实心细条，前端削成矛尖。矩形 r 是整条量规的外廓。
    /// </summary>
    public static void Bar(CanvasItem ci, Rect2 r, float frac)
    {
        var cy = r.GetCenter().Y;
        var cap = Mathf.Clamp(r.Size.Y * 0.62f, 5f, 11f);
        var x0 = r.Position.X + cap;
        var x1 = r.End.X - cap;
        var h = Mathf.Max(4f, r.Size.Y * 0.62f);
        ci.DrawLine(new Vector2(x0, cy - h / 2f - 1f), new Vector2(x1, cy - h / 2f - 1f), new Color(InkStyle.WoodDark, 0.9f), 1.5f, true);
        ci.DrawLine(new Vector2(x0, cy + h / 2f + 1f), new Vector2(x1, cy + h / 2f + 1f), new Color(InkStyle.WoodDark, 0.9f), 1.5f, true);
        var f = Mathf.Clamp(frac, 0f, 1f);
        if (f > 0f)
        {
            var end = x0 + (x1 - x0) * f;
            var tip = Mathf.Min(h, end - x0);
            ci.DrawColoredPolygon(new[]
            {
                new Vector2(x0, cy - h / 2f), new Vector2(end - tip, cy - h / 2f), new Vector2(end, cy),
                new Vector2(end - tip, cy + h / 2f), new Vector2(x0, cy + h / 2f),
            }, InkStyle.Line);
        }
        InkDraw.Jewel(ci, new Vector2(r.Position.X + cap * 0.5f, cy), cap, InkStyle.Dim);
        InkDraw.Jewel(ci, new Vector2(r.End.X - cap * 0.5f, cy), cap, f >= 1f ? InkStyle.Line : InkStyle.Dim);
    }

    // ======================================================================
    // 哥特框（取代缺角框）、徽章头像（取代圆头像）
    // ======================================================================

    /// <summary>
    /// 哥特框：倒角外线（骨白）＋内收 12px 暗木细线；ornate＝四角收位图卷草、上下沿正中各一枚小菱；
    /// crest＝上沿正中再立一枚冠饰（弹窗用）。
    /// </summary>
    public static void GothicFrame(CanvasItem ci, Rect2 r, Color? fill = null, bool ornate = true, bool crest = false)
    {
        Poly(ci, ChamferPoints(r, 22f), fill ?? InkStyle.Panel, InkStyle.Line, 4f);
        Poly(ci, ChamferPoints(r.Grow(-12f), 14f), null, InkStyle.WoodDark, 2f);
        if (!ornate)
            return;
        var k = Mathf.Min(120f, Mathf.Min(r.Size.X, r.Size.Y) * 0.26f);
        PortraitOrnaments.Corners(ci, r.Grow(-16f), k, 0.5f);
        var cx = r.GetCenter().X;
        InkDraw.Jewel(ci, new Vector2(cx, r.End.Y), 9f, InkStyle.Line);
        InkDraw.Jewel(ci, new Vector2(cx, r.End.Y), 4f, InkStyle.Bg);
        if (crest)
            PortraitOrnaments.Crest(ci, new Vector2(cx, r.Position.Y), Mathf.Min(560f, r.Size.X * 0.72f), 0.9f, above: true);
        else
        {
            InkDraw.Jewel(ci, new Vector2(cx, r.Position.Y), 9f, InkStyle.Line);
            InkDraw.Jewel(ci, new Vector2(cx, r.Position.Y), 4f, InkStyle.Bg);
        }
    }

    /// <summary>
    /// 徽章头像：贴图裁上半身后套进一枚四叶窗（quatrefoil），无图则 Panel 底＋名字首字。
    /// ring＝外一道骨白四叶线（半径够大时再加一道压暗外线与四枚斜角小菱）。
    /// </summary>
    public static void Avatar(CanvasItem ci, Vector2 center, float radius, Texture2D? tex, string name,
        bool ring = true, bool dim = false)
    {
        var pts = QuatrefoilPoints(center, radius, 72);
        if (tex != null)
        {
            var size = tex.GetSize();
            var side = Mathf.Min(size.X, size.Y);
            var origin = new Vector2((size.X - side) / 2f, 0f);
            var uvs = new Vector2[pts.Length];
            for (var i = 0; i < pts.Length; i++)
            {
                var d = (pts[i] - center) / radius;
                uvs[i] = (origin + (d * 0.5f + new Vector2(0.5f, 0.5f)) * side) / size;
            }
            ci.DrawColoredPolygon(pts, dim ? new Color(1f, 1f, 1f, 0.45f) : Colors.White, uvs, tex);
        }
        else
        {
            ci.DrawColoredPolygon(pts, InkStyle.Panel);
            if (name.Length > 0)
                InkDraw.Text(ci, center, name[..1], (int)Mathf.Max(PortraitLayout.FontMeta, radius * 0.8f),
                    dim ? InkStyle.Dim : InkStyle.Line, "cm");
        }
        if (!ring)
            return;
        var c = dim ? InkStyle.WoodDark : InkStyle.Line;
        ci.DrawPolyline(Loop(pts), c, radius >= 60f ? 4f : 3f, true);
        if (radius < 40f)
            return;
        ci.DrawPolyline(Loop(QuatrefoilPoints(center, radius + 9f, 72)), new Color(dim ? InkStyle.WoodDark : InkStyle.Dim, 0.8f), 2f, true);
        var j = radius * 0.80f + 12f;
        foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            InkDraw.Jewel(ci, center + d.Normalized() * j, radius >= 100f ? 9f : 6f, c);
    }

    /// <summary>纵向渐隐：自 topAlpha 到 botAlpha 的黑罩（压在插画下缘，让文字读得出）。</summary>
    public static void Fade(CanvasItem ci, Rect2 r, float topAlpha, float botAlpha)
    {
        ci.DrawPolygon(new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) },
            new[] { new Color(InkStyle.Bg, topAlpha), new Color(InkStyle.Bg, topAlpha),
                new Color(InkStyle.Bg, botAlpha), new Color(InkStyle.Bg, botAlpha) });
    }

    /// <summary>贴图按覆盖方式铺进矩形（anchorY＝竖向取景位置 0 顶 1 底），超出部分裁掉。</summary>
    public static void Cover(CanvasItem ci, Texture2D tex, Rect2 r, float anchorY = 0f)
    {
        var size = tex.GetSize();
        var scale = Mathf.Max(r.Size.X / size.X, r.Size.Y / size.Y);
        var src = new Vector2(r.Size.X / scale, r.Size.Y / scale);
        var origin = new Vector2((size.X - src.X) / 2f, (size.Y - src.Y) * anchorY);
        ci.DrawTextureRectRegion(tex, r, new Rect2(origin, src));
    }
}
