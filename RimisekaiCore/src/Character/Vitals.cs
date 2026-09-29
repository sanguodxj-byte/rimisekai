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

/// <summary>体力、气力、疲劳、对玩家的好感。数值有上限，疲劳到顶就停工。</summary>
public sealed class Vitals
{
    public const int StaminaId = 0;
    public const int SpiritId = 1;
    public const int DefaultMax = 1000;
    public const int TiredAt = 150;
    public const int FondAt = 100;
    public const int CloseAt = 300;
    public const int LoverAt = 600;
    public const int DislikeAt = -100;
    public const int HostileAt = -300;
    public const int HatredAt = -600;
    public const int FavorMin = -1000;
    public const int FavorMax = 1000;

    public int Stamina { get; private set; } = DefaultMax;
    public int MaxStamina { get; private set; } = DefaultMax;
    public int Spirit { get; private set; } = DefaultMax;
    public int MaxSpirit { get; private set; } = DefaultMax;
    public int Fatigue { get; private set; }
    public int Favor { get; private set; }
    public int Mana { get; private set; }
    public int MaxMana { get; private set; } = 10;

    public bool Tired => Fatigue >= TiredAt;
    public Bond Bond => Favor >= LoverAt ? Bond.Lover
        : Favor >= CloseAt ? Bond.Close
        : Favor >= FondAt ? Bond.Fond
        : Favor > DislikeAt ? Bond.None
        : Favor > HostileAt ? Bond.Dislike
        : Favor > HatredAt ? Bond.Hostile
        : Bond.Hatred;

    public void Spend(int stamina, int spirit, int fatigue)
    {
        Stamina = Clamp(Stamina - stamina, 0, MaxStamina);
        Spirit = Clamp(Spirit - spirit, 0, MaxSpirit);
        Fatigue = Clamp(Fatigue + fatigue, 0, TiredAt);
    }

    public void Recover(int stamina, int spirit, bool clearFatigue)
    {
        Stamina = Clamp(Stamina + stamina, 0, MaxStamina);
        Spirit = Clamp(Spirit + spirit, 0, MaxSpirit);
        if (clearFatigue)
            Fatigue = 0;
    }

    public void RestTick()
    {
        Stamina = Clamp(Stamina + 5, 0, MaxStamina);
        Spirit = Clamp(Spirit + 5, 0, MaxSpirit);
        Mana = Clamp(Mana + 5, 0, MaxMana);
        Fatigue = Clamp(Fatigue - 3, 0, TiredAt);
    }

    public void SleepTick()
    {
        Stamina = Clamp(Stamina + 20, 0, MaxStamina);
        Spirit = Clamp(Spirit + 20, 0, MaxSpirit);
        Mana = Clamp(Mana + 20, 0, MaxMana);
        Fatigue = Clamp(Fatigue - 8, 0, TiredAt);
    }

    public void RecoverFull()
    {
        Stamina = MaxStamina;
        Spirit = MaxSpirit;
        Mana = MaxMana;
        Fatigue = 0;
    }

    public void SetMaxMana(int max)
    {
        MaxMana = max < 0 ? 0 : max;
        if (Mana > MaxMana)
            Mana = MaxMana;
    }

    public void SpendMana(int amount) => Mana = Clamp(Mana - amount, 0, MaxMana);

    public void RecoverMana(int amount) => Mana = Clamp(Mana + amount, 0, MaxMana);

    public void Restore(int stamina, int maxStamina, int spirit, int maxSpirit, int fatigue, int favor, int mana, int maxMana)
    {
        MaxStamina = maxStamina <= 0 ? DefaultMax : maxStamina;
        MaxSpirit = maxSpirit <= 0 ? DefaultMax : maxSpirit;
        Stamina = Clamp(stamina, 0, MaxStamina);
        Spirit = Clamp(spirit, 0, MaxSpirit);
        Fatigue = Clamp(fatigue, 0, TiredAt);
        Favor = Clamp(favor, FavorMin, FavorMax);
        MaxMana = maxMana < 0 ? 0 : maxMana;
        Mana = Clamp(mana, 0, MaxMana);
    }

    public void AddFavor(int amount, int bonusPercent = 0)
    {
        Favor = Clamp(Favor + amount * (100 + bonusPercent) / 100, FavorMin, FavorMax);
    }

    public void Apply(CharacterState character)
    {
        var delta = Traits.FatigueRecoveryDelta(character);
        if (delta != 0)
            Fatigue = Clamp(Fatigue + delta, 0, TiredAt);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
