using System;
using System.Globalization;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 设置 / 存档 / 立绘缺图时的共用画法（标题系统页与据点「系统」页签两处共用一份）。
/// 只画画面：命中块由调用方照旧登记，这里一个都不加。
/// </summary>
public static class PortraitSystemArt
{
    private static readonly string[] VolumeStops = { "0", "25", "50", "75", "100" };

    /// <summary>
    /// 主音量设置行：哥特缺角卡作底，左列「主音量」＋当前百分比，右列菱头深槽滑条，
    /// 五个档位（与五档命中块一一对应）在槽下标刻度菱与数字，当前档亮起。
    /// y＝命中带上沿（与调用方登记命中块用的同一个 y）。
    /// </summary>
    public static void Volume(CanvasItem ci, float y, float volume)
    {
        var card = new Rect2(PortraitLayout.Pad, y - 6f, PortraitLayout.FullWidth, 236f);
        PortraitFrame.NotchedFrame(ci, card, new Color(InkStyle.Panel, 0.94f), jewels: false);

        var labelX = PortraitLayout.Pad + 48f;
        InkDraw.Text(ci, new Vector2(labelX, y + 59f), "主音量", PortraitLayout.FontBody, InkStyle.Line, "lm");
        var v = Mathf.Clamp(volume, 0f, 1f);
        InkDraw.Text(ci, new Vector2(labelX, y + 150f), v <= 0.001f ? "静音" : $"{Mathf.RoundToInt(v * 100f)}%",
            PortraitLayout.FontMeta, InkStyle.Dim, "lm");

        // 菱头深槽（不用圆头药丸槽）：黑槽暗灰描边，已调到的一段银白渐变。
        var track = new Rect2(380f, y + 51f, PortraitLayout.CanvasWidth - 380f - PortraitLayout.Pad - 40f, 16f);
        var groove = track.Grow(4f);
        PortraitFrame.Poly(ci, PortraitFrame.LozengeCapPoints(groove, 12f), new Color(InkStyle.Inset, 0.95f), InkStyle.WoodDark, 3f);
        if (v > 0f)
        {
            var fill = new Rect2(track.Position, new Vector2(Mathf.Max(16f, track.Size.X * v), track.Size.Y));
            PortraitFrame.Poly(ci, PortraitFrame.LozengeCapPoints(fill, 8f), InkStyle.Line);
        }

        // 五档刻度：刻度菱落在槽下，数字再往下；当前档亮、其余灰。
        var nearest = Mathf.RoundToInt(v * 4f);
        for (var i = 0; i < VolumeStops.Length; i++)
        {
            var cx = track.Position.X + track.Size.X * i / 4f;
            var lit = i == nearest;
            InkDraw.Jewel(ci, new Vector2(cx, track.End.Y + 26f), lit ? 8f : 6f, lit ? InkStyle.Line : InkStyle.WoodDark);
            // 末档靠右对齐，免得「100」顶出卡的内框线。
            var last = i == VolumeStops.Length - 1;
            InkDraw.Text(ci, new Vector2(last ? cx + 14f : cx, y + 150f), VolumeStops[i], 34,
                lit ? InkStyle.Line : InkStyle.Dim, last ? "rm" : "cm");
        }

        var knob = new Vector2(track.Position.X + track.Size.X * v, track.GetCenter().Y);
        InkDraw.Jewel(ci, knob, 26f, InkStyle.Bg);
        InkDraw.Jewel(ci, knob, 22f, InkStyle.Line);
        InkDraw.Jewel(ci, knob, 10f, InkStyle.Bg);
        InkDraw.Jewel(ci, knob, 5f, InkStyle.Line);

        // 卡下一道渐隐细线＋一句说明：改了即生效、自动记住。
        PortraitFrame.FadingRule(ci, PortraitLayout.Pad + 80f, PortraitLayout.CanvasWidth - PortraitLayout.Pad - 80f, card.End.Y + 60f);
        InkDraw.Text(ci, new Vector2(PortraitLayout.CanvasWidth / 2f, card.End.Y + 120f), "调整即刻生效，并自动保存",
            34, InkStyle.Dim, "cm");
    }

