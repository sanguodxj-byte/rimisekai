using System;

namespace Rimisekai.Character;

public enum Bond
{
    Hatred,
    Hostile,
    Dislike,
    None,
    Fond,
    Close,
    Lover,
}

/// <summary>
/// 体力、气力、对玩家的好感。上限由角色数据即时推导（体力=生命值，气力暂为常量），
/// 本类不存上限；当前值落存储，读取时夹进上限。
/// </summary>
public sealed class Vitals
{
    private readonly Func<int> _maxStamina;
    private readonly Func<int> _maxSpirit;

    public Vitals(Func<int> maxStamina, Func<int> maxSpirit)
    {
        _maxStamina = maxStamina;
        _maxSpirit = maxSpirit;
    }

    public const int StaminaId = 0;
    public const int SpiritId = 1;
    public const int DefaultMax = 1000;
    public const int FondAt = 100;
    public const int CloseAt = 300;
    public const int LoverAt = 600;
    public const int DislikeAt = -100;
    public const int HostileAt = -300;
    public const int HatredAt = -600;
    public const int FavorMin = -1000;
    public const int FavorMax = 1000;

    /// <summary>衣服湿度上限，也是"湿透"的判定线。</summary>
    public const int WetMax = 100;

    private int _stamina = DefaultMax;
    private int _spirit = DefaultMax;

    public int Stamina => Clamp(_stamina, 0, MaxStamina);
    public int Spirit => Clamp(_spirit, 0, MaxSpirit);
    public int MaxStamina => _maxStamina();
    public int MaxSpirit => _maxSpirit();
    public int Favor { get; private set; }

    /// <summary>衣服湿度。只在露天淋雨时上涨，湿透只作为口上/地文的状态条件，不另扣数值。</summary>
    public int Wetness { get; private set; }

    public bool Soaked => Wetness >= WetMax;

    public bool Tired => Spirit < MaxSpirit * 3 / 10;
    public Bond Bond => Favor >= LoverAt ? Bond.Lover
        : Favor >= CloseAt ? Bond.Close
        : Favor >= FondAt ? Bond.Fond
        : Favor > DislikeAt ? Bond.None
        : Favor > HostileAt ? Bond.Dislike
        : Favor > HatredAt ? Bond.Hostile
        : Bond.Hatred;

    public void Spend(int stamina, int spirit)
    {
        _stamina = Clamp(Stamina - stamina, 0, MaxStamina);
        _spirit = Clamp(Spirit - spirit, 0, MaxSpirit);
    }

    public void Recover(int stamina, int spirit)
    {
        _stamina = Clamp(Stamina + stamina, 0, MaxStamina);
        _spirit = Clamp(Spirit + spirit, 0, MaxSpirit);
    }

    public void RestTick()
    {
        _stamina = Clamp(Stamina + 5, 0, MaxStamina);
        _spirit = Clamp(Spirit + 5, 0, MaxSpirit);
    }

    public void SleepTick()
    {
        _stamina = Clamp(Stamina + 20, 0, MaxStamina);
        _spirit = Clamp(Spirit + 20, 0, MaxSpirit);
    }

    public void RecoverFull()
    {
        _stamina = MaxStamina;
        _spirit = MaxSpirit;
    }

    /// <summary>淋雨积湿。增速由调用方按天气给出。</summary>
    public void Soak(int amount) => Wetness = Clamp(Wetness + amount, 0, WetMax);

    /// <summary>晾干。不淋雨的每个时间格调用一次。</summary>
    public void Dry(int amount) => Wetness = Clamp(Wetness - amount, 0, WetMax);

    /// <summary>换了干衣服（入睡时调用）。</summary>
    public void ChangeIntoDryClothes() => Wetness = 0;

    /// <summary>读档回填湿度。</summary>
    public void RestoreWetness(int wetness) => Wetness = Clamp(wetness, 0, WetMax);

    public void Restore(int stamina, int spirit, int favor)
    {
        _stamina = Clamp(stamina, 0, MaxStamina);
        _spirit = Clamp(spirit, 0, MaxSpirit);
        Favor = Clamp(favor, FavorMin, FavorMax);
    }

    public void AddFavor(int amount, int bonusPercent = 0)
    {
        Favor = Clamp(Favor + amount * (100 + bonusPercent) / 100, FavorMin, FavorMax);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
