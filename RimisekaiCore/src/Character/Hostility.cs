namespace Rimisekai.Character;

/// <summary>
/// 对玩家姿态。好感按角色单独存（每个 CharacterState 一份对玩家的好感）。
/// 敌对及以下主动攻击；只有憎恨下死手，敌对被击败另算（不进死亡）。
/// </summary>
public enum Stance
{
    Ignore,
    Threaten,
    Attack,
    Kill,
}

public static class Hostility
{
    public static Stance StanceTowardPlayer(CharacterState character) => character.Condition.Bond switch
    {
        Bond.Hatred => Stance.Kill,
        Bond.Hostile => Stance.Attack,
        Bond.Dislike => Stance.Threaten,
        _ => Stance.Ignore,
    };

    public static bool AttacksPlayer(CharacterState character) =>
        StanceTowardPlayer(character) is Stance.Attack or Stance.Kill;

    public static bool MayKillPlayerSide(CharacterState character) =>
        StanceTowardPlayer(character) == Stance.Kill;
}
