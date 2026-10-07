using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 据点角色头像带（主人 2026-10-05 定）：删掉地点插画带后，地图网格上移，
/// 网格与设施栏之间空出的 617px 常显角色头像。
/// 每张头像卡＝方形头像＋下方名牌＋体力细线，最多四张（与角色面板同口径）。
/// </summary>
public static partial class PortraitLayout
{
    public const int AvatarSlots = 4;
    public const float AvatarGap = 16f;
    public const float AvatarCardHeight = 420f;

    /// <summary>卡宽：四张等分 1080 减去两侧留白与三道间隙。</summary>
    public static float AvatarCardWidth =>
        (CanvasWidth - Pad * 2f - AvatarGap * (AvatarSlots - 1)) / AvatarSlots;

    /// <summary>头像带在整条带里垂直居中。引用 <see cref="AvatarArea"/>，故走表达式体属性（跨文件静态字段初始化顺序不保证）。</summary>
    public static float AvatarTop =>
        AvatarArea.Position.Y + (AvatarArea.Size.Y - AvatarCardHeight) / 2f;

    public static Rect2 AvatarCard(int index) => new(
        Pad + index * (AvatarCardWidth + AvatarGap), AvatarTop, AvatarCardWidth, AvatarCardHeight);

    /// <summary>方形头像区：贴卡内上沿，宽高相等。</summary>
    public static Rect2 AvatarImage(Rect2 card) => new(
        card.Position.X + 14f, card.Position.Y + 14f, card.Size.X - 28f, card.Size.X - 28f);

    public static Rect2 AvatarName(Rect2 card) => new(
        card.Position.X + 10f, card.End.Y - 150f, card.Size.X - 20f, 96f);

    public static Rect2 AvatarMeter(Rect2 card) => new(
        card.Position.X + 24f, card.End.Y - 36f, card.Size.X - 48f, 8f);
}
