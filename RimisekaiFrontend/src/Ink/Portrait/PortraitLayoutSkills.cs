using Godot;

namespace Rimisekai.Portrait;

/// <summary>技能段里的技能网：五列格位，一格 200 宽、190 高，嵌在角色页滚动内容里（不再单开推入页）。</summary>
public static partial class PortraitLayout
{
    public const float SkillCellWidth = 200f;
    public const float SkillCellHeight = 190f;
    public const float SkillNodeRadius = 36f;
    public const float SkillWebPadY = 40f;

    public static Rect2 SkillWebRect(float y, int rows) =>
        new(Pad, y, FullWidth, rows * SkillCellHeight + SkillWebPadY * 2f);

    /// <summary>某格的章心：格子上部，名字写在章下。</summary>
    public static Vector2 SkillNodeCenter(Rect2 web, int row, float column) => new(
        web.Position.X + (web.Size.X - SkillCellWidth * PortraitSkillChart.Columns) / 2f + (column + 0.5f) * SkillCellWidth,
        web.Position.Y + SkillWebPadY + row * SkillCellHeight + 58f);

    /// <summary>某格的命中块：章与名字一起，180×160。</summary>
    public static Rect2 SkillNodeHit(Vector2 center) => new(center.X - 90f, center.Y - 50f, 180f, 160f);
}
