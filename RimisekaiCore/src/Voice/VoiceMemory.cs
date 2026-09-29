using System.Collections.Generic;

namespace Rimisekai.Voice;

/// <summary>
/// 一个角色说过哪些台词。只记“说过”和“上次说的时刻”，
/// 因此 Once / CooldownMinutes / RequireSaid / ForbidSaid 都能判。
/// 随存档走，读档后不会把同一句一次性台词再说一遍。
/// </summary>
public sealed class VoiceMemory
{
    /// <summary>台词 Id → 最后一次说出口的累计分钟数。</summary>
    public Dictionary<string, int> SaidAt { get; } = new();

    /// <summary>每个时机最近一次开口的累计分钟数，用来给闲话之类限流。</summary>
    public Dictionary<VoiceTrigger, int> LastSpokeAt { get; } = new();

    public bool HasSaid(string lineId) => lineId.Length > 0 && SaidAt.ContainsKey(lineId);

    public int LastSaidAt(string lineId) =>
        lineId.Length > 0 && SaidAt.TryGetValue(lineId, out var at) ? at : -1;

    public void MarkSaid(string lineId, int nowTotal)
    {
        if (lineId.Length > 0)
            SaidAt[lineId] = nowTotal;
    }

    public int LastSpoke(VoiceTrigger trigger) =>
        LastSpokeAt.TryGetValue(trigger, out var at) ? at : -1;

    public void MarkSpoke(VoiceTrigger trigger, int nowTotal) => LastSpokeAt[trigger] = nowTotal;

    // ---------- LLM 层 ----------

    /// <summary>
    /// 记忆缓冲的容量。满了丢最旧，与 eraFL 的 KOJO_MEMORY_SIZE 同思路。
    /// 只留最近这些条，避免 prompt 无限膨胀。
    /// </summary>
    public const int MemoryCapacity = 35;

    /// <summary>近期对话缓冲的容量。比记忆短，只用于让模型接上话头。</summary>
    public const int DialogueCapacity = 12;

    /// <summary>记忆条目，从旧到新。</summary>
    public List<string> Memories { get; } = new();

    /// <summary>近期对话，从旧到新，形如「说话人：内容」。</summary>
    public List<string> RecentDialogue { get; } = new();

    /// <summary>
    /// 生成结果缓存：缓存键 → (正文, 写入时刻)。
    /// 键由时机与场景维度拼成，因此"同一个按钮连点两次"只烧一次 token。
    /// </summary>
    public Dictionary<string, CachedLines> GeneratedCache { get; } = new();

    /// <summary>
    /// 场景事件上次跑过是第几天。键是事件 Id。
    /// 用于事件的冷却判定，与"说过哪些台词"分开记。
    /// </summary>
    public Dictionary<string, int> SceneLastDay { get; } = new();

    /// <summary>追加一条记忆。满了丢最旧。</summary>
    public void AddMemory(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        Memories.Add(text);
        while (Memories.Count > MemoryCapacity)
            Memories.RemoveAt(0);
    }

    /// <summary>追加一句对话。满了丢最旧。</summary>
    public void AddDialogue(string speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        RecentDialogue.Add($"{speaker}：{text}");
        while (RecentDialogue.Count > DialogueCapacity)
            RecentDialogue.RemoveAt(0);
    }

    /// <summary>清空记忆与近期对话，但保留"说过哪些台词"的记账。</summary>
    public void ClearContext()
    {
        Memories.Clear();
        RecentDialogue.Clear();
    }
}

/// <summary>一条生成结果缓存。存正文与写入时刻，按分钟判定过期。</summary>
public sealed class CachedLines
{
    public List<string> Lines { get; set; } = new();
    public int AtTotal { get; set; }
}
