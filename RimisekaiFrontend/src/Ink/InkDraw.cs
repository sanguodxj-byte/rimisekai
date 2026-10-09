using System.Linq;
using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Hub;

namespace Rimisekai.Ink;

/// <summary>
/// 线稿绘制原语。所有界面元素都由这些基本笔画组成：
/// 抖动折线（手绘感）、涡卷、叶片、珠饰、人形、文本。
/// </summary>
public static class InkDraw
{
    // ---------- 笔画 ----------

    /// <summary>
    /// 抖动折线：按约 8px 重采样，沿法线做确定性偏移。
    /// 端点收幅到 0，保证转折处接得干净，不会出现豁口。
    /// </summary>
    public static void Ink(CanvasItem ci, IReadOnlyList<Vector2> pts, Color color,
        float width = 1f, float amp = 1f, int seed = 1, bool closed = false)
    {
        if (pts.Count < 2)
            return;

        var dense = Resample(pts, 8f, closed);
        if (dense.Count < 2)
            return;

        // 主人指示：彻底废除扭曲与手绘抖动，所有线条拉直，画干净挺拔的线条。
        var ptsArray = pts.ToArray();
        if (closed && ptsArray.Length > 0 && ptsArray[0] != ptsArray[^1])
        {
            var closedPts = new Vector2[ptsArray.Length + 1];
            Array.Copy(ptsArray, closedPts, ptsArray.Length);
            closedPts[^1] = ptsArray[0];
            ci.DrawPolyline(closedPts, color, width, true);
        }
        else
        {
            ci.DrawPolyline(ptsArray, color, width, true);
        }
    }

    private static float Taper(int i, int last)
    {
        if (last <= 0)
            return 1f;
        const int edge = 3;
        var d = Math.Min(i, last - i);
        return d >= edge ? 1f : d / (float)edge;
    }

    // 🚨 手绘抖动笔画（曾用名 `Sketch`）已于 2026-10-01 被主人**全局禁止**并删除，
    // 禁止以任何名义再引入。它曾被短暂恢复过一版（按 8px 重采样、沿法线做确定性偏移、
    // 端点收幅），主人看到成品后明确否掉：「不要抖动笔画，全局禁止这玩意」。
    // 结论：**所有线条一律拉直**。要手绘感靠**纹样本身的疏密与转折**，
    // 不靠把线画歪——把线画歪只会让界面显脏。

    /// <summary>
    /// 直线笔画。干净直线，不做手绘抖动（扭曲折线已废除，主人 2026-09-30 定）；
    /// width 之外的 amp/seed 参数位保留，调用点无需改动，不再生效。
    /// </summary>
    public static void InkLine(CanvasItem ci, Vector2 a, Vector2 b, Color color,
        float width = 1f, float amp = 1f, int seed = 1) =>
        ci.DrawLine(a, b, color, width, antialiased: true);

    private static List<Vector2> Resample(IReadOnlyList<Vector2> pts, float step, bool closed)
    {
        var result = new List<Vector2>();
        var count = pts.Count;
        var segs = closed ? count : count - 1;
        for (var s = 0; s < segs; s++)
        {
            var a = pts[s];
            var b = pts[(s + 1) % count];
            var len = a.DistanceTo(b);
            var n = Math.Max(1, (int)(len / step));
            for (var i = 0; i < n; i++)
                result.Add(a.Lerp(b, i / (float)n));
        }
        if (!closed)
            result.Add(pts[count - 1]);
        return result;
    }

    /// <summary>两段正弦叠加的确定性噪声。</summary>
    private static float Noise(float t, int seed)
    {
        var rng = new InkRng(seed);
        var v = 0.0;
        var amp = 1.0;
        for (var k = 0; k < 2; k++)
        {
            v += amp * Math.Sin(t * Math.Pow(1.9, k) + rng.Range(0f, Mathf.Tau));
            amp *= 0.5;
        }
        return (float)(v / 1.4);
    }

    // ---------- 几何构件 ----------

