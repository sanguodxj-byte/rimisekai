using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rimisekai.Voice;

/// <summary>
/// LLM 生成层与引擎之间的唯一接口。引擎不认 HTTP、不认具体厂商，
/// 只把一份拼好的请求交出去，拿回若干句正文。
///
/// 实现方可以是远端 API、本地模型、或测试用的假生成器。
/// 没有接入实现时整层自动旁路，挂 Generation 的句子直接作废，游戏照常跑。
/// </summary>
public interface IVoiceGenerator
{
    /// <summary>
    /// 生成台词。返回空列表表示这次生成没成功，引擎会放弃该句。
    /// 实现方不应抛异常；出错就返回空列表，把原因写进 <paramref name="request"/> 的日志回调。
    /// </summary>
    Task<IReadOnlyList<string>> GenerateAsync(VoiceRequest request);

    /// <summary>本生成器是否可用。为假时引擎不尝试生成，直接跳过。</summary>
    bool Available { get; }
}

/// <summary>
/// 一次生成请求。引擎把能自动填的都填好，实现方只管拼 prompt 发出去。
/// 字段是只读快照，实现方不该修改。
/// </summary>
public sealed class VoiceRequest
{
    /// <summary>要说话的角色名。</summary>
    public required string CharacterName { get; init; }

    /// <summary>角色本身的固定设定（来自内容包的 persona）。</summary>
    public string Persona { get; init; } = "";

    /// <summary>作者写的本次指令：此刻是什么状况、要说什么。</summary>
    public string Instruction { get; init; } = "";

    /// <summary>作者写的语气/风格要求。</summary>
    public string Style { get; init; } = "";

    /// <summary>要几句。</summary>
    public int LineCount { get; init; } = 1;

    /// <summary>每句期望长度上限（字符），0 为不限。软约束。</summary>
    public int MaxCharsPerLine { get; init; }

    /// <summary>
    /// 世界与关系状态的文字摘要。引擎自动生成，已按可读形式排好。
    /// 直接拼进 prompt 即可。
    /// </summary>
    public string Situation { get; init; } = "";

    /// <summary>该角色最近的记忆条目，从旧到新。可能为空。</summary>
    public IReadOnlyList<string> Memory { get; init; } = System.Array.Empty<string>();

    /// <summary>近期对话，从旧到新，形如「说话人：内容」。可能为空。</summary>
    public IReadOnlyList<string> RecentDialogue { get; init; } = System.Array.Empty<string>();

    /// <summary>作者在 Generation 里写的自定义采样参数，原样透传。</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>();

    /// <summary>触发的时机，实现方可以据此调整措辞。</summary>
    public VoiceTrigger Trigger { get; init; }
}
