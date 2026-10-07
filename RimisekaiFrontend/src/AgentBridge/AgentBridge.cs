using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Godot;
using Rimisekai.Portrait;

namespace Rimisekai.AgentBridge;

/// <summary>
/// GABP 桥（GABS 实机游玩测试用）。游戏侧作 TCP server，GABS 启动游戏时经环境变量
/// 下发 GABP_SERVER_PORT / GABP_TOKEN / GABS_GAME_ID；三者缺一就不监听任何端口，
/// 平时游玩零行为差异。线缆帧与 gabp/1 信封均对齐 Lib.GAB：
/// Content-Length 头 + JSON 体，request/response/event 三种信封。
/// </summary>
public partial class AgentBridge : Node
{
    private const string ProtocolVersion = "gabp/1";
    private const string BridgeVersion = "1.0.0";
    private static readonly string[] ProtocolMethods =
        { "session/hello", "tools/list", "tools/call", "events/subscribe", "events/unsubscribe" };

    private readonly ConcurrentQueue<Invocation> _queue = new();
    private readonly List<BridgeClient> _clients = new();
    private readonly object _clientsLock = new();
    private TcpListener? _listener;
    private Thread? _acceptThread;
    private volatile bool _running;
    private int _port;
    private string _token = "";
    private string _gameId = "";
    private PortraitRoot? _root;

    private sealed class Invocation
    {
        public BridgeClient Client = null!;
        public string Id = "";
        public string Name = "";
        public JsonNode? Args;
    }

    private sealed class BridgeClient
    {
        public TcpClient Tcp = null!;
        public bool Authenticated;
        public readonly object WriteLock = new();

        public NetworkStream Stream() => Tcp.GetStream();
    }

