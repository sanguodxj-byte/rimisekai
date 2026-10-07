using System;
using System.IO;
using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 按压反馈核对探针：按住一个按钮不放，当场截图，再松开。
/// 触摸界面的按压态只有"按下期间"存在，常规截图工具按下即松，抓不到，故单列一个探针。
/// 用法：-- --ppress=&lt;输出前缀&gt;
/// </summary>
public partial class PortraitPressProbe : Node
{
    private string _prefix = "";
    private SubViewport _sub = null!;
    private PortraitRoot _root = null!;
    private readonly System.Collections.Generic.Queue<Action> _steps = new();
    private int _wait;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--ppress="))
                _prefix = arg["--ppress=".Length..];
        if (_prefix.Length == 0)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_prefix))!);
        InkSaveStore.OverrideDirectory =
            Path.Combine(Path.GetTempPath(), $"rimisekai-press-{Guid.NewGuid():N}").Replace('\\', '/');
        _sub = new SubViewport
        {
            Size = new Vector2I((int)PortraitLayout.CanvasWidth, (int)PortraitLayout.CanvasHeight),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_sub);
        _root = new PortraitRoot { Name = "PortraitRoot" };
        _sub.AddChild(_root);

        // 据点主界面：按住「日志」页签，当场截图，再松开。
        _steps.Enqueue(() => _root.DebugStart());
        _steps.Enqueue(() => _root.HubScreen.ShowTab(3));
        _steps.Enqueue(() => Hold(_root.HubScreen, PortraitLayout.Tab(1).GetCenter()));
        _steps.Enqueue(() => Shoot("tab_pressed"));
        _steps.Enqueue(() => Release(_root.HubScreen, PortraitLayout.Tab(1).GetCenter()));
        _steps.Enqueue(() => Shoot("tab_released"));
        // 回到操作页，按住第一个入口按钮（大按钮），当场截图，再松开。
        _steps.Enqueue(() => _root.HubScreen.ShowTab(3));
        _steps.Enqueue(() => Hold(_root.HubScreen, EntryCenter(0)));
        _steps.Enqueue(() => Shoot("entry_pressed"));
        _steps.Enqueue(() => Release(_root.HubScreen, EntryCenter(0)));
        // 回地图页，按住已开放的房间格，当场截图。
        _steps.Enqueue(() => _root.HubScreen.ShowTab(0));
        _steps.Enqueue(() => Hold(_root.HubScreen, PortraitLayout.Cell(2, 2).GetCenter()));
        _steps.Enqueue(() => Shoot("cell_pressed"));
        _steps.Enqueue(() => Release(_root.HubScreen, PortraitLayout.Cell(2, 2).GetCenter()));
        _steps.Enqueue(() => GetTree().Quit());
    }

    private Vector2 EntryCenter(int index)
    {
        foreach (var widget in _root.HubScreen.DebugWidgets)
            if (widget.Action == PortraitAction.Entry && widget.Index == index)
                return widget.Rect.GetCenter();
        return Vector2.Zero;
    }

    private static void Hold(Control control, Vector2 position) =>
        control.GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = position, GlobalPosition = position,
        });

    private static void Release(Control control, Vector2 position) =>
        control.GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = position, GlobalPosition = position,
        });

    private void Shoot(string tag)
    {
        var path = $"{_prefix}_{tag}.png";
        if (_sub.GetTexture().GetImage().SavePng(path) != Error.Ok)
            GD.PushError($"press probe capture failed: {tag}");
        else
            GD.Print($"press probe ok: {path}");
    }

    public override void _Process(double delta)
    {
        if (_steps.Count == 0 || ++_wait < 3)
            return;
        _wait = 0;
        _steps.Dequeue()();
    }
}
