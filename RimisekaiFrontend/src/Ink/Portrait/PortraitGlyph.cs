using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 竖屏线描图标：页签、顶栏、按钮里的小符号，全部由直线与弧线构成（无位图、无 emoji）。
/// 坐标约定与参考稿一致：以 (x,y) 为中心、r 为半径，颜色由调用方给（一律取 InkStyle 调色板）。
/// </summary>
public static class PortraitGlyph
{
    private static float W(float r) => MathF.Max(4.5f, r / 5f);

    private static void L(CanvasItem ci, Color c, float w, params Vector2[] pts) =>
        ci.DrawPolyline(pts, c, w, true);

    private static Vector2 P(float x, float y) => new(x, y);

    /// <summary>椭圆弧（角度按度，0 度朝右，顺时针为正——与 y 轴向下的画布一致）。</summary>
    private static void Arc(CanvasItem ci, float cx, float cy, float rx, float ry, float a0, float a1, Color c, float w)
    {
        const int steps = 24;
        var pts = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var a = Mathf.DegToRad(a0 + (a1 - a0) * i / steps);
            pts[i] = new Vector2(cx + MathF.Cos(a) * rx, cy + MathF.Sin(a) * ry);
        }
        ci.DrawPolyline(pts, c, w, true);
    }

    private static void Ring(CanvasItem ci, float cx, float cy, float r, Color c, float w) =>
        ci.DrawArc(new Vector2(cx, cy), r, 0f, Mathf.Tau, 40, c, w, true);

    private static void Box(CanvasItem ci, float x0, float y0, float x1, float y1, Color c, float w) =>
        L(ci, c, w, P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1), P(x0, y0));

    public static void Diamond(CanvasItem ci, float cx, float cy, float r, Color c) =>
        ci.DrawColoredPolygon(new[] { P(cx, cy - r), P(cx + r, cy), P(cx, cy + r), P(cx - r, cy) }, c);

    public static void Back(CanvasItem ci, float x, float y, float r, Color c) =>
        L(ci, c, W(r) + 2, P(x + r * .4f, y - r * .8f), P(x - r * .4f, y), P(x + r * .4f, y + r * .8f));

    public static void Close(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r) + 1;
        L(ci, c, w, P(x - r * .6f, y - r * .6f), P(x + r * .6f, y + r * .6f));
        L(ci, c, w, P(x - r * .6f, y + r * .6f), P(x + r * .6f, y - r * .6f));
    }

    public static void Plus(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r) + 2;
        L(ci, c, w, P(x - r * .7f, y), P(x + r * .7f, y));
        L(ci, c, w, P(x, y - r * .7f), P(x, y + r * .7f));
    }

    public static void Minus(CanvasItem ci, float x, float y, float r, Color c) =>
        L(ci, c, W(r) + 2, P(x - r * .7f, y), P(x + r * .7f, y));

    public static void Check(CanvasItem ci, float x, float y, float r, Color c) =>
        L(ci, c, W(r) + 2, P(x - r * .7f, y), P(x - r * .2f, y + r * .55f), P(x + r * .8f, y - r * .6f));

    public static void Gear(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.PI / 4f;
            L(ci, c, w + 2, P(x + MathF.Cos(a) * r * .55f, y + MathF.Sin(a) * r * .55f),
                P(x + MathF.Cos(a) * r * .95f, y + MathF.Sin(a) * r * .95f));
        }
        Ring(ci, x, y, r * .62f, c, w);
        Ring(ci, x, y, r * .25f, c, w);
    }

    public static void Search(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Ring(ci, x - r * .2f, y - r * .2f, r * .55f, c, w);
        L(ci, c, w + 1, P(x + r * .25f, y + r * .25f), P(x + r * .85f, y + r * .85f));
    }

    public static void Castle(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        L(ci, c, w, P(x - r * .8f, y + r * .8f), P(x - r * .8f, y - r * .5f), P(x - r * .45f, y - r * .5f),
            P(x - r * .45f, y - r * .2f), P(x - r * .15f, y - r * .2f), P(x - r * .15f, y - r * .8f),
            P(x + r * .15f, y - r * .8f), P(x + r * .15f, y - r * .2f), P(x + r * .45f, y - r * .2f),
            P(x + r * .45f, y - r * .5f), P(x + r * .8f, y - r * .5f), P(x + r * .8f, y + r * .8f),
            P(x - r * .8f, y + r * .8f));
        Arc(ci, x, y + r * .7f, r * .25f, r * .5f, 180, 360, c, w);
    }

    public static void Person(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Ring(ci, x, y - r * .52f, r * .38f, c, w);
        Arc(ci, x, y + r * .82f, r * .8f, r * .77f, 180, 360, c, w);
    }

    public static void Scroll(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Box(ci, x - r * .6f, y - r * .75f, x + r * .6f, y + r * .75f, c, w);
        foreach (var k in new[] { -.35f, 0f, .35f })
            L(ci, c, w - 1, P(x - r * .3f, y + r * k), P(x + r * .3f, y + r * k));
        L(ci, c, w + 1, P(x - r * .85f, y - r * .75f), P(x + r * .85f, y - r * .75f));
        L(ci, c, w + 1, P(x - r * .85f, y + r * .75f), P(x + r * .85f, y + r * .75f));
    }

    public static void Chest(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Box(ci, x - r * .85f, y - r * .2f, x + r * .85f, y + r * .75f, c, w);
        Arc(ci, x, y - r * .2f, r * .85f, r * .65f, 180, 360, c, w);
        ci.DrawRect(new Rect2(x - r * .15f, y - r * .3f, r * .3f, r * .4f), c);
    }

    public static void Book(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        L(ci, c, w, P(x, y - r * .6f), P(x, y + r * .8f));
        L(ci, c, w, P(x, y - r * .6f), P(x - r * .9f, y - r * .8f), P(x - r * .9f, y + r * .6f), P(x, y + r * .8f));
        L(ci, c, w, P(x, y - r * .6f), P(x + r * .9f, y - r * .8f), P(x + r * .9f, y + r * .6f), P(x, y + r * .8f));
    }

    public static void Coin(CanvasItem ci, float x, float y, float r, Color c)
    {
        Ring(ci, x, y, r, c, W(r) + 1);
        Diamond(ci, x, y, r * .45f, c);
    }

    public static void Leaf(CanvasItem ci, float x, float y, float r, Color c)
    {
        // 叶：两段圆弧合成的梭形，沿左下—右上对角，中间一道叶脉。
        var w = W(r);
        var k = r * 0.7071f;
        var a = P(x - k, y + k);
        var b = P(x + k, y - k);
        var pts1 = new Vector2[13];
        var pts2 = new Vector2[13];
        for (var i = 0; i <= 12; i++)
        {
            var t = i / 12f;
            var mid = a.Lerp(b, t);
            var bulge = MathF.Sin(t * MathF.PI) * r * .45f;
            var n = new Vector2(.7071f, .7071f);
            pts1[i] = mid + n * bulge;
            pts2[i] = mid - n * bulge;
        }
        ci.DrawPolyline(pts1, c, w, true);
        ci.DrawPolyline(pts2, c, w, true);
        L(ci, c, w - 1, a, b);
    }

    public static void Sun(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Ring(ci, x, y, r * .45f, c, w);
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.PI / 4f;
            L(ci, c, w, P(x + MathF.Cos(a) * r * .7f, y + MathF.Sin(a) * r * .7f),
                P(x + MathF.Cos(a) * r, y + MathF.Sin(a) * r));
        }
    }

    public static void Cloud(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Arc(ci, x, y + r * .1f, r, r * .7f, 180, 360, c, w);
        L(ci, c, w, P(x - r, y + r * .1f), P(x + r, y + r * .1f));
    }

    public static void Rain(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Arc(ci, x, y - r * .2f, r, r * .7f, 180, 360, c, w);
        L(ci, c, w, P(x - r, y - r * .2f), P(x + r, y - r * .2f));
        foreach (var k in new[] { -.5f, 0f, .5f })
            L(ci, c, w, P(x + r * k, y + r * .2f), P(x + r * k - r * .2f, y + r * .8f));
    }

    public static void Snow(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        for (var i = 0; i < 3; i++)
        {
            var a = i * MathF.PI / 3f + MathF.PI / 2f;
            L(ci, c, w, P(x - MathF.Cos(a) * r, y - MathF.Sin(a) * r), P(x + MathF.Cos(a) * r, y + MathF.Sin(a) * r));
        }
    }

    public static void Clock(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Ring(ci, x, y, r, c, w);
        L(ci, c, w, P(x, y - r * .6f), P(x, y), P(x + r * .45f, y));
    }

    public static void Pen(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r) + 1;
        L(ci, c, w, P(x - r, y + r), P(x + r * .8f, y - r * .8f));
        L(ci, c, w, P(x - r, y + r), P(x - r * .6f, y + r * .2f));
    }

    public static void Hammer(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r) + 1;
        L(ci, c, w, P(x - r * .7f, y + r * .8f), P(x + r * .3f, y - r * .2f));
        L(ci, c, w - 1, P(x - r * .1f, y - r * .7f), P(x + r * .5f, y - r * .9f), P(x + r * .9f, y - r * .3f),
            P(x + r * .5f, y + r * .05f), P(x - r * .1f, y - r * .7f));
    }

    public static void Swords(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r) + 1;
        L(ci, c, w, P(x - r * .8f, y - r * .8f), P(x + r * .8f, y + r * .8f));
        L(ci, c, w, P(x + r * .8f, y - r * .8f), P(x - r * .8f, y + r * .8f));
        L(ci, c, w, P(x - r * .7f, y + r * .3f), P(x - r * .3f, y + r * .7f));
        L(ci, c, w, P(x + r * .7f, y + r * .3f), P(x + r * .3f, y + r * .7f));
    }

    public static void Map(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        var p = new[]
        {
            P(x - r * .9f, y - r * .6f), P(x - r * .3f, y - r * .85f), P(x + r * .3f, y - r * .6f),
            P(x + r * .9f, y - r * .85f), P(x + r * .9f, y + r * .6f), P(x + r * .3f, y + r * .85f),
            P(x - r * .3f, y + r * .6f), P(x - r * .9f, y + r * .85f), P(x - r * .9f, y - r * .6f),
        };
        L(ci, c, w, p);
        L(ci, c, w - 1, p[1], p[6]);
        L(ci, c, w - 1, p[2], p[5]);
    }

    public static void Lock(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Box(ci, x - r * .7f, y - r * .1f, x + r * .7f, y + r * .9f, c, w);
        Arc(ci, x, y - r * .1f, r * .45f, r * .55f, 180, 360, c, w);
    }

    public static void Flee(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r) + 1;
        L(ci, c, w, P(x + r * .8f, y), P(x - r * .8f, y));
        L(ci, c, w, P(x - r * .2f, y - r * .6f), P(x - r * .8f, y), P(x - r * .2f, y + r * .6f));
    }

    public static void Bell(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Arc(ci, x, y - r * .15f, r * .7f, r * .75f, 180, 360, c, w);
        L(ci, c, w, P(x - r * .7f, y - r * .15f), P(x - r * .9f, y + r * .55f), P(x + r * .9f, y + r * .55f),
            P(x + r * .7f, y - r * .15f));
        ci.DrawCircle(new Vector2(x, y + r * .8f), r * .2f, c);
    }

    public static void Bag(CanvasItem ci, float x, float y, float r, Color c)
    {
        var w = W(r);
        Box(ci, x - r * .8f, y - r * .4f, x + r * .8f, y + r * .9f, c, w);
        Arc(ci, x, y - r * .4f, r * .4f, r * .5f, 180, 360, c, w);
    }

    /// <summary>按天气值挑一个图标（晴＝日，阴/风＝云，雨类＝雨，雪类＝雪）。</summary>
    public static void Weather(CanvasItem ci, Rimisekai.Clock.Weather weather, float x, float y, float r, Color c)
    {
        switch (weather)
        {
            case Rimisekai.Clock.Weather.Clear: Sun(ci, x, y, r, c); break;
            case Rimisekai.Clock.Weather.Rain:
            case Rimisekai.Clock.Weather.HeavyRain:
            case Rimisekai.Clock.Weather.Thunder: Rain(ci, x, y, r, c); break;
            case Rimisekai.Clock.Weather.Snow:
            case Rimisekai.Clock.Weather.HeavySnow:
            case Rimisekai.Clock.Weather.Blizzard: Snow(ci, x, y, r, c); break;
            default: Cloud(ci, x, y, r, c); break;
        }
    }

    /// <summary>底部页签的五个图标：领地 / 角色 / 委托 / 仓储 / 日志。</summary>
    public static readonly Action<CanvasItem, float, float, float, Color>[] TabIcons =
    {
        Castle, Person, Scroll, Chest, Book,
    };
}
