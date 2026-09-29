using Rimisekai.Character;

namespace Rimisekai.Voice;

/// <summary>
/// 自动节律里角色开口的出口。Housing 不认识存档层，只认这个接口，
/// 由主界面会话在推进时间时塞进来。
/// 台词唯一的呈现是对话框，日志不写任何台词，因此这里没有“返回文本落日志”的口子。
/// </summary>
public interface IVoiceSink
{
    /// <summary>
    /// 角色主动找玩家搭话：台词进对话框。这是弹层唯一的自动入口。
    /// 返回是否真的说了话；没说时调用方退回自己的行为叙述。
    /// </summary>
    bool Dialogue(CharacterState who, VoiceTrigger trigger);

    /// <summary>
    /// 角色此刻在做什么的状态描述（地文）。按角色当前活动与所在设施取一句。
    /// 返回 null 表示这个角色没有对应的状态地文——调用方**什么都不显示**，
    /// 不退回通用文案（引擎不替角色编造行为）。
    /// </summary>
    string? StateLine(CharacterState who, VoiceActivity activity, string facilityName);
}
