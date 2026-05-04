using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using Microsoft.Extensions.Logging;
using Sharp.Shared.GameEntities;

namespace InsanityGaming.Store.Services;

public class EquipmentManager : IEquipmentManager
{
    private readonly IItemService _itemService;
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly IInventoryRepository _inventoryRepo;
    private readonly SessionItemStore _sessionStore;
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<EquipmentManager> _logger;

    public EquipmentManager(
        IItemService itemService,
        IEquipmentRepository equipmentRepo,
        IInventoryRepository inventoryRepo,
        SessionItemStore sessionStore,
        InterfaceBridge bridge,
        ILogger<EquipmentManager> logger)
    {
        _itemService = itemService;
        _equipmentRepo = equipmentRepo;
        _inventoryRepo = inventoryRepo;
        _sessionStore = sessionStore;
        _bridge = bridge;
        _logger = logger;
    }

    public async Task<bool> EquipAsync(ulong steamId, string unique, int team)
    {
        var definition = _itemService.GetByUnique(unique);
        if (definition is null)
        {
            _logger.LogWarning("EquipAsync: unknown item '{Unique}'", unique);
            return false;
        }

        if (definition.Team != 0 && definition.Team != team)
        {
            _logger.LogWarning("EquipAsync: item '{Unique}' is team-restricted (item team {ItemTeam}, player team {PlayerTeam})",
                unique, definition.Team, team);
            return false;
        }

        var inventory = await _inventoryRepo.GetByPlayerAsync(steamId);
        if (!inventory.Any(i => i.Unique == unique) && !_sessionStore.Has(steamId, unique))
        {
            _logger.LogWarning("EquipAsync: player {SteamId} does not own item '{Unique}'", steamId, unique);
            return false;
        }

        var itemType = _itemService.GetItemType(unique);
        if (itemType is null)
        {
            _logger.LogWarning("EquipAsync: no item type registered for '{Unique}'", unique);
            return false;
        }

        await _equipmentRepo.DeleteByPlayerSlotAndTeamAsync(steamId, itemType.Slot, team);

        return await _equipmentRepo.InsertAsync(new Equipment
        {
            Id       = steamId,
            ServerId = _bridge.ServerAddress(),
            Unique   = unique,
            Slot     = itemType.Slot,
            Team     = team
        });
    }

    public async Task<bool> UnequipAsync(ulong steamId, int slot, int team)
    {
        return await _equipmentRepo.DeleteByPlayerSlotAndTeamAsync(steamId, slot, team);
    }

    public async Task ApplyEquippedItemsAsync(IPlayerPawn pawn, ulong steamId, int team)
    {
        _logger.LogInformation("[Apply] ApplyEquippedItems: steamId={SteamId} team={Team}", steamId, team);

        var equipped = await _equipmentRepo.GetByPlayerAndTeamAsync(steamId, team);
        var inventory = await _inventoryRepo.GetByPlayerAsync(steamId);
        var ownedUniques = inventory.Select(i => i.Unique).ToHashSet(StringComparer.Ordinal);

        _logger.LogInformation("[Apply] Found {EquippedCount} equipped row(s), {InvCount} inventory item(s)", equipped.Count(), ownedUniques.Count);

        foreach (var equipment in equipped)
        {
            var owned = ownedUniques.Contains(equipment.Unique) || _sessionStore.Has(steamId, equipment.Unique);
            if (!owned)
            {
                _logger.LogWarning("[Apply] Skipping '{Unique}' — not owned by {SteamId}", equipment.Unique, steamId);
                continue;
            }

            var definition = _itemService.GetByUnique(equipment.Unique);
            if (definition is null)
            {
                _logger.LogWarning("[Apply] No definition found for '{Unique}'", equipment.Unique);
                continue;
            }

            var itemType = _itemService.GetItemType(equipment.Unique);
            if (itemType is null)
            {
                _logger.LogWarning("[Apply] No item type registered for '{Unique}'", equipment.Unique);
                continue;
            }

            _logger.LogInformation("[Apply] Equipping '{Unique}' via {TypeName}", equipment.Unique, itemType.TypeName);
            itemType.Equip(pawn, definition);
        }
    }
}
