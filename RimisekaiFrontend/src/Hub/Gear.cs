using System.Collections.Generic;
using System.Linq;
using Rimisekai.Defs;

namespace Rimisekai.Hub;

/// <summary>装备界面的一条可换候选：背包里的一件武器或防具 / 饰品实例。</summary>
public readonly record struct GearOption(string ItemId, string Name, int Count, Quality Quality, IReadOnlyList<DetailLine> Details);

/// <summary>
/// 换装：候选一律取主角背包（武器进 Weapons、防具饰品进 Equips 登记表，背包按实例 Id 记件数）。
/// 换下的实例回背包；原配武器入伙时已落成实例（见 GameState.Outfit），同样回背包。
/// </summary>
public sealed partial class HubSession
{
    private static bool IsWeaponSlot(EquipSlot slot) => slot is EquipSlot.MainHand or EquipSlot.OffHand;

    private static bool IsRingSlot(EquipSlot slot) => slot is EquipSlot.Ring1 or EquipSlot.Ring2;

    private static bool Fits(EquipSlot slot, EquipInstance gear) =>
        EquipSlots.Accepts(slot, gear.Kind) && (gear.Slot == slot || (IsRingSlot(gear.Slot) && IsRingSlot(slot)));

    /// <summary>某槽当前装着的东西的详情行；空槽返回空表。</summary>
    public IReadOnlyList<DetailLine> EquippedDetails(int characterId, EquipSlot slot)
    {
        var c = State.Roster.Find(characterId);
        if (c == null)
            return System.Array.Empty<DetailLine>();
        var id = c.EquippedId(slot);
        if (id.Length > 0)
        {
            if (State.Equips.Get(id) is { } gear)
                return gear.DescribeDetails();
            if (State.Weapons.Get(id) is { } weapon)
                return weapon.DescribeDetails();
        }
        return System.Array.Empty<DetailLine>();
    }

    /// <summary>某槽当前装着的那件的品质；空槽或无实例的原配武器为 null。</summary>
    public Quality? EquippedQuality(int characterId, EquipSlot slot)
    {
        var id = State.Roster.Find(characterId)?.EquippedId(slot) ?? "";
        if (State.Equips.Get(id) is { } gear)
            return gear.Quality;
        return State.Weapons.Get(id)?.Quality;
    }

    /// <summary>背包里能放进该槽的候选，按名字排。</summary>
    public IReadOnlyList<GearOption> GearOptions(EquipSlot slot)
    {
        var bag = State.Roster.Master?.Bag;
        var list = new List<GearOption>();
        if (bag == null)
            return list;
        foreach (var pair in bag.Items)
        {
            if (pair.Value <= 0)
                continue;
            if (IsWeaponSlot(slot) && State.Weapons.Get(pair.Key) is { } weapon)
                list.Add(new GearOption(pair.Key, WeaponForge.NameOf(weapon), pair.Value, weapon.Quality, weapon.DescribeDetails()));
            else if (State.Equips.Get(pair.Key) is { } gear && Fits(slot, gear))
                list.Add(new GearOption(pair.Key, EquipForge.NameOf(gear), pair.Value, gear.Quality, gear.DescribeDetails()));
        }
        return list.OrderBy(o => o.Name).ToList();
    }

    /// <summary>从背包取一件换进某槽；原先那件（有实例的）回背包。副手需先有主手。</summary>
    public bool EquipFromBag(int characterId, EquipSlot slot, string itemId)
    {
        var c = State.Roster.Find(characterId);
        var bag = State.Roster.Master?.Bag;
        if (c == null || bag == null || bag.Get(itemId) < 1)
            return false;
        string name;
        if (IsWeaponSlot(slot) && State.Weapons.Get(itemId) is { } weapon)
        {
            var main = slot == EquipSlot.MainHand ? weapon.Type : c.MainWeapon;
            var off = slot == EquipSlot.OffHand ? weapon.Type : c.OffWeapon;
            var shield = slot != EquipSlot.OffHand && c.OffHandShield;
            if (!c.Equip(main, off, shield))
                return false;
            name = WeaponForge.NameOf(weapon);
        }
        else
        {
            var gear = State.Equips.Get(itemId);
            if (gear == null || !Fits(slot, gear))
                return false;
            // 盾进副手：主手照旧，副手武器让位给盾。
            if (slot == EquipSlot.OffHand && !c.Equip(c.MainWeapon, null, true))
                return false;
            name = EquipForge.NameOf(gear);
        }
        ReturnToBag(c, slot, bag);
        bag.Add(itemId, -1);
        c.SetEquippedId(slot, itemId);
        Write($"{c.Name}换上了{name}。");
        return true;
    }

    /// <summary>卸下某槽（有实例的回背包）。主手在副手还占着时不让卸。</summary>
    public bool UnequipToBag(int characterId, EquipSlot slot)
    {
        var c = State.Roster.Find(characterId);
        var bag = State.Roster.Master?.Bag;
        if (c == null || bag == null)
            return false;
        if (IsWeaponSlot(slot))
        {
            if (slot == EquipSlot.MainHand)
            {
                if (c.MainWeapon == null || c.OffWeapon != null || c.OffHandShield)
                    return false;
                c.Equip(null);
            }
            else
            {
                if (c.OffWeapon == null && !c.OffHandShield)
                    return false;
                c.Equip(c.MainWeapon);
            }
        }
        else if (c.EquippedId(slot).Length == 0)
            return false;
        ReturnToBag(c, slot, bag);
        c.SetEquippedId(slot, "");
        Write($"{c.Name}卸下了{EquipSlots.Label(slot)}的装备。");
        return true;
    }

    private void ReturnToBag(Rimisekai.Character.CharacterState c, EquipSlot slot, Rimisekai.Housing.Stock bag)
    {
        var old = c.EquippedId(slot);
        if (old.Length > 0 && (State.Weapons.Exists(old) || State.Equips.Exists(old)))
            bag.Add(old, 1);
    }
}
