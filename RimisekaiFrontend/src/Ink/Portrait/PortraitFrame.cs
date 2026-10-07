using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 竖屏的框线语汇：与横屏同一套（细双线、四角角花、无标题牌、无框中框、直线不抖动），
/// 只把线宽与留白换成按毫米反推的值——横屏的 1px 内线在这块屏上只有 0.064mm，会画成灰影。
/// 颜色与字体一律取自 InkStyle，笔画一律走 InkDraw。
/// </summary>
public static class PortraitFrame
{
    private static Rect2? _press;
    private static float _pressStrength = 1f;

    /// <summary>
    /// 按下态：矩形与它一致者按选中画。触摸没有悬停，按下当场不给反馈就只剩"点没点中"的疑问。
    /// 每个画面在 _Draw 开头调一次（没有按下就传 null）；后画的画面会覆盖前一个的值。
    /// strength＜1 是松手后的淡出（按压浅填随之变淡）。
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

    /// <summary>按压填色（自绘块与 Button 共用同一档，保持全界面一致）。</summary>
    public static Color PressFill => new(InkStyle.Line, 0.16f * _pressStrength);

    private static IReadOnlyList<Vector2> Loop(Rect2 r) => new[]
    {
        r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), r.Position,
    };

    /// <summary>全屏黑底。</summary>
    public static void Backdrop(CanvasItem ci) =>
        ci.DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), InkStyle.Bg);

    /// <summary>
    /// 双线框：外线主线、内线压暗，内缩 12px（横屏是 6px，手机上读不出两层），框内收四角角花。
    /// flourish 传 0 则不画角花（构件太矮、或框内整块被按钮盖住时）。
    /// </summary>
    public static void Panel(CanvasItem ci, Rect2 r, Color? fill = null,
        float flourish = PortraitLayout.Flourish)
    {
        ci.DrawRect(r, fill ?? InkStyle.Panel);
        InkDraw.Ink(ci, Loop(r), InkStyle.Line, PortraitLayout.LineBold);
        InkDraw.Ink(ci, Loop(r.Grow(-12f)), InkStyle.Dim, PortraitLayout.LineHair);
        if (flourish > 0f)
            Flourishes(ci, r, InkStyle.Line, flourish);
    }

    /// <summary>
    /// 铜版花框：双线框＋四角角花＋贴角排线——参考稿四角那种"细排线贴着角、向黑场渐隐"的底纹。
    /// 只给构件级容器（标题画面外框、顶栏、弹窗）用；内容面板仍走 Panel，
    /// 排线铺在内容底下会把字压灰。
    /// </summary>
    public static void Plate(CanvasItem ci, Rect2 r, Color? fill = null,
        float flourish = PortraitLayout.Flourish)
    {
        ci.DrawRect(r, fill ?? InkStyle.Panel);
        // 排线区按框收敛：矮框（顶栏 120 高）若按 300 铺，纹样会越出框外压到内容区。
        var zone = Mathf.Min(PortraitLayout.EtchZone, Mathf.Min(r.Size.X, r.Size.Y) * 0.46f);
        InkDraw.CornerEtching(ci, r.Grow(-16f), new Color(InkStyle.Line, 0.17f),
            zone: zone, width: PortraitLayout.OrnamentWidth * 0.62f,
            spacing: PortraitLayout.EtchSpacing);
        InkDraw.Ink(ci, Loop(r), InkStyle.Line, PortraitLayout.LineBold);
        InkDraw.Ink(ci, Loop(r.Grow(-12f)), InkStyle.Dim, PortraitLayout.LineHair);
        if (flourish > 0f)
            Flourishes(ci, r, InkStyle.Line, flourish);
    }

    /// <summary>四角角花：贴框边向内长，尺寸按面板短边收敛，绝不外溢。</summary>
    public static void Flourishes(CanvasItem ci, Rect2 r, Color color, float size)
    {
        var k = Mathf.Min(size, Mathf.Min(r.Size.X, r.Size.Y) * 0.42f);
        if (k < 20f)
            return;
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
            InkDraw.CornerFlourish(ci, new Vector2(cx, cy), dx, dy, color, k, 4100 + i * 13,
                PortraitLayout.OrnamentWidth, PortraitLayout.OrnamentJewel);
        }
    }

    /// <summary>
    /// 只长两只上角：下沿被内容占满的面板用（状态页立绘卡的名牌就压在下沿两角）。
    /// </summary>
    public static void TopFlourishes(CanvasItem ci, Rect2 r, float size)
    {
        var k = Mathf.Min(size, Mathf.Min(r.Size.X, r.Size.Y) * 0.42f);
        if (k < 20f)
            return;
        InkDraw.CornerFlourish(ci, r.Position, 1f, 1f, InkStyle.Line, k, 4100,
            PortraitLayout.OrnamentWidth, PortraitLayout.OrnamentJewel);
        InkDraw.CornerFlourish(ci, new Vector2(r.End.X, r.Position.Y), -1f, 1f, InkStyle.Line, k, 4113,
            PortraitLayout.OrnamentWidth, PortraitLayout.OrnamentJewel);
    }

    /// <summary>竖版分割细线：与横版同一画法（<see cref="InkDraw.FadeRule"/>），最粗处取细线档 5px。</summary>
    public static void FadingRule(CanvasItem ci, float left, float right, float y) =>
        InkDraw.FadeRule(ci, left, right, y, PortraitLayout.LineHair, InkStyle.Dim);

    /// <summary>
    /// 按钮：双线矩形。放不下就在 [FontMeta, FontBody] 内自动缩字号，不截断不溢出。
    /// value 非空＝名称贴左、数值贴右，各吃自己那半。
    /// beads＝两端各缀一枚实心菱珠（参考稿菜单钮的做法），只给一级入口用。
    /// </summary>
    public static void Button(CanvasItem ci, Rect2 r, string label, bool selected = false,
        bool enabled = true, string value = "", bool beads = false)
    {
        var c = selected || enabled ? InkStyle.Line : InkStyle.Dim;
        var pressed = Pressed(r);
        var active = selected || pressed;
        ci.DrawRect(r, pressed ? PressFill : active ? InkStyle.Hover : InkStyle.Inset);
        InkDraw.Ink(ci, Loop(r), enabled ? InkStyle.Line : InkStyle.Dim,
            pressed ? PortraitLayout.LineBold + 2f : PortraitLayout.LineBold);
        InkDraw.Ink(ci, Loop(r.Grow(-12f)), active ? InkStyle.Line : InkStyle.Dim, PortraitLayout.LineHair);

        if (beads)
        {
            var cy = r.GetCenter().Y;
            InkDraw.Jewel(ci, new Vector2(r.Position.X + 30f, cy), PortraitLayout.OrnamentJewel,
                enabled ? InkStyle.Line : InkStyle.Dim);
            InkDraw.Jewel(ci, new Vector2(r.End.X - 30f, cy), PortraitLayout.OrnamentJewel,
                enabled ? InkStyle.Line : InkStyle.Dim);
        }

        var inner = r.Grow(-(PortraitLayout.Pad / 2f));
        if (beads)
            inner = new Rect2(inner.Position.X + 24f, inner.Position.Y, inner.Size.X - 48f, inner.Size.Y);
        if (value.Length == 0)
        {
            var size = InkDraw.FitSize(label, inner.Size.X, PortraitLayout.FontBody, 30);
            InkDraw.Text(ci, r.GetCenter(), label, size, c, "cm");
            return;
        }

        var half = inner.Size.X / 2f;
        var nameSize = InkDraw.FitSize(label, half, PortraitLayout.FontBody, 30);
        var valSize = InkDraw.FitSize(value, half, PortraitLayout.FontBody, 30);
        InkDraw.Text(ci, new Vector2(inner.Position.X, r.GetCenter().Y), label, nameSize, c, "lm");
        InkDraw.Text(ci, new Vector2(inner.End.X, r.GetCenter().Y), value, valSize, InkStyle.Dim, "rm");
    }

    /// <summary>
    /// 战斗头像卡片框：不填底（底由调用方铺，免得盖掉插画），可点目标用粗亮框标出。
    /// 横屏那版 1.3px 的框在手机上是 0.08mm，读不出一条线，所以竖屏另给毫米档。
    /// </summary>
    public static void CardOutline(CanvasItem ci, Rect2 r, bool highlighted = false)
    {
        InkDraw.Ink(ci, Loop(r), InkStyle.Line,
            highlighted ? PortraitLayout.LineBold : PortraitLayout.LineHair);
        InkDraw.Ink(ci, Loop(r.Grow(-12f)), highlighted ? InkStyle.Line : new Color(InkStyle.Dim, 0.8f),
            PortraitLayout.LineHair - 1f);
    }

    // ======================================================================
    // 2026-10-07 新语汇：圆角药丸钮、分段控件、标签签、卡片、底部抽屉、
    // 分节线「─◆ 标题 ◆─」、菱形刻度、圆头进度条、缺角双线框＋角珠、圆形头像。
    // 颜色一律取 InkStyle 调色板（透明度可变），线条一律直线/圆弧，不抖动。
    // ======================================================================

    /// <summary>圆角矩形的顶点环（顺时针、首尾不重复）。radius 自动收在短边一半以内。</summary>
    public static Vector2[] RoundRectPoints(Rect2 r, float radius, int arcSteps = 8)
    {
        var rad = Mathf.Min(radius, Mathf.Min(r.Size.X, r.Size.Y) / 2f);
        var pts = new List<Vector2>();
        void Corner(Vector2 c, float a0)
        {
            for (var i = 0; i <= arcSteps; i++)
            {
                var a = a0 + Mathf.Pi / 2f * i / arcSteps;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
            }
        }
        Corner(new Vector2(r.End.X - rad, r.Position.Y + rad), -Mathf.Pi / 2f);
        Corner(new Vector2(r.End.X - rad, r.End.Y - rad), 0f);
        Corner(new Vector2(r.Position.X + rad, r.End.Y - rad), Mathf.Pi / 2f);
        Corner(new Vector2(r.Position.X + rad, r.Position.Y + rad), Mathf.Pi);
        return pts.ToArray();
    }

    /// <summary>圆角矩形：fill 为空不填，line 为空不描。</summary>
    public static void RoundRect(CanvasItem ci, Rect2 r, float radius, Color? fill, Color? line = null,
        float width = PortraitLayout.LineHair)
    {
        var pts = RoundRectPoints(r, radius);
        if (fill is { } f)
            ci.DrawColoredPolygon(pts, f);
        if (line is { } l)
        {
            var loop = new Vector2[pts.Length + 1];
            pts.CopyTo(loop, 0);
            loop[^1] = pts[0];
            ci.DrawPolyline(loop, l, width, true);
        }
    }

    /// <summary>
    /// 药丸钮：primary＝骨白实底黑字（一屏只给一个主操作）；否则黑底骨白描边。
    /// 不可用时描边降为暗木色、字降为银灰。glyph 非空时图标在字左。sub 是第二行小字。
    /// </summary>
    public static void Pill(CanvasItem ci, Rect2 r, string label, bool primary = false, bool enabled = true,
        Action<CanvasItem, float, float, float, Color>? glyph = null, string sub = "", int size = PortraitLayout.FontBody)
    {
        var pressed = Pressed(r);
        Color text;
        if (primary && enabled)
        {
            RoundRect(ci, r, r.Size.Y / 2f, pressed ? new Color(InkStyle.Line, 0.78f) : InkStyle.Line);
            text = InkStyle.Bg;
        }
        else
        {
            RoundRect(ci, r, r.Size.Y / 2f, pressed ? PressFill : InkStyle.Bg,
                enabled ? InkStyle.Line : InkStyle.WoodDark, enabled ? PortraitLayout.LineHair : PortraitLayout.LineHair - 1f);
            text = enabled ? InkStyle.Line : InkStyle.Dim;
        }
        var cy = r.GetCenter().Y - (sub.Length > 0 ? 18f : 0f);
        var room = r.Size.X - r.Size.Y * 0.6f - (glyph != null ? 76f : 0f);
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
            InkDraw.TextBounded(ci, new Rect2(r.Position.X + r.Size.Y / 2f, cy + 22f, r.Size.X - r.Size.Y, 48f), sub,
                PortraitLayout.FontMeta, PortraitLayout.FontMeta, primary && enabled ? InkStyle.WoodDark : InkStyle.Dim, "cm");
    }

    /// <summary>分段控件第 i 段的矩形（整段可点，命中块与画面同源）。</summary>
    public static Rect2 SegmentRect(Rect2 r, int count, int i)
    {
        var w = r.Size.X / count;
        return new Rect2(r.Position.X + i * w, r.Position.Y, w, r.Size.Y);
    }

    /// <summary>分段控件：圆角槽＋选中段骨白实心药丸。</summary>
    public static void Segmented(CanvasItem ci, Rect2 r, IReadOnlyList<string> labels, int selected)
    {
        RoundRect(ci, r, r.Size.Y / 2f, InkStyle.Panel, InkStyle.WoodDark, PortraitLayout.LineHair - 1f);
        for (var i = 0; i < labels.Count; i++)
        {
            var seg = SegmentRect(r, labels.Count, i);
            var inner = seg.Grow(-10f);
            var on = i == selected;
            if (on)
                RoundRect(ci, inner, inner.Size.Y / 2f, InkStyle.Line);
            else if (Pressed(seg))
                RoundRect(ci, inner, inner.Size.Y / 2f, PressFill);
            InkDraw.TextBounded(ci, inner.Grow(-12f), labels[i], PortraitLayout.FontBody, PortraitLayout.FontMeta,
                on ? InkStyle.Bg : InkStyle.Dim, "cm");
        }
    }

    /// <summary>标签签宽度：字宽＋左右各 36。</summary>
    public static float ChipWidth(string label, int size = PortraitLayout.FontMeta) =>
        InkDraw.Measure(label, size).X + 72f;

    /// <summary>
    /// 标签签：命中块是整格 r（≥118 高），画出来的药丸在格内垂直居中、高 visual。
    /// 选中＝骨白实心黑字；未选＝暗木色描边银灰字。
    /// </summary>
    public static void Chip(CanvasItem ci, Rect2 r, string label, bool selected, float visual = 96f)
    {
        var pill = new Rect2(r.Position.X, r.GetCenter().Y - visual / 2f, r.Size.X, visual);
        if (selected)
            RoundRect(ci, pill, visual / 2f, InkStyle.Line);
        else
            RoundRect(ci, pill, visual / 2f, Pressed(r) ? PressFill : null, InkStyle.WoodDark, PortraitLayout.LineHair - 1f);
        InkDraw.TextBounded(ci, pill.Grow(-20f), label, PortraitLayout.FontMeta, PortraitLayout.FontMeta,
            selected ? InkStyle.Bg : InkStyle.Dim, "cm");
    }

    /// <summary>只读小签（特质等）：暗木描边，不可点。</summary>
    public static float Tag(CanvasItem ci, Vector2 at, string label, float height = 72f, bool lit = false)
    {
        var w = ChipWidth(label) - 16f;
        var r = new Rect2(at, new Vector2(w, height));
        if (lit)
            RoundRect(ci, r, height / 2f, InkStyle.Line);
        else
            RoundRect(ci, r, height / 2f, null, InkStyle.WoodDark, PortraitLayout.LineHair - 1f);
        InkDraw.Text(ci, r.GetCenter(), label, PortraitLayout.FontMeta, lit ? InkStyle.Bg : InkStyle.Dim, "cm");
        return w;
    }

    /// <summary>卡片：圆角，未选暗木细描边，选中/按下时 Hover 浅填＋骨白描边。</summary>
    public static void Card(CanvasItem ci, Rect2 r, bool selected = false, float radius = 24f)
    {
        var pressed = Pressed(r);
        var active = selected || pressed;
        RoundRect(ci, r, radius, pressed ? PressFill : active ? InkStyle.Hover : InkStyle.Panel,
            active ? InkStyle.Line : InkStyle.WoodDark, active ? PortraitLayout.LineHair : PortraitLayout.LineHair - 1f);
    }

    /// <summary>抽屉上方压暗的满档透明度。</summary>
    public const float ScrimAlpha = 0.66f;

    /// <summary>
    /// 底部抽屉：一块圆顶面板与把手，返回面板矩形。上方压暗由调用方画
    /// （压暗随抽屉滑入淡入，下层命中块也由调用方移除）。
    /// </summary>
    public static Rect2 Sheet(CanvasItem ci, float top)
    {
        var r = new Rect2(0, top, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - top + 80f);
        RoundRect(ci, r, 56f, InkStyle.Panel, InkStyle.Dim, PortraitLayout.LineHair - 1f);
        RoundRect(ci, new Rect2(PortraitLayout.CanvasWidth / 2f - 70f, top + 24f, 140f, 12f), 6f, InkStyle.WoodDark);
        return new Rect2(0, top, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - top);
    }

    /// <summary>
    /// 分节线「──◆ 标题 ◆──」：标题两旁各一枚实心菱，两翼各一截渐隐线（靠标题一端最亮，向屏边收尖）。
    /// label 空则只画一条正中嵌小菱的渐隐线。
    /// </summary>
    public static void SectionRule(CanvasItem ci, float x1, float x2, float y, string label = "")
    {
        if (label.Length == 0)
        {
            InkDraw.FadeRule(ci, x1, x2, y, PortraitLayout.LineHair, InkStyle.Dim, lozenge: true);
            return;
        }
        var w = InkDraw.Measure(label, PortraitLayout.FontMeta).X;
        var cx = (x1 + x2) / 2f;
        InkDraw.Text(ci, new Vector2(cx, y), label, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        InkDraw.Jewel(ci, new Vector2(cx - w / 2f - 34f, y), 9f, InkStyle.Line);
        InkDraw.Jewel(ci, new Vector2(cx + w / 2f + 34f, y), 9f, InkStyle.Line);
        InkDraw.FadeRule(ci, x1, cx - w / 2f - 56f, y, PortraitLayout.LineHair, InkStyle.Dim, InkDraw.FadeTaper.Left);
        InkDraw.FadeRule(ci, cx + w / 2f + 56f, x2, y, PortraitLayout.LineHair, InkStyle.Dim, InkDraw.FadeTaper.Right);
    }

    /// <summary>菱形刻度：n 枚，前 k 枚实心骨白，其余暗木小菱。half 表示第 k+1 枚画成半亮。</summary>
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

    /// <summary>圆头进度条：暗木细槽＋骨白实心填充（填充至少一个圆头宽）。</summary>
    public static void Bar(CanvasItem ci, Rect2 r, float frac)
    {
        RoundRect(ci, r, r.Size.Y / 2f, null, InkStyle.WoodDark, 3f);
        var f = Mathf.Clamp(frac, 0f, 1f);
        if (f <= 0f)
            return;
        var w = Mathf.Max(r.Size.Y, r.Size.X * f);
        RoundRect(ci, new Rect2(r.Position, new Vector2(w, r.Size.Y)), r.Size.Y / 2f, InkStyle.Line);
    }

    /// <summary>缺角双线框：外框八边形主线，内收 14px 暗木细线，四角嵌空心菱＋实心小菱。</summary>
    public static void NotchedFrame(CanvasItem ci, Rect2 r, Color? fill = null, bool jewels = true)
    {
        const float c = 22f;
        var x = r.Position.X;
        var y = r.Position.Y;
        var w = r.Size.X;
        var h = r.Size.Y;
        var pts = new[]
        {
            new Vector2(x + c, y), new Vector2(x + w - c, y), new Vector2(x + w, y + c), new Vector2(x + w, y + h - c),
            new Vector2(x + w - c, y + h), new Vector2(x + c, y + h), new Vector2(x, y + h - c), new Vector2(x, y + c),
        };
        ci.DrawColoredPolygon(pts, fill ?? InkStyle.Panel);
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0);
        loop[^1] = pts[0];
        ci.DrawPolyline(loop, InkStyle.Line, PortraitLayout.LineHair, true);
        InkDraw.Ink(ci, Loop(r.Grow(-14f)), InkStyle.WoodDark, 2.5f);
        if (!jewels)
            return;
        foreach (var corner in new[] { r.Position, new Vector2(r.End.X, y), new Vector2(x, r.End.Y), r.End })
        {
            InkDraw.Jewel(ci, corner, 13f, InkStyle.Line);
            InkDraw.Jewel(ci, corner, 9f, InkStyle.Bg);
            InkDraw.Jewel(ci, corner, 4f, InkStyle.Line);
        }
    }

    /// <summary>圆形头像：贴图按正方形裁上半身后套进圆；无图则 Panel 底＋名字首字。ring 外圈骨白环。</summary>
    public static void Avatar(CanvasItem ci, Vector2 center, float radius, Texture2D? tex, string name,
        bool ring = true, bool dim = false)
    {
        const int steps = 48;
        var pts = new Vector2[steps];
        var uvs = new Vector2[steps];
        for (var i = 0; i < steps; i++)
        {
            var a = Mathf.Tau * i / steps;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            pts[i] = center + d * radius;
            uvs[i] = d;
        }
        if (tex != null)
        {
            var size = tex.GetSize();
            var side = Mathf.Min(size.X, size.Y);
            var origin = new Vector2((size.X - side) / 2f, 0f);
            for (var i = 0; i < steps; i++)
                uvs[i] = (origin + (uvs[i] * 0.5f + new Vector2(0.5f, 0.5f)) * side) / size;
            ci.DrawColoredPolygon(pts, dim ? new Color(1f, 1f, 1f, 0.45f) : Colors.White, uvs, tex);
        }
        else
        {
            ci.DrawColoredPolygon(pts, InkStyle.Panel);
            if (name.Length > 0)
                InkDraw.Text(ci, center, name[..1], (int)Mathf.Max(PortraitLayout.FontMeta, radius * 0.8f),
                    dim ? InkStyle.Dim : InkStyle.Line, "cm");
        }
        if (ring)
            ci.DrawArc(center, radius + 6f, 0f, Mathf.Tau, 56, dim ? InkStyle.WoodDark : InkStyle.Line, 4f, true);
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