    /// <summary>工具执行期契约错误，code 直接进 gabp error 信封。</summary>
    public sealed class BridgeException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;
        var portText = System.Environment.GetEnvironmentVariable("GABP_SERVER_PORT");
        var token = System.Environment.GetEnvironmentVariable("GABP_TOKEN");
        var gameId = System.Environment.GetEnvironmentVariable("GABS_GAME_ID");
        if (portText == null || token == null || gameId == null || !int.TryParse(portText, out _port))
            return;
        _token = token;
        _gameId = gameId;
        StartListener();
    }

    public override void _ExitTree()
    {
        StopListener();
    }

    public override void _Process(double delta)
    {
        while (_queue.TryDequeue(out var invocation))
        {
            try
            {
                var result = ExecuteTool(invocation.Name, invocation.Args);
                SendResponse(invocation.Client, invocation.Id, result);
            }
            catch (BridgeException ex)
            {
                SendError(invocation.Client, invocation.Id, ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                SendError(invocation.Client, invocation.Id, -32603, ex.Message);
            }
        }
    }

    private void StartListener()
    {
        _running = true;
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "AgentBridge.Accept" };
        _acceptThread.Start();
        GD.Print($"[AgentBridge] GABP server listening on 127.0.0.1:{_port} (gameId {_gameId})");
    }

    private void StopListener()
    {
        _running = false;
        try { _listener?.Stop(); } catch (Exception) { }
        lock (_clientsLock)
        {
            foreach (var client in _clients)
            {
                try { client.Tcp.Close(); } catch (Exception) { }
            }
            _clients.Clear();
        }
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient tcp;
            try
            {
                if (!_listener!.Pending())
                {
                    Thread.Sleep(50);
                    continue;
                }
                tcp = _listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                if (_running) Thread.Sleep(200);
                else break;
                continue;
            }
            var client = new BridgeClient { Tcp = tcp };
            lock (_clientsLock) _clients.Add(client);
            new Thread(() => ReadLoop(client)) { IsBackground = true, Name = "AgentBridge.Read" }.Start();
        }
    }

    /// <summary>字节级解析 Content-Length 帧（对齐 Lib.GAB 的收包语义，避免多字节字符被截断）。</summary>
    private void ReadLoop(BridgeClient client)
    {
        using var tcp = client.Tcp;
        var stream = tcp.GetStream();
        var buffer = new MemoryStream();
        var chunk = new byte[16384];
        try
        {
            while (_running)
            {
                var read = stream.Read(chunk, 0, chunk.Length);
                if (read == 0)
                    break;
                buffer.Write(chunk, 0, read);
                while (TryTakeFrame(buffer, out var frame))
                    HandleFrame(client, frame);
            }
        }
        catch (Exception) { /* 连接断开即结束，GABS 会按需重连 */ }
        finally
        {
            lock (_clientsLock) _clients.Remove(client);
        }
    }

    private static bool TryTakeFrame(MemoryStream buffer, out string frame)
    {
        frame = "";
        var bytes = buffer.GetBuffer();
        var length = (int)buffer.Length;
        var headEnd = IndexOf(bytes, length, [13, 10, 13, 10]);
        if (headEnd < 0)
            return false;
        var headText = Encoding.UTF8.GetString(bytes, 0, headEnd);
        var marker = "Content-Length:";
        var at = headText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return false;
        var start = at + marker.Length;
        var end = headText.IndexOfAny(['\r', '\n'], start);
        if (end < 0)
            end = headText.Length;
        if (!int.TryParse(headText[start..end].Trim(), out var bodyLength))
            return false;
        var bodyStart = headEnd + 4;
        if (length < bodyStart + bodyLength)
            return false;
        frame = Encoding.UTF8.GetString(bytes, bodyStart, bodyLength);
        var rest = length - (bodyStart + bodyLength);
        if (rest > 0)
            Array.Copy(bytes, bodyStart + bodyLength, bytes, 0, rest);
        buffer.SetLength(rest);
        return true;
    }

    private static int IndexOf(byte[] haystack, int length, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j])
                    continue;
                hit = false;
                break;
            }
            if (hit)
                return i;
        }
        return -1;
    }

    private void HandleFrame(BridgeClient client, string json)
    {
        var envelope = JsonNode.Parse(json);
        var id = envelope?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
        var type = envelope?["type"]?.GetValue<string>();
        if (type != "request")
            return;
        var method = envelope!["method"]?.GetValue<string>() ?? "";
        try
        {
            switch (method)
            {
                case "session/hello":
                    HandleHello(client, id, envelope["params"]);
                    break;
                case "tools/list":
                    RequireAuth(client);
                    SendResponse(client, id, new { tools = ToolDescriptors() });
                    break;
                case "tools/call":
                    RequireAuth(client);
                    // GABS 1.1.0 实测发 parameters；GABP 规范文档写 arguments。两者都收。
                    var callParameters = envelope["params"]?["parameters"] ?? envelope["params"]?["arguments"];
                    _queue.Enqueue(new Invocation
                    {
                        Client = client,
                        Id = id,
                        Name = envelope["params"]?["name"]?.GetValue<string>() ?? "",
                        Args = callParameters,
                    });
                    break;
                case "events/subscribe":
                case "events/unsubscribe":
                    RequireAuth(client);
                    SendResponse(client, id, new { channels = Array.Empty<string>() });
                    break;
                default:
                    SendError(client, id, -32601, $"method '{method}' not found");
                    break;
            }
        }
        catch (BridgeException ex)
        {
            SendError(client, id, ex.Code, ex.Message);
        }
    }

    private void HandleHello(BridgeClient client, string id, JsonNode? parameters)
    {
        var token = parameters?["token"]?.GetValue<string>();
        if (token != _token)
        {
            SendError(client, id, -31000, "Invalid authentication token");
            return;
        }
        client.Authenticated = true;
        SendResponse(client, id, new
        {
            agentId = _gameId,
            app = new { name = "rimisekai", version = BridgeVersion },
            capabilities = new
            {
                methods = ProtocolMethods,
                events = Array.Empty<string>(),
                resources = Array.Empty<string>(),
            },
            schemaVersion = "1.0",
        });
    }

    private static void RequireAuth(BridgeClient client)
    {
        if (!client.Authenticated)
            throw new BridgeException(-31001, "Session not established. Send session/hello first.");
    }

    private void SendResponse(BridgeClient client, string id, object result) =>
        SendFrame(client, JsonSerializer.Serialize(new
        {
            v = ProtocolVersion,
            id,
            type = "response",
            result,
        }));

    private void SendError(BridgeClient client, string id, int code, string message) =>
        SendFrame(client, JsonSerializer.Serialize(new
        {
            v = ProtocolVersion,
            id,
            type = "response",
            error = new { code, message },
        }));

    private void SendFrame(BridgeClient client, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var head = Encoding.UTF8.GetBytes($"Content-Length: {body.Length}\r\nContent-Type: application/json\r\n\r\n");
        try
        {
            lock (client.WriteLock)
            {
                var stream = client.Stream();
                stream.Write(head);
                stream.Write(body);
                stream.Flush();
            }
        }
        catch (Exception) { /* 对端已断开，响应随连接丢弃 */ }
    }

    /// <summary>主场景根画面；启动早期还没挂上时按契约报错。</summary>
    private PortraitRoot Root()
    {
        if (_root != null && GodotObject.IsInstanceValid(_root))
            return _root;
        foreach (var child in GetTree().Root.GetChildren())
        {
            if (child is PortraitRoot portrait)
            {
                _root = portrait;
                return _root;
            }
        }
        throw new BridgeException(-32000, "main scene not ready");
    }
}
