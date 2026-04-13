using InsanityGaming.Store.Configs;
using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;

namespace InsanityGaming.Store.Modules;

public class EquipmentModule : IModule, IEventListener
{
    private readonly InterfaceBridge _bridge;
    private readonly IEquipmentManager _equipmentManager;
    private readonly IItemService _itemService;
    private readonly ILogger<EquipmentModule> _logger;

    public EquipmentModule(
        InterfaceBridge bridge,
        IEquipmentManager equipmentManager,
        IItemService itemService,
        ILogger<EquipmentModule> logger)
    {
        _bridge = bridge;
        _equipmentManager = equipmentManager;
        _itemService = itemService;
        _logger = logger;
    }

    public bool Init()
    {
        _itemService.LoadAll();
        _bridge.EventManager.InstallEventListener(this);
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
        if (!e.Name.Equals("player_spawn", StringComparison.OrdinalIgnoreCase))
            return;

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

    int IEventListener.ListenerVersion => IEventListener.ApiVersion;
    int IEventListener.ListenerPriority => 0;
}
