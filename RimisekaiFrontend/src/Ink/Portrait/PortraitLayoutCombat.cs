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
    public static readonly Rect2 CombatBackground = new(0, 0, CanvasWidth, 1310);
    public static readonly Rect2 CombatBoss = new(40, 32, 1000, 212);
    public static readonly Rect2 CombatRound = new(40, 264, 1000, 72);
    /// <summary>速度跑条轨道：左上角竖轨（无外框），中线分左右两列——左列我方、右列敌方，各自越靠上越先出手。</summary>
    public static Rect2 CombatSpeedTrack => new(52f, 260f, 96f, 800f);
    /// <summary>右上角设置齿轮：命中块 118px，完整收在顶部 boss 区条带内（boss 文字居中，右上为空角）。</summary>
    public static Rect2 CombatGearHit => new(CanvasWidth - 162f, 44f, 118f, 118f);
    /// <summary>操作面板 2×2 四钮：攻击 / 技能 / 防御 / 逃跑，无滑条。</summary>
    public static Rect2 CombatButton(int slot)
    {
        var inner = CombatActions.Grow(-24f);
        var width = (inner.Size.X - 16f) / 2f;
        var height = (inner.Size.Y - 16f) / 2f;
        return new Rect2(inner.Position.X + slot % 2 * (width + 16f),
            inner.Position.Y + slot / 2 * (height + 16f), width, height);
    }
    /// <summary>首领血条下方的行动点行：实心菱 18px、间距 30px，水平居中。</summary>
    public static Vector2 BossPipCenter(int index, int count) =>
        new(CombatBoss.GetCenter().X + (index - (count - 1) / 2f) * 30f, CombatBoss.Position.Y + 202f);
    public static readonly Rect2 CombatField = new(40, 360, 1000, 890);
    public static readonly Rect2 CombatActions = new(40, 1340, 1000, 392);
    public static readonly Rect2 CombatAvatars = new(40, 1744, 1000, 556);
    public static Rect2 BossName => new(CombatBoss.Position, new Vector2(CombatBoss.Size.X, 68));
    public static Rect2 BossHp => new(CombatBoss.Position.X, CombatBoss.Position.Y + 74, CombatBoss.Size.X, 58);
    public static Rect2 BossMeter => new(CombatBoss.Position.X, CombatBoss.Position.Y + 152, CombatBoss.Size.X, 30);

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

    public static Rect2 AllyCard(int index, int threatTier)
    {
        var width = (CombatAvatars.Size.X - 3f * 16f) / 4f;
        return new Rect2(CombatAvatars.Position.X + index * (width + 16f),
            1856f - (Math.Clamp(threatTier, 0, 5) - 3) * 12f, width, 360f);
    }

    /// <summary>头像占满卡内上部（buff 图标在卡外上方，不占卡内空间）。</summary>
    public static Rect2 AllyImage(Rect2 card) => new(card.Position + new Vector2(16, 16),
        new Vector2(card.Size.X - 32, card.Size.Y - 152));
    public static Rect2 AllyName(Rect2 card) => new(card.Position.X + 12, card.End.Y - 128, card.Size.X - 24, 60);
    public static Rect2 AllyMeter(Rect2 card) => new(card.Position.X + 16, card.End.Y - 46, card.Size.X - 32, 16);
    public static Rect2 EnemyName(Rect2 card) => new(card.Position + new Vector2(4, 6), new Vector2(card.Size.X - 8, 32));
    public static Rect2 EnemyMeter(Rect2 card) => new(card.Position.X + 6, card.End.Y - 16, card.Size.X - 12, 10);
    public static Rect2 EnemyImage(Rect2 card) => new(card.Position.X + 4, card.Position.Y + 42,
        card.Size.X - 8, Math.Max(0, card.Size.Y - 100));
    /// <summary>buff 图标条：悬在角色卡上方（卡外），绝不落入卡内头像区。</summary>
    public static Rect2 StatusIcon(Rect2 card, int index) => new(card.Position.X + 16 + index * 52,
        card.Position.Y - 56, 44, 44);

    public static IReadOnlyList<PortraitRegion> CombatRegions => new[]
    {
        new PortraitRegion("boss", CombatBoss), new PortraitRegion("round", CombatRound),
        new PortraitRegion("enemies", CombatField), new PortraitRegion("actions", CombatActions),
        new PortraitRegion("avatars", CombatAvatars),
    };
}
