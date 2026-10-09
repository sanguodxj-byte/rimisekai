using Godot;

namespace Rimisekai.Portrait;

/// <summary>图鉴入口与列表视图坐标，沿用全屏推入页区域，不移动现有 HUD 或根页签。</summary>
public static partial class PortraitLayout
{
    public static Rect2 HudCodex => new(HudSystem.Position.X - TouchMin - 14f, HudLine2 - TouchMin / 2f, TouchMin, TouchMin);
    public static Rect2 CodexSegments => new(Pad, PageBody.Position.Y + 24f, FullWidth, TouchMin);
    public static Rect2 CodexViewport => new(0f, CodexSegments.End.Y + 24f, CanvasWidth,
        CanvasHeight - CodexSegments.End.Y - 24f);
    public const float CodexRowHeight = 174f;
    public const float CodexRowGap = 18f;
    public const float CodexRowStep = CodexRowHeight + CodexRowGap;
}
