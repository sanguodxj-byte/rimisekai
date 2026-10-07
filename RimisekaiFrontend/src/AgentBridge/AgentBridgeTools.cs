using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Rimisekai.Ink;
using Rimisekai.Portrait;

namespace Rimisekai.AgentBridge;

/// <summary>
/// 桥的工具面：观察（截图 / 状态 / 热区清单）＋ 操作（触摸 / 拖动 / 键入）＋ 测试辅助（新开局 / 存档）。
/// 坐标一律用画布坐标 1080×2340（与截图画面、PortraitLayout 一致），注入走 PushInput 本地坐标，
/// 与真实触摸同一条引擎管线。
/// </summary>
public sealed partial class AgentBridge
{
    private static JsonNode Schema(params (string name, string type, string description)[] properties) =>
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(
                from p in properties
                select new KeyValuePair<string, JsonNode?>(p.name, new JsonObject
                {
                    ["type"] = p.type,
                    ["description"] = p.description,
                })),
        };

    private static IEnumerable<object> ToolDescriptors()
    {
        yield return Desc("ping", "桥健康检查，返回当前画面阶段。", Schema());
        yield return Desc("screen/screenshot", "截取当前画面存 PNG，返回绝对路径与像素尺寸。坐标体系是画布 1080×2340。", Schema(("tag", "string", "文件名附加标记")));
        yield return Desc("state/game", "读取游戏状态（阶段 / 时刻 / 金钱 / 天气 / 地点 / 队伍 / 日志尾 / 弹窗）。", Schema());
        yield return Desc("ui/widgets", "列出当前可见画面的全部可点热区（含矩形与多边形），label 与截图上的文字对应。", Schema());
        yield return Desc("input/click", "在画布坐标处点击（先移动再按下再松开，与真实触摸一致）。", Schema(
            ("x", "number", "画布 x"), ("y", "number", "画布 y"),
            ("button", "string", "left 或 right，默认 left")));
        yield return Desc("input/move", "移动指针到画布坐标（悬停）。", Schema(("x", "number", "画布 x"), ("y", "number", "画布 y")));
        yield return Desc("input/swipe", "从一点拖到另一点（列表滚动用）。", Schema(
            ("fromX", "number", "起点 x"), ("fromY", "number", "起点 y"),
            ("toX", "number", "终点 x"), ("toY", "number", "终点 y"),
            ("steps", "number", "插值步数，默认 10")));
        yield return Desc("input/wheel", "在画布坐标处滚一轮滚轮。", Schema(
            ("x", "number", "画布 x"), ("y", "number", "画布 y"),
            ("dy", "number", "正为向下滚，默认 1")));
        yield return Desc("input/text", "向焦点控件逐字键入文本（弹窗输入框用）。", Schema(
            ("text", "string", "要键入的文本"), ("submit", "boolean", "true 时追加回车提交")));
        yield return Desc("input/key", "按一个功能键。", Schema(("key", "string", "enter/escape/backspace/tab/up/down/left/right/delete/space")));
        yield return Desc("game/new", "跳过标题画面直接开新局（等同标题画面点开始）。", Schema());
        yield return Desc("save/save", "把当前进度存进存档目录。", Schema(("name", "string", "备注标签")));
        yield return Desc("save/list", "列出存档目录里的存档槽。", Schema());
        yield return Desc("time/advance", "开发用时间推进：走生产 PassTime 管线推 N 分钟（NPC 排班、天气、跨日结算全真实）。", Schema(("minutes", "number", "推进的分钟数")));
    }

    private static object Desc(string name, string description, JsonNode inputSchema) => new
    {
        name,
        description,
        inputSchema,
        outputSchema = new JsonObject { ["type"] = "object" },
    };

    private object ExecuteTool(string name, JsonNode? args) => name switch
    {
        "ping" => ToolPing(),
        "screen/screenshot" => ToolScreenshot(args),
        "state/game" => ToolState(),
        "ui/widgets" => ToolWidgets(),
        "input/click" => ToolClick(args),
        "input/move" => ToolMove(args),
        "input/swipe" => ToolSwipe(args),
        "input/wheel" => ToolWheel(args),
        "input/text" => ToolText(args),
        "input/key" => ToolKey(args),
        "game/new" => ToolNewGame(),
        "save/save" => ToolSave(args),
        "save/list" => ToolSaveList(),
        "time/advance" => ToolTimeAdvance(args),
        _ => throw new BridgeException(-31002, $"tool '{name}' not found"),
    };

    private object ToolPing() => new
    {
        pong = true,
        gameId = _gameId,
        phase = Root().DebugPhase.ToString(),
    };

    private object ToolScreenshot(JsonNode? args)
    {
        var image = GetViewport().GetTexture().GetImage();
        var dir = Path.Combine(OS.GetUserDataDir(), "agent");
        Directory.CreateDirectory(dir);
        var tag = OptionalText(args, "tag") ?? "shot";
        var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{Sanitize(tag)}.png");
        var err = image.SavePng(path);
        if (err != Error.Ok)
            throw new BridgeException(-32000, $"save png failed {err}");
        return new
        {
            path = path.Replace('\\', '/'),
            width = image.GetWidth(),
            height = image.GetHeight(),
            canvasWidth = (double)PortraitLayout.CanvasWidth,
            canvasHeight = (double)PortraitLayout.CanvasHeight,
        };
    }

    private object ToolState()
    {
        var root = Root();
        var payload = new JsonObject
        {
            ["phase"] = root.DebugPhase.ToString(),
            ["canvasWidth"] = PortraitLayout.CanvasWidth,
            ["canvasHeight"] = PortraitLayout.CanvasHeight,
        };
        if (root.DebugCombat != null)
            payload["combatActive"] = true;
        var modal = root.ModalLayer;
        if (modal.IsActive)
        {
            var page = modal.Current;
            payload["modal"] = new JsonObject
            {
                ["title"] = page?.Title ?? "",
                ["body"] = page?.Body ?? "",
                ["hasInput"] = page?.Input != null,
                ["inputText"] = page?.Input?.Text ?? "",
                ["choiceCount"] = page?.Choices.Count ?? 0,
            };
        }
        var vm = root.DebugVm;
        if (vm != null)
        {
            var hub = vm.Hub;
            var state = hub.State;
            var roster = new JsonArray();
            foreach (var member in state.Roster.Members)
            {
                var vitals = member.Condition;
                roster.Add(new JsonObject
                {
                    ["name"] = member.Name,
                    ["master"] = member.IsMaster,
                    ["stamina"] = vitals.Stamina,
                    ["maxStamina"] = vitals.MaxStamina,
                    ["spirit"] = vitals.Spirit,
                    ["maxSpirit"] = vitals.MaxSpirit,
                    ["favor"] = vitals.Favor,
                    ["tired"] = vitals.Tired,
                });
            }
            var log = new JsonArray();
            var lines = vm.LogLines();
            for (var i = Math.Max(0, lines.Count - 12); i < lines.Count; i++)
                log.Add(lines[i]);
            payload["hub"] = new JsonObject
            {
                ["money"] = state.Money,
                ["place"] = hub.PlaceName(),
                ["mapTitle"] = hub.MapTitle(),
                ["layer"] = hub.Layer.ToString(),
                ["regionId"] = hub.RegionId,
                ["playerRoomId"] = hub.PlayerRoomId,
                ["weather"] = state.Weather.ToString(),
                ["tab"] = root.HubScreen.DebugTab,
                ["notice"] = root.HubScreen.DebugNotice,
                ["clock"] = new JsonObject
                {
                    ["day"] = state.Clock.Day,
                    ["hour"] = state.Clock.Hour,
                    ["minute"] = state.Clock.Minutes % 60,
                    ["slot"] = state.Clock.Slot,
                    ["season"] = state.Clock.Season.ToString(),
                    ["week"] = state.Clock.Week,
                    ["year"] = state.Clock.Year,
                },
                ["roster"] = roster,
                ["log"] = log,
                ["scene"] = new JsonObject
                {
                    ["playing"] = hub.ScenePlaying,
                    ["title"] = hub.SceneTitle,
                    ["actor"] = hub.SceneActor?.Name ?? "",
                    ["waiting"] = hub.SceneChoices.Count > 0,
                    ["line"] = hub.SceneLines.Count > 0
                        ? $"{hub.SceneLines[^1].Speaker}|{hub.SceneLines[^1].Text}"
                        : "",
                    ["choices"] = new JsonArray(hub.SceneChoices
                        .Select(c => (JsonNode)JsonValue.Create(c.Id)!).ToArray()),
                },
                // 定时事件的排班与后台预生成状态（诊断用）。
                ["scheduled"] = new JsonObject
                {
                    ["pending"] = hub.PendingGenerationCount,
                    ["staged"] = hub.StagedActors.Count,
                    ["generator"] = hub.GenerationAvailable,
                },
            };
        }
        return payload;
    }

    private object ToolWidgets()
    {
        var root = Root();
        return new JsonObject
        {
            ["phase"] = root.DebugPhase.ToString(),
            ["title"] = WidgetsOf(root.TitleView),
            ["hub"] = WidgetsOf(root.HubScreen),
            ["combat"] = WidgetsOf(root.CombatView),
            ["modal"] = WidgetsOf(root.ModalLayer),
        };
    }

    private static JsonArray WidgetsOf(Control view)
    {
        var list = new JsonArray();
        if (!view.Visible)
            return list;
        IReadOnlyList<PortraitWidget> widgets = view switch
        {
            PortraitTitleView title => title.DebugWidgets,
            PortraitHubScreen hub => hub.DebugWidgets,
            PortraitCombatView combat => combat.DebugWidgets,
            PortraitModalLayer modal => modal.DebugWidgets,
            _ => Array.Empty<PortraitWidget>(),
        };
        foreach (var widget in widgets)
        {
            var item = new JsonObject
            {
                ["label"] = widget.Label,
                ["action"] = widget.Action.ToString(),
                ["index"] = widget.Index,
                ["enabled"] = widget.Enabled,
                ["rect"] = RectJson(widget.Rect),
            };
            if (widget.Polygon != null)
            {
                var points = new JsonArray();
                foreach (var point in widget.Polygon)
                    points.Add(new JsonArray((double)point.X, (double)point.Y));
                item["polygon"] = points;
            }
            list.Add(item);
        }
        return list;
    }

    private static JsonObject RectJson(Rect2 rect) => new()
    {
        ["x"] = (double)rect.Position.X,
        ["y"] = (double)rect.Position.Y,
        ["w"] = (double)rect.Size.X,
        ["h"] = (double)rect.Size.Y,
        ["cx"] = (double)(rect.Position.X + rect.Size.X / 2f),
        ["cy"] = (double)(rect.Position.Y + rect.Size.Y / 2f),
    };

    private object ToolClick(JsonNode? args)
    {
        var position = Point(args);
        var button = OptionalText(args, "button") == "right" ? MouseButton.Right : MouseButton.Left;
        var root = GetTree().Root;
        Push(root, MouseMotion(position));
        Push(root, MouseButtonEvent(position, button, true));
        Push(root, MouseButtonEvent(position, button, false));
        return Clicked();
    }

    private object ToolMove(JsonNode? args)
    {
        Push(GetTree().Root, MouseMotion(Point(args)));
        return Clicked();
    }

    private object ToolSwipe(JsonNode? args)
    {
        var from = new Vector2((float)Num(args, "fromX"), (float)Num(args, "fromY"));
        var to = new Vector2((float)Num(args, "toX"), (float)Num(args, "toY"));
        var steps = args?["steps"] is { } stepNode ? (int)stepNode.GetValue<double>() : 10;
        var root = GetTree().Root;
        Push(root, MouseMotion(from));
        Push(root, MouseButtonEvent(from, MouseButton.Left, true));
        for (var i = 1; i <= steps; i++)
        {
            var position = from.Lerp(to, i / (float)steps);
            Push(root, MouseMotion(position));
        }
        Push(root, MouseButtonEvent(to, MouseButton.Left, false));
        return Clicked();
    }

    private object ToolWheel(JsonNode? args)
    {
        var position = Point(args);
        var down = args?["dy"] is { } dyNode ? dyNode.GetValue<double>() >= 0 : true;
        var button = down ? MouseButton.WheelDown : MouseButton.WheelUp;
        var root = GetTree().Root;
        Push(root, MouseMotion(position));
        Push(root, MouseButtonEvent(position, button, true));
        Push(root, MouseButtonEvent(position, button, false));
        return Clicked();
    }

    private object ToolText(JsonNode? args)
    {
        var text = OptionalText(args, "text") ?? throw new BridgeException(-32602, "missing argument text");
        var root = GetTree().Root;
        foreach (var ch in text)
        {
            if (char.IsSurrogate(ch))
                continue;
            Push(root, KeyEvent(ch, true));
            Push(root, KeyEvent(ch, false));
        }
        if (args?["submit"]?.GetValue<bool>() == true)
        {
            Push(root, SpecialKey(Key.Enter, true));
            Push(root, SpecialKey(Key.Enter, false));
        }
        return Clicked();
    }

    private object ToolKey(JsonNode? args)
    {
        var key = OptionalText(args, "key") ?? throw new BridgeException(-32602, "missing argument key");
        var godotKey = key switch
        {
            "enter" => Key.Enter,
            "escape" => Key.Escape,
            "backspace" => Key.Backspace,
            "tab" => Key.Tab,
            "up" => Key.Up,
            "down" => Key.Down,
            "left" => Key.Left,
            "right" => Key.Right,
            "delete" => Key.Delete,
            "space" => Key.Space,
            _ => throw new BridgeException(-32602, $"unknown key '{key}'"),
        };
        var root = GetTree().Root;
        Push(root, SpecialKey(godotKey, true));
        Push(root, SpecialKey(godotKey, false));
        return Clicked();
    }

    private object ToolNewGame()
    {
        Root().DebugStart();
        return ToolState();
    }

    private object ToolSave(JsonNode? args)
    {
        var vm = Root().DebugVm ?? throw new BridgeException(-32000, "no hub session");
        var name = InkSaveStore.SaveNew(vm.Hub.State, vm.Hub, out var error);
        if (name == null)
            throw new BridgeException(-32000, error ?? "save failed");
        return new { file = name };
    }

    private object ToolTimeAdvance(JsonNode? args)
    {
        Root().DebugVm.Hub.PassTime((int)Num(args, "minutes"));
        return ToolState();
    }

    private object ToolSaveList()
    {
        var slots = new JsonArray();
        foreach (var slot in InkSaveStore.ListSaves())
        {
            slots.Add(new JsonObject
            {
                ["fileName"] = slot.FileName,
                ["territoryName"] = slot.TerritoryName,
                ["day"] = slot.Day,
                ["minutes"] = slot.Minutes,
                ["summary"] = slot.Summary,
                ["timestamp"] = slot.Timestamp,
                ["corrupt"] = slot.IsCorrupt,
            });
        }
        return new { saves = slots };
    }

    // ---- 输入合成 ----

    private static void Push(Viewport viewport, InputEvent @event) => viewport.PushInput(@event, true);

    private static InputEventMouseMotion MouseMotion(Vector2 position) => new()
    {
        Position = position,
        GlobalPosition = position,
    };

    private static InputEventMouseButton MouseButtonEvent(Vector2 position, MouseButton button, bool pressed) => new()
    {
        Position = position,
        GlobalPosition = position,
        ButtonIndex = button,
        Pressed = pressed,
    };

    private static InputEventKey KeyEvent(char ch, bool pressed) => new()
    {
        Pressed = pressed,
        Unicode = ch,
    };

    private static InputEventKey SpecialKey(Key key, bool pressed) => new()
    {
        Pressed = pressed,
        Keycode = key,
    };

    private object Clicked() => new { phase = Root().DebugPhase.ToString() };

    private static Vector2 Point(JsonNode? args) => new((float)Num(args, "x"), (float)Num(args, "y"));

    private static double Num(JsonNode? args, string name) =>
        args?[name]?.GetValue<double>() ?? throw new BridgeException(-32602, $"missing argument {name}");

    private static string? OptionalText(JsonNode? args, string name) => args?[name]?.GetValue<string>();

    private static string Sanitize(string tag)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in tag)
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        return builder.Length == 0 ? "shot" : builder.ToString();
    }
}
