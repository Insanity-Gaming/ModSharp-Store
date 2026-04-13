using InsanityGaming.Store.Items;

namespace InsanityGaming.Store.Interfaces;

public interface IItemService
{
    /// <summary>Load all enabled item type JSON files. Called during Init.</summary>
    void LoadAll();

    /// <summary>Get item definition by its unique identifier. Returns null if not found.</summary>
    ItemDefinition? GetByUnique(string unique);

    /// <summary>Get all purchasable item definitions.</summary>
    IReadOnlyList<ItemDefinition> GetPurchasable();

    /// <summary>Get all item definitions belonging to a specific equipment slot.</summary>
    IReadOnlyList<ItemDefinition> GetBySlot(int slot);

    /// <summary>
    /// Get all item definitions accessible to a player with the given permission.
    /// Returns items with no permission requirement plus items matching the given permission.
    /// Pass null for players with no special permissions.
    /// </summary>
    IReadOnlyList<ItemDefinition> GetAccessible(string? permission);

    /// <summary>Resolve the IItemType handler for a given item unique. Returns null if type not loaded.</summary>
    IItemType? GetItemType(string unique);
}
