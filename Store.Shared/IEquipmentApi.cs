namespace InsanityGaming.Store.Shared;

public interface IEquipmentApi
{
    const string Identity = "InsanityGaming.Store.Shared.IEquipmentApi";

    /// <summary>
    /// Equips an item in the appropriate slot for the given team.
    /// Validates ownership and team restriction. Replaces any existing item in the same slot+team.
    /// Returns false if the item is unknown, not owned by the player, or team-restricted.
    /// </summary>
    Task<bool> EquipAsync(ulong steamId, string unique, int team);

    /// <summary>
    /// Unequips whatever item is in the given slot+team for the player.
    /// Returns false if nothing was equipped there.
    /// </summary>
    Task<bool> UnequipAsync(ulong steamId, int slot, int team);

    /// <summary>
    /// Returns all equipped items for the player across all slots and teams.
    /// Returns an empty list if the player has nothing equipped.
    /// </summary>
    Task<IReadOnlyList<EquippedItemDto>> GetEquippedItemsAsync(ulong steamId);

    /// <summary>
    /// Returns all equipped items for the player on a specific team.
    /// Returns an empty list if the player has nothing equipped for that team.
    /// </summary>
    Task<IReadOnlyList<EquippedItemDto>> GetEquippedItemsByTeamAsync(ulong steamId, int team);
}
