using Godot;
using Rimisekai.Clock;

namespace Rimisekai.Ink;

/// <summary>
/// 天气特效层：盖在地图面板上的一块 ColorRect，shader 只在室外房间格内
/// 画雨丝、风线与落雪（银白墨线、低透明度，遵守单色线稿约束）。
/// 只在纯地图视图出现——对话弹层、全屏页、开发模式、存储页、
/// 场景插画遮盖或世界大地图层时都隐藏。音效后续从 Weather 模式接入，本层不管声音。
/// </summary>
public partial class InkWeatherFx : ColorRect
{
    private static readonly string ShaderPath = "res://RimisekaiFrontend/shaders/InkWeather.gdshader";

    private ShaderMaterial _mat = null!;
    private float _time;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = InkLayout.MapPanel.Position;
        Size = InkLayout.MapPanel.Size;
        Color = Colors.White;

        _mat = new ShaderMaterial();
        Material = _mat;
        _mat.Shader = GD.Load<Shader>(ShaderPath);
        _mat.SetShaderParameter("panel_size", InkLayout.MapPanel.Size);
        _mat.SetShaderParameter("grid_origin", InkLayout.MapContent.Position - InkLayout.MapPanel.Position);
        _mat.SetShaderParameter("cell_size", InkLayout.Cell(0, 0).Size);
        Visible = false;
    }

    /// <summary>
    /// 随模型刷新：模式取天气，掩码取各格房间的"室外"标签。
    /// 掩码里只认已开拓的房间，未开拓格、室内格与被插画盖住的领地层
    /// （此时网格不建，掩码自然全空）都全透明。
    /// 弹层、全屏页、开发模式、存储页这些整块替换版面的视图直接隐藏。
    /// </summary>
    public void UpdateFrom(InkHubModel model, Weather weather)
    {
        var mode = ModeOf(weather);
        var shown = !model.StorageOpen
            && !model.DevMode
            && model.OpenPage == InkPage.None
            && model.Overlay == null;
        if (mode < 0 || !shown)
        {
            Visible = false;
            return;
        }

        var mask = new float[InkLayout.GridCols * InkLayout.GridRows];
        for (var row = 0; row < InkLayout.GridRows; row++)
        {
            for (var col = 0; col < InkLayout.GridCols; col++)
            {
                var room = model.MapAt(col, row);
                // 世界大地图整屏都是露天：全场落雨雪；领地/POI 层只认"室外"格。
                mask[row * InkLayout.GridCols + col] =
                    model.WorldLayer
                        ? (room is { Open: true } ? 1f : 0f)
                        : (room is { Open: true } && room.HasTag("室外") ? 1f : 0f);
            }
        }

        _mat.SetShaderParameter("mode", mode);
        _mat.SetShaderParameter("mask", mask);
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _time += (float)delta;
        _mat.SetShaderParameter("time_sec", _time);
    }

    /// <summary>天气到 shader 模式的映射。晴与多云无特效，返回 -1 表示隐藏。</summary>
    private static int ModeOf(Weather weather) => weather switch
    {
        Weather.Rain => 0,
        Weather.HeavyRain => 1,
        Weather.Thunder => 2,
        Weather.Wind => 3,
        Weather.Snow => 4,
        Weather.HeavySnow => 5,
        Weather.Blizzard => 6,
        _ => -1,
    };
}
