using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 线稿界面的调色板与字体。
/// 数值由主人定夺：当前为对照参考图逐轮目测调整中的版本
/// （禁止像素取样，只许肉眼对照迭代）。
/// </summary>
public static class InkStyle
{
    /// <summary>背景：深的板岩青，要看得见青味，不能死黑。</summary>
    public static readonly Color Bg = Color.FromHtml("#0B1D20");

    /// <summary>边框内部：比背景亮一阶的暗青灰。</summary>
    public static readonly Color Panel = Color.FromHtml("#132A2D");

    /// <summary>线／字：骨白，微冷但不偏青。</summary>
    public static readonly Color Line = Color.FromHtml("#E9EFEA");

    /// <summary>次级灰绿，降饱和。</summary>
    public static readonly Color Dim = Color.FromHtml("#84928E");

    /// <summary>内嵌区底色：比面板略深，做出下陷感。</summary>
    public static readonly Color Inset = Color.FromHtml("#0A1B1E");

    /// <summary>木框底色：漂白浮木那种灰绿白。</summary>
    public static readonly Color Wood = Color.FromHtml("#D2DAD0");

    /// <summary>木纹深线。</summary>
    public static readonly Color WoodDark = Color.FromHtml("#5C6B64");

    /// <summary>木纹亮线。</summary>
    public static readonly Color WoodLight = Color.FromHtml("#F0F5EC");

    /// <summary>悬停或选中时的浅填。</summary>
    public static readonly Color Hover = Color.FromHtml("#1A3134");

    /// <summary>定稿使用的字体。Godot 直接读系统字体文件，与 PIL 稿一致。</summary>
    public const string FontPath = "C:/Windows/Fonts/simsun.ttc";

    private static Font? _font;

    public static Font Font => _font ??= LoadFont();

    private static Font LoadFont()
    {
        var file = new FontFile();
        if (file.LoadDynamicFont(FontPath) == Error.Ok)
            return file;

        GD.PushWarning($"Rimisekai: 无法加载 {FontPath}，回退到系统字体。");
        return new SystemFont { FontNames = new[] { "SimSun", "宋体", "Microsoft YaHei" } };
    }
}
