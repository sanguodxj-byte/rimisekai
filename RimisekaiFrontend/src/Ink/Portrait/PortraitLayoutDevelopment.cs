using Godot;

namespace Rimisekai.Portrait;

/// <summary>建造推入页：缺角双线框里的 5×5 开发网格＋下方常驻目录面板（分段 操作 / 设施 / 待安装）。</summary>
public static partial class PortraitLayout
{
    public const float DevCell = 196f;
    public static Vector2 DevOrigin => new((CanvasWidth - DevCell * GridCols) / 2f, PageTop.End.Y + 64f);
    public static Rect2 DevelopmentGrid => new(DevOrigin, new Vector2(DevCell * GridCols, DevCell * GridRows));
    public static Rect2 DevelopmentFrame => DevelopmentGrid.Grow(24f);
    public static Rect2 DevelopmentCell(int x, int y) => new(DevOrigin.X + x * DevCell, DevOrigin.Y + y * DevCell, DevCell, DevCell);

    public static Rect2 DevelopmentPanel => new(0, DevelopmentFrame.End.Y + 36f, CanvasWidth, CanvasHeight - DevelopmentFrame.End.Y - 36f);
    public static Rect2 DevelopmentSegment => new(Pad, DevelopmentPanel.Position.Y + 60f, FullWidth, TouchMin);
    public static float DevelopmentListTop => DevelopmentSegment.End.Y + 30f;
    public static int DevelopmentRows => (int)((CanvasHeight - 40f - DevelopmentListTop) / SheetRowStep);
    public static Rect2 DevelopmentRow(int i) => new(Pad, DevelopmentListTop + i * SheetRowStep, FullWidth, TouchMin);
}
