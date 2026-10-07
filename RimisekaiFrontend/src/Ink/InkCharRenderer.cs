using System;
using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>角色栏：头像卡、翻页三角、下方页面入口。</summary>
public static class InkCharRenderer
{
    private static readonly Dictionary<int, float> CardLifts = new();
    private static readonly Dictionary<int, float> CardStaminaRatios = new();
    public static bool HasActiveAnimations { get; private set; }

    private const int NameFontMax = 26;
    private const int NameFontMin = 26;

    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        // 战斗版式：标题随边框交给消融层；头像按威胁等级上移一点（前后即上下，无重排）。
        if (!model.InCombat)
        {
            InkFrame.Title(ci, InkLayout.CharPanel, "角色", 26);
            InkFrame.Button(ci, InkLayout.CharPanelStatusButton, "状态", selected: false, enabled: true, fontSize: 26);
            InkFrame.Button(ci, InkLayout.CharPanelSkillButton, "技能", selected: false, enabled: true, fontSize: 26);
        }

        HasActiveAnimations = false;
        // 动画时序铁律（主人定）：先一起平滑下降，再分别移动到危险等级的位置。
        // 当下沉动画进度 CombatT >= 0.70 时，才逐步展开各威胁等级的前后位移。
        var unfoldProgress = model.InCombat
            ? Mathf.Clamp((model.CombatT - 0.70f) / 0.30f, 0f, 1f)
            : 0f;

        for (var i = 0; i < model.Cards.Count; i++)
        {
            var card = model.Cards[i].Card;
            var rect = InkLayout.Card(i);

            // 主人定：威胁等级3才是原位，0要后退，4前进。每级位移步长 14px
            // 4/5 向上前进 (newLift > 0)，3 原位 (newLift = 0)，0/1/2 向下后退 (newLift < 0)
            var finalTarget = model.InCombat ? (card.ThreatTier - 3) * 14f : 0f;
            var targetLift = finalTarget * unfoldProgress;

            var curLift = CardLifts.TryGetValue(card.Id, out var cl) ? cl : (model.InCombat ? 0f : targetLift);
            var newLift = Mathf.Lerp(curLift, targetLift, 0.20f);
            if (Math.Abs(newLift - targetLift) > 0.4f)
                HasActiveAnimations = true;
            else
                newLift = targetLift;
            CardLifts[card.Id] = newLift;

            // 前进向上(-Y)，后退向下(+Y)
            rect = new Rect2(rect.Position - new Vector2(0f, newLift), rect.Size);

            // 叠加攻击突进与受击震颤动画偏移
            var fxOffset = InkCombatFx.GetOffset(card.Id);
            if (fxOffset != Vector2.Zero)
                rect = new Rect2(rect.Position + fxOffset, rect.Size);

            DrawCard(ci, model.Cards[i], rect, model.InCombat);

            // 行动角色头上显示一个缓慢变暗变亮的大钝角三角形指向角色
            if (model.InCombat && model.PendingActorId == card.Id)
            {
                var cardTopCenter = new Vector2(rect.GetCenter().X, rect.Position.Y);
                DrawTurnIndicator(ci, cardTopCenter, 1.0f);
            }
        }

