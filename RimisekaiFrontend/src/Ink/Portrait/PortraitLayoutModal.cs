using Godot;

namespace Rimisekai.Portrait;

/// <summary>弹窗几何：居中缺角双线框，宽 920；标题带 / 正文 / 输入框 / 药丸钮自上而下居中排。</summary>
public static partial class PortraitLayout
{
    public const float ModalWidth = 920f;
    public const float ModalPad = 70f;
    public const float ModalLineHeight = 76f;
    public const float ModalGap = 24f;
    public const float ModalArrowBand = 88f;
    public const float ModalButtonHeight = 128f;

    public static Rect2 ModalBounds(float height)
    {
        var h = Mathf.Clamp(height, 480f, CanvasHeight - SafeTop - 120f);
        return new Rect2((CanvasWidth - ModalWidth) / 2f, SafeTop + (CanvasHeight - SafeTop - h) / 2f, ModalWidth, h);
    }

    public static Vector2[] ModalArrow(Rect2 panel)
    {
        var tip = new Vector2(panel.GetCenter().X, panel.End.Y - 30f);
        return new[] { tip + new Vector2(-20f, -24f), tip + new Vector2(20f, -24f), tip };
    }
}
