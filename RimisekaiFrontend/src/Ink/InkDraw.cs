using System;
using System.Collections.Generic;
using Godot;

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

        var count = dense.Count;
        var last = closed ? count : count - 1;
        var outPts = new Vector2[closed ? count : count];
        for (var i = 0; i < count; i++)
        {
            var prev = dense[(i - 1 + count) % count];
            var next = dense[(i + 1) % count];
            var tangent = (next - prev);
            if (tangent.LengthSquared() < 1e-8f)
                tangent = Vector2.Right;
            var normal = tangent.Normalized().Orthogonal();

            // 开线两端收敛，闭合线不做收敛。
            var taper = closed ? 1f : Taper(i, last);
            var off = Noise(i * 0.37f, seed) * amp * taper;
            outPts[i] = dense[i] + normal * off;
        }

        if (closed)
            outPts[count - 1] = outPts[0];

        ci.DrawPolyline(outPts, color, width, true);
    }

    private static float Taper(int i, int last)
    {
        if (last <= 0)
            return 1f;
        const int edge = 3;
        var d = Math.Min(i, last - i);
        return d >= edge ? 1f : d / (float)edge;
    }

    /// <summary>直线笔画。</summary>
    public static void InkLine(CanvasItem ci, Vector2 a, Vector2 b, Color color,
        float width = 1f, float amp = 1f, int seed = 1) =>
        Ink(ci, new[] { a, b }, color, width, amp, seed);

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

    /// <summary>菱形珠饰，中心实心点。界面里的端点装饰都用它。</summary>
    public static void Jewel(CanvasItem ci, Vector2 c, float r, Color color, bool filled = true)
    {
        var pts = new[]
        {
            new Vector2(c.X, c.Y - r),
            new Vector2(c.X + r, c.Y),
            new Vector2(c.X, c.Y + r),
            new Vector2(c.X - r, c.Y),
            new Vector2(c.X, c.Y - r),
        };
        ci.DrawPolyline(pts, color, 1f, true);
        if (filled)
            ci.DrawCircle(c, Mathf.Max(1f, r * 0.32f), color);
    }

    /// <summary>
    /// 纸纹：在矩形里铺稀疏的短横纹，模拟铜版纸的颗粒，不要密到发脏。
    /// </summary>
    public static void Paper(CanvasItem ci, Rect2 r, Color? color = null, int seed = 1)
    {
        var c = color ?? new Color(InkStyle.Line, 0.045f);
        var rng = new InkRng(seed + (int)r.Position.X * 13 + (int)r.Position.Y * 29);
        var count = Mathf.Clamp((int)(r.Size.X * r.Size.Y / 2800f), 18, 90);
        for (var i = 0; i < count; i++)
        {
            var x = r.Position.X + rng.Range(2f, r.Size.X - 2f);
            var y = r.Position.Y + rng.Range(2f, r.Size.Y - 2f);
            var w = rng.Range(8f, 22f);
            ci.DrawLine(new Vector2(x, y), new Vector2(x + w, y + rng.Range(-0.6f, 0.6f)), c, 1f);
        }
    }

    /// <summary>
    /// 角花：沿两条边各画一段 C 卷，末端向内收成小涡，角上嵌一颗珠。
    /// dx/dy 指向框内。装饰贴着边走，不斜着穿过标题。
    /// </summary>
    public static void CornerFlourish(CanvasItem ci, Vector2 corner, float dx, float dy,
        Color color, float size = 36f, int seed = 1)
    {
        var k = Mathf.Max(20f, size);
        var origin = corner + new Vector2(dx * 7f, dy * 7f);
        var inward = new Vector2(dx, dy);

        Scroll(ci, origin, new Vector2(dx * k, 0), new Vector2(0, dy), color, seed);
        Scroll(ci, origin, new Vector2(0, dy * k), new Vector2(dx, 0), color, seed + 3);

        var inner = k * 0.48f;
        Scroll(ci, origin + inward * 10f, new Vector2(dx * inner, 0), new Vector2(0, dy),
            new Color(color, 0.75f), seed + 6);
        Scroll(ci, origin + inward * 10f, new Vector2(0, dy * inner), new Vector2(dx, 0),
            new Color(color, 0.75f), seed + 9);

        Jewel(ci, origin, 2.4f, color);
        Jewel(ci, origin + new Vector2(dx * (k * 0.22f), dy * (k * 0.22f)), 1.5f, color, filled: false);
    }

    /// <summary>一段贴边的卷轴：直线略弯，末端向内卷一下。</summary>
    public static void Scroll(CanvasItem ci, Vector2 from, Vector2 along, Vector2 inward,
        Color color, int seed)
    {
        var len = along.Length();
        if (len < 8f)
            return;
        var dir = along / len;
        var n = inward.LengthSquared() < 1e-6f ? dir.Orthogonal() : inward.Normalized();
        var end = from + along;
        Ink(ci, CubicArc(from,
            from + dir * (len * 0.28f) + n * 2.5f,
            from + dir * (len * 0.72f) + n * 1.2f,
            end, 8), color, 1.2f, 0.14f, seed);

        Ink(ci, CubicArc(end,
            end + dir * 4f + n * 6f,
            end - dir * 2f + n * 10f,
            end - dir * 7f + n * 4f, 6), color, 1.05f, 0.12f, seed + 1);
    }

    /// <summary>墨线圆：闭合抖动小圆，花瓣与玫瑰结的基本件。</summary>
    public static void InkCircle(CanvasItem ci, Vector2 c, float r, Color color, int seed = 1)
    {
        const int n = 12;
        var pts = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Tau * i / n;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        Ink(ci, pts, color, 1f, Mathf.Clamp(r * 0.12f, 0.1f, 0.4f), seed, closed: true);
    }

    /// <summary>玫瑰结：六瓣小环抱着中心珠，花带方格里的填充纹样。</summary>
    public static void Rosette(CanvasItem ci, Vector2 c, float r, Color color, int seed = 1)
    {
        const int petals = 6;
        for (var i = 0; i < petals; i++)
        {
            var a = Mathf.Tau * i / petals + seed * 0.07f;
            InkCircle(ci, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.46f,
                r * 0.42f, color, seed + i);
        }
        Jewel(ci, c, Mathf.Max(1.4f, r * 0.18f), color);
    }

    /// <summary>
    /// 连续花带：上下双轨之间排满方格，每格一种纹样（玫瑰结/菱珠/对叶），格间短竖刻。
    /// 参考稿外框与标题带下缘的花边就是它；只画横向通栏。
    /// </summary>
    public static void Frieze(CanvasItem ci, Rect2 band, Color color, int seed = 1)
    {
        var h = band.Size.Y;
        if (h < 10f || band.Size.X < h * 3f)
            return;

        var dim = new Color(InkStyle.Dim, 0.6f);

        // 双轨：亮线贴边，暗线靠内。
        InkLine(ci, new Vector2(band.Position.X, band.Position.Y + 1.5f),
            new Vector2(band.End.X, band.Position.Y + 1.5f), color, 1.2f, 0.22f, seed);
        InkLine(ci, new Vector2(band.Position.X, band.Position.Y + 4.5f),
            new Vector2(band.End.X, band.Position.Y + 4.5f), dim, 0.9f, 0.16f, seed + 1);
        InkLine(ci, new Vector2(band.Position.X, band.End.Y - 4.5f),
            new Vector2(band.End.X, band.End.Y - 4.5f), dim, 0.9f, 0.16f, seed + 2);
        InkLine(ci, new Vector2(band.Position.X, band.End.Y - 1.5f),
            new Vector2(band.End.X, band.End.Y - 1.5f), color, 1.2f, 0.22f, seed + 3);

        // 方格纹样：不足一格的余量均分到两端，纹样带整体居中。
        var cell = h - 6f;
        var n = Math.Max(1, (int)(band.Size.X / cell));
        var x0 = band.Position.X + (band.Size.X - n * cell) / 2f;
        var cy = band.GetCenter().Y;

        for (var i = 0; i < n; i++)
        {
            var c = new Vector2(x0 + (i + 0.5f) * cell, cy);
            switch ((seed + i) % 3)
            {
                case 0:
                    Rosette(ci, c, cell * 0.34f, color, seed + i * 7);
                    break;
                case 1:
                    Jewel(ci, c, cell * 0.24f, color);
                    Jewel(ci, c + new Vector2(-cell * 0.34f, 0f), 1.3f, dim, filled: false);
                    Jewel(ci, c + new Vector2(cell * 0.34f, 0f), 1.3f, dim, filled: false);
                    break;
                default:
                    // 对叶：一对镜像弧，从底中分向两侧。
                    var foot = c + new Vector2(0f, cell * 0.26f);
                    Ink(ci, QuadArc(foot,
                        c + new Vector2(-cell * 0.30f, cell * 0.02f),
                        c + new Vector2(-cell * 0.20f, -cell * 0.26f), 5),
                        color, 1f, 0.10f, seed + i * 7 + 3);
                    Ink(ci, QuadArc(foot,
                        c + new Vector2(cell * 0.30f, cell * 0.02f),
                        c + new Vector2(cell * 0.20f, -cell * 0.26f), 5),
                        color, 1f, 0.10f, seed + i * 7 + 4);
                    break;
            }

            if (i > 0)
            {
                var x = x0 + i * cell;
                InkLine(ci, new Vector2(x, band.Position.Y + 6f),
                    new Vector2(x, band.End.Y - 6f), dim, 0.9f, 0.12f, seed + 100 + i);
            }
        }
    }

    /// <summary>
    /// 箭头收头：指向 dir（+1 右 / -1 左）的外向箭头。
    /// 主刺两笔外撇，根部一枚菱珠压住接口；花带两端的闭合件。
    /// </summary>
    public static void ArrowHead(CanvasItem ci, Vector2 tip, float dir, Color color, int seed = 1)
    {
        const float spread = 9f;
        const float back = 13f;
        InkLine(ci, tip, new Vector2(tip.X - dir * back, tip.Y - spread), color, 1.4f, 0.25f, seed);
        InkLine(ci, tip, new Vector2(tip.X - dir * back, tip.Y + spread), color, 1.4f, 0.25f, seed + 1);
        InkLine(ci, new Vector2(tip.X - dir * 3f, tip.Y - spread * 0.55f),
            new Vector2(tip.X - dir * 3f, tip.Y + spread * 0.55f), color, 1f, 0.18f, seed + 2);
        Jewel(ci, new Vector2(tip.X - dir * (back + 5f), tip.Y), 2.2f, color);
    }

    /// <summary>
    /// 箭头闭合花带：连续花带两端各一个外向箭头收头，
    /// 让通栏饰带从箭头到箭头读作一件完整饰品。标题带下缘用。
    /// </summary>
    public static void FriezeClosed(CanvasItem ci, Rect2 band, Color color, int seed = 1)
    {
        Frieze(ci, band, color, seed);
        var cy = band.GetCenter().Y;
        ArrowHead(ci, new Vector2(band.Position.X - 11f, cy), -1f, color, seed + 40);
        ArrowHead(ci, new Vector2(band.End.X + 11f, cy), 1f, color, seed + 44);
    }

    /// <summary>
    /// 四角交叉排线：v1 参考图里"细排线贴着四角、向黑场渐隐"的手绘底纹。
    /// 沿两条对角线方向排细线，越靠外越淡，偶发断线保留手绘感；
    /// 只做角部气氛，不抢角花。排线画在角花之前，让卷草压在纹理上。
    /// </summary>
    public static void CornerEtching(CanvasItem ci, Rect2 r, Color? color = null,
        float alpha = 0.09f, float zone = 240f, int seed = 9030)
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

            EtchFan(ci, corner, zr, sx, sy, c, seed + k * 17);
        }
    }

    /// <summary>一个角上的双向扇形排线：以角为源，四个斜向铺细线。</summary>
    private static void EtchFan(CanvasItem ci, Vector2 corner, Rect2 zone,
        float sx, float sy, Color c, int seed)
    {
        var rng = new InkRng(seed);
        const float step = 4f;
        const float spacing = 7f;

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
                        ci.DrawPolyline(line.ToArray(), new Color(c, c.A * fade), 1f);
                        line.Clear();
                    }
                    else
                        line.Clear();
                }
                if (line.Count > 1)
                    ci.DrawPolyline(line.ToArray(), new Color(c, c.A * fade), 1f);
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

    // ---------- 人形 ----------

    /// <summary>
    /// 站立人形线稿。cx 是中轴，footY 是脚底，h 是总高。
    /// 头、肩、束腰长袍轮廓加两道衣褶，比单纯的圆圈加梯形更耐看。
    /// </summary>
    public static void Figure(CanvasItem ci, float cx, float footY, float h, Color color, int seed = 7)
    {
        var headR = h * 0.115f;
        var headY = footY - h * 0.875f;
        var shoulderY = footY - h * 0.70f;
        var waistY = footY - h * 0.42f;
        var hemHalf = h * 0.185f;
        var shoulderHalf = h * 0.155f;
        var waistHalf = h * 0.115f;

        // 头
        var head = new Vector2[25];
        for (var i = 0; i < 24; i++)
        {
            var a = Mathf.Tau * i / 24f;
            head[i] = new Vector2(cx + Mathf.Cos(a) * headR, headY + Mathf.Sin(a) * headR * 1.08f);
        }
        head[24] = head[0];
        Ink(ci, head, color, 1.6f, 0.5f, seed);

        // 颈
        InkLine(ci, new Vector2(cx, headY + headR * 1.05f),
            new Vector2(cx, shoulderY), color, 1.4f, 0.4f, seed + 1);

        // 肩线
        var shoulder = new[]
        {
            new Vector2(cx - shoulderHalf, shoulderY + h * 0.012f),
            new Vector2(cx, shoulderY - h * 0.012f),
            new Vector2(cx + shoulderHalf, shoulderY + h * 0.012f),
        };
        Ink(ci, shoulder, color, 1.6f, 0.6f, seed + 2);

        // 袍身两侧：肩 → 腰 → 下摆
        Ink(ci, new[]
        {
            new Vector2(cx - shoulderHalf, shoulderY + h * 0.012f),
            new Vector2(cx - waistHalf, waistY),
            new Vector2(cx - hemHalf, footY - h * 0.02f),
        }, color, 1.6f, 0.8f, seed + 3);
        Ink(ci, new[]
        {
            new Vector2(cx + shoulderHalf, shoulderY + h * 0.012f),
            new Vector2(cx + waistHalf, waistY),
            new Vector2(cx + hemHalf, footY - h * 0.02f),
        }, color, 1.6f, 0.8f, seed + 4);

        // 下摆弧：两点二次贝塞尔，中点作为控制点，形成自然下垂。
        Ink(ci, QuadArc(
            new Vector2(cx - hemHalf, footY - h * 0.02f),
            new Vector2(cx, footY + h * 0.045f),
            new Vector2(cx + hemHalf, footY - h * 0.02f), 12), color, 1.6f, 0.8f, seed + 5);

        // 衣褶
        InkLine(ci, new Vector2(cx - h * 0.035f, shoulderY + h * 0.06f),
            new Vector2(cx - h * 0.055f, footY - h * 0.05f), color, 1f, 0.6f, seed + 6);
        InkLine(ci, new Vector2(cx + h * 0.045f, waistY + h * 0.02f),
            new Vector2(cx + h * 0.065f, footY - h * 0.05f), color, 1f, 0.6f, seed + 8);

        // 束腰
        InkLine(ci, new Vector2(cx - waistHalf * 1.02f, waistY),
            new Vector2(cx + waistHalf * 1.02f, waistY), color, 1.2f, 0.5f, seed + 9);
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
        var font = InkStyle.Font;
        var ascent = font.GetAscent(size);
        var descent = font.GetDescent(size);
        var w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

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

        ci.DrawString(font, new Vector2(x, baseline), text,
            HorizontalAlignment.Left, -1, size, color);
    }

    public static Vector2 Measure(string text, int size) =>
        InkStyle.Font.GetStringSize(text, HorizontalAlignment.Left, -1, size);

    /// <summary>
    /// 自适应字号：在 [min, max] 内挑一个能把 text 放进 maxWidth 的最大字号。
    /// 缩到 min 仍放不下就返回 min（不再继续缩，由调用方决定截断或换行）。
    /// </summary>
    public static int FitSize(string text, float maxWidth, int max, int min)
    {
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
        var size = FitSize(text, maxWidth, max, min);
        Text(ci, at, text, size, color, anchor);
        return size;
    }

    /// <summary>单行文本先缩字号，再省略尾部；绘制范围不超出给定矩形。</summary>
    public static int TextBounded(CanvasItem ci, Rect2 r, string text,
        int max = 22, int min = 14, Color? color = null, string anchor = "lm")
    {
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

    /// <summary>按实际字宽换行，保留换行符形成的空行。</summary>
    public static IReadOnlyList<string> WrapLines(string text, float width, int size)
    {
        var lines = new List<string>();
        if (width <= 0f || string.IsNullOrEmpty(text))
            return lines;
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var rune in paragraph.EnumerateRunes())
            {
                var next = line + rune;
                if (line.Length > 0 && Measure(next, size).X > width)
                {
                    lines.Add(line);
                    line = rune.ToString();
                }
                else
                    line = next;
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
