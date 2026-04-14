using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Services;

public class PurchaseService : IPurchaseService
{
    private readonly ICredits _credits;
    private readonly IItemService _itemService;
    private readonly IInventoryRepository _inventoryRepo;
    private readonly ILogger<PurchaseService> _logger;

    public PurchaseService(
        ICredits credits,
        IItemService itemService,
        IInventoryRepository inventoryRepo,
        ILogger<PurchaseService> logger)
    {
        _credits = credits;
        _itemService = itemService;
        _inventoryRepo = inventoryRepo;
        _logger = logger;
    }

    public async Task<PurchaseResult> BuyAsync(ulong steamId, PlayerSlot slot, string unique)
    {
        var definition = _itemService.GetByUnique(unique);
        if (definition is null)
        {
            _logger.LogWarning("BuyAsync: unknown item '{Unique}'", unique);
            return PurchaseResult.ItemNotFound;
        }

        if (!definition.Purchasable)
        {
            _logger.LogWarning("BuyAsync: item '{Unique}' is not purchasable", unique);
            return PurchaseResult.NotPurchasable;
        }

        var inventory = await _inventoryRepo.GetByPlayerAsync(steamId);
        if (inventory.Any(i => i.Unique == unique))
        {
            return PurchaseResult.AlreadyOwned;
        }

        if (_credits.GetCreditsBySlot(slot) < definition.Price)
        {
            return PurchaseResult.InsufficientCredits;
        }

        if (!_credits.AddCreditsBySlot(slot, -(int)definition.Price))
        {
            _logger.LogWarning("BuyAsync: failed to deduct {Price} credits from slot {Slot}", definition.Price, slot);
            return PurchaseResult.InsufficientCredits;
        }

        var inserted = await _inventoryRepo.InsertAsync(new Inventory
        {
            Id = steamId,
            Unique = unique,
        });

        if (!inserted)
        {
            // Roll back the credit deduction so the player isn't charged for a failed insert
            _credits.AddCreditsBySlot(slot, (int)definition.Price);
            _logger.LogError("BuyAsync: inventory insert failed for player {SteamId}, item '{Unique}' — credits refunded", steamId, unique);
            return PurchaseResult.ItemNotFound;
        }

        _logger.LogInformation("BuyAsync: player {SteamId} purchased '{Unique}' for {Price} credits", steamId, unique, definition.Price);
        return PurchaseResult.Success;
    }
}
