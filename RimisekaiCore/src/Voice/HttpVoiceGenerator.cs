using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Rimisekai.Voice;

/// <summary>
/// 兼容 OpenAI /v1/chat/completions 规范的标准 HTTP LLM 客户端。
/// 严格遵守 AGENTS.md 铁律：
/// 1. 全局禁设 max_tokens 铁律：绝不发送 max_tokens/max_output_tokens 等参数；
/// 2. 网络错误、超时或 429 均静默回退（返回空列表），由 VoiceDirector 自动触发静态高质兜底；
/// 3. Prompt 内置 docs/voice-writing-guide.md 的约束：沉静克制、敬称贯穿、单句短小、无 emoji、无括号神态。
/// </summary>
public sealed class HttpVoiceGenerator : IVoiceGenerator
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;

    public bool Available => !string.IsNullOrWhiteSpace(_endpoint) && !string.IsNullOrWhiteSpace(_apiKey);

    public HttpVoiceGenerator(
        string endpoint = "https://xjbh.lol/v1",
        string apiKey = "REDACTED_API_KEY",
        string model = "gpt-4o",
        HttpClient? client = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
        _http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<IReadOnlyList<string>> GenerateAsync(VoiceRequest request)
    {
        if (!Available)
            return Array.Empty<string>();

        try
        {
            var systemPrompt = BuildSystemPrompt(request);
            var userPrompt = BuildUserPrompt(request);

            var messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            };

            var payload = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["messages"] = messages,
                ["temperature"] = 0.7,
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _http.SendAsync(httpRequest).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<string>();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseResponse(json, request.LineCount);
        }
        catch
        {
            // 异常与网络中断静默回退，保证游戏零红字、零挂起
            return Array.Empty<string>();
        }
    }

    private static string BuildSystemPrompt(VoiceRequest req)
    {
        var sb = new StringBuilder();
        if (req.Kind == VoiceKind.Narration)
        {
            sb.AppendLine("你是单色线稿日式西幻生活游戏《Rimisekai》的角色动作神态地文反应生成核心。");
            sb.AppendLine("【写作硬约束】");
            sb.AppendLine("1. 客观生动描写该角色受到玩家动作后的即时神态微表情、肢体动作与细腻情绪反应。");
            sb.AppendLine("2. 纯正日系轻文学·治愈慢生活西幻汉化风（eraFL 经典生活风格），语调温和、体贴、自然，严禁网文造作词与北方乡土土话。");
            sb.AppendLine("3. 单句长度控制在 12 到 24 个汉字之间，严格不得超过 26 个汉字。");
            sb.AppendLine("4. 严禁任何 emoji、特殊符号、半角或全角冒号，严禁带有括号动作标注，直接输出客观地文叙述。");
            sb.AppendLine($"5. 严格输出 {Math.Max(1, req.LineCount)} 行地文，每行一句，不要带有序号、角色名字前缀或任何前后引号。");
            return sb.ToString();
        }

        sb.AppendLine("你是单色线稿日式西幻生活游戏《Rimisekai》的角色扮演生成核心。");
        sb.AppendLine("【写作硬约束】");
        sb.AppendLine("1. 沉静克制、语调平和，严禁轻浮活泼、傲娇、流行烂梗与粗俗口语。");
        sb.AppendLine("2. 敬称贯穿：对主角一律称呼“大人”，严禁直呼“你”。");
        sb.AppendLine("3. 单句长度控制在 10 到 18 个汉字之间，严格不得超过 28 个汉字。");
        sb.AppendLine("4. 严禁任何 emoji、特殊符号，严禁带有括号动作神态描写（如（脸红）、（微笑）等一律禁止出现），只输出纯净台词。");
        sb.AppendLine($"5. 严格输出 {Math.Max(1, req.LineCount)} 句台词，每句一行，不要带有序号、说话人名字或前后引号。");
        return sb.ToString();
    }

    private static string BuildUserPrompt(VoiceRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"【说话角色】：{req.CharacterName}");
        if (!string.IsNullOrEmpty(req.Persona))
            sb.AppendLine($"【角色设定】：{req.Persona}");
        if (!string.IsNullOrEmpty(req.Situation))
            sb.AppendLine($"【当前情境】：{req.Situation}");
        if (req.Memory.Count > 0)
            sb.AppendLine($"【角色记忆】：{string.Join("；", req.Memory)}");
        if (req.RecentDialogue.Count > 0)
            sb.AppendLine($"【近期对话】：\n{string.Join("\n", req.RecentDialogue)}");
        if (!string.IsNullOrEmpty(req.Instruction))
            sb.AppendLine($"【本次意图】：{req.Instruction}");
        if (!string.IsNullOrEmpty(req.Style))
            sb.AppendLine($"【语气风格】：{req.Style}");
        return sb.ToString();
    }

    private static IReadOnlyList<string> ParseResponse(string json, int expectedCount)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return Array.Empty<string>();

        var message = choices[0].GetProperty("message");
        var text = message.GetProperty("content").GetString() ?? "";

        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cleaned = line.Trim().Trim('"', '“', '”', '「', '」');
            if (!string.IsNullOrEmpty(cleaned))
                lines.Add(cleaned);
            if (lines.Count >= expectedCount)
                break;
        }
        return lines;
    }
}
