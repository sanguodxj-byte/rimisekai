namespace Rimisekai.Voice;

/// <summary>
/// 说话人此刻在做什么。对应 eraFL 的 HO_Act，但做成枚举而不是字符串，
/// 内容里打错字会在解析期被发现，而不是静默永不匹配。
///
/// 由宿主按角色的当前状态填入。角色在干活时也有挂点，
/// 因此“边做饭边说话”“挖矿时回一句”都能写。
/// </summary>
public enum VoiceActivity
{
    /// <summary>没在做什么，闲时。</summary>
    Idle = 0,

    /// <summary>在移动/赶路。</summary>
    Moving,

    /// <summary>一般劳作（采集等）。</summary>
    Working,

    /// <summary>在灶台做饭。</summary>
    Cooking,

    /// <summary>在采矿。</summary>
    Mining,

    /// <summary>在种植。</summary>
    Farming,

    /// <summary>在工作台制作。</summary>
    Crafting,

    /// <summary>在锻炼/练武。</summary>
    Training,

    /// <summary>在休息。</summary>
    Resting,

    /// <summary>在吃饭。</summary>
    Eating,

    /// <summary>在睡觉。</summary>
    Sleeping,

    /// <summary>正走向玩家，想说话。</summary>
    Seeking,

    /// <summary>跟着玩家同行。</summary>
    Following,

    /// <summary>在娱乐/游玩。</summary>
    Playing,
}

/// <summary>
/// 说话人在这次互动里扮演的角色。对应 eraFL 的 PLYorTGT。
/// 同一个动作，动手的一方和承受的一方说的话不一样。
/// </summary>
public enum VoiceRole
{
    /// <summary>说话人就是动手的那个。</summary>
    Actor = 0,

    /// <summary>说话人是被作用的对象。</summary>
    Partner,

    /// <summary>说话人只是旁观者。</summary>
    Observer,
}

/// <summary>
/// 动作发生的阶段。对应 eraFL 的 kojoPlace（TOP/MIDDLE/BOTTOM）。
/// 事前一句、事中一句、事后一句，是口上最基本的节奏。
/// </summary>
public enum VoicePlace
{
    /// <summary>动作发生前。</summary>
    Before = 0,

    /// <summary>动作进行中。</summary>
    Middle,

    /// <summary>动作结束后。</summary>
    After,
}

/// <summary>
/// 细分情绪。比心情数值更好写——作者想表达“害羞的时候”就直接写 Shy，
/// 不必去推 0-100 的区间。宿主按角色的状态推导出一个情绪。
/// </summary>
public enum VoiceEmotion
{
    /// <summary>不指定，任何情绪都算。</summary>
    Any = 0,

    Neutral,
    Happy,
    Excited,
    Sad,
    Lonely,
    Angry,
    Afraid,
    Tired,
    Shy,
    Aroused,
}
