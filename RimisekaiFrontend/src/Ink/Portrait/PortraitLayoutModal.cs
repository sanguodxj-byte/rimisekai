using Godot;

namespace Rimisekai.Portrait;

public static partial class PortraitLayout
{
    public const float ModalWidth = 800f;
    public const float ModalLineHeight = 80f;
    public const float ModalGap = 24f;
    public const float ModalArrowBand = 88f;
    public static Rect2 ModalBounds(float bodyHeight, float controlsHeight, bool title)
    {
        var heading = title ? TitleBand : 0f;
        var height = Mathf.Min(CanvasHeight - Pad * 2f,
            Mathf.Max(480f, heading + bodyHeight + controlsHeight + Pad * 2f + ModalArrowBand));
        return new Rect2((CanvasWidth - ModalWidth) / 2f, (CanvasHeight - height) / 2f, ModalWidth, height);
    }
    public static Rect2 ModalTitle(Rect2 panel) => new(panel.Position.X + Pad, panel.Position.Y + Pad,
        panel.Size.X - Pad * 2f, TitleBand - 24f);
    public static Vector2[] ModalArrow(Rect2 panel)
    {
        var tip = new Vector2(panel.GetCenter().X, panel.End.Y - 26f);
        return new[] { tip + new Vector2(-20f, -24f), tip + new Vector2(20f, -24f), tip };
    }
}
