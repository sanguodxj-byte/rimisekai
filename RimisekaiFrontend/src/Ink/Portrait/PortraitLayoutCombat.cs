using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Combat;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

public readonly record struct PortraitRegion(string Name, Rect2 Rect);

/// <summary>战斗几何同时供绘制、点选与运行时隔离检查使用。</summary>
public static partial class PortraitLayout
{
    // 2026-10-07 重设计：顶栏（战场名 / 回合 / 设置）→ 行动顺序条 → 首领血条 → 透视敌阵 → 行动面板 → 我方卡一排。
    // 2026-10-08 主人改：行动面板夹在敌阵与我方之间，我方卡贴屏底；敌阵向下吃满余下高度，屏底不留空。
    // 同日再改：回合数居中；行动顺序条撤掉，改成左上的竖向速度跑条（只占原顺序条与首领条左段，战场网格不动）（沿用历史版：顶端出手线，左列我方右列敌方）。
    public static Rect2 CombatBackground => new(0, 0, CanvasWidth, CombatField.End.Y + 20f);
    public static Rect2 CombatTop => new(0, SafeTop, CanvasWidth, 130f);
    /// <summary>首领条：紧贴顶栏下方（主人定 2026-10-08：不留无用空白），宽 1000，不得被侵占。</summary>
    public static Rect2 CombatBoss => new(40, CombatTop.End.Y + 10f, 1000, 90f);
    public static Rect2 CombatRound => CombatTop;
    /// <summary>右上设置齿轮：命中块 118px，收在顶栏内。</summary>
    public static Rect2 CombatGearHit => new(CanvasWidth - Pad - 118f, SafeTop + 6f, 118f, 118f);
    /// <summary>行动面板高：标题带 120 ＋ 两行钮（200＋20＋200）＋底边 40。</summary>
    public const float CombatActionsHeight = 580f;
    public const float CombatAvatarsHeight = 300f;
    public static Rect2 CombatAvatars => new(40, CanvasHeight - Pad - CombatAvatarsHeight, 1000, CombatAvatarsHeight);
    public static Rect2 CombatActions => new(0, CombatAvatars.Position.Y - 30f - CombatActionsHeight, CanvasWidth, CombatActionsHeight);
    /// <summary>
    /// 竖向速度跑条：嵌在战场网格左上角（属于 enemies 区内的绘制，不单开容器、不报独立 region），无命中块。
    /// 主人定（2026-10-08）：跑条嵌入战场内左上；顶栏、首领条恢复原位原宽。
    /// 常规敌阵（含 3×3 首领）左上是透视网格上方的空地；4×4 首领格吃满战场时跑条直接压在首领画像左上（不加底框）。
    /// 2026-10-08 主人定：各区互不侵占，跑条不得挤占战场——敌阵网格几何保持原样（40, 380, 1000, …）。
    /// </summary>
    public const float CombatTrackWidth = 220f;
    /// <summary>敌阵上缘：与原版同为 SafeTop+380，跑条改动不得移动它。</summary>
    private static float FieldTop => SafeTop + 380f;
    public static Rect2 CombatTrack => new(CombatField.Position.X + 20f, CombatBoss.End.Y + 20f, CombatTrackWidth, 460f);
    public static Rect2 CombatField => new(40, FieldTop, 1000, CombatActions.Position.Y - 30f - FieldTop);
    /// <summary>
    /// 战场容器（主人定 2026-10-08）：从首领名称行起到行动面板上缘，首领条＋跑条＋敌阵网格同属一个容器（region 仍叫 enemies）。
    /// 网格几何 CombatField 与首领条位置不变；顶栏（「地名」/ 回合 / 设置）是另一个容器。
    /// </summary>
    public static Rect2 CombatArena => new(40, CombatBoss.Position.Y, 1000, CombatField.End.Y - CombatBoss.Position.Y);
    /// <summary>行动面板 2×2 四钮：攻击 / 技能 / 道具 / 逃跑。</summary>
    public static Rect2 CombatButton(int slot)
    {
        var width = (CanvasWidth - 100f) / 2f;
        return new Rect2(40f + slot % 2 * (width + 20f), CombatActions.Position.Y + (CombatActions.Size.Y - 420f) / 2f + slot / 2 * 220f, width, 200f);
    }
    /// <summary>首领血条右侧的行动点行：实心菱 18px、间距 30px。</summary>
    public static Vector2 BossPipCenter(int index, int count) =>
        new(CombatBoss.End.X - 20f - (count - 1 - index) * 30f, CombatBoss.Position.Y + 64f);
    public static Rect2 BossName => new(CombatBoss.Position.X, CombatBoss.Position.Y, 420f, 52f);
    public static Rect2 BossHp => new(CombatBoss.Position.X + 440f, CombatBoss.Position.Y, 420f, 52f);
    public static Rect2 BossMeter => new(CombatBoss.Position.X, CombatBoss.Position.Y + 58f, CombatBoss.Size.X - 160f, 14f);

