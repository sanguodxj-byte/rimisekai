using Godot;
using Rimisekai.Character;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 角色头像解析：据点主界面头像带取 <c>assets/avatars/</c> 下的 1:1 头像。
///
/// 角色数据里不存身份（身份只是生成器掷骰时的中间量，随特质与武器进入角色），
/// 所以头像按「主角 → 已知名字 → 已授予特质 → 稳定兜底」四段解析，全部确定性、不随帧跳变：
/// 1. 主角固定取 <c>special/avatar_human_paladin.png</c>（开局种子里的「你」）；
/// 2. 开局种子里点名的角色按名字取（与旧状态页口径一致）；
/// 3. 已授予身份特质者按特质取（女仆 / 魔法师）；
/// 4. 其余（生成器掷出的同伴）按 Id 稳定落在一张身份头像上，保证每个角色都有面孔且不跳变。
/// </summary>
public static class PortraitAvatars
{
    private const string Root = "res://assets/avatars/";
    private const string PlayerAvatar = Root + "special/avatar_human_paladin.png";

    /// <summary>31 个身份头像的英文名（与 assets/avatars/identity 目录一致）。</summary>
    private static readonly string[] Identities =
    {
        "alchemist", "apothecary", "archer", "assassin", "astrologer", "bard",
        "blacksmith", "butler", "carpenter", "cook", "courier", "dancer",
        "druid", "gardener", "guard", "hunter", "knight", "mage",
        "maid", "mercenary", "merchant", "monk", "noble", "nun",
        "paladin", "priestess", "ranger", "scholar", "scholar_assistant", "thief", "warrior",
    };

    /// <summary>开局种子点名的角色 → 身份头像。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> ByName = new()
    {
        ["璐米埃尔"] = "maid",
        ["瑞雅莉"] = "knight",
        ["瑞茵"] = "scholar",
    };

    private static string Identity(string key) => Root + $"identity/{key}_diff1.png";

    public static Texture2D? Resolve(CharacterState? who)
    {
        if (who == null)
            return null;
        if (who.IsMaster)
            return InkIllustration.LoadTexture(PlayerAvatar);
        if (ByName.TryGetValue(who.Name, out var named))
            return InkIllustration.LoadTexture(Identity(named));
        if (who.IsMaid())
            return InkIllustration.LoadTexture(Identity("maid"));
        if (who.IsMage())
            return InkIllustration.LoadTexture(Identity("mage"));
        return InkIllustration.LoadTexture(Identity(Identities[who.Id % Identities.Length]));
    }
}
