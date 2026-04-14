namespace InsanityGaming.Store.Shared;

public interface IItemsApi
{
    const string Identity = "InsanityGaming.Store.Shared.IItemsApi";

    /// <summary>
    /// Force-grants an item to a player. Does not check ownership, credits, or purchasability.
    /// Returns false if the player already owns the item or the DB insert fails.
    /// </summary>
    Task<bool> GiveItemAsync(ulong steamId, string unique);

    /// <summary>
    /// Removes an item from a player's inventory. Also unequips it from all
    /// slot+team combinations where it is currently equipped.
    /// Returns false if the player does not own the item.
    /// </summary>
    Task<bool> RemoveItemAsync(ulong steamId, string unique);

    /// <summary>
    /// Returns true if the player owns the item with the given unique identifier.
    /// </summary>
    Task<bool> HasItemAsync(ulong steamId, string unique);

    /// <summary>
    /// Returns all unique identifiers for items in the player's inventory.
    /// Returns an empty list if the player has no items or does not exist.
    /// </summary>
    Task<IReadOnlyList<string>> GetItemsAsync(ulong steamId);
}