    // 纵深档位放缓：前后排卡片尺寸差距减小（最远排内宽仍 148px，26px 字号完整可显）。
    /// <summary>3D DRPG 纵深投影深度分档（从近到远）。</summary>
    private static readonly float[] CombatZDepths = { 1.0f, 1.15f, 1.32f, 1.52f, 1.75f };

    /// <summary>灭点：上方面板水平中心、战斗区顶缘下方 90px——网格纵线全部指向它（单点透视）。</summary>
    public static Vector2 CombatVanish => new(CombatField.GetCenter().X, CombatField.Position.Y + 90f);
    public static float CombatDepthScale(int depth) => 1f / CombatZDepths[depth];

    public static Vector2 CombatGridPoint(float column, int depth)
    {
        var s = CombatDepthScale(depth);
        var vp = CombatVanish;
        return new Vector2(
            vp.X + (column - 2f) * CombatField.Size.X / 4f * s,
            vp.Y + (CombatField.End.Y - vp.Y) * s);
    }

    public static Vector2[] CombatColumnPolygon(int column) => new[]
    {
        CombatGridPoint(column - 1, 0), CombatGridPoint(column, 0),
        CombatGridPoint(column, 4), CombatGridPoint(column - 1, 4),
    };

    public static Rect2 EnemyCard(Combatant unit)
    {
        var depth = 4 - Math.Clamp(unit.ThreatTier, 1, 4);
        var left = Math.Clamp(unit.Column, 1, 4) - 1;
        var size = Math.Clamp(unit.Size, 1, 4);
        var a = CombatGridPoint(left, depth);
        var b = CombatGridPoint(left + size, depth);
        // 正方形卡片：边长 = 占列跨度宽，前缘对齐、向上伸展；顶到战区上缘则缩边，
        // 缩边后在本方占列跨度内水平居中。跨格照旧按 Size 占格，卡片只是以正方形呈现。
        var span = b.X - a.X - 8f;
        var side = MathF.Min(span, a.Y - 8f - CombatField.Position.Y);
        return new Rect2(a.X + 4f + (span - side) / 2f, a.Y - side, side, side);
    }

    /// <summary>
    /// 远排卡在真透视下画面照实缩小，但触控热区居中放大到 118px 下限；
    /// 靠近画布边缘时整体内推，始终收在战斗区内。
    /// </summary>
    public static Rect2 CombatTouchRect(Rect2 card)
    {
        var size = new Vector2(MathF.Max(card.Size.X, TouchMin), MathF.Max(card.Size.Y, TouchMin));
        var x = Mathf.Clamp(card.GetCenter().X - size.X / 2f, CombatField.Position.X, CombatField.End.X - size.X);
        var y = Mathf.Clamp(card.GetCenter().Y - size.Y / 2f, CombatField.Position.Y, CombatField.End.Y - size.Y);
        return new Rect2(x, y, size.X, size.Y);
    }

    public static Rect2 EnemyColumnCard(Rect2 card, int offset, int size) =>
        new(card.Position.X + offset * card.Size.X / size, card.Position.Y, card.Size.X / size, card.Size.Y);

    public static Rect2 AllyCard(int index)
    {
        var width = (CombatAvatars.Size.X - 3f * 16f) / 4f;
        return new Rect2(CombatAvatars.Position.X + index * (width + 16f), CombatAvatars.Position.Y, width, CombatAvatars.Size.Y);
    }

    public static Vector2 AllyAvatar(Rect2 card) => new(card.GetCenter().X, card.Position.Y + 72f);
    public static Rect2 AllyName(Rect2 card) => new(card.Position.X + 12, card.Position.Y + 132f, card.Size.X - 24, 56);
    public static Rect2 AllyMeter(Rect2 card) => new(card.Position.X + 24, card.Position.Y + 202f, card.Size.X - 48, 14);
    public static Rect2 EnemyName(Rect2 card) => new(card.Position + new Vector2(4, 6), new Vector2(card.Size.X - 8, 32));
    public static Rect2 EnemyMeter(Rect2 card) => new(card.Position.X + 6, card.End.Y - 16, card.Size.X - 12, 10);
    public static Rect2 EnemyImage(Rect2 card) => new(card.Position.X + 4, card.Position.Y + 42,
        card.Size.X - 8, Math.Max(0, card.Size.Y - 100));
    /// <summary>buff 图标条：悬在角色卡上方（卡外），绝不落入卡内头像区。</summary>
    public static Rect2 StatusIcon(Rect2 card, int index) => new(card.Position.X + 20 + index * 52,
        card.Position.Y + 234f, 44, 44);

    public static IReadOnlyList<PortraitRegion> CombatRegions => new[]
    {
        new PortraitRegion("top", CombatTop),
        new PortraitRegion("enemies", CombatArena), new PortraitRegion("actions", CombatActions),
        new PortraitRegion("avatars", CombatAvatars),
    };
}