    /// <summary>二次贝塞尔弧：p0 到 p2，p1 为控制点。用于下摆这类单弧线。</summary>
    public static Vector2[] QuadArc(Vector2 p0, Vector2 p1, Vector2 p2, int steps)
    {
        var pts = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1f - t;
            pts[i] = u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }
        return pts;
    }

    /// <summary>三次贝塞尔：p0 到 p3，p1/p2 为控制点。卷草与叶片用它。</summary>
    public static Vector2[] CubicArc(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int steps)
    {
        var pts = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1f - t;
            pts[i] = u * u * u * p0
                     + 3f * u * u * t * p1
                     + 3f * u * t * t * p2
                     + t * t * t * p3;
        }
        return pts;
    }

    /// <summary>渐隐线的收尖方向。</summary>
    public enum FadeTaper
    {
        /// <summary>两端渐隐、正中最亮（分割线、标题下饰）。</summary>
        Both,

        /// <summary>左端渐隐、右端最亮（贴在右侧文字/菱珠左边的半截线）。</summary>
        Left,

        /// <summary>右端渐隐、左端最亮（贴在左侧文字/菱珠右边的半截线）。</summary>
        Right,
    }

    /// <summary>
    /// 全项目唯一的渐隐线画法（分割细线、标题下饰、分节线两翼、日序线共用）。
    /// 一条平滑连续的纺锤形细线：沿长度切成 48 段，每段一块逐顶点上色的四边形，
    /// 亮度与粗细都在最亮处达峰（正中，或 taper 指定的那一端），向收尖端平滑收成透明的细尖。
    /// width＝最粗处线宽；color 取调色板色，透明度由画法自己乘；lozenge＝最亮处嵌一枚小实心菱。
    /// </summary>
    public static void FadeRule(CanvasItem ci, float left, float right, float y, float width, Color color,
        FadeTaper taper = FadeTaper.Both, bool lozenge = false)
    {
        if (right - left < 2f)
            return;
        const int segs = 48;
        var points = new Vector2[4];
        var colors = new Color[4];
        float Weight(float t)
        {
            // d：离最亮处的归一化距离（0＝最亮，1＝收尖端）。
            var d = taper switch
            {
                FadeTaper.Left => 1f - t,
                FadeTaper.Right => t,
                _ => Mathf.Abs(t * 2f - 1f),
            };
            var k = 1f - d;
            return k * k * (3f - 2f * k);
        }
        for (var i = 0; i < segs; i++)
        {
            var t0 = (float)i / segs;
            var t1 = (float)(i + 1) / segs;
            var w0 = Weight(t0);
            var w1 = Weight(t1);
            var x0 = Mathf.Lerp(left, right, t0);
            var x1 = Mathf.Lerp(left, right, t1);
            var h0 = Mathf.Max(0.35f, width * (0.3f + 0.7f * w0)) / 2f;
            var h1 = Mathf.Max(0.35f, width * (0.3f + 0.7f * w1)) / 2f;
            points[0] = new Vector2(x0, y - h0);
            points[1] = new Vector2(x1, y - h1);
            points[2] = new Vector2(x1, y + h1);
            points[3] = new Vector2(x0, y + h0);
            var c0 = new Color(color, color.A * w0);
            var c1 = new Color(color, color.A * w1);
            colors[0] = c0;
            colors[1] = c1;
            colors[2] = c1;
            colors[3] = c0;
            ci.DrawPolygon(points, colors);
        }
        if (lozenge)
        {
            var at = taper switch
            {
                FadeTaper.Left => right,
                FadeTaper.Right => left,
                _ => (left + right) / 2f,
            };
            Jewel(ci, new Vector2(at, y), Mathf.Max(3f, width * 1.6f), color);
        }
    }

    /// <summary>菱形珠饰。全项目严禁空心，一律实心填充。</summary>
    public static void Jewel(CanvasItem ci, Vector2 c, float r, Color color, bool filled = true)
    {
        var pts = new[]
        {
            new Vector2(c.X, c.Y - r),
            new Vector2(c.X + r, c.Y),
            new Vector2(c.X, c.Y + r),
            new Vector2(c.X - r, c.Y),
        };
        ci.DrawColoredPolygon(pts, color);
    }

    /// <summary>
    /// 角花：沿两条边各画一段 C 卷，末端向内收成小涡，角上嵌一颗珠。
    /// dx/dy 指向框内。装饰贴着边走，不斜着穿过标题。
    ///
    /// width / jewel 供竖屏用毫米档笔画：默认值即横屏原值（1.2px 线、2.4px 珠），
    /// 传默认值时输出与从前逐像素一致。竖屏 1.2px 只有 0.077mm，会画成灰影，必须放大。
    /// </summary>
    public static void CornerFlourish(CanvasItem ci, Vector2 corner, float dx, float dy,
        Color color, float size = 36f, int seed = 1, float width = 1.2f, float jewel = 2.4f)
    {
        var k = Mathf.Max(20f, size);
        var origin = corner + new Vector2(dx, dy) * Mathf.Max(7f, width * 2.4f);
        var inward = new Vector2(dx, dy);

        Scroll(ci, origin, new Vector2(dx * k, 0), new Vector2(0, dy), color, seed, width);
        Scroll(ci, origin, new Vector2(0, dy * k), new Vector2(dx, 0), color, seed + 3, width);

        var inner = k * 0.48f;
        Scroll(ci, origin + inward * (k * 0.16f), new Vector2(dx * inner, 0), new Vector2(0, dy),
            new Color(color, 0.75f), seed + 6, width * 0.875f);
        Scroll(ci, origin + inward * (k * 0.16f), new Vector2(0, dy * inner), new Vector2(dx, 0),
            new Color(color, 0.75f), seed + 9, width * 0.875f);

        Jewel(ci, origin, jewel, color);
        Jewel(ci, origin + new Vector2(dx * (k * 0.22f), dy * (k * 0.22f)), jewel * 0.625f, color, filled: false);
    }

    /// <summary>
    /// 一段贴边的卷轴：直线略弯，末端向内卷一下。
    /// width 默认 1.2f 即横屏原值；卷曲段的笔画按 width 的 0.875 收细。
    /// </summary>
    public static void Scroll(CanvasItem ci, Vector2 from, Vector2 along, Vector2 inward,
        Color color, int seed, float width = 1.2f)
    {
        var len = along.Length();
        if (len < 8f)
            return;
        var dir = along / len;
        var n = inward.LengthSquared() < 1e-6f ? dir.Orthogonal() : inward.Normalized();
        var end = from + along;
        var curl = width * 0.875f;
        Ink(ci, CubicArc(from,
            from + dir * (len * 0.28f) + n * (width * 2.1f),
            from + dir * (len * 0.72f) + n * (width * 1.0f),
            end, 8), color, width, 0.14f, seed);

        Ink(ci, CubicArc(end,
            end + dir * (width * 3.4f) + n * (width * 5.0f),
            end - dir * (width * 1.7f) + n * (width * 8.4f),
            end - dir * (width * 5.9f) + n * (width * 3.4f), 6), color, curl, 0.12f, seed + 1);
    }

    // 🚨 2026-10-01 主人否掉全部「厚框条」方案，整支已删除，禁止以任何名义回退：
    //   `BandGrain`（框条走向细纹）、`Rosette` / `RoseFrieze`（玫瑰花结/花带）、
    //   `BorderBand`（厚白浮雕框 + 铜版墨框两案）、`WhitePlateFrame` 开关。
    // 主人原话：「都不通过，回到最初的边框形态」。
    // **边框一律回到最初的细双线框＋四角角花**（见 `InkFrame.Panel` / `InkFrame.Zone`）。
    // 走过的弯路（别再走）：卷草缠枝实心浮雕 → 骨脊框 → 骨板框 → 玫瑰花带雕花框，
    // 全部被否。教训：框条一旦加厚，它就变成画面的主角，把内容压死。

    /// <summary>
    /// 四角交叉排线：v1 参考图里"细排线贴着四角、向黑场渐隐"的手绘底纹。
    /// 沿两条对角线方向排细线，越靠外越淡，偶发断线保留手绘感；
    /// 只做角部气氛，不抢角花。排线画在角花之前，让卷草压在纹理上。
    ///
    /// width / spacing 供竖屏用毫米档：默认 1px 线、7px 间隔即横屏原值。
    /// 竖屏传 3px 线、15px 间隔——1px 排线在手机上糊成一层灰雾，读不出是排线。
    /// </summary>
    public static void CornerEtching(CanvasItem ci, Rect2 r, Color? color = null,
        float alpha = 0.09f, float zone = 240f, int seed = 9030,
        float width = 1f, float spacing = 7f)
    {
        var c = color ?? new Color(InkStyle.Line, alpha);
        var corners = new[]
        {
            r.Position,
            new Vector2(r.End.X, r.Position.Y),
            new Vector2(r.Position.X, r.End.Y),
            r.End,
        };

        for (var k = 0; k < corners.Length; k++)
        {
            var corner = corners[k];
            var sx = k == 1 || k == 3 ? -1f : 1f;
            var sy = k == 2 || k == 3 ? -1f : 1f;
            var zr = new Rect2(
                new Vector2(sx > 0 ? corner.X : corner.X - zone,
                            sy > 0 ? corner.Y : corner.Y - zone),
                new Vector2(zone, zone));

            EtchFan(ci, corner, zr, sx, sy, c, seed + k * 17, width, spacing);
        }
    }

    /// <summary>一个角上的双向扇形排线：以角为源，四个斜向铺细线。</summary>
    private static void EtchFan(CanvasItem ci, Vector2 corner, Rect2 zone,
        float sx, float sy, Color c, int seed, float width, float spacing)
    {
        var rng = new InkRng(seed);
        const float step = 4f;

        foreach (var (ux, uy) in new[] { (sx, sy), (sx, -sy) })
        {
            var len = Mathf.Sqrt(ux * ux + uy * uy);
            var dir = new Vector2(ux / len, uy / len);
            var perp = dir.Orthogonal();

            var maxD = zone.Size.X * 1.42f;
            for (var d = 0f; d < maxD; d += spacing)
            {
                // 陡衰减：只有贴角一小片看得见，远处几乎溶进黑底。
                var fade = Mathf.Pow(1f - d / maxD, 1.6f);
                if (fade < 0.04f)
                    break;
                if (rng.Range(0f, 1f) < 0.3f)
                    continue;

                var line = new List<Vector2>();
                for (var t = -zone.Size.X * 1.42f; t < zone.Size.X * 1.42f; t += step)
                {
                    var p = corner + perp * d + dir * t;
                    if (zone.HasPoint(p))
                        line.Add(p);
                    else if (line.Count > 1)
                    {
                        ci.DrawPolyline(line.ToArray(), new Color(c, c.A * fade), width);
                        line.Clear();
                    }
                    else
                        line.Clear();
                }
                if (line.Count > 1)
                    ci.DrawPolyline(line.ToArray(), new Color(c, c.A * fade), width);
            }
        }
    }

    /// <summary>
    /// 漂白木框：矩形四周铺一条有厚度的木条，带纵向/横向木纹。
    /// 厚度默认 18，适合交易栏那种粗木框。
    /// </summary>
    public static void WoodBand(CanvasItem ci, Rect2 r, float thickness = 18f, int seed = 1)
    {
        var t = Mathf.Clamp(thickness, 8f, 28f);
        DrawWoodPlank(ci, new Rect2(r.Position.X, r.Position.Y, r.Size.X, t), horizontal: true, seed);
        DrawWoodPlank(ci, new Rect2(r.Position.X, r.End.Y - t, r.Size.X, t), horizontal: true, seed + 3);
        DrawWoodPlank(ci, new Rect2(r.Position.X, r.Position.Y + t, t, r.Size.Y - t * 2f), horizontal: false, seed + 6);
        DrawWoodPlank(ci, new Rect2(r.End.X - t, r.Position.Y + t, t, r.Size.Y - t * 2f), horizontal: false, seed + 9);

        var edge = InkStyle.WoodDark;
        Ink(ci, new[]
        {
            r.Position,
            new Vector2(r.End.X, r.Position.Y),
            r.End,
            new Vector2(r.Position.X, r.End.Y),
            r.Position,
        }, edge, 1.4f, 0.35f, seed + 20);
        var inner = r.Grow(-t);
        Ink(ci, new[]
        {
            inner.Position,
            new Vector2(inner.End.X, inner.Position.Y),
            inner.End,
            new Vector2(inner.Position.X, inner.End.Y),
            inner.Position,
        }, InkStyle.WoodLight, 1.1f, 0.3f, seed + 21);
    }

    private static void DrawWoodPlank(CanvasItem ci, Rect2 r, bool horizontal, int seed)
    {
        if (r.Size.X < 2f || r.Size.Y < 2f)
            return;

        ci.DrawRect(r, InkStyle.Wood);
        var rng = new InkRng(seed);
        var grain = new Color(InkStyle.WoodDark, 0.72f);
        var light = new Color(InkStyle.WoodLight, 0.85f);

        if (horizontal)
        {
            var rows = Mathf.Max(3, (int)(r.Size.Y / 3.2f));
            for (var i = 1; i < rows; i++)
            {
                var y = r.Position.Y + r.Size.Y * (i / (float)rows) + rng.Range(-0.5f, 0.5f);
                var a = new Vector2(r.Position.X + 1f, y);
                var b = new Vector2(r.End.X - 1f, y + rng.Range(-1.2f, 1.2f));
                ci.DrawLine(a, b, i % 2 == 0 ? grain : light, 1.1f);
            }
        }
        else
        {
            var cols = Mathf.Max(3, (int)(r.Size.X / 3.2f));
            for (var i = 1; i < cols; i++)
            {
                var x = r.Position.X + r.Size.X * (i / (float)cols) + rng.Range(-0.5f, 0.5f);
                var a = new Vector2(x, r.Position.Y + 1f);
                var b = new Vector2(x + rng.Range(-1.2f, 1.2f), r.End.Y - 1f);
                ci.DrawLine(a, b, i % 2 == 0 ? grain : light, 1.1f);
            }
        }
    }

    /// <summary>
    /// 折角内框：参考稿里购买栏那种切掉一角的矩形。
    /// 默认切左上，fold 是切角边长。
    /// </summary>
    public static void FoldedBox(CanvasItem ci, Rect2 r, Color color, float fold = 22f,
        bool fill = true, int seed = 1)
    {
        var k = Mathf.Clamp(fold, 10f, Mathf.Min(r.Size.X, r.Size.Y) * 0.35f);
        var pts = new[]
        {
            new Vector2(r.Position.X + k, r.Position.Y),
            new Vector2(r.End.X, r.Position.Y),
            r.End,
            new Vector2(r.Position.X, r.End.Y),
            new Vector2(r.Position.X, r.Position.Y + k),
            new Vector2(r.Position.X + k, r.Position.Y),
        };
        if (fill)
            ci.DrawColoredPolygon(new[]
            {
                new Vector2(r.Position.X + k, r.Position.Y),
                new Vector2(r.End.X, r.Position.Y),
                r.End,
                new Vector2(r.Position.X, r.End.Y),
                new Vector2(r.Position.X, r.Position.Y + k),
            }, InkStyle.Inset);
        Ink(ci, pts, color, 1.4f, 0.35f, seed);
        InkLine(ci, new Vector2(r.Position.X, r.Position.Y + k),
            new Vector2(r.Position.X + k, r.Position.Y), color, 1.2f, 0.25f, seed + 1);
    }

    /// <summary>虚线，用于“未开拓”格。</summary>
    public static void Dashed(CanvasItem ci, Vector2 a, Vector2 b, Color color,
        float width = 1f, float dash = 10f, float gap = 8f)
    {
        var len = a.DistanceTo(b);
        if (len < 1e-4f)
            return;
        var d = (b - a) / len;
        var s = 0f;
        while (s < len)
        {
            var e = Math.Min(s + dash, len);
            ci.DrawLine(a + d * s, a + d * e, color, width, true);
            s = e + gap;
        }
    }

    /// <summary>
    /// 多边形：先实填底色（压住后方网格线），再沿边描一圈干净墨线。
    /// 三角瓦片、任意多边构件都用它；轮廓走 Ink，线条风格与全界面一致。
    /// </summary>
    public static void Polygon(CanvasItem ci, IReadOnlyList<Vector2> pts, Color color,
        Color? fill = null, float width = 1.3f, float amp = 0.3f, int seed = 1)
    {
        if (pts.Count < 3)
            return;

        var loop = new Vector2[pts.Count];
        for (var i = 0; i < pts.Count; i++)
            loop[i] = pts[i];

        if (fill.HasValue)
            ci.DrawColoredPolygon(loop, fill.Value);

        Ink(ci, loop, color, width, amp, seed, closed: true);
    }

    /// <summary>
    /// 弧线：以 center 为心、radius 为半径，从 a0 到 a1 的墨线圆弧。
    /// 星盘的外圈与环界用它；steps 越大越圆滑。
    /// </summary>
    public static void Arc(CanvasItem ci, Vector2 center, float radius, float a0, float a1,
        Color color, float width = 1f, int steps = 48, int seed = 1)
    {
        if (steps < 2)
            return;
        var pts = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var a = Mathf.Lerp(a0, a1, i / (float)steps);
            pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        Ink(ci, pts, color, width, 0.25f, seed);
    }

    // ---------- 边框裁剪与受限绘制 ----------

    /// <summary>
    /// Cohen-Sutherland 线段裁剪算法：将直线段 (p0, p1) 裁剪至矩形 r 内部。
    /// 若线段完全在矩形外部则返回 false。
    /// </summary>
    public static bool ClipSegment(Vector2 p0, Vector2 p1, Rect2 r, out Vector2 c0, out Vector2 c1)
    {
        c0 = p0;
        c1 = p1;
        var x0 = p0.X; var y0 = p0.Y;
        var x1 = p1.X; var y1 = p1.Y;
        var xmin = r.Position.X; var ymin = r.Position.Y;
        var xmax = r.End.X; var ymax = r.End.Y;

        const int Inside = 0; const int Left = 1; const int Right = 2; const int Bottom = 4; const int Top = 8;
        int Code(float x, float y)
        {
            var c = Inside;
            if (x < xmin) c |= Left;
            else if (x > xmax) c |= Right;
            if (y < ymin) c |= Top;
            else if (y > ymax) c |= Bottom;
            return c;
        }

        var code0 = Code(x0, y0);
        var code1 = Code(x1, y1);

        while (true)
        {
            if ((code0 | code1) == 0)
            {
                c0 = new Vector2(x0, y0);
                c1 = new Vector2(x1, y1);
                return true;
            }
            if ((code0 & code1) != 0)
                return false;

            var codeOut = code0 != 0 ? code0 : code1;
            float x, y;

            if ((codeOut & Bottom) != 0)
            {
                x = x0 + (x1 - x0) * (ymax - y0) / (y1 - y0);
                y = ymax;
            }
            else if ((codeOut & Top) != 0)
            {
                x = x0 + (x1 - x0) * (ymin - y0) / (y1 - y0);
                y = ymin;
            }
            else if ((codeOut & Right) != 0)
            {
                y = y0 + (y1 - y0) * (xmax - x0) / (x1 - x0);
                x = xmax;
            }
            else
            {
                y = y0 + (y1 - y0) * (xmin - x0) / (x1 - x0);
                x = xmin;
            }

            if (codeOut == code0)
            {
                x0 = x; y0 = y;
                code0 = Code(x0, y0);
            }
            else
            {
                x1 = x; y1 = y;
                code1 = Code(x1, y1);
            }
        }
    }

    /// <summary>
    /// Sutherland-Hodgman 多边形裁剪算法：将凸多边形/凹多边形 poly 裁剪至矩形 r 内部。
    /// 边框外的部分一律被边框裁剪截除。
    /// </summary>
    public static Vector2[] ClipPolygon(IReadOnlyList<Vector2> poly, Rect2 r)
    {
        if (poly.Count < 3)
            return Array.Empty<Vector2>();

        var xmin = r.Position.X; var ymin = r.Position.Y;
        var xmax = r.End.X; var ymax = r.End.Y;

        var output = new List<Vector2>(poly);

        output = ClipEdge(output, p => p.X >= xmin, (p1, p2) => new Vector2(xmin, p1.Y + (p2.Y - p1.Y) * (xmin - p1.X) / (p2.X - p1.X)));
        if (output.Count < 3) return Array.Empty<Vector2>();

        output = ClipEdge(output, p => p.X <= xmax, (p1, p2) => new Vector2(xmax, p1.Y + (p2.Y - p1.Y) * (xmax - p1.X) / (p2.X - p1.X)));
        if (output.Count < 3) return Array.Empty<Vector2>();

        output = ClipEdge(output, p => p.Y >= ymin, (p1, p2) => new Vector2(p1.X + (p2.X - p1.X) * (ymin - p1.Y) / (p2.Y - p1.Y), ymin));
        if (output.Count < 3) return Array.Empty<Vector2>();

        output = ClipEdge(output, p => p.Y <= ymax, (p1, p2) => new Vector2(p1.X + (p2.X - p1.X) * (ymax - p1.Y) / (p2.Y - p1.Y), ymax));
        if (output.Count < 3) return Array.Empty<Vector2>();

        return output.ToArray();

        static List<Vector2> ClipEdge(List<Vector2> points, Func<Vector2, bool> inside, Func<Vector2, Vector2, Vector2> intersect)
        {
            var result = new List<Vector2>();
            if (points.Count == 0) return result;
            var prev = points[^1];
            for (var i = 0; i < points.Count; i++)
            {
                var curr = points[i];
                var inCurr = inside(curr);
                var inPrev = inside(prev);
                if (inCurr)
                {
                    if (!inPrev)
                        result.Add(intersect(prev, curr));
                    result.Add(curr);
                }
                else if (inPrev)
                {
                    result.Add(intersect(prev, curr));
                }
                prev = curr;
            }
            return result;
        }
    }

    /// <summary>裁剪墨线：只画矩形 r 内部的线段部分。</summary>
    public static void ClippedInkLine(CanvasItem ci, Vector2 p0, Vector2 p1, Rect2 r,
        Color color, float width = 1f, float amp = 0.15f, int seed = 1)
    {
        if (ClipSegment(p0, p1, r, out var c0, out var c1))
            InkLine(ci, c0, c1, color, width, amp, seed);
    }

    /// <summary>裁剪圆弧：圆弧上超出矩形 r 的部分被精确截断裁剪，不出框。</summary>
    public static void ClippedArc(CanvasItem ci, Vector2 center, float radius, float a0, float a1,
        Rect2 r, Color color, float width = 1f, int steps = 192, int seed = 1)
    {
        if (steps < 2)
            return;
        for (var i = 0; i < steps; i++)
        {
            var angle0 = a0 + (a1 - a0) * i / steps;
            var angle1 = a0 + (a1 - a0) * (i + 1) / steps;
            var pt0 = center + new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0)) * radius;
            var pt1 = center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius;
            if (ClipSegment(pt0, pt1, r, out var c0, out var c1))
                InkLine(ci, c0, c1, color, width, 0.15f, seed + i);
        }
    }

    /// <summary>裁剪多边形：实体填充与墨线轮廓超出矩形 r 的部分被精确截断裁剪，不出框。</summary>
    public static void ClippedPolygon(CanvasItem ci, IReadOnlyList<Vector2> poly, Rect2 r,
        Color color, Color? fill = null, float width = 1.2f, float amp = 0.2f, int seed = 1)
    {
        var clipped = ClipPolygon(poly, r);
        if (clipped.Length >= 3)
            Polygon(ci, clipped, color, fill, width, amp, seed);
    }

    /// <summary>裁剪菱珠：仅当珠心在矩形内部时绘制。</summary>
    public static void ClippedJewel(CanvasItem ci, Vector2 center, float radius, Rect2 r, Color color)
    {
        if (r.HasPoint(center))
            Jewel(ci, center, radius, color);
    }

    // ---------- 角色标识 ----------

    /// <summary>国际象棋棋子类型。</summary>
    public enum ChessPiece
    {
        King,   // ♔ 国王（玩家）
        Queen,  // ♕ 王后（好感 > 600）
        Rook,   // ♖ 城堡/车（好感 100-600 随机）
        Bishop, // ♗ 主教/象（好感 100-600 随机）
        Knight, // ♘ 骑士/马（好感 100-600 随机）
        Pawn,   // ♙ 士兵（好感 < 100，或未占用的空槽上限）
    }

    /// <summary>
    /// 程序化绘制古典国际象棋实心单色棋子。
    /// 在场角色为实心纯白（InkStyle.Line）；空位上限为暗色实心小兵（InkStyle.Dim，永不画空心）。
    /// </summary>
    public static void Chess(CanvasItem ci, Vector2 basePt, float h, ChessPiece piece, bool isLimitCap = false)
    {
        var cx = basePt.X;
        var cy = basePt.Y;
        var bw = h * 0.50f;     // 底座总宽
        var bh = h * 0.11f;     // 底座总高
        var color = isLimitCap ? InkStyle.Dim : InkStyle.Line;
        var waistY = cy - bh;

        // 1. 通用双阶基座：底层台座 + 次层凸环阶梯
        var baseBottom = new[]
        {
            new Vector2(cx - bw * 0.50f, cy),
            new Vector2(cx - bw * 0.46f, cy - bh * 0.52f),
            new Vector2(cx + bw * 0.46f, cy - bh * 0.52f),
            new Vector2(cx + bw * 0.50f, cy),
        };
        var baseTop = new[]
        {
            new Vector2(cx - bw * 0.40f, cy - bh * 0.52f),
            new Vector2(cx - bw * 0.34f, waistY),
            new Vector2(cx + bw * 0.34f, waistY),
            new Vector2(cx + bw * 0.40f, cy - bh * 0.52f),
        };
        ci.DrawColoredPolygon(baseBottom, color);
        ci.DrawColoredPolygon(baseTop, color);

        // 空位上限标识强制画暗色实心小兵
        if (isLimitCap)
            piece = ChessPiece.Pawn;

        switch (piece)
        {
            case ChessPiece.Pawn:
            {
                // 兵：显著改矮（约王高70%）、大头圆球（高辨识度矮实心造型）
                var neckY = cy - h * 0.36f;
                var collarY = cy - h * 0.40f;
                var bodyPoly = new[]
                {
                    new Vector2(cx - bw * 0.32f, waistY),
                    new Vector2(cx - bw * 0.16f, neckY),
                    new Vector2(cx - bw * 0.23f, neckY),
                    new Vector2(cx - bw * 0.23f, collarY),
                    new Vector2(cx + bw * 0.23f, collarY),
                    new Vector2(cx + bw * 0.23f, neckY),
                    new Vector2(cx + bw * 0.16f, neckY),
                    new Vector2(cx + bw * 0.32f, waistY),
                };
                var ballR = h * 0.17f;
                var ballCenter = new Vector2(cx, collarY - ballR + h * 0.02f);

                ci.DrawColoredPolygon(bodyPoly, color);
                ci.DrawCircle(ballCenter, ballR, color);
                ci.DrawCircle(new Vector2(cx, ballCenter.Y - ballR), h < 40f ? 1.5f : 3.0f, color);
                break;
            }

            case ChessPiece.Knight:
            {
                // 骑士/马：标准 Staunton 极度典雅威武的战马昂首实心雕塑
                // 优美 S 型轮廓：后颈背向右饱满隆起拱弧、警惕双耳、额头鼻梁挺拔、鼻吻圆润下探、喉部深陷内收、前胸威武隆起
                var pts = new[]
                {
                    new Vector2(cx + bw * 0.32f, waistY),
                    new Vector2(cx + bw * 0.38f, cy - h * 0.25f),   // 后颈饱满向外拱弧下段
                    new Vector2(cx + bw * 0.42f, cy - h * 0.45f),   // 后颈拱弧顶点（饱满的马鬃颈肌！）
                    new Vector2(cx + bw * 0.36f, cy - h * 0.65f),   // 颈上段向头内收
                    new Vector2(cx + bw * 0.25f, cy - h * 0.80f),   // 耳后枕骨
                    new Vector2(cx + bw * 0.18f, cy - h * 0.94f),   // 后耳尖
                    new Vector2(cx + bw * 0.11f, cy - h * 0.84f),   // 双耳间隙
                    new Vector2(cx + bw * 0.08f, cy - h * 0.92f),   // 前耳尖
                    new Vector2(cx + bw * 0.02f, cy - h * 0.80f),   // 前耳根
                    new Vector2(cx - bw * 0.10f, cy - h * 0.74f),   // 额头骨线
                    new Vector2(cx - bw * 0.28f, cy - h * 0.64f),   // 鼻梁斜直挺拔
                    new Vector2(cx - bw * 0.48f, cy - h * 0.55f),   // 鼻端轮廓
                    new Vector2(cx - bw * 0.52f, cy - h * 0.48f),   // 鼻吻前端
                    new Vector2(cx - bw * 0.45f, cy - h * 0.43f),   // 上唇
                    new Vector2(cx - bw * 0.32f, cy - h * 0.43f),   // 下巴下颌
                    new Vector2(cx - bw * 0.18f, cy - h * 0.41f),   // 下颚转折
                    new Vector2(cx - bw * 0.06f, cy - h * 0.36f),   // 咽喉深邃内收拐角（优雅喉线！）
                    new Vector2(cx - bw * 0.18f, cy - h * 0.25f),   // 强健隆起的前胸中段
                    new Vector2(cx - bw * 0.26f, cy - h * 0.14f),   // 下前胸
                    new Vector2(cx - bw * 0.30f, waistY),           // 前胸入座根部
                };
                ci.DrawColoredPolygon(pts, color);
                break;
            }

            case ChessPiece.Bishop:
            {
                // 主教/象：纯净实心尖拱主教法冠（完整无缺口，左右对称优雅造型）
                var collarY = cy - h * 0.54f;
                var bodyPoly = new[]
                {
                    new Vector2(cx - bw * 0.30f, waistY),
                    new Vector2(cx - bw * 0.14f, collarY + h * 0.04f),
                    new Vector2(cx + bw * 0.14f, collarY + h * 0.04f),
                    new Vector2(cx + bw * 0.30f, waistY),
                };
                var collarPoly = new[]
                {
                    new Vector2(cx - bw * 0.22f, collarY + h * 0.04f),
                    new Vector2(cx - bw * 0.22f, collarY),
                    new Vector2(cx + bw * 0.22f, collarY),
                    new Vector2(cx + bw * 0.22f, collarY + h * 0.04f),
                };
                var mitreTopY = cy - h * 0.88f;
                var mitreMidY = collarY - h * 0.16f;

                // 尖拱法冠：完整流畅的椭圆尖拱，左右完全对称，无任何凹口缺痕
                var mitrePoly = new[]
                {
                    new Vector2(cx - bw * 0.18f, collarY),
                    new Vector2(cx - bw * 0.23f, mitreMidY),
                    new Vector2(cx - bw * 0.16f, mitreTopY + h * 0.06f),
                    new Vector2(cx, mitreTopY),
                    new Vector2(cx + bw * 0.16f, mitreTopY + h * 0.06f),
                    new Vector2(cx + bw * 0.23f, mitreMidY),
                    new Vector2(cx + bw * 0.18f, collarY),
                };

                ci.DrawColoredPolygon(bodyPoly, color);
                ci.DrawColoredPolygon(collarPoly, color);
                ci.DrawColoredPolygon(mitrePoly, color);

                // 冠顶实心圆珠
                ci.DrawCircle(new Vector2(cx, mitreTopY - 2.4f), 2.4f, color);
                break;
            }

            case ChessPiece.Rook:
            {
                // 车/城堡：实心石砌塔身 + 挑檐横台 + 凸凹四齿城垛
                var roofY = cy - h * 0.72f;
                var tower = new[]
                {
                    new Vector2(cx - bw * 0.30f, waistY),
                    new Vector2(cx - bw * 0.25f, roofY),
                    new Vector2(cx + bw * 0.25f, roofY),
                    new Vector2(cx + bw * 0.30f, waistY),
                };
                var crenelTopY = cy - h * 0.88f;
                var cw = bw * 0.62f;

                ci.DrawColoredPolygon(tower, color);
                ci.DrawRect(new Rect2(cx - bw * 0.32f, roofY, bw * 0.64f, -h * 0.04f), color);
                ci.DrawRect(new Rect2(cx - cw * 0.50f, crenelTopY, cw * 0.24f, roofY - crenelTopY), color);
                ci.DrawRect(new Rect2(cx - cw * 0.12f, crenelTopY, cw * 0.24f, roofY - crenelTopY), color);
                ci.DrawRect(new Rect2(cx + cw * 0.26f, crenelTopY, cw * 0.24f, roofY - crenelTopY), color);
                break;
            }

            case ChessPiece.Queen:
            {
                // 王后：实心收腰身躯 + 盛放冠冕 + 齿顶宝珠
                // 小尺寸（房间与设施标识）采用 3 齿宽展冠，保证每个冠齿与宝珠在低像素下绝对分明
                var neckY = cy - h * 0.56f;
                var bodyPoly = new[]
                {
                    new Vector2(cx - bw * 0.32f, waistY),
                    new Vector2(cx - bw * 0.16f, neckY),
                    new Vector2(cx + bw * 0.16f, neckY),
                    new Vector2(cx + bw * 0.32f, waistY),
                };
                var crownBaseY = neckY - h * 0.03f;
                var cTopY = cy - h * 0.88f;
                var cMidY = cy - h * 0.78f;

                ci.DrawColoredPolygon(bodyPoly, color);

                if (h < 30f)
                {
                    // 房间与设施标识尺寸（h <= 28px）：3 齿宽展冠冕，轮廓极度鲜明
                    var crownPts = new[]
                    {
                        new Vector2(cx - bw * 0.22f, crownBaseY),
                        new Vector2(cx - bw * 0.34f, cTopY + h * 0.02f), // 左外展齿
                        new Vector2(cx - bw * 0.14f, cMidY),            // 左凹
                        new Vector2(cx, cTopY),                         // 中正齿
                        new Vector2(cx + bw * 0.14f, cMidY),            // 右凹
                        new Vector2(cx + bw * 0.34f, cTopY + h * 0.02f), // 右外展齿
                        new Vector2(cx + bw * 0.22f, crownBaseY),
                    };
                    ci.DrawColoredPolygon(new[] { crownPts[0], crownPts[1], crownPts[2] }, color);
                    ci.DrawColoredPolygon(new[] { crownPts[0], crownPts[2], crownPts[3], crownPts[4] }, color);
                    ci.DrawColoredPolygon(new[] { crownPts[0], crownPts[4], crownPts[5], crownPts[6] }, color);

                    ci.DrawCircle(crownPts[1], 1.8f, color);
                    ci.DrawCircle(crownPts[3], 2.2f, color);
                    ci.DrawCircle(crownPts[5], 1.8f, color);
                }
                else
                {
                    // 大号展示：五齿盛放冠冕
                    var crownPts = new[]
                    {
                        new Vector2(cx - bw * 0.22f, crownBaseY),
                        new Vector2(cx - bw * 0.32f, cTopY + h * 0.03f),
                        new Vector2(cx - bw * 0.16f, cy - h * 0.81f),
                        new Vector2(cx - bw * 0.08f, cTopY),
                        new Vector2(cx, cy - h * 0.79f),
                        new Vector2(cx + bw * 0.08f, cTopY),
                        new Vector2(cx + bw * 0.16f, cy - h * 0.81f),
                        new Vector2(cx + bw * 0.32f, cTopY + h * 0.03f),
                        new Vector2(cx + bw * 0.22f, crownBaseY),
                    };
                    ci.DrawColoredPolygon(new[] { crownPts[0], crownPts[1], crownPts[2] }, color);
                    ci.DrawColoredPolygon(new[] { crownPts[0], crownPts[2], crownPts[3], crownPts[4] }, color);
                    ci.DrawColoredPolygon(new[] { crownPts[0], crownPts[4], crownPts[5], crownPts[6] }, color);
                    ci.DrawColoredPolygon(new[] { crownPts[0], crownPts[6], crownPts[7], crownPts[8] }, color);

                    ci.DrawCircle(crownPts[1], 2.0f, color);
                    ci.DrawCircle(crownPts[3], 2.2f, color);
                    ci.DrawCircle(crownPts[5], 2.2f, color);
                    ci.DrawCircle(crownPts[7], 2.0f, color);
                    ci.DrawCircle(new Vector2(cx, cTopY - 2.8f), 2.8f, color);
                }
                break;
            }

            case ChessPiece.King:
            {
                // 国王：实心威严身躯 + 拱顶大王冠 + 顶立实心拉丁十字架（无任何内部杂线！）
                var neckY = cy - h * 0.58f;
                var bodyPoly = new[]
                {
                    new Vector2(cx - bw * 0.34f, waistY),
                    new Vector2(cx - bw * 0.17f, neckY),
                    new Vector2(cx + bw * 0.17f, neckY),
                    new Vector2(cx + bw * 0.34f, waistY),
                };
                var domeTopY = cy - h * 0.82f;
                var domeMidY = cy - h * 0.72f;
                var dome = new[]
                {
                    new Vector2(cx - bw * 0.25f, neckY),
                    new Vector2(cx - bw * 0.29f, domeMidY),
                    new Vector2(cx - bw * 0.15f, domeTopY),
                    new Vector2(cx, domeTopY - h * 0.02f),
                    new Vector2(cx + bw * 0.15f, domeTopY),
                    new Vector2(cx + bw * 0.29f, domeMidY),
                    new Vector2(cx + bw * 0.25f, neckY),
                };

                var crossBottom = domeTopY - h * 0.02f;
                var crossTop = cy - h * 1.04f;
                var crossArmY = crossBottom - (crossBottom - crossTop) * 0.60f;
                var armHalf = Mathf.Max(3.0f, h * 0.15f);
                var barW = Mathf.Max(1.8f, h * 0.05f);

                ci.DrawColoredPolygon(bodyPoly, color);
                ci.DrawColoredPolygon(dome, color);

                // 顶端清晰端庄的实心十字架
                ci.DrawLine(new Vector2(cx, crossBottom), new Vector2(cx, crossTop), color, barW);
                ci.DrawLine(new Vector2(cx - armHalf, crossArmY), new Vector2(cx + armHalf, crossArmY), color, barW);
                break;
            }
        }
    }

    /// <summary>兼容重载：全部强制以实心单色输出。</summary>
    public static void Chess(CanvasItem ci, Vector2 basePt, float h, ChessPiece piece, Color color, float alphaFill = 0f)
    {
        var isCap = color == InkStyle.Dim || alphaFill == 0f && piece == ChessPiece.Pawn && color != InkStyle.Line;
        Chess(ci, basePt, h, piece, isCap);
    }

    /// <summary>根据角色卡属性映射其代表的国际象棋棋子类型。</summary>
    public static ChessPiece PieceFor(CharacterCard card)
    {
        if (card.IsPlayer)
            return ChessPiece.King;
        if (card.Favor > 600)
            return ChessPiece.Queen;
        if (card.Favor >= 100)
        {
            // 100-600 的随机一个类型：按角色 Id 确定性哈希，同一角色类型稳定不跳变
            return (card.Id % 3) switch
            {
                0 => ChessPiece.Rook,
                1 => ChessPiece.Bishop,
                _ => ChessPiece.Knight,
            };
        }
        return ChessPiece.Pawn;
    }

    /// <summary>“此处”条目右侧的槽位小人，实心剪影。</summary>
    public static void MiniFigure(CanvasItem ci, Vector2 basePt, float h, Color color)
    {
        var headR = h * 0.17f;
        ci.DrawCircle(new Vector2(basePt.X, basePt.Y - h * 0.80f), headR, color);
        var body = new[]
        {
            new Vector2(basePt.X - h * 0.30f, basePt.Y),
            new Vector2(basePt.X - h * 0.26f, basePt.Y - h * 0.50f),
            new Vector2(basePt.X + h * 0.26f, basePt.Y - h * 0.50f),
            new Vector2(basePt.X + h * 0.30f, basePt.Y),
        };
        ci.DrawColoredPolygon(body, color);
    }

    // ---------- 文本 ----------

    /// <summary>
    /// 文本。anchor 沿用 PIL 语义：首字母管水平（l/c/r），次字母管垂直（t/m/b）。
    /// 以基线定位，保证中英混排时底部对齐一致。
    /// </summary>
    public static void Text(CanvasItem ci, Vector2 at, string text, int size, Color color,
        string anchor = "lt")
    {
        size = Mathf.Max(InkStyle.MinFontSize, size);
        var font = InkStyle.Font;
        var ascent = font.GetAscent(size);
        var descent = font.GetDescent(size);
        var w = Measure(text, size).X;

        var x = anchor[0] switch
        {
            'c' => at.X - w / 2f,
            'r' => at.X - w,
            _ => at.X,
        };
        var baseline = anchor[1] switch
        {
            'm' => at.Y + (ascent - descent) / 2f,
            'b' => at.Y - descent,
            _ => at.Y + ascent,
        };

        if (text.Contains('\u2009'))
        {
            var parts = text.Split('\u2009');
            var curX = x;
            var gap = Mathf.Max(2f, 4f * (size / 26f));
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    ci.DrawString(font, new Vector2(curX, baseline), parts[i],
                        HorizontalAlignment.Left, -1, size, color);
                    curX += font.GetStringSize(parts[i], HorizontalAlignment.Left, -1, size).X;
                }
                if (i < parts.Length - 1)
                    curX += gap;
            }
            return;
        }

        ci.DrawString(font, new Vector2(x, baseline), text,
            HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>带黑色描边的文字（数字角标用）：先画描边再画字，叠在图标上也完整清晰。</summary>
    public static void TextOutlined(CanvasItem ci, Vector2 at, string text, int size, Color color, int outline,
        string anchor = "lt", Color? outlineColor = null)
    {
        size = Mathf.Max(InkStyle.MinFontSize, size);
        var font = InkStyle.Font;
        var ascent = font.GetAscent(size);
        var descent = font.GetDescent(size);
        var w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var x = anchor[0] switch { 'c' => at.X - w / 2f, 'r' => at.X - w, _ => at.X };
        var baseline = anchor[1] switch
        {
            'm' => at.Y + (ascent - descent) / 2f,
            'b' => at.Y - descent,
            _ => at.Y + ascent,
        };
        var pos = new Vector2(x, baseline);
        ci.DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, size, outline, outlineColor ?? InkStyle.Bg);
        ci.DrawString(font, pos, text, HorizontalAlignment.Left, -1, size, color);
    }

    public static Vector2 Measure(string text, int size)
    {
        var font = InkStyle.Font;
        if (!text.Contains('\u2009'))
            return font.GetStringSize(text, HorizontalAlignment.Left, -1, size);

        var parts = text.Split('\u2009');
        var totalW = 0f;
        var gap = Mathf.Max(2f, 4f * (size / 26f));
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                totalW += gap;
            totalW += font.GetStringSize(parts[i], HorizontalAlignment.Left, -1, size).X;
        }
        var h = font.GetStringSize(text.Replace('\u2009', ' '), HorizontalAlignment.Left, -1, size).Y;
        return new Vector2(totalW, h);
    }

    /// <summary>
    /// 自适应字号：在 [min, max] 之间自动缩字号，直到宽度放得下。
    /// 约定（2026-10-02 主人定）：最小字号下限为 26。
    /// 缩到 min 仍放不下就返回 min（不再继续缩，由调用方决定截断或换行）。
    /// </summary>
    public static int FitSize(string text, float maxWidth, int max, int min)
    {
        max = Mathf.Max(InkStyle.MinFontSize, max);
        min = Mathf.Max(InkStyle.MinFontSize, min);
        if (text.Length == 0 || maxWidth <= 0f)
            return max;

        var size = max;
        while (size > min && Measure(text, size).X > maxWidth)
            size--;
        return size;
    }

    /// <summary>
    /// 自适应字号并绘制，返回实际使用的字号。
    /// 单行放不下时按最小字号绘制，由调用方另行处理溢出。
    /// </summary>
    public static int TextFitted(CanvasItem ci, Vector2 at, string text,
        float maxWidth, int max, int min, Color color, string anchor = "lt")
    {
        max = Mathf.Max(InkStyle.MinFontSize, max);
        min = Mathf.Max(InkStyle.MinFontSize, min);
        var size = FitSize(text, maxWidth, max, min);
        Text(ci, at, text, size, color, anchor);
        return size;
    }

    /// <summary>单行文本先缩字号，再省略尾部；绘制范围不超出给定矩形。</summary>
    public static int TextBounded(CanvasItem ci, Rect2 r, string text,
        int max = 26, int min = 26, Color? color = null, string anchor = "lm")
    {
        max = Mathf.Max(InkStyle.MinFontSize, max);
        min = Mathf.Max(InkStyle.MinFontSize, min);
        if (r.Size.X <= 0f || r.Size.Y <= 0f || string.IsNullOrEmpty(text))
            return max;
        var size = FitSize(text, r.Size.X, max, min);
        var shown = Ellipsize(text.Replace('\n', ' '), r.Size.X, size);
        var at = new Vector2(
            anchor[0] == 'c' ? r.GetCenter().X : anchor[0] == 'r' ? r.End.X : r.Position.X,
            anchor[1] == 'm' ? r.GetCenter().Y : anchor[1] == 'b' ? r.End.Y : r.Position.Y);
        Text(ci, at, shown, size, color ?? InkStyle.Line, anchor);
        return size;
    }

    /// <summary>
    /// 格内短名：一行放得下就按 oneLine 居中画一行；放不下就对半拆成两行，在 twoLines 里上下居中，
    /// 字号不变（竖版下限 44 不许为塞字而缩），整名显示、不截断。
    /// </summary>
    public static void TextStacked(CanvasItem ci, Rect2 oneLine, Rect2 twoLines, string text, int size, Color color)
    {
        if (Measure(text, size).X <= oneLine.Size.X)
        {
            Text(ci, oneLine.GetCenter(), text, size, color, "cm");
            return;
        }
        var cut = (text.Length + 1) / 2;
        var step = size + 2f;
        var c = twoLines.GetCenter();
        Text(ci, c - new Vector2(0f, step / 2f), text[..cut], size, color, "cm");
        Text(ci, c + new Vector2(0f, step / 2f), text[cut..], size, color, "cm");
    }

    public static string Ellipsize(string text, float width, int size)
    {
        if (width <= 0f)
            return "";
        if (Measure(text, size).X <= width)
            return text;
        const string suffix = "…";
        while (text.Length > 0 && Measure(text + suffix, size).X > width)
            text = text[..^1];
        return Measure(suffix, size).X <= width ? text + suffix : "";
    }

    /// <summary>不得出现在行首的标点（中文避头）。</summary>
    private const string NoLineStart = "，。、；：！？）」』】》〉…—·,.;:!?)]";

    /// <summary>不得留在行尾的标点（中文避尾）。</summary>
    private const string NoLineEnd = "（「『【《〈([";

    /// <summary>换行时不拆开的词（人名）：由据点画面按名册设。</summary>
    private static string[] _unbreakable = Array.Empty<string>();

    public static void SetUnbreakable(IEnumerable<string> words) =>
        _unbreakable = words.Where(w => w.Length > 1).ToArray();

    /// <summary>
    /// 按实际字宽换行，保留换行符形成的空行。中文避头尾：要断在避头标点前时，把上一行末字一并带到下一行；
    /// 上一行以开括号收尾时，开括号也挪到下一行。人名（<see cref="SetUnbreakable"/>）不在中间断开，整名挪到下一行。
    /// </summary>
    public static IReadOnlyList<string> WrapLines(string text, float width, int size)
    {
        var lines = new List<string>();
        if (width <= 0f || string.IsNullOrEmpty(text))
            return lines;
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var start = lines.Count;
            var line = "";
            // wordStart[i]＝在第 i 个字符前断行会拆开的那个人名的起点；不拆名为 -1。
            var wordStart = new int[paragraph.Length + 1];
            Array.Fill(wordStart, -1);
            foreach (var word in _unbreakable)
                for (var at = paragraph.IndexOf(word, StringComparison.Ordinal); at >= 0; at = paragraph.IndexOf(word, at + 1, StringComparison.Ordinal))
                    for (var i = at + 1; i < at + word.Length; i++)
                        wordStart[i] = at;
            var pos = 0;
            foreach (var rune in paragraph.EnumerateRunes())
            {
                var next = line + rune;
                // 标点悬挂：行尾的收尾标点（」。，？…）多出半个字以内时挂在行外，不为它把前面几个字一起挤下去。
                // 这些标点字面本身大半是留白，挂出去看上去仍在框内。
                var over = line.Length > 0 && Measure(next, size).X > width;
                if (over && rune.Utf16SequenceLength == 1 && NoLineStart.Contains((char)rune.Value) && Measure(next, size).X <= width + size * 0.5f)
                    over = false;
                if (over)
                {
                    var carry = rune.ToString();
                    var cut = wordStart[pos];
                    if (cut > pos - line.Length)
                    {
                        carry = line[(line.Length - (pos - cut))..] + carry;
                        line = line[..(line.Length - (pos - cut))];
                    }
                    while (line.Length > 1 && (NoLineStart.Contains(carry[0]) || NoLineEnd.Contains(line[^1])))
                    {
                        carry = line[^1] + carry;
                        line = line[..^1];
                    }
                    lines.Add(line);
                    line = carry;
                }
                else
                    line = next;
                pos += rune.Utf16SequenceLength;
            }
            // 段末不留孤字：末行只剩一个字（标点不算）时，从上一行再带一个字下来（「结算伤 / 害。」→「结算 / 伤害。」）。
            if (lines.Count > start && line.TrimEnd(NoLineStart.ToCharArray()).Length == 1 && lines[^1].Length > 2
                && !NoLineStart.Contains(lines[^1][^1]) && wordStart[paragraph.Length - line.Length - 1] < 0)
            {
                line = lines[^1][^1] + line;
                lines[^1] = lines[^1][..^1];
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>在矩形内折行绘制；超长段落在最后可见行省略。</summary>
    public static int Wrapped(CanvasItem ci, Rect2 r, string text,
        int size = 20, Color? color = null, float lineHeight = 32f)
    {
        var lines = WrapLines(text, r.Size.X, size);
        var capacity = Math.Max(0, (int)((r.Size.Y - size) / lineHeight) + 1);
        var count = Math.Min(lines.Count, capacity);
        for (var i = 0; i < count; i++)
        {
            var line = lines[i];
            if (i == count - 1 && count < lines.Count)
                line = Ellipsize(line + "…", r.Size.X, size);
            Text(ci, r.Position + new Vector2(0, i * lineHeight), line, size, color ?? InkStyle.Line);
        }
        return count;
    }

    /// <summary>计量槽/量表：底槽加填充。</summary>
    /// <summary>
    /// 竖向滚动条：细轨居中一条暗线，滑块是双线小矩形、上下端各一枚菱珠（端珠合规）。
    /// ratio = 首行/(总行-可见行)（0..1）；thumbRatio = 可见/总行（0..1]。
    /// 内容不溢出（thumbRatio >= 1）时由调用方不画。
    /// </summary>
    public static void Scrollbar(CanvasItem ci, Rect2 track, float ratio, float thumbRatio)
    {
        // 轨：一条居中暗竖线。
        var cx = track.GetCenter().X;
        InkLine(ci, new Vector2(cx, track.Position.Y), new Vector2(cx, track.End.Y),
            new Color(InkStyle.Dim, 0.6f), 1.1f, 0.25f, 9500);

        var thumbH = Mathf.Clamp(track.Size.Y * Mathf.Clamp(thumbRatio, 0f, 1f), 28f, track.Size.Y);
        var max = Mathf.Max(0f, track.Size.Y - thumbH);
        var y = track.Position.Y + Mathf.Clamp(ratio, 0f, 1f) * max;
        var thumb = new Rect2(track.Position.X, y, track.Size.X, thumbH);

        // 滑块：浅填 + 双线框。
        ci.DrawRect(thumb, InkStyle.Hover);
        Ink(ci, new[]
        {
            thumb.Position,
            new Vector2(thumb.End.X, thumb.Position.Y),
            thumb.End,
            new Vector2(thumb.Position.X, thumb.End.Y),
            thumb.Position,
        }, InkStyle.Line, 1.3f, 0.3f, 9501);
        Ink(ci, new[]
        {
            thumb.Position + new Vector2(2f, 2f),
            new Vector2(thumb.End.X - 2f, thumb.Position.Y + 2f),
            thumb.End - new Vector2(2f, 2f),
            new Vector2(thumb.Position.X + 2f, thumb.End.Y - 2f),
            thumb.Position + new Vector2(2f, 2f),
        }, InkStyle.Dim, 0.9f, 0.22f, 9502);

        // 端珠：滑块上下端各一枚菱珠（只缀两端，合规）。
        Jewel(ci, new Vector2(cx, thumb.Position.Y), 2.6f, InkStyle.Line);
        Jewel(ci, new Vector2(cx, thumb.End.Y), 2.6f, InkStyle.Line);
    }

    public static void Meter(CanvasItem ci, Rect2 r, float ratio)
    {
        ci.DrawRect(r, InkStyle.Inset);
        Ink(ci, new[]
        {
            r.Position,
            new Vector2(r.End.X, r.Position.Y),
            r.End,
            new Vector2(r.Position.X, r.End.Y),
            r.Position,
        }, InkStyle.Dim, 0.8f, 0.2f, 9200);

        var fillW = Mathf.Clamp(ratio, 0f, 1f) * (r.Size.X - 2f);
        if (fillW > 0f)
            ci.DrawRect(new Rect2(r.Position.X + 1f, r.Position.Y + 1f, fillW, r.Size.Y - 2f), InkStyle.Line);
    }
}
