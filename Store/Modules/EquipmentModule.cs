using InsanityGaming.Store.Configs;
using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Repository;
using InsanityGaming.Store.Services;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;

namespace InsanityGaming.Store.Modules;

public class EquipmentModule : IModule, IEventListener, IEquipment
{
    private readonly InterfaceBridge _bridge;
    private readonly Store _plugin;
    private readonly IEquipmentManager _equipmentManager;
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly IItemService _itemService;
    private readonly SessionItemStore _sessionStore;
    private readonly ILogger<EquipmentModule> _logger;

    public EquipmentModule(
        InterfaceBridge bridge,
        Store plugin,
        IEquipmentManager equipmentManager,
        IEquipmentRepository equipmentRepo,
        IItemService itemService,
        SessionItemStore sessionStore,
        ILogger<EquipmentModule> logger)
    {
        _bridge = bridge;
        _plugin = plugin;
        _equipmentManager = equipmentManager;
        _equipmentRepo = equipmentRepo;
        _itemService = itemService;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public bool Init()
    {
        _itemService.LoadAll();
        _bridge.EventManager.InstallEventListener(this);

        _bridge.ModuleManager.RegisterSharpModuleInterface<IEquipmentApi>(
            _plugin,
            IEquipmentApi.Identity,
            this
        );

        _logger.LogInformation("EquipmentModule initialized");
        return true;
    }

    public void Shutdown()
    {
        _bridge.EventManager.RemoveEventListener(this);
    }

    public void ConfigReload(IConfig config)
    {
        if (config is not StoreConfig)
            return;

        _itemService.LoadAll();
        _logger.LogInformation("Item definitions reloaded");
    }

    public void FireGameEvent(IGameEvent e)
    {
        if (e.Name.Equals("player_spawn", StringComparison.OrdinalIgnoreCase))
            HandlePlayerSpawn(e);
        else if (e.Name.Equals("player_disconnect", StringComparison.OrdinalIgnoreCase))
            HandlePlayerDisconnect(e);
    }

    private void HandlePlayerSpawn(IGameEvent e)
    {
        var slot = e.GetInt("userid");

        var controller = _bridge.EntityManager
            .GetPlayerControllers()
            .FirstOrDefault(c => (int)c.PlayerSlot == slot);

        if (controller is null) return;

        var pawn = controller.GetPlayerPawn();
        if (pawn is null) return;

        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.Slot == controller.PlayerSlot);

        if (client is null) return;

        var team = (int)controller.Team;

        _ = _equipmentManager.ApplyEquippedItemsAsync(pawn, client.SteamId, team);
    }

    private void HandlePlayerDisconnect(IGameEvent e)
    {
        var slot = e.GetInt("userid");

        var controller = _bridge.EntityManager
            .GetPlayerControllers()
            .FirstOrDefault(c => (int)c.PlayerSlot == slot);

        if (controller is null) return;

        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.Slot == controller.PlayerSlot);

        if (client is null) return;

        _sessionStore.Clear(client.SteamId);
    }

    // -------------------------
    // IEquipmentApi
    // -------------------------

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

    int IEventListener.ListenerVersion => IEventListener.ApiVersion;
    int IEventListener.ListenerPriority => 0;
}
