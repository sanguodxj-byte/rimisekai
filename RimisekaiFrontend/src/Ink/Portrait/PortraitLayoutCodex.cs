using Godot;

namespace Rimisekai.Portrait;

/// <summary>
/// 怪物图鉴：入口在系统页「设置」段主音量卡下方（2026-10-10 主人定：移出 HUD，原位压着时刻），
/// 列表沿用全屏推入页区域。
/// </summary>
public static partial class PortraitLayout
{
    /// <summary>
    /// 设置段里的图鉴入口卡：主音量卡（236 高）连同卡下说明行（卡底 +120 处、34 号字）之后再隔 60。
    /// <paramref name="top"/> 为设置内容首行 y。
    /// </summary>
    public static Rect2 SettingsCodex(float top) => new(Pad, top - 6f + 236f + 120f + 17f + 60f, FullWidth, 200f);
    public static Rect2 CodexViewport => new(0f, PageBody.Position.Y + 24f, CanvasWidth,
        CanvasHeight - PageBody.Position.Y - 24f);
    public const float CodexRowHeight = 174f;
    public const float CodexRowGap = 18f;
    public const float CodexRowStep = CodexRowHeight + CodexRowGap;
}
