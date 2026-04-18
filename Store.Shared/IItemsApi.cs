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

    /// <summary>
    /// Grants a session-only item to a player. Never persisted to the database.
    /// Automatically removed when the player disconnects.
    /// Returns false if the player already has the item (session or persistent).
    /// </summary>
    Task<bool> GiveTemporaryItemAsync(ulong steamId, string unique);

    /// <summary>
    /// Grants a timed item to a player. Persisted to the database with an expiry timestamp.
    /// Survives reconnects and is silently removed after the duration elapses.
    /// Returns false if the player already has the item (session or persistent).
    /// </summary>
    Task<bool> GiveTimedItemAsync(ulong steamId, string unique, TimeSpan duration);
}
