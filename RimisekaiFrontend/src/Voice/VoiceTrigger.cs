namespace Rimisekai.Voice;

/// <summary>
/// 口上与地文的分野。Speech 是角色开口说的话，Narration 是旁白。
/// 两者都走同一套触发与筛选，区别只在宿主怎么呈现。
/// </summary>
public enum VoiceKind
{
    Speech,
    Narration,
}

/// <summary>
/// 说话的时机。每个值对应宿主里一个明确的挂点，不由内容包自由发明。
/// 加新时机要同时在宿主里加挂点，否则该触发永远选不出句子。
/// </summary>
public enum VoiceTrigger
{
    /// <summary>玩家走进一间有该角色的房间。</summary>
    Meet,

    /// <summary>交谈成功。</summary>
    Talk,

    /// <summary>观察。</summary>
    Observe,

    /// <summary>赠物。</summary>
    Gift,

    /// <summary>接触。</summary>
    Touch,

    /// <summary>摸头。</summary>
    PatHead,

    /// <summary>身体接触。</summary>
    BodyContact,

    /// <summary>拥抱。</summary>
    Hug,

    /// <summary>亲吻。</summary>
    Kiss,

    /// <summary>冷淡/高傲者第一次不接话。</summary>
    TalkRefused,

    /// <summary>接受了玩家的邀请。</summary>
    Invited,

    /// <summary>拒绝了玩家的邀请。</summary>
    InviteRefused,

    /// <summary>好感跨过一档（None/Fond/Close/Lover）。</summary>
    BondUp,

    /// <summary>想找玩家说话（替代“似乎想对你说什么”）。</summary>
    Seek,

    /// <summary>等不到人，打消念头（替代“好像打消了念头”）。</summary>
    SeekGaveUp,

    /// <summary>吃了一餐。</summary>
    Meal,

    /// <summary>入睡。</summary>
    Sleep,

    /// <summary>醒来。</summary>
    Wake,

    /// <summary>日终。</summary>
    DayEnd,

    /// <summary>闲时同处一室，随机的闲话。</summary>
    Idle,

    /// <summary>
    /// 状态描述。角色此刻在做什么（闲时待着、打扫、坐在某件设施上……），
    /// 由引擎给出结构化事实、地文包按角色写文本。没有地文就不显示——
    /// 引擎不提供通用兜底句，避免替角色编造行为。
    /// </summary>
    State,

    /// <summary>场景事件（多步剧情）开演。</summary>
    Scene,

    /// <summary>
    /// 场景内的主动对话：同处一地（「此刻」里看得见）的同伴自己开口。
    /// 呈现为指向说话人头像的气泡，点一下推进，说完收起。频率由台词的 chance / cooldownMinutes 管。
    /// </summary>
    Chatter,
}
