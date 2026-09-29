using System;
using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 程序笔画变形字：标题 "Rimisekai" 的游戏 logo 式变形字库。
/// 字形以 100 高（顶 0、基线 100）的规格框存骨架笔画：
/// 笔画等粗、棱角切槽、楔形端点，不做花体连笔与粗细变化。
/// 绘制走 InkDraw.Ink 抖动笔画，与界面同一笔触；i 的点用菱形珠。
/// 未收录的字符整体回退宋体，保证任意文案不炸。
/// </summary>
public static class InkLettering
{
    private sealed record Glyph(float Advance, Vector2[][] Strokes, Vector2[]? Dots = null);

    private static readonly Dictionary<char, Glyph> Glyphs = new()
    {
        ['R'] = new(64f, new[]
        {
            // 主茎：顶下收、底端外撇，接碗。
            Stroke(P(18, 0), P(15, 40), P(14, 68), P(16, 100)),
            Stroke(P(16, 100), P(9, 96)),
            // 碗：近方折的环，右上切角。
            Stroke(P(18, 4), P(52, 0), P(60, 20), P(46, 40), P(18, 44)),
            // 右腿：斜切下探，收成楔形缺口。
            Stroke(P(30, 44), P(52, 66), P(56, 92), P(48, 98), P(40, 92)),
        }),
        ['i'] = new(26f, new[]
        {
            Stroke(P(14, 40), P(12, 70), P(15, 100)),
        }, new[] { P(13, 10) }),
        ['m'] = new(80f, new[]
        {
            // 左茎带一点入笔。
            Stroke(P(14, 36), P(10, 70), P(11, 100)),
            // 两道尖拱，方折不下圆。
            Stroke(P(14, 44), P(26, 30), P(38, 40), P(34, 70), P(35, 100)),
            Stroke(P(38, 40), P(50, 30), P(62, 40), P(58, 70), P(59, 100)),
        }),
        ['s'] = new(46f, new[]
        {
            Stroke(P(40, 14), P(24, 4), P(10, 20), P(24, 32),
                P(38, 50), P(30, 74), P(36, 92), P(18, 100), P(10, 92)),
        }),
        ['e'] = new(48f, new[]
        {
            Stroke(P(46, 38), P(28, 28), P(10, 42), P(14, 64),
                P(28, 86), P(42, 82), P(48, 68)),
        }),
        ['k'] = new(54f, new[]
        {
            Stroke(P(14, 0), P(11, 50), P(12, 100)),
            // 上臂短促下探。
            Stroke(P(46, 28), P(30, 40), P(16, 54)),
            // 下腿斜切，端部缺口。
            Stroke(P(30, 48), P(46, 80), P(42, 98), P(34, 96)),
        }),
        ['a'] = new(52f, new[]
        {
            // 方折碗，右侧接直茎，底部不出钩。
            Stroke(P(46, 48), P(30, 40), P(14, 50), P(18, 74), P(30, 90), P(44, 82), P(46, 64)),
            Stroke(P(46, 44), P(43, 72), P(47, 100)),
        }),
    };

    /// <summary>绘制变形字标题：整体以 center 为视觉中心。anchor 语义等同 "cm"。</summary>
    public static void Draw(CanvasItem ci, Vector2 center, string text, float capHeight,
        Color color, int seed = 1)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // 未收录字符不逐个混排：整体回退宋体，避免两套笔画打架。
        foreach (var ch in text)
        {
            if (!Glyphs.ContainsKey(ch))
            {
                InkDraw.Text(ci, center, text, Mathf.RoundToInt(capHeight), color, "cm");
                return;
            }
        }

        var scale = capHeight / 100f;
        var track = 8f * scale;

        var total = -track;
        foreach (var ch in text)
            total += Glyphs[ch].Advance * scale + track;

        var weight = capHeight * 0.11f;
        var x = center.X - total / 2f;
        var top = center.Y - capHeight / 2f;

        var strokeIdx = 0;
        foreach (var ch in text)
        {
            var g = Glyphs[ch];
            foreach (var stroke in g.Strokes)
            {
                var pts = new Vector2[stroke.Length];
                for (var i = 0; i < stroke.Length; i++)
                    pts[i] = new Vector2(x + stroke[i].X * scale, top + stroke[i].Y * scale);
                Ribbon(ci, pts, color, weight, seed + strokeIdx * 3);
                strokeIdx++;
            }

            if (g.Dots != null)
            {
                foreach (var dot in g.Dots)
                    InkDraw.Jewel(ci, new Vector2(x + dot.X * scale, top + dot.Y * scale),
                        capHeight * 0.07f, color);
            }

            x += g.Advance * scale + track;
        }
    }

    /// <summary>
    /// 实心笔画：沿骨架向两侧各偏半个字重，缝成填充多边形，
    /// 让变形字有 logo 的块面感而不是细线；法线噪声给一点手绘晃动。
    /// </summary>
    private static void Ribbon(CanvasItem ci, Vector2[] pts, Color color, float weight, int seed)
    {
        var n = pts.Length;
        if (n < 2)
            return;

        var rng = new InkRng(seed);
        var left = new Vector2[n];
        var right = new Vector2[n];
        var half = weight / 2f;

        for (var i = 0; i < n; i++)
        {
            var prev = pts[Mathf.Max(0, i - 1)];
            var next = pts[Mathf.Min(n - 1, i + 1)];
            var tangent = next - prev;
            if (tangent.LengthSquared() < 1e-8f)
                tangent = Vector2.Right;
            var normal = tangent.Normalized().Orthogonal();
            var jitter = rng.Range(-0.55f, 0.55f);
            var p = pts[i] + normal * jitter;
            left[i] = p + normal * half;
            right[i] = p - normal * half;
        }

        var loop = new Vector2[n * 2];
        for (var i = 0; i < n; i++)
            loop[i] = left[i];
        for (var i = n - 1; i >= 0; i--)
            loop[n + (n - 1 - i)] = right[i];

        ci.DrawColoredPolygon(loop, color);
    }

    private static Vector2 P(float x, float y) => new(x, y);

    private static Vector2[] Stroke(params Vector2[] pts) => pts;
}
