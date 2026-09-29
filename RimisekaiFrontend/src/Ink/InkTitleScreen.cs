using System;
using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 标题画面：背景直接用参考图（title_reference.png，16:9 铺满画布），
/// 四个菜单按钮按参考图位置摆放，代码只负责命中与反馈：
/// 鼠标悬浮／手机触碰时边框变暗并浅填，空闲态完全显示参考图的按钮。
/// “开始”由路由接线：按内容包建局并切到据点；“退出”直接关游戏。
/// </summary>
public partial class InkTitleScreen : Control
{
    private enum TitleAction
    {
        Start,
        Continue,
        Settings,
        Quit,
    }

    private readonly record struct Entry(Rect2 Rect, TitleAction Action, string Label);

    /// <summary>点击“开始”。世界在这里才建，进游戏前的黑屏时间最短。</summary>
    public Action? StartRequested;

    /// <summary>点击“继续”。读档入口，接线前为空动作。</summary>
    public Action? ContinueRequested;

    /// <summary>点击“设置”。设置页入口，接线前为空动作。</summary>
    public Action? SettingsRequested;

    /// <summary>点击“退出”。</summary>
    public Action? QuitRequested;

    // 参考图内按钮的实测位置（图 1672x941 → 画布 1920x1080）：
    // x 745-1173，首项顶 582，项高 70，步距 100。
    private const float MenuWidth = 428f;
    private const float MenuHeight = 70f;
    private const float MenuStep = 100f;
    private const float MenuTop = 582f;

    private static readonly string TexPath = "res://assets/title_reference.png";

    private List<Entry> _menu = new();
    private int _hover = -1;
    private int _pressed = -1;
    private int _shown = -1;
    private float _anim;
    private Texture2D? _tex;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Rebuild();
    }

    /// <summary>悬停动画进度：0→1 约 0.3 秒，驱动手绘线同步长出。</summary>
    public override void _Process(double delta)
    {
        var active = _hover >= 0 ? _hover : _pressed;
        if (active < 0)
        {
            _shown = -1;
            _anim = 0f;
            return;
        }

        if (active != _shown)
        {
            _shown = active;
            _anim = 0f;
        }

        if (_anim < 1f)
        {
            // 全程约 0.3 秒：每秒累加 1/0.3 个进度单位。
            _anim = Mathf.Min(1f, (float)(_anim + delta / 0.3f));
            QueueRedraw();
        }
    }

    /// <summary>菜单几何进场算一次即可：按钮不随状态移动。</summary>
    private void Rebuild()
    {
        _menu = new List<Entry>
        {
            new(MenuRect(0), TitleAction.Start, "开始"),
            new(MenuRect(1), TitleAction.Continue, "继续"),
            new(MenuRect(2), TitleAction.Settings, "设置"),
            new(MenuRect(3), TitleAction.Quit, "退出"),
        };
    }

    private static Rect2 MenuRect(int index) => new(
        InkLayout.CanvasWidth / 2f - MenuWidth / 2f,
        MenuTop + index * MenuStep, MenuWidth, MenuHeight);

    // ---------- 输入 ----------

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            var index = IndexOf(motion.Position);
            if (index != _hover)
            {
                _hover = index;
                QueueRedraw();
            }
            return;
        }

        // 手机触碰由系统转成鼠标事件（Godot 默认开启），
        // 触屏与鼠标共用下面两条路径：按下即暗框并触发，抬手清除。
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } down)
        {
            var target = IndexOf(down.Position);
            if (target < 0)
                return;

            _pressed = target;
            QueueRedraw();
            Dispatch(_menu[target].Action);
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: false })
        {
            if (_pressed == -1)
                return;
            _pressed = -1;
            QueueRedraw();
        }
    }

    private int IndexOf(Vector2 at)
    {
        for (var i = 0; i < _menu.Count; i++)
        {
            if (_menu[i].Rect.HasPoint(at))
                return i;
        }
        return -1;
    }

    private void Dispatch(TitleAction action)
    {
        switch (action)
        {
            case TitleAction.Start:
                StartRequested?.Invoke();
                break;
            case TitleAction.Continue:
                ContinueRequested?.Invoke();
                break;
            case TitleAction.Settings:
                SettingsRequested?.Invoke();
                break;
            case TitleAction.Quit:
                QuitRequested?.Invoke();
                break;
        }
    }

    // ---------- 绘制 ----------

    public override void _Draw()
    {
        // 参考图铺满画布：同为 16:9，零变形。取不到图时退回墨绿底，不至于白屏。
        _tex ??= GD.Load<Texture2D>(TexPath);
        if (_tex != null)
            DrawTextureRect(_tex, new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight),
                false);
        else
            DrawRect(new Rect2(0, 0, InkLayout.CanvasWidth, InkLayout.CanvasHeight), InkStyle.Bg);

        // 悬停/按下：从文字左右两侧逐条长出手绘线，伸到两端的菱形块。
        var active = _hover >= 0 ? _hover : _pressed;
        if (active >= 0)
            DrawHoverLines(_menu[active].Rect, _menu[active].Label, _anim);
    }

    /// <summary>
    /// 悬停手绘线：每侧一条整线，从文字旁长向框内两端的菱形块，左右同步生长，
    /// 全程约 0.3 秒。与文字净空 24px，在离菱形块 3px 处收笔——不碰文字、菱块与框线。
    /// 线的高度取按钮矩形中心 +2.4（实测框线/内外菱珠的亮度重心都在 y≈718.9，
    /// 笔画渲染有约 0.5px 上飘，此处已补偿）。
    /// </summary>
    private void DrawHoverLines(Rect2 r, string label, float t)
    {
        var lineY = r.GetCenter().Y + 2.4f;
        var cx = r.GetCenter().X;
        var textHalf = InkDraw.Measure(label, 26).X / 2f;

        // 参考图按钮内侧两端的菱形块，实测约在中线两侧 187px 处。
        const float diamondInset = 187f;
        const float textClear = 24f;
        const float diamondClear = 3f;

        var leftFrom = cx - textHalf - textClear;
        var leftTo = cx - diamondInset + diamondClear;
        var rightFrom = cx + textHalf + textClear;
        var rightTo = cx + diamondInset - diamondClear;

        GrowLine(leftFrom, leftTo, lineY, t, 4700);
        GrowLine(rightFrom, rightTo, lineY, t, 4720);
    }

    /// <summary>一条线按 grow(0~1) 从 from 长向 to；手绘感来自抖动笔画。</summary>
    private void GrowLine(float from, float to, float lineY, float grow, int seed)
    {
        grow = Mathf.Clamp(grow, 0f, 1f);
        if (grow <= 0f)
            return;

        var b = from + (to - from) * grow;
        var pts = new[]
        {
            new Vector2(from, lineY),
            new Vector2(b, lineY),
        };
        InkDraw.Ink(this, pts, InkStyle.Line, 1.8f, 0.25f, seed);
    }
}