        DrawPager(ci, model);
    }

    /// <summary>
    /// 在行动单位（我方角色或敌方单位）头顶绘制缓慢变暗变亮的大钝角三角形，尖角向下指向该角色。
    /// 底边宽 44px、高 11px，底角张角约 127 度（标准大钝角）。
    /// </summary>
    public static void DrawTurnIndicator(CanvasItem ci, Vector2 targetTip, float scale = 1.0f)
    {
        var halfW = 22f * scale;
        var h = 11f * scale;
        var tip = targetTip - new Vector2(0f, 6f * scale);
        var p1 = new Vector2(tip.X - halfW, tip.Y - h);
        var p2 = new Vector2(tip.X + halfW, tip.Y - h);

        // 缓慢呼吸变暗变亮动效：周期约 2 秒，亮度在 0.28 与 1.0 之间舒缓往复
        var pulse = 0.5f + 0.5f * Mathf.Sin(InkCombatRenderer.IndicatorTime * 3.14159f);
        var alpha = 0.28f + 0.72f * pulse;

        var col = new Color(InkStyle.Line, alpha);
        var poly = new[] { p1, p2, tip };

        // 铁律（主人定）：实心填充，全项目严禁出现空心图像！
        ci.DrawColoredPolygon(poly, col);
    }

    private static void DrawCard(CanvasItem ci, CardView view, Rect2 rect, bool inCombat = false)
    {
        // 战斗形态：增益/减益小图标挂在头像上方，角标数字为剩余轮数。
        if (view.Statuses is { Length: > 0 })
        {
            var iconX = rect.Position.X + 6f;
            foreach (var mark in view.Statuses)
            {
                var chip = new Rect2(iconX, rect.Position.Y - 19f, 34f, 18f);
                ci.DrawRect(chip, InkStyle.Bg);
                InkDraw.TextBounded(ci, new Rect2(chip.Position.X + 3f, chip.Position.Y, 19f, 18f),
                    mark.Name.Length > 0 ? mark.Name[..1] : "?", 12, 10,
                    mark.Buff ? InkStyle.Line : InkStyle.Dim, "lm");
                InkDraw.TextBounded(ci, new Rect2(chip.End.X - 14f, chip.Position.Y, 12f, 18f),
                    mark.Rounds.ToString(), 10, 9, InkStyle.Dim, "rm");
                iconX += 38f;
            }
        }

        // 位置未知（RoomId == -1）且不是玩家本人的，画成暗色。
        var present = view.Card.RoomId >= 0 || view.Card.IsPlayer;
        var color = present ? InkStyle.Line : InkStyle.Dim;

        var cardFill = inCombat
            ? (view.Selected ? new Color(InkStyle.Line, 0.12f) : (Color?)Colors.Transparent)
            : (view.Selected ? InkStyle.Bg.Lightened(0.07f) : null);
        InkFrame.Card(ci, rect, color, view.Selected, cardFill);

        // 名字先占好底部一条，头像在剩余区域里按统一尺寸落底。
        var nameBand = 34f;
        // 有体力条的卡（玩家）再让出一条给体力条。
        var meterBand = view.MaxStamina > 0 ? InkLayout.CardMeter(rect).Size.Y + 14f : 0f;
        var figureArea = new Rect2(
            rect.Position.X,
            rect.Position.Y + 8f,
            rect.Size.X,
            rect.Size.Y - nameBand - 12f - meterBand);

        // 角色头像与图形已完整删除（主人定）：不再调用 Figure 绘制任何图形

        // 主人定：头像布局锁死禁止改动；删除位x后缀
        var displayName = view.Card.Name;
        var nameSize = InkDraw.FitSize(displayName, rect.Size.X - 16f,
            NameFontMax, NameFontMin);
        InkDraw.Text(ci, new Vector2(rect.GetCenter().X, rect.End.Y - nameBand / 2f - 6f),
            displayName, nameSize, color, "cm");

        if (view.MaxStamina > 0)
            DrawStamina(ci, InkLayout.CardMeter(rect), view.Stamina, view.MaxStamina);
    }

    /// <summary>体力/血量条：血量增长右延绿条随后白条跟进，扣减暗红左延随后消除。</summary>
    private static void DrawStamina(CanvasItem ci, Rect2 rect, int stamina, int maxStamina)
    {
        var targetRatio = maxStamina <= 0
            ? 0f
            : Mathf.Clamp((float)stamina / maxStamina, 0f, 1f);

        InkDynamicMeter.Draw(ci, $"card_stamina_{rect.GetHashCode()}", rect, targetRatio, isHp: true);
    }

    /// <summary>
    /// 翻页钮：标题行右端一对紧凑箭头，只有多页时才出现；不再画页码。
    /// </summary>
    private static void DrawPager(CanvasItem ci, InkHubModel model)
    {
        if (model.CardPageCount <= 1)
            return;
        DrawPagerButton(ci, InkLayout.CharPagerPrev, toRight: false, enabled: model.CardPage > 0);
        DrawPagerButton(ci, InkLayout.CharPagerNext, toRight: true, enabled: model.CardPage < model.CardPageCount - 1);
    }

    /// <summary>
    /// 翻页钮：矮钮 + 实心三角形箭头。三角形是填充的（不是描边折角），
    /// 指向左/右分别由 toRight 决定。
    /// </summary>
    private static void DrawPagerButton(CanvasItem ci, Rect2 rect, bool toRight, bool enabled)
    {
        InkFrame.Button(ci, rect, "", enabled: enabled, centered: true);

        var c = enabled ? InkStyle.Line : InkStyle.Dim;
        var cx = rect.GetCenter().X;
        var cy = rect.GetCenter().Y;
        var dir = toRight ? 1f : -1f;
        // 半高 10、半宽 9：高 20 的实心三角收在 26 高的钮里，四周留匀称空隙。
        var halfH = InkLayout.CharPagerArrowHalf;
        var halfW = halfH * 0.9f;

        var tip = new Vector2(cx + dir * halfW, cy);
        var top = new Vector2(cx - dir * halfW, cy - halfH);
        var bottom = new Vector2(cx - dir * halfW, cy + halfH);
        ci.DrawColoredPolygon(new[] { tip, top, bottom }, c);
    }
}
