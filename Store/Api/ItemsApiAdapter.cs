using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Logging;

namespace InsanityGaming.Store.Api;

public class ItemsApiAdapter : IItemsApi
{
    private readonly IInventoryRepository _inventoryRepo;
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly ILogger<ItemsApiAdapter> _logger;

    public ItemsApiAdapter(
        IInventoryRepository inventoryRepo,
        IEquipmentRepository equipmentRepo,
        ILogger<ItemsApiAdapter> logger)
    {
        _inventoryRepo = inventoryRepo;
        _equipmentRepo = equipmentRepo;
        _logger = logger;
    }

    public async Task<bool> GiveItemAsync(ulong steamId, string unique)
    {
        if (await HasItemAsync(steamId, unique))
            return false;

        return await _inventoryRepo.InsertAsync(new Inventory { Id = steamId, Unique = unique });
    }

    public async Task<bool> RemoveItemAsync(ulong steamId, string unique)
    {
        if (!await HasItemAsync(steamId, unique))
            return false;

        var equipped = await _equipmentRepo.GetByPlayerAsync(steamId);
        foreach (var row in equipped.Where(e => e.Unique == unique))
        {
            await _equipmentRepo.DeleteByPlayerSlotAndTeamAsync(steamId, row.Slot, row.Team);
        }

        return await _inventoryRepo.DeleteByPlayerAndUniqueAsync(steamId, unique);
    }

    public async Task<bool> HasItemAsync(ulong steamId, string unique)
    {
        var items = await _inventoryRepo.GetByPlayerAsync(steamId);
        return items.Any(i => i.Unique == unique);
    }

    public async Task<IReadOnlyList<string>> GetItemsAsync(ulong steamId)
    {
        var items = await _inventoryRepo.GetByPlayerAsync(steamId);
        return items.Select(i => i.Unique).ToList();
    }
}
