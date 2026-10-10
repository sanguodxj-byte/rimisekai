using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 标题底纹（2026-10-10 主人定：弹窗顶上外挂的冠饰撤掉，收进标题底下当背景）。
/// 代码画的哥特窗花：正中一扇玫瑰窗（双圈＋八瓣尖叶＋芯菱），两翼一排尖拱连廊，
/// 拱高向外递减、透明度向外渐隐，末端收一枚小菱。只用骨白 Line 色调透明度，不新增色相；
/// 整幅落在给定矩形内，不越出弹窗框，也不登记任何命中块。
/// </summary>
public static class PortraitTracery
{
    private const float Stroke = 2f;

    /// <summary>在 band 内画标题底纹；alpha 为最亮处（玫瑰窗）的透明度。</summary>
    public static void TitleBackdrop(CanvasItem ci, Rect2 band, float alpha = 0.2f)
    {
        var c = band.GetCenter();
        var r = band.Size.Y * 0.47f;
        Rose(ci, c, r, alpha);

        var archW = Mathf.Clamp(band.Size.Y * 0.3f, 34f, 56f);
        var gap = 14f;
        var start = r + gap;
        var room = band.Size.X / 2f - start - 26f;
        var count = Mathf.Max(0, (int)(room / archW));
        var baseY = c.Y + r * 0.62f;
        for (var side = -1; side <= 1; side += 2)
        {
            var reach = start + count * archW;
            // 连廊的基线与顶线：从玫瑰窗外缘一路收向末端菱。
            Fade(ci, c.X + side * (r + 4f), c.X + side * reach, baseY, alpha * 0.9f);
            Fade(ci, c.X + side * (r * 0.72f), c.X + side * reach, c.Y - r * 0.62f, alpha * 0.55f);
            for (var i = 0; i < count; i++)
            {
                var t = count <= 1 ? 0f : i / (float)(count - 1);
                var a = alpha * (0.95f - 0.6f * t);
                var x0 = side > 0 ? c.X + start + i * archW : c.X - start - (i + 1) * archW;
                var legs = (r * 0.62f + baseY - c.Y) * (0.55f - 0.35f * t);
                Lancet(ci, x0 + 3f, archW - 6f, baseY, legs, new Color(InkStyle.Line, a));
            }
            var tip = new Vector2(c.X + side * (reach + 14f), baseY);
            InkDraw.Jewel(ci, tip, 7f, new Color(InkStyle.Line, alpha * 0.7f), filled: false);
        }
    }

    /// <summary>玫瑰窗：外双圈、八瓣尖叶、瓣间小圆、芯部菱。</summary>
    private static void Rose(CanvasItem ci, Vector2 c, float r, float alpha)
    {
        var line = new Color(InkStyle.Line, alpha);
        var soft = new Color(InkStyle.Line, alpha * 0.6f);
        ci.DrawArc(c, r, 0f, Mathf.Tau, 96, line, Stroke, true);
        ci.DrawArc(c, r * 0.9f, 0f, Mathf.Tau, 96, soft, Stroke * 0.75f, true);
        ci.DrawArc(c, r * 0.3f, 0f, Mathf.Tau, 48, line, Stroke, true);
        const int petals = 8;
        for (var k = 0; k < petals; k++)
        {
            var phi = -Mathf.Pi / 2f + k * Mathf.Tau / petals;
            Petal(ci, c, phi, r * 0.34f, r * 0.84f, r * 0.17f, line);
            var mid = phi + Mathf.Pi / petals;
            ci.DrawArc(c + new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * r * 0.74f, r * 0.07f, 0f, Mathf.Tau, 20, soft, Stroke * 0.75f, true);
        }
        InkDraw.Jewel(ci, c, r * 0.14f, line, filled: false);
    }

    /// <summary>沿 phi 方向从 r1 到 r2 的尖叶（两端收尖的透镜）。</summary>
    private static void Petal(CanvasItem ci, Vector2 c, float phi, float r1, float r2, float half, Color color)
    {
        var dir = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
        var nor = new Vector2(-dir.Y, dir.X);
        const int n = 14;
        var pts = new List<Vector2>(n * 2 + 1);
        for (var i = 0; i <= n; i++)
        {
            var t = i / (float)n;
            pts.Add(c + dir * Mathf.Lerp(r1, r2, t) + nor * half * Mathf.Sin(Mathf.Pi * t));
        }
        for (var i = n - 1; i >= 0; i--)
        {
            var t = i / (float)n;
            pts.Add(c + dir * Mathf.Lerp(r1, r2, t) - nor * half * Mathf.Sin(Mathf.Pi * t));
        }
        ci.DrawPolyline(pts.ToArray(), color, Stroke * 0.85f, true);
    }

    /// <summary>一枚等边尖拱：起拱点在 (x0, baseY - legs) 与 (x0 + w, …)，两腿落到基线。</summary>
    private static void Lancet(CanvasItem ci, float x0, float w, float baseY, float legs, Color color)
    {
        var spring = baseY - legs;
        var a = new Vector2(x0, spring);
        var b = new Vector2(x0 + w, spring);
        const int n = 10;
        var pts = new List<Vector2>(n * 2 + 4) { new(x0, baseY) };
        for (var i = 0; i <= n; i++)
        {
            var th = Mathf.Pi + Mathf.Pi / 3f * i / n;
            pts.Add(b + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * w);
        }
        for (var i = n; i >= 0; i--)
        {
            var th = -Mathf.Pi / 3f * i / n;
            pts.Add(a + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * w);
        }
        pts.Add(new Vector2(x0 + w, baseY));
        ci.DrawPolyline(pts.ToArray(), color, Stroke * 0.85f, true);
    }

    /// <summary>从 x1 到 x2 的水平细线，透明度由 alpha 线性收到 0。</summary>
    private static void Fade(CanvasItem ci, float x1, float x2, float y, float alpha)
    {
        const int steps = 8;
        for (var i = 0; i < steps; i++)
        {
            var a = Mathf.Lerp(x1, x2, i / (float)steps);
            var b = Mathf.Lerp(x1, x2, (i + 1) / (float)steps);
            ci.DrawLine(new Vector2(a, y), new Vector2(b, y), new Color(InkStyle.Line, alpha * (1f - i / (float)steps)), Stroke * 0.75f, true);
        }
    }
}
