using Sharp.Shared.Units;

namespace InsanityGaming.Store.Interfaces;

public enum PurchaseResult
{
    Success,
    ItemNotFound,
    NotPurchasable,
    AlreadyOwned,
    InsufficientCredits,
}

public interface IPurchaseService
{
    /// <summary>
    /// Attempt to purchase an item for a player.
    /// Checks ownership, credits, and purchasability. Deducts credits and inserts inventory on success.
    /// </summary>
    Task<PurchaseResult> BuyAsync(ulong steamId, PlayerSlot slot, string unique);
}
