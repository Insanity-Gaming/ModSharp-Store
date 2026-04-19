using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Repository;
using InsanityGaming.Store.Services;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Logging;

namespace InsanityGaming.Store.Modules;

public class ItemsModule : IModule, IItems
{
    private readonly InterfaceBridge _bridge;
    private readonly Store _plugin;
    private readonly IInventoryRepository _inventoryRepo;
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly SessionItemStore _sessionStore;
    private readonly ILogger<ItemsModule> _logger;

    public ItemsModule(
        InterfaceBridge bridge,
        Store plugin,
        IInventoryRepository inventoryRepo,
        IEquipmentRepository equipmentRepo,
        SessionItemStore sessionStore,
        ILogger<ItemsModule> logger)
    {
        _bridge = bridge;
        _plugin = plugin;
        _inventoryRepo = inventoryRepo;
        _equipmentRepo = equipmentRepo;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public bool Init()
    {
        _bridge.ModuleManager.RegisterSharpModuleInterface<IItemsApi>(
            _plugin,
            IItemsApi.Identity,
            this
        );

        _logger.LogInformation("ItemsModule initialized");
        return true;
    }

    public async Task<bool> GiveItemAsync(ulong steamId, string unique)
    {
        if (await HasItemAsync(steamId, unique))
            return false;
        return await _inventoryRepo.InsertAsync(new Inventory { Id = steamId, Unique = unique });
    }

    public async Task<bool> RemoveItemAsync(ulong steamId, string unique)
    {
        var sessionRemoved = _sessionStore.Remove(steamId, unique);
        var dbItems = await _inventoryRepo.GetByPlayerAsync(steamId);
        if (!dbItems.Any(i => i.Unique == unique))
            return sessionRemoved;
        var equipped = await _equipmentRepo.GetByPlayerAsync(steamId);
        foreach (var row in equipped.Where(e => e.Unique == unique))
            await _equipmentRepo.DeleteByPlayerSlotAndTeamAsync(steamId, row.Slot, row.Team);
        return await _inventoryRepo.DeleteByPlayerAndUniqueAsync(steamId, unique);
    }

    public async Task<bool> HasItemAsync(ulong steamId, string unique)
    {
        if (_sessionStore.Has(steamId, unique))
            return true;
        var items = await _inventoryRepo.GetByPlayerAsync(steamId);
        return items.Any(i => i.Unique == unique);
    }

    public async Task<IReadOnlyList<string>> GetItemsAsync(ulong steamId)
    {
        var dbItems = await _inventoryRepo.GetByPlayerAsync(steamId);
        var sessionItems = _sessionStore.GetAll(steamId);
        return dbItems.Select(i => i.Unique).Union(sessionItems).ToList();
    }

    public Task<bool> GiveTemporaryItemAsync(ulong steamId, string unique)
    {
        if (_sessionStore.Has(steamId, unique))
            return Task.FromResult(false);
        _sessionStore.Add(steamId, unique);
        return Task.FromResult(true);
    }

    public async Task<bool> GiveTimedItemAsync(ulong steamId, string unique, TimeSpan duration)
    {
        if (await HasItemAsync(steamId, unique))
            return false;
        return await _inventoryRepo.InsertAsync(new Inventory
        {
            Id = steamId,
            Unique = unique,
            ExpiresAt = DateTime.UtcNow + duration
        });
    }
}
