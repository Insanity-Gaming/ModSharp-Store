using Sharp.Shared.GameEntities;

namespace InsanityGaming.Store.Interfaces;

public interface IEquipmentManager
{
    /// <summary>
    /// Equip an item for a player on a specific team.
    /// Validates ownership and team restriction. Replaces any existing item in the same slot+team.
    /// Returns false if item not found, not owned, or team-restricted.
    /// </summary>
    Task<bool> EquipAsync(ulong steamId, string unique, int team);

    /// <summary>Unequip whatever is in the given slot+team for a player.</summary>
    Task<bool> UnequipAsync(ulong steamId, int slot, int team);

    /// <summary>
    /// Apply all equipped items for a player on a specific team to their pawn.
    /// Called on player_spawn. Resolves each equipped item's IItemType and calls Equip().
    /// </summary>
    Task ApplyEquippedItemsAsync(IPlayerPawn pawn, ulong steamId, int team);
}
