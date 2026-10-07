using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 线稿界面的调色板与字体。
/// 数值由主人定夺：当前为对照参考图逐轮目测调整中的版本
/// （禁止像素取样，只许肉眼对照迭代）。
/// </summary>
public static class InkStyle
{
    /// <summary>
    /// 全局硬性最小字号约定（2026-10-02 主人定）：
    /// 目前角色领地等标题的字号（26）为项目最小字号。比这小的都加大到该字号。
    /// </summary>
    public const int MinFontSize = 26;

    /// <summary>背景：纯黑，与插画、标题画面的黑纸底一致。</summary>
    public static readonly Color Bg = Color.FromHtml("#000000");

    /// <summary>边框内部：比背景亮一阶的深灰，面板内腔。</summary>
    public static readonly Color Panel = Color.FromHtml("#111111");

    /// <summary>线／字：骨白，微冷但不偏青。</summary>
    public static readonly Color Line = Color.FromHtml("#E9EFEA");

    /// <summary>次级银灰，中性无彩色相。</summary>
    public static readonly Color Dim = Color.FromHtml("#8E928F");

    /// <summary>内嵌区底色：纯黑，做出下陷感。</summary>
    public static readonly Color Inset = Color.FromHtml("#000000");

    /// <summary>木框底色：漂白银白，中性化。</summary>
    public static readonly Color Wood = Color.FromHtml("#CFD2CC");

    /// <summary>木纹深线，中性灰。</summary>
    public static readonly Color WoodDark = Color.FromHtml("#5B5E5B");

    /// <summary>木纹亮线，近骨白。</summary>
    public static readonly Color WoodLight = Color.FromHtml("#EFF1EC");

    /// <summary>悬停或选中时的浅填，中性深灰。</summary>
    public static readonly Color Hover = Color.FromHtml("#1A1A1A");

    /// <summary>定稿使用的字体。优先读 Windows 系统字体或项目内置字体。</summary>
    public const string WindowsFontPath = "C:/Windows/Fonts/simsun.ttc";
    public const string ProjectFontPath = "res://assets/simsun.ttc";

    private static Font? _font;

    public static Font Font => _font ??= LoadFont();

    private static Font LoadFont()
    {
        var file = new FontFile();
        if (System.IO.File.Exists(WindowsFontPath) && file.LoadDynamicFont(WindowsFontPath) == Error.Ok)
            return file;

        if (Godot.FileAccess.FileExists(ProjectFontPath) && file.LoadDynamicFont(ProjectFontPath) == Error.Ok)
            return file;

        GD.PushWarning($"Rimisekai: 无法加载 {WindowsFontPath} 或 {ProjectFontPath}，回退到系统字体。");
        return new SystemFont
        {
            FontNames = new[]
            {
                "SimSun", "宋体", "Microsoft YaHei", "微软雅黑",
                "Noto Sans CJK SC", "Source Han Sans CN", "Droid Sans Fallback", "sans-serif"
            }
        };
    }
}
