using System.IO;
using Godot;
using Rimisekai.Character;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Tools;

public partial class RealtimeCaptureProbe : Node
{
    private int _frame;
    private InkHubScreen? _screen;
    private HubSession? _hub;

    public override void _Ready()
    {
        var pack = new ContentPack();
        _hub = InkWorldBootstrap.OpenHub(pack, out _);
        _screen = new InkHubScreen();
        _screen.Size = new Vector2(InkLayout.CanvasWidth, InkLayout.CanvasHeight);
        _screen.Bind(new InkViewModel(_hub, pack));
        AddChild(_screen);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame == 3)
        {
            _screen!.DebugOpenPage("skills");
        }
        else if (_frame == 8)
        {
            SaveView("技能盘_全景视角.png");

            // 切换到聚焦扇区 1 (灵巧) 放大
            _screen!.DebugOpenPage("skills", sector: 1);
        }
        else if (_frame == 16)
        {
            SaveView("技能盘_扇区放大.png");
            GD.Print("[realtime-cap] 截图完成，准备退出。");
            GetTree().Quit();
        }
    }

    private void SaveView(string fileName)
    {
        var img = GetViewport().GetTexture().GetImage();
        if (img != null)
        {
            var p = ProjectSettings.GlobalizePath($"res://{fileName}");
            img.SavePng(p);
            GD.Print($"[realtime-cap] 成功保存: {p}");
        }
        else
        {
            GD.PrintErr($"[realtime-cap] 无法获取 Viewport 图像: {fileName}");
        }
    }
}
