using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 竖屏的框线语汇（2026-10-07 起哥特 · 西幻风）：银白细双线、尖拱/切角外形、骨白主操作、
/// 正方形头像；不出现安卓式圆角药丸、圆形头像、抽屉把手。一切几何（矩形、命中块）与改造前一致，
/// 只换画面表现。颜色一律取自 InkStyle，位图素材一律走 GothicArt。
/// </summary>
public static class PortraitFrame
{
    private static Rect2? _press;

    /// <summary>
    /// 按下态：矩形与它一致者按选中画。触摸没有悬停，按下当场不给反馈就只剩"点没点中"的疑问。
    /// 每个画面在 _Draw 开头调一次（没有按下就传 null）；后画的画面会覆盖前一个的值。
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

    /// <summary>全屏底：哥特石壁暗纹底图。</summary>
    public static void Backdrop(CanvasItem ci) => GothicArt.Backdrop(ci);

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
            GothicArt.Corners(ci, r, flourish * 1.4f);
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
            GothicArt.Corners(ci, r, flourish * 1.4f);
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
        GothicArt.TopCorners(ci, r, k);
    }

    /// <summary>银白分隔线：中间亮、两端渐隐，够宽时正中缀一枚金菱。</summary>
    public static void FadingRule(CanvasItem ci, float left, float right, float y)
    {
        if (right <= left)
            return;
        var mid = (left + right) / 2f;
        GradLine(ci, left, mid, y, 3f, new Color(InkStyle.Line, 0f), new Color(InkStyle.Line, 0.85f));
        GradLine(ci, mid, right, y, 3f, new Color(InkStyle.Line, 0.85f), new Color(InkStyle.Line, 0f));
        if (right - left > 400f)
        {
            InkDraw.Jewel(ci, new Vector2(mid, y), 9f, InkStyle.Line);
            InkDraw.Jewel(ci, new Vector2(mid, y), 4f, InkStyle.Bg);
        }
    }

    /// <summary>横向渐变细线（左右两色），用细四边形画，免得 DrawLine 只能单色。</summary>
    public static void GradLine(CanvasItem ci, float x1, float x2, float y, float width, Color a, Color b)
    {
        if (x2 <= x1)
            return;
        var h = width / 2f;
        ci.DrawPolygon(new[] { new Vector2(x1, y - h), new Vector2(x2, y - h), new Vector2(x2, y + h), new Vector2(x1, y + h) },
            new[] { a, b, b, a });
    }

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
        InkDraw.Ink(ci, Loop(r), enabled ? InkStyle.Line : InkStyle.WoodDark,
            pressed ? PortraitLayout.LineBold + 2f : PortraitLayout.LineBold);
        InkDraw.Ink(ci, Loop(r.Grow(-12f)), active ? InkStyle.Line : InkStyle.Dim, PortraitLayout.LineHair);

        if (beads)
        {
            var cy = r.GetCenter().Y;
            InkDraw.Jewel(ci, new Vector2(r.Position.X + 30f, cy), PortraitLayout.OrnamentJewel,
                enabled ? InkStyle.Line : InkStyle.WoodDark);
            InkDraw.Jewel(ci, new Vector2(r.End.X - 30f, cy), PortraitLayout.OrnamentJewel,
                enabled ? InkStyle.Line : InkStyle.WoodDark);
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
        InkDraw.Ink(ci, Loop(r), highlighted ? InkStyle.Line : InkStyle.WoodDark,
            highlighted ? PortraitLayout.LineBold : PortraitLayout.LineHair);
        InkDraw.Ink(ci, Loop(r.Grow(-12f)), highlighted ? InkStyle.Bg : new Color(InkStyle.Dim, 0.8f),
            PortraitLayout.LineHair - 1f);
        if (highlighted)
            GothicArt.Corners(ci, r, 56f);
    }

    // ======================================================================
    // 2026-10-07 哥特 · 西幻语汇（同名同签名、同几何，只换画面表现）：
    // 尖拱牌钮（原药丸钮）、分段控件、标签签、切角卡片、银白抽屉（无把手）、
    // 分节线「─◆ 标题 ◆─」、金菱刻度、尖头银白条、缺角双线框＋黑芯角珠、正方形头像。
    // ======================================================================

    private static bool IsLineColor(Color f) =>
        Mathf.IsEqualApprox(f.R, InkStyle.Line.R) && Mathf.IsEqualApprox(f.G, InkStyle.Line.G)
        && Mathf.IsEqualApprox(f.B, InkStyle.Line.B);

    /// <summary>
    /// 哥特外形的顶点环（顺时针、首尾不重复），外接矩形恒为 r：
    /// radius ≥ 短边一半＝尖拱牌（两端收尖的六边形，取代安卓药丸）；
    /// 小于则切角八边形；radius ≤ 4 则直角矩形（细条、滑轨）。
    /// arcSteps 仅为保持旧签名，不再使用。
    /// </summary>
    public static Vector2[] RoundRectPoints(Rect2 r, float radius, int arcSteps = 8)
    {
        var x = r.Position.X;
        var y = r.Position.Y;
        var w = r.Size.X;
        var h = r.Size.Y;
        var shortSide = Mathf.Min(w, h);
        var rad = Mathf.Min(radius, shortSide / 2f);
        if (rad <= 4f)
            return new[] { r.Position, new Vector2(r.End.X, y), r.End, new Vector2(x, r.End.Y) };
        if (rad >= shortSide / 2f - 0.5f)
        {
            if (w >= h)
            {
                var d = Mathf.Min(h * 0.32f, w * 0.25f);
                return new[]
                {
                    new Vector2(x + d, y), new Vector2(x + w - d, y), new Vector2(x + w, y + h / 2f),
                    new Vector2(x + w - d, y + h), new Vector2(x + d, y + h), new Vector2(x, y + h / 2f),
                };
            }
            var dv = Mathf.Min(w * 0.32f, h * 0.25f);
            return new[]
            {
                new Vector2(x + w / 2f, y), new Vector2(x + w, y + dv), new Vector2(x + w, y + h - dv),
                new Vector2(x + w / 2f, y + h), new Vector2(x, y + h - dv), new Vector2(x, y + dv),
            };
        }
        var c = Mathf.Min(rad * 0.6f, shortSide * 0.3f);
        return new[]
        {
            new Vector2(x + c, y), new Vector2(x + w - c, y), new Vector2(x + w, y + c), new Vector2(x + w, y + h - c),
            new Vector2(x + w - c, y + h), new Vector2(x + c, y + h), new Vector2(x, y + h - c), new Vector2(x, y + c),
        };
    }

    private static void Outline(CanvasItem ci, Vector2[] pts, Color color, float width)
    {
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0);
        loop[^1] = pts[0];
        ci.DrawPolyline(loop, color, width, true);
    }

    /// <summary>纵向渐变填充（top → bottom），按各顶点的 y 插值。</summary>
    private static void GradientFill(CanvasItem ci, Vector2[] pts, Color top, Color bottom)
    {
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        foreach (var p in pts)
        {
            minY = Mathf.Min(minY, p.Y);
            maxY = Mathf.Max(maxY, p.Y);
        }
        var span = Mathf.Max(1f, maxY - minY);
        var colors = new Color[pts.Length];
        for (var i = 0; i < pts.Length; i++)
            colors[i] = top.Lerp(bottom, (pts[i].Y - minY) / span);
        ci.DrawPolygon(pts, colors);
    }

    /// <summary>银白填充：亮金到古金的竖向渐变（原「骨白实心」的位置，字仍用深色）。</summary>
    private static void Gilded(CanvasItem ci, Vector2[] pts, float alpha = 1f) =>
        GradientFill(ci, pts, new Color(InkStyle.WoodLight, alpha), new Color(InkStyle.Wood, alpha));

    /// <summary>骨白填充：亮绯到骨白的竖向渐变（主操作与选中）。</summary>
    private static void Ruby(CanvasItem ci, Vector2[] pts, float alpha = 1f) =>
        GradientFill(ci, pts, new Color(InkStyle.WoodLight, alpha), new Color(InkStyle.Wood, alpha));

    /// <summary>
    /// 哥特外形块：fill 为空不填，line 为空不描。fill 传骨白（Line 色）的旧调用一律画成银白渐变，
    /// 这样所有「骨白实心＋黑字」的选中块自动换成「银白＋深字」，字依旧读得清。
    /// </summary>
    public static void RoundRect(CanvasItem ci, Rect2 r, float radius, Color? fill, Color? line = null,
        float width = PortraitLayout.LineHair)
    {
        var pts = RoundRectPoints(r, radius);
        if (fill is { } f)
        {
            if (IsLineColor(f) && f.A >= 0.5f)
                Gilded(ci, pts, f.A);
            else
                ci.DrawColoredPolygon(pts, f);
        }
        if (line is { } l)
            Outline(ci, pts, IsLineColor(l) ? InkStyle.Line : l, width);
    }

    /// <summary>
    /// 牌钮（原药丸钮）：primary＝骨白渐变＋银白双线＋两尖各一枚金菱（一屏只给一个主操作）；
    /// 否则深石底＋暗金描边。不可用时描边降为暗铜、字降为褐灰。glyph 非空时图标在字左。sub 是第二行小字。
    /// </summary>
    public static void Pill(CanvasItem ci, Rect2 r, string label, bool primary = false, bool enabled = true,
        Action<CanvasItem, float, float, float, Color>? glyph = null, string sub = "", int size = PortraitLayout.FontBody)
    {
        var pressed = Pressed(r);
        var pts = RoundRectPoints(r, r.Size.Y / 2f);
        Color text;
        if (primary && enabled)
        {
            if (pressed)
                ci.DrawColoredPolygon(pts, new Color(InkStyle.Line, 0.78f));
            else
                Ruby(ci, pts);
            Outline(ci, pts, InkStyle.Line, PortraitLayout.LineHair - 1f);
            Outline(ci, RoundRectPoints(r.Grow(-9f), (r.Size.Y - 18f) / 2f), new Color(InkStyle.Bg, 0.45f), 2f);
            var cy0 = r.GetCenter().Y;
            InkDraw.Jewel(ci, new Vector2(r.Position.X + 4f, cy0), 9f, InkStyle.Line);
            InkDraw.Jewel(ci, new Vector2(r.End.X - 4f, cy0), 9f, InkStyle.Line);
            text = InkStyle.Bg;
        }
        else
        {
            ci.DrawColoredPolygon(pts, pressed ? InkStyle.Hover : new Color(InkStyle.Panel, 0.94f));
            if (pressed)
                ci.DrawColoredPolygon(pts, PressFill);
            Outline(ci, pts, enabled ? new Color(InkStyle.Line, 0.8f) : InkStyle.WoodDark,
                enabled ? PortraitLayout.LineHair - 1f : PortraitLayout.LineHair - 2f);
            if (enabled)
                Outline(ci, RoundRectPoints(r.Grow(-9f), (r.Size.Y - 18f) / 2f), new Color(InkStyle.Dim, 0.6f), 2f);
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
                PortraitLayout.FontMeta, PortraitLayout.FontMeta,
                primary && enabled ? InkStyle.WoodDark : InkStyle.Dim, "cm");
    }

    /// <summary>分段控件第 i 段的矩形（整段可点，命中块与画面同源）。</summary>
    public static Rect2 SegmentRect(Rect2 r, int count, int i)
    {
        var w = r.Size.X / count;
        return new Rect2(r.Position.X + i * w, r.Position.Y, w, r.Size.Y);
    }

    /// <summary>分段控件：尖拱深槽＋暗金描边，选中段骨白牌＋银白边，段间一道暗金细竖线。</summary>
    public static void Segmented(CanvasItem ci, Rect2 r, IReadOnlyList<string> labels, int selected)
    {
        var trough = RoundRectPoints(r, r.Size.Y / 2f);
        ci.DrawColoredPolygon(trough, new Color(InkStyle.Inset, 0.9f));
        Outline(ci, trough, new Color(InkStyle.Line, 0.7f), PortraitLayout.LineHair - 2f);
        for (var i = 0; i < labels.Count; i++)
        {
            var seg = SegmentRect(r, labels.Count, i);
            var inner = seg.Grow(-10f);
            var on = i == selected;
            if (i > 0 && i != selected && i - 1 != selected)
                ci.DrawLine(new Vector2(seg.Position.X, seg.Position.Y + 30f), new Vector2(seg.Position.X, seg.End.Y - 30f),
                    new Color(InkStyle.WoodDark, 0.8f), 2f);
            var pts = RoundRectPoints(inner, inner.Size.Y / 2f);
            if (on)
            {
                Ruby(ci, pts);
                Outline(ci, pts, InkStyle.Line, 3f);
            }
            else if (Pressed(seg))
                ci.DrawColoredPolygon(pts, PressFill);
            InkDraw.TextBounded(ci, inner.Grow(-12f), labels[i], PortraitLayout.FontBody, PortraitLayout.FontMeta,
                on ? InkStyle.Bg : InkStyle.Dim, "cm");
        }
    }

    /// <summary>标签签宽度：字宽＋左右各 36。</summary>
    public static float ChipWidth(string label, int size = PortraitLayout.FontMeta) =>
        InkDraw.Measure(label, size).X + 72f;

    /// <summary>
    /// 标签签：命中块是整格 r（≥118 高），画出来的尖拱小牌在格内垂直居中、高 visual。
    /// 选中＝骨白牌＋银白边＋象牙字；未选＝暗铜描边褐灰字。
    /// </summary>
    public static void Chip(CanvasItem ci, Rect2 r, string label, bool selected, float visual = 96f)
    {
        var plate = new Rect2(r.Position.X, r.GetCenter().Y - visual / 2f, r.Size.X, visual);
        var pts = RoundRectPoints(plate, visual / 2f);
        if (selected)
        {
            Ruby(ci, pts);
            Outline(ci, pts, InkStyle.Line, 3f);
        }
        else
        {
            ci.DrawColoredPolygon(pts, Pressed(r) ? PressFill : new Color(InkStyle.Panel, 0.85f));
            Outline(ci, pts, InkStyle.WoodDark, PortraitLayout.LineHair - 2f);
        }
        InkDraw.TextBounded(ci, plate.Grow(-20f), label, PortraitLayout.FontMeta, PortraitLayout.FontMeta,
            selected ? InkStyle.Bg : InkStyle.Dim, "cm");
    }

    /// <summary>只读小签（特质等）：暗铜描边，不可点；lit＝银白牌深字。</summary>
    public static float Tag(CanvasItem ci, Vector2 at, string label, float height = 72f, bool lit = false)
    {
        var w = ChipWidth(label) - 16f;
        var r = new Rect2(at, new Vector2(w, height));
        var pts = RoundRectPoints(r, height / 2f);
        if (lit)
            Gilded(ci, pts);
        else
        {
            ci.DrawColoredPolygon(pts, new Color(InkStyle.Panel, 0.85f));
            Outline(ci, pts, InkStyle.WoodDark, PortraitLayout.LineHair - 2f);
        }
        InkDraw.Text(ci, r.GetCenter(), label, PortraitLayout.FontMeta, lit ? InkStyle.Bg : InkStyle.Dim, "cm");
        return w;
    }

    /// <summary>
    /// 卡片：切角深石底，未选暗铜描边＋四角金色短护角；选中/按下时暗酒红浅填＋银白描边＋银白角花。
    /// </summary>
    public static void Card(CanvasItem ci, Rect2 r, bool selected = false, float radius = 24f)
    {
        var pressed = Pressed(r);
        var active = selected || pressed;
        var pts = RoundRectPoints(r, radius);
        ci.DrawColoredPolygon(pts, active ? InkStyle.Hover : new Color(InkStyle.Panel, 0.92f));
        if (pressed)
            ci.DrawColoredPolygon(pts, PressFill);
        Outline(ci, RoundRectPoints(r.Grow(-8f), Mathf.Max(0f, radius - 8f)), new Color(InkStyle.Dim, 0.35f), 2f);
        Outline(ci, pts, active ? InkStyle.Line : InkStyle.WoodDark,
            active ? PortraitLayout.LineHair - 1f : PortraitLayout.LineHair - 2f);
        if (active)
        {
            GothicArt.Corners(ci, r, Mathf.Min(54f, Mathf.Min(r.Size.X, r.Size.Y) * 0.3f));
            return;
        }
        // 未选：四角各一对金色短护角（沿两边各 26px），读作包角铜饰。
        var c = Mathf.Min(radius * 0.6f, Mathf.Min(r.Size.X, r.Size.Y) * 0.3f);
        const float len = 26f;
        var gold = new Color(InkStyle.Line, 0.75f);
        var x0 = r.Position.X;
        var y0 = r.Position.Y;
        var x1 = r.End.X;
        var y1 = r.End.Y;
        ci.DrawLine(new Vector2(x0 + c, y0), new Vector2(x0 + c + len, y0), gold, 3f);
        ci.DrawLine(new Vector2(x0, y0 + c), new Vector2(x0, y0 + c + len), gold, 3f);
        ci.DrawLine(new Vector2(x1 - c, y0), new Vector2(x1 - c - len, y0), gold, 3f);
        ci.DrawLine(new Vector2(x1, y0 + c), new Vector2(x1, y0 + c + len), gold, 3f);
        ci.DrawLine(new Vector2(x0 + c, y1), new Vector2(x0 + c + len, y1), gold, 3f);
        ci.DrawLine(new Vector2(x0, y1 - c), new Vector2(x0, y1 - c - len), gold, 3f);
        ci.DrawLine(new Vector2(x1 - c, y1), new Vector2(x1 - c - len, y1), gold, 3f);
        ci.DrawLine(new Vector2(x1, y1 - c), new Vector2(x1, y1 - c - len), gold, 3f);
    }

    /// <summary>
    /// 底部抽屉：先把上方整幅压暗（下层画面仍可见、但已不可点——命中块由调用方移除），
    /// 再铺一块平顶的暗纹石板：顶沿银白双线、两上角银白角花、正中银白徽饰（取代安卓把手）。返回面板矩形。
    /// </summary>
    public static Rect2 Sheet(CanvasItem ci, float top)
    {
        ci.DrawRect(new Rect2(0, 0, PortraitLayout.CanvasWidth, top), new Color(InkStyle.Bg, 0.72f));
        var r = new Rect2(0, top, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - top);
        ci.DrawRect(r, InkStyle.Panel);
        GothicArt.Tile(ci, r, 0.85f);
        ci.DrawRect(new Rect2(0, top, PortraitLayout.CanvasWidth, 4f), InkStyle.Line);
        ci.DrawLine(new Vector2(0, top + 13f), new Vector2(PortraitLayout.CanvasWidth, top + 13f), InkStyle.Dim, 2f);
        GothicArt.TopCorners(ci, r, 112f, 0.95f);
        GothicArt.Crest(ci, new Vector2(PortraitLayout.CanvasWidth / 2f, top + 34f), 340f);
        return r;
    }

    /// <summary>
    /// 固定底座（战斗行动面板、建造面板）：与抽屉同一套暗纹石板＋顶沿银白双线＋两上角银白角花，
    /// 不压暗上层、不画徽饰（底座顶沿两端常有文字）。r 为底座矩形（可越出画布下沿）。
    /// </summary>
    public static void Dock(CanvasItem ci, Rect2 r, bool closed = false)
    {
        ci.DrawRect(r, InkStyle.Panel);
        GothicArt.Tile(ci, r, 0.85f);
        ci.DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 4f), InkStyle.Line);
        ci.DrawLine(new Vector2(r.Position.X, r.Position.Y + 13f), new Vector2(r.End.X, r.Position.Y + 13f), InkStyle.Dim, 2f);
        if (!closed)
        {
            GothicArt.TopCorners(ci, r, 96f, 0.95f);
            return;
        }
        // 夹在画面中段的面板：底沿同样双线收口，四角角花。
        ci.DrawRect(new Rect2(r.Position.X, r.End.Y - 4f, r.Size.X, 4f), InkStyle.Line);
        ci.DrawLine(new Vector2(r.Position.X, r.End.Y - 13f), new Vector2(r.End.X, r.End.Y - 13f), InkStyle.Dim, 2f);
        GothicArt.Corners(ci, r, 96f, 0.95f);
    }

    /// <summary>分节线「──◆ 标题 ◆──」：两侧银白渐隐线，标题两旁各一枚金菱嵌黑芯。label 空则只画渐隐线。</summary>
    public static void SectionRule(CanvasItem ci, float x1, float x2, float y, string label = "")
    {
        if (label.Length == 0)
        {
            FadingRule(ci, x1, x2, y);
            return;
        }
        var w = InkDraw.Measure(label, PortraitLayout.FontMeta).X;
        var cx = (x1 + x2) / 2f;
        InkDraw.Text(ci, new Vector2(cx, y), label, PortraitLayout.FontMeta, InkStyle.Line, "cm");
        foreach (var jx in new[] { cx - w / 2f - 34f, cx + w / 2f + 34f })
        {
            InkDraw.Jewel(ci, new Vector2(jx, y), 11f, InkStyle.Line);
            InkDraw.Jewel(ci, new Vector2(jx, y), 5f, InkStyle.Bg);
        }
        GradLine(ci, x1, cx - w / 2f - 56f, y, 3f, new Color(InkStyle.Line, 0f), new Color(InkStyle.Line, 0.8f));
        GradLine(ci, cx + w / 2f + 56f, x2, y, 3f, new Color(InkStyle.Line, 0.8f), new Color(InkStyle.Line, 0f));
    }

    /// <summary>菱形刻度：n 枚，前 k 枚实心银白，其余暗铜小菱。half 表示第 k+1 枚画成半亮。</summary>
    public static void Ticks(CanvasItem ci, float x, float y, int n, float k, float size = 30f, float gap = 20f)
    {
        for (var i = 0; i < n; i++)
        {
            var cx = x + i * (size + gap) + size / 2f;
            var c = new Vector2(cx, y);
            if (i + 1 <= k)
            {
                InkDraw.Jewel(ci, c, size / 2f, InkStyle.Line);
                InkDraw.Jewel(ci, c, size / 5f, InkStyle.Bg);
            }
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

    /// <summary>尖头银白条：深槽暗铜描边＋银白渐变填充（填充至少一个尖头宽），顶缘一道高光。</summary>
    public static void Bar(CanvasItem ci, Rect2 r, float frac)
    {
        var trough = RoundRectPoints(r, r.Size.Y / 2f);
        ci.DrawColoredPolygon(trough, new Color(InkStyle.Inset, 0.9f));
        Outline(ci, trough, InkStyle.WoodDark, 3f);
        var f = Mathf.Clamp(frac, 0f, 1f);
        if (f <= 0f)
            return;
        var w = Mathf.Max(r.Size.Y, r.Size.X * f);
        var fill = new Rect2(r.Position, new Vector2(w, r.Size.Y));
        Gilded(ci, RoundRectPoints(fill, r.Size.Y / 2f));
        if (r.Size.Y >= 14f)
        {
            var d = Mathf.Min(r.Size.Y * 0.32f, w * 0.25f);
            ci.DrawLine(new Vector2(fill.Position.X + d, fill.Position.Y + 3f), new Vector2(fill.End.X - d, fill.Position.Y + 3f),
                new Color(InkStyle.Line, 0.45f), 2f);
        }
    }

    /// <summary>缺角双线框：外框八边形银白主线，内收 14px 深金细线，四角嵌金菱＋黑芯。</summary>
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
        Outline(ci, pts, InkStyle.Line, PortraitLayout.LineHair);
        InkDraw.Ink(ci, Loop(r.Grow(-14f)), InkStyle.Dim, 2.5f);
        if (!jewels)
            return;
        foreach (var corner in new[] { r.Position, new Vector2(r.End.X, y), new Vector2(x, r.End.Y), r.End })
        {
            InkDraw.Jewel(ci, corner, 14f, InkStyle.Line);
            InkDraw.Jewel(ci, corner, 9f, InkStyle.Bg);
            InkDraw.Jewel(ci, corner, 6f, InkStyle.Bg);
        }
    }

    /// <summary>
    /// 正方形头像（1:1，2026-10-07 主人定：头像始终正方形）：外接正方形边长 2×radius、以 center 为中心，
    /// 贴图按正方形裁上半身；无图则石底＋名字首字。ring＝外圈银白框＋四角金菱（与原外环同样外扩 6px）。
    /// </summary>
    public static void Avatar(CanvasItem ci, Vector2 center, float radius, Texture2D? tex, string name,
        bool ring = true, bool dim = false)
    {
        var box = new Rect2(center - new Vector2(radius, radius), new Vector2(radius * 2f, radius * 2f));
        if (tex != null)
        {
            var size = tex.GetSize();
            var side = Mathf.Min(size.X, size.Y);
            var origin = new Vector2((size.X - side) / 2f, 0f);
            ci.DrawRect(box, InkStyle.Panel);
            ci.DrawTextureRectRegion(tex, box, new Rect2(origin, new Vector2(side, side)),
                dim ? new Color(1f, 1f, 1f, 0.45f) : Colors.White);
        }
        else
        {
            ci.DrawRect(box, InkStyle.Panel);
            if (name.Length > 0)
                InkDraw.Text(ci, center, name[..1], (int)Mathf.Max(PortraitLayout.FontMeta, radius * 0.8f),
                    dim ? InkStyle.Dim : InkStyle.Line, "cm");
        }
        if (!ring)
        {
            ci.DrawRect(box, new Color(InkStyle.WoodDark, 0.9f), false, 2f);
            return;
        }
        var frame = dim ? InkStyle.WoodDark : InkStyle.Line;
        ci.DrawRect(box.Grow(2f), InkStyle.Bg, false, 3f);
        ci.DrawRect(box.Grow(5f), frame, false, 4f);
        if (radius >= 30f)
            foreach (var corner in new[] { box.Grow(5f).Position, new Vector2(box.End.X + 5f, box.Position.Y - 5f),
                         new Vector2(box.Position.X - 5f, box.End.Y + 5f), box.Grow(5f).End })
            {
                InkDraw.Jewel(ci, corner, 7f, frame);
                if (!dim)
                    InkDraw.Jewel(ci, corner, 3f, InkStyle.Bg);
            }
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


    // ======================================================================
    // 合并自 ui-motion-territory：哥特饰件与几何工具（铭牌、尖拱、四叶、石碑等）
    // ======================================================================

    private static Vector2[] Loop(Vector2[] pts)
    {
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0);
        loop[^1] = pts[0];
        return loop;
    }
    private static float _pressStrength = 1f;

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

    /// <summary>碑板上方压暗的满档透明度。</summary>
    public const float ScrimAlpha = 0.70f;

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
}
