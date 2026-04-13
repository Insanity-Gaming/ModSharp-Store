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
    private readonly ILogger<EquipmentManager> _logger;

    public EquipmentManager(
        IItemService itemService,
        IEquipmentRepository equipmentRepo,
        IInventoryRepository inventoryRepo,
        ILogger<EquipmentManager> logger)
    {
        _itemService = itemService;
        _equipmentRepo = equipmentRepo;
        _inventoryRepo = inventoryRepo;
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

        // Team restriction: 0 = any team allowed
        if (definition.Team != 0 && definition.Team != team)
        {
            _logger.LogWarning("EquipAsync: item '{Unique}' is team-restricted (item team {ItemTeam}, player team {PlayerTeam})",
                unique, definition.Team, team);
            return false;
        }

        // Ownership check
        var inventory = await _inventoryRepo.GetByPlayerAsync(steamId);
        if (!inventory.Any(i => i.Unique == unique))
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

        // Remove any existing item in this slot+team, then insert new row
        await _equipmentRepo.DeleteByPlayerSlotAndTeamAsync(steamId, itemType.Slot, team);

        return await _equipmentRepo.InsertAsync(new Equipment
        {
            Id = steamId,
            Unique = unique,
            Slot = itemType.Slot,
            Team = team
        });
    }

    public async Task<bool> UnequipAsync(ulong steamId, int slot, int team)
    {
        return await _equipmentRepo.DeleteByPlayerSlotAndTeamAsync(steamId, slot, team);
    }

    public async Task ApplyEquippedItemsAsync(IPlayerPawn pawn, ulong steamId, int team)
    {
        var equipped = await _equipmentRepo.GetByPlayerAndTeamAsync(steamId, team);

        foreach (var equipment in equipped)
        {
            var definition = _itemService.GetByUnique(equipment.Unique);
            if (definition is null) continue;

            var itemType = _itemService.GetItemType(equipment.Unique);
            if (itemType is null) continue;

            itemType.Equip(pawn, definition);
        }
    }
}
