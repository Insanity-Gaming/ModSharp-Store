using InsanityGaming.Store.Configs;
using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Services;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Logging;
using Sharp.Shared.HookParams;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;

namespace InsanityGaming.Store.Modules;

public class EquipmentModule : IModule, IEventListener, IClientListener, IEquipment
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
        _bridge.HookManager.PlayerSpawnPost.InstallForward(OnPlayerSpawnPost);
        _bridge.ClientManager.InstallClientListener(this);

        _logger.LogInformation("EquipmentModule initialized");
        return true;
    }

    public void Shutdown()
    {
        _bridge.HookManager.PlayerSpawnPost.RemoveForward(OnPlayerSpawnPost);
        _bridge.ClientManager.RemoveClientListener(this);
        _bridge.EventManager.RemoveEventListener(this);
    }

    public void ConfigReload(IConfig config)
    {
        if (config is not StoreConfig)
            return;

        _itemService.LoadAll();
        _logger.LogInformation("Item definitions reloaded");
    }

    // -------------------------
    // IHookManager — spawn
    // -------------------------

    private void OnPlayerSpawnPost(IPlayerSpawnForwardParams obj)
    {
        _logger.LogInformation("[Spawn] OnPlayerSpawnPost fired");

        var pawn = obj.Pawn;
        var controller = pawn.GetController();
        if (controller is null)
        {
            _logger.LogWarning("[Spawn] pawn.GetController() returned null — skipping equipment");
            return;
        }

        _logger.LogInformation("[Spawn] Controller resolved: slot={Slot} name={Name}", controller.PlayerSlot, controller.PlayerName);

        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.Slot == controller.PlayerSlot);
        if (client is null)
        {
            _logger.LogWarning("[Spawn] No game client found for slot={Slot} — skipping equipment", controller.PlayerSlot);
            return;
        }

        _logger.LogInformation("[Spawn] Client matched: steamId={SteamId} team={Team}", client.SteamId, (int)controller.Team);

        var team = (int)controller.Team;
        _ = _equipmentManager.ApplyEquippedItemsAsync(pawn, client.SteamId, team);
    }

    // -------------------------
    // IClientListener — disconnect
    // -------------------------

    public void OnClientConnected(IGameClient client)
    {
        _logger.LogInformation("[Client] Connected: steamId={SteamId} slot={Slot}", client.SteamId, client.Slot);
    }

    public void OnClientDisconnected(IGameClient client)
    {
        _logger.LogInformation("[Client] Disconnected: steamId={SteamId} slot={Slot}", client.SteamId, client.Slot);
        _sessionStore.Clear(client.SteamId);
    }

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    // -------------------------
    // IEventListener — future events
    // -------------------------

    public void FireGameEvent(IGameEvent e) { }

    int IEventListener.ListenerVersion => IEventListener.ApiVersion;
    int IEventListener.ListenerPriority => 0;

    // -------------------------
    // IEquipment API
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
}
