using System.Collections.Generic;

namespace Rimisekai.Voice;

/// <summary>
/// 一句由 LLM 现场生成的台词该怎么问。
///
/// 设计要点：静态台词库与 LLM 生成走同一条挑选与呈现链路，
/// 区别只在"正文从哪来"。因此作者可以给同一时机写几句静态兜底，
/// 再挂一句 Generation 让模型接管——门槛与优先级完全一致。
/// </summary>
public sealed class VoiceGeneration
{
    /// <summary>
    /// 交给模型的人物与场景指令。写"你是谁、此刻什么状况、要说什么"。
    /// 世界状态、关系、记忆会由引擎自动补进 prompt，这里不必重复。
    /// </summary>
    public string Instruction { get; init; } = "";

    /// <summary>
    /// 期望的语气/风格提示，例如"冷淡、简短、不用敬语"。
    /// 与 Instruction 分开是为了让通用角色模板能复用。
    /// </summary>
    public string Style { get; init; } = "";

    /// <summary>要模型说几句。默认 1。</summary>
    public int LineCount { get; init; } = 1;

    /// <summary>
    /// 每句的期望长度上限（字符）。0 表示不限制。
    /// 这是给模型的软约束，引擎不截断——截断会破坏语气。
    /// </summary>
    public int MaxCharsPerLine { get; init; }

    /// <summary>是否把该角色的记忆缓冲喂给模型。默认喂。</summary>
    public bool UseMemory { get; init; } = true;

    /// <summary>是否把近期对话摘要喂给模型。默认喂。</summary>
    public bool UseRecentDialogue { get; init; } = true;

    /// <summary>
    /// 生成结果缓存多久（分钟）。同一角色同一时机在窗口内复用上一次结果，
    /// 避免玩家反复点同一个按钮就反复烧 token。0 表示不缓存。
    /// </summary>
    public int CacheMinutes { get; init; } = 30;

    /// <summary>
    /// 温度之类的采样参数，原样透传给生成器。
    /// 引擎不认识这些键，不同后端各取所需。
    /// </summary>
    public Dictionary<string, string> Options { get; init; } = new();
}
