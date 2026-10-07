using System;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 泼墨消融层：战斗形态下，原位的地图网格、日志面板与下三面板边框标题
/// 画在这一层，被随机大小的泼散状黑色墨痕逐片盖没。据点本屏只画下沉后的净内容。
/// </summary>
public partial class InkCombatDissolve : Control
{
    private ShaderMaterial? _material;
    private float _progress;

    public InkHubModel? Model { get; set; }

    /// <summary>随层一起消融的额外内容（顶栏标题与右下入口钮）。</summary>
    public Action<CanvasItem>? ExtraDraw;

    /// <summary>消融进度 0=完整，1=化尽。</summary>
    public float Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            _material?.SetShaderParameter("progress", value);
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var shader = ResourceLoader.Load<Shader>("res://RimisekaiFrontend/src/Ink/ink_dissolve.gdshader");
        _material = new ShaderMaterial { Shader = shader };
        Material = _material;
    }

    public override void _Draw()
    {
        if (Model == null || _progress >= 1f)
            return;

        ExtraDraw?.Invoke(this);

        // 退出的上区：地图网格与日志合在同一个分区容器里，框带内容整体化掉
        //（与据点主界面同一套分区，不另画每块面板的外框）。
        InkFrame.Zone(this, InkLayout.UpperZone, InkLayout.UpperSplitX);
        InkMapRenderer.Draw(this, Model);
        InkLogRenderer.Draw(this, Model);

        // 下三面板整体下移（连框带内容），不在这层消融。
    }
}
