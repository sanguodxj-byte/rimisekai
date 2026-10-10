using Godot;

namespace Rimisekai.Tools;

/// <summary>
/// 特效 shader 预览：黑底上并排渲染两枚持续特效，连拍两帧后退出。
/// 运行：godot --path . res://tools/ShaderPreview.tscn
/// </summary>
public partial class ShaderPreview : Node
{
    private const int Width = 1800;
    private const int Height = 640;

    private SubViewport _sub = null!;
    private int _shots;
    private double _elapsed;

    public override void _Ready()
    {
        _sub = new SubViewport
        {
            Size = new Vector2I(Width, Height),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_sub);

        var backdrop = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f), Size = new Vector2(Width, Height) };
        _sub.AddChild(backdrop);

        AddEffect("res://RimisekaiFrontend/shaders/rareitem_blowfog.gdshader",
            new Rect2(40, 70, 500, 500), ("direction", new Vector2(1, -0.35f)));

        PlacePortrait("res://assets/avatars/monster/monster_basilisk.png",
            new Rect2(600, 70, 500, 500), new Vector2(0.42f, 0.51f), new Vector2(0.55f, 0.51f));
        PlacePortrait("res://assets/avatars/monster/monster_banshee.png",
            new Rect2(1160, 70, 500, 500), new Vector2(0.51f, 0.08f), new Vector2(0.56f, 0.09f));
        GD.Print("SHADER_PREVIEW ready");
    }

    private void PlacePortrait(string texturePath, Rect2 rect, Vector2 eyeA, Vector2 eyeB)
    {
        var portrait = new TextureRect
        {
            Texture = GD.Load<Texture2D>(texturePath),
            Position = rect.Position,
            Size = rect.Size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _sub.AddChild(portrait);

        var fx = new ColorRect { Position = rect.Position, Size = rect.Size, Color = Colors.White };
        var mat = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://RimisekaiFrontend/shaders/monster_redeye_updraught.gdshader"),
        };
        mat.SetShaderParameter("eye_a", eyeA);
        mat.SetShaderParameter("eye_b", eyeB);
        mat.SetShaderParameter("eye_count", 2);
        fx.Material = mat;
        _sub.AddChild(fx);
    }

    private void AddEffect(string shaderPath, Rect2 rect, (string Name, Vector2 Value)? extra)
    {
        var rect2 = new ColorRect { Position = rect.Position, Size = rect.Size, Color = Colors.White };
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>(shaderPath) };
        if (extra is { } e)
            mat.SetShaderParameter(e.Name, e.Value);
        rect2.Material = mat;
        _sub.AddChild(rect2);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_shots == 0 && _elapsed > 0.4)
            Snap("fx_preview_a");
        else if (_shots == 1 && _elapsed > 1.1)
            Snap("fx_preview_b");
        else if (_shots >= 2)
            GetTree().Quit();
    }

    private void Snap(string name)
    {
        var img = _sub.GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath($"user://{name}.png");
        img.SavePng(path);
        GD.Print($"SHADER_PREVIEW {path}");
        _shots++;
    }
}
