using InsanityGaming.Store.Items;

namespace InsanityGaming.Store.Hud;

public enum ItemAvailability
{
    Available,
    Locked,
    Unaffordable,
}

/// <summary>
/// Pure display rules for the store HUD: whether an item can be bought or equipped, the list
/// order, the copy shown when it can't, and how category sections split around the open one.
/// </summary>
public static class StoreItemRules
{
    private const int TeamT  = 2;
    private const int TeamCT = 3;

    private const string DefaultPermissionText = "You don't have access to this item.";

    /// <summary>
    /// Locked when the item needs a permission the player lacks; otherwise unaffordable when
    /// buying with fewer credits than the price. Locked wins, since credits can't fix it.
    /// </summary>
    public static ItemAvailability Evaluate(ItemDefinition item, bool hasPermission, uint credits, bool buying)
    {
        if (item.Permission is not null && !hasPermission)
            return ItemAvailability.Locked;

        return buying && credits < item.Price ? ItemAvailability.Unaffordable : ItemAvailability.Available;
    }

    /// <summary>Price descending, then name ascending (case-insensitive).</summary>
    public static List<ItemDefinition> Order(IEnumerable<ItemDefinition> items)
        => items.OrderByDescending(i => i.Price)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>Text for the disabled action of an item that can't be bought or equipped; null when it can.</summary>
    public static string? BlockedActionText(ItemAvailability availability, uint price, uint credits) => availability switch
    {
        ItemAvailability.Unaffordable => $"Need {price - credits} more credits",
        ItemAvailability.Locked       => "Locked",
        _                             => null,
    };

    /// <summary>Right-panel explanation for a locked item.</summary>
    public static string PermissionText(ItemDefinition item)
        => item.PermissionDescription ?? item.PermissionError ?? DefaultPermissionText;

    /// <summary>
    /// Section headers at or before the open one sit above the item list, the rest below it.
    /// With no section open (or an out-of-range index) every header sits above.
    /// </summary>
    public static (int[] Above, int[] Below) SplitSections(int count, int openIndex)
    {
        var split = openIndex >= 0 && openIndex < count ? openIndex + 1 : count;
        return (Enumerable.Range(0, split).ToArray(), Enumerable.Range(split, count - split).ToArray());
    }

    /// <summary>
    /// Team the preview model represents: the player's own team when on T or CT, otherwise the
    /// item's team when it is single-team, otherwise T.
    /// </summary>
    public static int PreviewTeam(int playerTeam, int itemTeam)
    {
        if (playerTeam is TeamT or TeamCT)
            return playerTeam;

        return itemTeam is TeamT or TeamCT ? itemTeam : TeamT;
    }
}
