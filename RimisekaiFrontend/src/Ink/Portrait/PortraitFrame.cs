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

    /// <summary>
    /// 按下态：矩形与它一致者按选中画。触摸没有悬停，按下当场不给反馈就只剩"点没点中"的疑问。
    /// 每个画面在 _Draw 开头调一次（没有按下就传 null）；后画的画面会覆盖前一个的值。
    /// </summary>
    public static void SetPress(Rect2? rect) => _press = rect;

    private static bool Pressed(Rect2 r) =>
        _press is { } p && p.Position == r.Position && p.Size == r.Size;

    /// <summary>自绘块（地图格等）查询自己是否正被按住，用来给按压反馈。</summary>
    public static bool IsPressed(Rect2 r) => Pressed(r);

    /// <summary>按压填色（自绘块与 Button 共用同一档，保持全界面一致）。</summary>
    public static Color PressFill => new(InkStyle.Line, 0.16f);

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

    /// <summary>一行字＋一条三段渐隐线。禁止标题牌、禁止端珠。</summary>
    public static void Title(CanvasItem ci, Rect2 r, string text, int size = PortraitLayout.FontTitle)
    {
        var x = r.Position.X + PortraitLayout.Pad;
        var y = r.Position.Y + 20f;
        InkDraw.Text(ci, new Vector2(x, y), text, size, InkStyle.Line, "lt");
        FadingRule(ci, x, r.End.X - PortraitLayout.Pad, y + size + 18f);
    }

    public static void FadingRule(CanvasItem ci, float left, float right, float y)
    {
        if (right <= left)
            return;
        var seg = (right - left) / 3f;
        for (var i = 0; i < 3; i++)
        {
            var a = 1f - i * 0.3f;
            InkDraw.Ink(ci, new[]
            {
                new Vector2(left + i * seg, y), new Vector2(left + (i + 1) * seg, y),
            }, new Color(InkStyle.Dim, a), PortraitLayout.LineHair);
        }
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
    /// 弱化按钮：单层极细暗线，不给白双线——用在内容为主、按钮只作出口的场合，
    /// 让画面重心留在内容上（如状态页的能力抽屉）。
    /// </summary>
    public static void SubtleButton(CanvasItem ci, Rect2 r, bool selected = false)
    {
        var pressed = Pressed(r);
        var active = selected || pressed;
        ci.DrawRect(r, pressed ? PressFill : active ? InkStyle.Hover : InkStyle.Inset);
        InkDraw.Ink(ci, Loop(r), active ? InkStyle.Line : new Color(InkStyle.Dim, 0.65f),
            active ? PortraitLayout.LineHair + 1f : PortraitLayout.LineHair);
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

    /// <summary>
    /// 列表行：整行一个手指位。
    /// 未选中只留一条极细分隔线——一行一圈框会把整页读成表格；分隔线才读得成"一条一条的账目"。
    /// 选中与按下才起双线框＋浅填。只做浅填与线宽变化，不改线色。
    /// </summary>
    public static void Row(CanvasItem ci, Rect2 r, string name, string value, bool selected)
    {
        var pressed = Pressed(r);
        if (selected || pressed)
        {
            ci.DrawRect(r, pressed ? PressFill : InkStyle.Hover);
            InkDraw.Ink(ci, Loop(r.Grow(-6f)), InkStyle.Line, PortraitLayout.LineHair + 1f);
            InkDraw.Ink(ci, Loop(r.Grow(-12f)), InkStyle.Dim, PortraitLayout.LineHair);
        }
        else
            InkDraw.InkLine(ci, new Vector2(r.Position.X + 14f, r.End.Y - 3f),
                new Vector2(r.End.X - 14f, r.End.Y - 3f), new Color(InkStyle.Dim, 0.42f),
                PortraitLayout.LineHair);

        var inner = r.Grow(-14f);
        var hasIcon = InkIcon.Draw(ci, name, PortraitLayout.RowIcon(r));
        var text = PortraitLayout.RowText(r, hasIcon);
        var valueWidth = value.Length > 0
            ? Mathf.Min(text.Size.X * 0.42f, InkDraw.Measure(value, PortraitLayout.FontMeta).X + 20f) : 0f;
        var nameRect = new Rect2(text.Position, new Vector2(text.Size.X - valueWidth, text.Size.Y));
        InkDraw.TextBounded(ci, nameRect, name, PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        if (value.Length > 0)
            InkDraw.TextBounded(ci, new Rect2(text.End.X - valueWidth, text.Position.Y, valueWidth, text.Size.Y),
                value, PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Dim, "rm");
    }
}
