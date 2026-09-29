using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 复古转场盖层：换屏前抓一幅旧画面，换屏后由 shader 把旧画面与实时新画面
/// 混合过渡。盖层是独立 CanvasLayer（layer 128），压在界面之上，
/// 空闲时不可见、不吃输入；特效只动明度，遵守单色线稿的调色板约束。
///
/// 四个变体（shader 在 RimisekaiFrontend/shaders/）：
/// FilmBurn 胶片烧灼 / Crt CRT 换台 / PageTurn 书页翻页 / InkWash 墨迹扩散。
/// </summary>
public partial class InkTransition : CanvasLayer
{
    public enum Variant
    {
        FilmBurn,
        Crt,
        PageTurn,
        InkWash,
    }

    private static readonly string[] ShaderPaths =
    {
        "res://RimisekaiFrontend/shaders/RetroFilmBurn.gdshader",
        "res://RimisekaiFrontend/shaders/RetroCrt.gdshader",
        "res://RimisekaiFrontend/shaders/RetroPageTurn.gdshader",
        "res://RimisekaiFrontend/shaders/RetroInkWash.gdshader",
    };

    /// <summary>当前使用的转场变体。标题→据点走这一支，可整体替换。</summary>
    public static Variant Current { get; set; } = Variant.FilmBurn;

    private ColorRect _rect = null!;
    private ShaderMaterial _mat = null!;
    private ImageTexture? _old;
    private float _elapsed = -1f;
    private float _duration = 1f;

    public override void _Ready()
    {
        Layer = 128;

        _rect = new ColorRect
        {
            Name = "TransitionRect",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = Colors.White, // 纯白底：shader 全屏覆盖，白色避免边缘漏色
        };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        _mat = new ShaderMaterial();
        _rect.Material = _mat;
        _rect.Visible = false;
        AddChild(_rect);
    }

    /// <summary>
    /// 抓当前帧为旧画面。必须在换屏之前调用——
    /// 此时视口里还是上一屏的内容。
    /// </summary>
    public void Capture()
    {
        var img = GetViewport().GetTexture().GetImage();
        _old = ImageTexture.CreateFromImage(img);
    }

    /// <summary>播放转场。没抓过旧画面时静默跳过（不影响换屏本身）。</summary>
    public void Play(Variant variant, float duration)
    {
        if (_old == null)
            return;

        var shader = GD.Load<Shader>(ShaderPaths[(int)variant]);
        if (shader == null)
        {
            GD.PushWarning($"Rimisekai: 转场 shader 缺失 {ShaderPaths[(int)variant]}");
            return;
        }

        _mat.Shader = shader;
        _mat.SetShaderParameter("old_screen", _old);
        _mat.SetShaderParameter("progress", 0f);
        _mat.SetShaderParameter("time_sec", 0f);
        _rect.Visible = true;
        _duration = Mathf.Max(0.05f, duration);
        _elapsed = 0f;
    }

    public override void _Process(double delta)
    {
        if (_elapsed < 0f)
            return;

        _elapsed += (float)delta;
        var p = Mathf.Clamp(_elapsed / _duration, 0f, 1f);
        _mat.SetShaderParameter("progress", p);
        _mat.SetShaderParameter("time_sec", _elapsed);

        if (p < 1f)
            return;

        _elapsed = -1f;
        _rect.Visible = false;
        _old = null;
    }
}