    /// <summary>
    /// 存档卡：缺角双线框；左侧尖拱小窗里是城堡纹与「第 N 日」，右侧领地名、存档时刻（换成游戏内时辰）。
    /// latest＝列表第一条（最新），右上角挂一枚「最新」菱头签。
    /// </summary>
    public static void SaveCard(CanvasItem ci, Rect2 r, SaveSlotInfo slot, bool latest, bool pressed)
    {
        PortraitFrame.NotchedFrame(ci, r, pressed ? new Color(InkStyle.Hover, 1f) : new Color(InkStyle.Panel, 0.96f), jewels: false);

        // 尖拱小窗：黑底、细描边，里面城堡纹＋日数。
        var win = new Rect2(r.Position.X + 40f, r.Position.Y + 28f, 200f, r.Size.Y - 56f);
        PortraitFrame.Arch(ci, win, 54f, InkStyle.Bg, InkStyle.WoodDark, 3f);
        PortraitFrame.Arch(ci, win.Grow(-10f), 46f, null, new Color(InkStyle.Dim, 0.5f), 2f);
        PortraitGlyph.Castle(ci, win.GetCenter().X, win.Position.Y + 86f, 34f, InkStyle.Dim);
        InkDraw.Text(ci, new Vector2(win.GetCenter().X, win.End.Y - 38f), $"第{slot.Day}日", 34, InkStyle.Line, "cm");

        var x = win.End.X + 40f;
        var tagRoom = latest ? 150f : 40f;
        InkDraw.TextBounded(ci, new Rect2(x, r.Position.Y + 36f, r.End.X - x - tagRoom, 70f),
            slot.TerritoryName.Length > 0 ? slot.TerritoryName : "无名领地",
            PortraitLayout.FontBody, PortraitLayout.FontMeta, InkStyle.Line, "lm");
        if (latest)
        {
            var tag = new Rect2(r.End.X - 40f - 104f, r.Position.Y + 44f, 104f, 54f);
            PortraitFrame.Poly(ci, PortraitFrame.LozengeCapPoints(tag, 16f), null, InkStyle.Line, 2.5f);
            InkDraw.Text(ci, tag.GetCenter(), "最新", 32, InkStyle.Line, "cm");
        }

        InkDraw.InkLine(ci, new Vector2(x, r.Position.Y + 110f), new Vector2(r.End.X - 40f, r.Position.Y + 110f),
            new Color(InkStyle.WoodDark, 0.8f), 2f);
        var hour = slot.Minutes / 60 % 24;
        InkDraw.Text(ci, new Vector2(x, r.Position.Y + 150f), $"游戏内 {hour:00}:{slot.Minutes % 60:00}",
            PortraitLayout.FontMeta, InkStyle.Dim, "lm");
        if (DateTime.TryParseExact(slot.Timestamp, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            InkDraw.Text(ci, new Vector2(r.End.X - 40f, r.Position.Y + 150f), time.ToString("M月d日 HH:mm", CultureInfo.InvariantCulture),
                34, InkStyle.Dim, "rm");
    }

    /// <summary>读档列表为空时的提示：尖拱空龛＋一句话。</summary>
    public static void EmptySaves(CanvasItem ci, Rect2 view)
    {
        var cx = view.GetCenter().X;
        var niche = new Rect2(cx - 110f, view.Position.Y + 220f, 220f, 300f);
        PortraitFrame.Arch(ci, niche, 90f, InkStyle.Bg, InkStyle.WoodDark, 3f);
        PortraitGlyph.Castle(ci, cx, niche.GetCenter().Y + 20f, 44f, InkStyle.WoodDark);
        InkDraw.Text(ci, new Vector2(cx, niche.End.Y + 80f), "还没有存档", PortraitLayout.FontBody, InkStyle.Dim, "cm");
        InkDraw.Text(ci, new Vector2(cx, niche.End.Y + 150f), "开始新的旅程后，可在「系统」页签保存进度", 34, InkStyle.Dim, "cm");
    }

    /// <summary>
    /// 立绘缺图时的占位：尖拱壁龛（黑底、双线、拱顶十字珠），龛里一枚大方头像（名字首字），
    /// 下缘渐隐进黑场——让立绘位不再是一片空白。niche＝壁龛外框。
    /// </summary>
    public static void PortraitNiche(CanvasItem ci, Rect2 niche, Texture2D? avatar, string name)
    {
        var rise = niche.Size.X * 0.42f;
        PortraitFrame.Arch(ci, niche, rise, new Color(InkStyle.Bg, 0.82f), InkStyle.WoodDark, 3f);
        PortraitFrame.Arch(ci, niche.Grow(-16f), rise - 12f, null, new Color(InkStyle.Dim, 0.55f), 2f);
        PortraitFrame.CrossJewel(ci, new Vector2(niche.GetCenter().X, niche.Position.Y - 26f), 14f, InkStyle.Dim);
        var radius = Mathf.Min(niche.Size.X * 0.3f, (niche.Size.Y - rise) * 0.36f);
        var center = new Vector2(niche.GetCenter().X, niche.Position.Y + rise + (niche.Size.Y - rise) * 0.46f);
        PortraitFrame.Avatar(ci, center, radius, avatar, name);
        PortraitFrame.Fade(ci, new Rect2(niche.Position.X - 4f, niche.End.Y - niche.Size.Y * 0.18f, niche.Size.X + 8f, niche.Size.Y * 0.18f + 2f), 0f, 1f);
    }
}
