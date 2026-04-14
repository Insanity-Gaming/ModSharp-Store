using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Logging;

namespace InsanityGaming.Store.Api;

public class EquipmentApiAdapter : IEquipmentApi
{
    private readonly IEquipmentManager _equipmentManager;
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly ILogger<EquipmentApiAdapter> _logger;

    public EquipmentApiAdapter(
        IEquipmentManager equipmentManager,
        IEquipmentRepository equipmentRepo,
        ILogger<EquipmentApiAdapter> logger)
    {
        _equipmentManager = equipmentManager;
        _equipmentRepo = equipmentRepo;
        _logger = logger;
    }

    public Task<bool> EquipAsync(ulong steamId, string unique, int team) =>
        _equipmentManager.EquipAsync(steamId, unique, team);

    public Task<bool> UnequipAsync(ulong steamId, int slot, int team) =>
        _equipmentManager.UnequipAsync(steamId, slot, team);

    public async Task<IReadOnlyList<EquippedItemDto>> GetEquippedItemsAsync(ulong steamId)
    {
        var rows = await _equipmentRepo.GetByPlayerAsync(steamId);
        return rows.Select(e => new EquippedItemDto { Unique = e.Unique, Slot = e.Slot, Team = e.Team }).ToList();
    }

    public async Task<IReadOnlyList<EquippedItemDto>> GetEquippedItemsByTeamAsync(ulong steamId, int team)
    {
        var rows = await _equipmentRepo.GetByPlayerAndTeamAsync(steamId, team);
        return rows.Select(e => new EquippedItemDto { Unique = e.Unique, Slot = e.Slot, Team = e.Team }).ToList();
    }
}
