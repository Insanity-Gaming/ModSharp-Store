using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Modules;

public class StoreMenuModule : IModule, IClientListener
{
    private const string MenuManagerAssemblyName  = "Sharp.Modules.MenuManager";
    private const string AdminManagerAssemblyName = "Sharp.Modules.AdminManager";

    private readonly InterfaceBridge _bridge;
    private readonly IItemService _itemService;
    private readonly IItemType[] _itemTypes;
    private readonly IPurchaseService _purchaseService;
    private readonly IEquipmentManager _equipmentManager;
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly IInventoryRepository _inventoryRepo;
    private readonly ICredits _credits;
    private readonly ILogger<StoreMenuModule> _logger;

    private IModSharpModuleInterface<IMenuManager>? _menuManager;
    private IModSharpModuleInterface<IAdminManager>? _adminManager;

    // Static menus, built in Init() once item types are loaded
    private Menu _mainMenu = null!;

    private sealed class PlayerMenuCache
    {
        public HashSet<string> OwnedUniques { get; set; } = [];
        public List<Equipment> Equipped     { get; set; } = [];
        public uint            Credits      { get; set; }
    }

    private readonly Dictionary<ulong, PlayerMenuCache> _cache            = new();
    private readonly HashSet<ulong>                    _pendingPurchases  = [];

    public StoreMenuModule(
        InterfaceBridge bridge,
        IItemService itemService,
        IEnumerable<IItemType> itemTypes,
        IPurchaseService purchaseService,
        IEquipmentManager equipmentManager,
        IEquipmentRepository equipmentRepo,
        IInventoryRepository inventoryRepo,
        ICredits credits,
        ILogger<StoreMenuModule> logger)
    {
        _bridge           = bridge;
        _itemService      = itemService;
        _itemTypes        = itemTypes.ToArray();
        _purchaseService  = purchaseService;
        _equipmentManager = equipmentManager;
        _equipmentRepo    = equipmentRepo;
        _inventoryRepo    = inventoryRepo;
        _credits          = credits;
        _logger           = logger;
    }

    // -------------------------
    // IModule
    // -------------------------

    public bool Init()
    {
        _bridge.ClientManager.InstallCommandCallback("store", OnStoreCommand);
        _bridge.ClientManager.InstallCommandCallback("shop",  OnStoreCommand);
        _bridge.ClientManager.InstallClientListener(this);

        BuildStaticMenus();

        TryResolveMenuManager();
        TryResolveAdminManager();

        _logger.LogInformation("StoreMenuModule initialized");
        return true;
    }

    public void Shutdown()
    {
        _bridge.ClientManager.RemoveCommandCallback("store", OnStoreCommand);
        _bridge.ClientManager.RemoveCommandCallback("shop",  OnStoreCommand);
        _bridge.ClientManager.RemoveClientListener(this);
    }

    public void ConfigReload(IConfig config) { }

    public void OnLibraryConnected(string name)
    {
        if (name.Equals(MenuManagerAssemblyName, StringComparison.OrdinalIgnoreCase))
            TryResolveMenuManager();
        else if (name.Equals(AdminManagerAssemblyName, StringComparison.OrdinalIgnoreCase))
            TryResolveAdminManager();
    }

    public void OnLibraryDisconnect(string name) { }

    public void OnAllModulesLoaded()
    {
        TryResolveMenuManager(logFailure: true);
        TryResolveAdminManager();
    }

    // -------------------------
    // IClientListener
    // -------------------------

    public void OnClientConnected(IGameClient client) { }

    public void OnClientDisconnected(IGameClient client)
    {
        _cache.Remove(client.SteamId);
        _pendingPurchases.Remove(client.SteamId);
    }

    int IClientListener.ListenerVersion => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    // -------------------------
    // External module resolution
    // -------------------------

    private void TryResolveMenuManager(bool logFailure = false)
    {
        if (_menuManager?.Instance is not null)
            return;

        _menuManager = _bridge.ModuleManager
                              .GetOptionalSharpModuleInterface<IMenuManager>(IMenuManager.Identity);

        if (_menuManager?.Instance is null && logFailure)
        {
            _logger.LogWarning("MenuManager is not installed. The store menu will not be available.");
        }
    }

    private void TryResolveAdminManager()
    {
        if (_adminManager?.Instance is not null)
            return;

        _adminManager = _bridge.ModuleManager
                               .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity);
    }

    // -------------------------
    // Commands
    // -------------------------

    private ECommandAction OnStoreCommand(IGameClient client, StringCommand cmd)
    {
        // Lazily resolve MenuManager if it wasn't available at Init time
        if (_menuManager?.Instance is null)
            TryResolveMenuManager();

        if (_menuManager?.Instance is not { } menuManager)
        {
            _logger.LogWarning("OnStoreCommand: MenuManager is not available");
            return ECommandAction.Handled;
        }

        _ = OpenStoreAsync(client, menuManager);
        return ECommandAction.Handled;
    }

    private async Task OpenStoreAsync(IGameClient client, IMenuManager menuManager)
    {
        await RefreshCacheAsync(client);
        menuManager.DisplayMenu(client, _mainMenu);
    }

    // -------------------------
    // Static menu construction
    // -------------------------

    private void BuildStaticMenus()
    {
        var buyBuilder   = Menu.Create().Title("Buy Items");
        var equipBuilder = Menu.Create().Title("Equip Items");

        foreach (var type in _itemTypes)
        {
            var capturedType = type;

            buyBuilder.Item(type.TypeName, controller =>
                controller.Next(BuildBuyItemList(capturedType, controller.Client)));

            equipBuilder.Item(type.TypeName, controller =>
                controller.Next(BuildEquipItemList(capturedType, controller.Client)));
        }

        var buyCategories   = buyBuilder.BackItem().ExitItem().Build();
        var equipCategories = equipBuilder.BackItem().ExitItem().Build();

        _mainMenu = Menu.Create()
                        .Title("Store")
                        .SubMenu("Buy Items",   buyCategories)
                        .SubMenu("Equip Items", equipCategories)
                        .Item("Equipped Items", controller =>
                            controller.Next(BuildEquippedMenu(controller.Client)))
                        .ExitItem()
                        .Build();

        _mainMenu.SetTitle(gameClient =>
        {
            var cache = GetOrCreateCache(gameClient.SteamId);
            return $"Store — {cache.Credits} credits";
        });
    }

    // -------------------------
    // Dynamic menu builders
    // -------------------------

    private Menu BuildBuyItemList(IItemType type, IGameClient client)
    {
        var menu = new Menu();

        // Title re-evaluated each render so credits stay current
        menu.SetTitle(gameClient =>
        {
            var cache = GetOrCreateCache(gameClient.SteamId);
            return $"{type.TypeName} — {cache.Credits} credits";
        });

        foreach (var item in _itemService.GetBySlot(type.Slot))
        {
            var capturedItem = item;

            menu.AddItem((IGameClient gameClient, ref MenuItemContext ctx) =>
            {
                var cache = GetOrCreateCache(gameClient.SteamId);

                if (cache.OwnedUniques.Contains(capturedItem.Unique))
                {
                    ctx.Title = $"{capturedItem.Name} [OWNED]";
                    ctx.State = MenuItemState.Disabled;
                    return;
                }

                ctx.Title  = $"{capturedItem.Name} [{capturedItem.Price}cr]";
                ctx.Action = menuController => _ = DoBuyAsync(menuController, capturedItem);
            });
        }

        menu.AddBackItem();
        menu.AddExitItem();

        return menu;
    }

    private Menu BuildEquipItemList(IItemType type, IGameClient client)
    {
        var menu = new Menu();
        menu.SetTitle(type.TypeName);

        foreach (var item in _itemService.GetBySlot(type.Slot))
        {
            var capturedItem = item;

            menu.AddItem((IGameClient gameClient, ref MenuItemContext ctx) =>
            {
                var cache = GetOrCreateCache(gameClient.SteamId);

                if (!cache.OwnedUniques.Contains(capturedItem.Unique))
                {
                    ctx.Title = capturedItem.Name;
                    ctx.State = MenuItemState.Disabled;
                    return;
                }

                if (cache.Equipped.Any(e => e.Unique == capturedItem.Unique))
                {
                    ctx.Title = $"{capturedItem.Name} [EQUIPPED]";
                    ctx.State = MenuItemState.Disabled;
                    return;
                }

                ctx.Title  = capturedItem.Name;
                ctx.Action = menuController => _ = DoEquipAsync(menuController, capturedItem);
            });
        }

        menu.AddBackItem();
        menu.AddExitItem();

        return menu;
    }

    private Menu BuildEquippedMenu(IGameClient client)
    {
        var menu  = new Menu();
        menu.SetTitle("Equipped Items");

        var cache = GetOrCreateCache(client.SteamId);

        if (cache.Equipped.Count == 0)
        {
            menu.AddDisabledItem("Nothing equipped");
        }
        else
        {
            foreach (var equipment in cache.Equipped)
            {
                var capturedEquipment = equipment;
                var definition        = _itemService.GetByUnique(equipment.Unique);
                var typeName          = _itemTypes.FirstOrDefault(t => t.Slot == equipment.Slot)?.TypeName ?? "Unknown";
                var displayName       = definition?.Name ?? equipment.Unique;

                menu.AddItem($"{typeName}: {displayName}", menuController =>
                    _ = DoUnequipAsync(menuController, capturedEquipment));
            }
        }

        menu.AddBackItem();
        menu.AddExitItem();

        return menu;
    }

    // -------------------------
    // Buy action
    // -------------------------

    private async Task DoBuyAsync(IMenuController controller, Items.ItemDefinition item)
    {
        var client = controller.Client;

        if (!_pendingPurchases.Add(client.SteamId))
            return;

        try
        {
            var result = await _purchaseService.BuyAsync(client.SteamId, client.Slot, item.Unique);

            if (result != PurchaseResult.Success)
            {
                var message = result switch
                {
                    PurchaseResult.InsufficientCredits => $"You need {item.Price} credits to buy {item.Name}.",
                    PurchaseResult.AlreadyOwned        => $"You already own {item.Name}.",
                    PurchaseResult.NotPurchasable      => $"{item.Name} is not for sale.",
                    _                                  => "Purchase failed. Please try again.",
                };

                client.Print(HudPrintChannel.Chat, message);
                return;
            }

            client.Print(HudPrintChannel.Chat, $"You purchased {item.Name}!");
            await RefreshCacheAsync(client);
            controller.Next(_mainMenu);
        }
        finally
        {
            _pendingPurchases.Remove(client.SteamId);
        }
    }

    // -------------------------
    // Equip action
    // -------------------------

    private async Task DoEquipAsync(IMenuController controller, Items.ItemDefinition item)
    {
        var client     = controller.Client;
        var controller2 = client.GetPlayerController();
        var pawn       = controller2?.GetPlayerPawn();

        // Must be alive
        if (pawn is null || !pawn.IsAlive)
        {
            client.Print(HudPrintChannel.Chat, "You must be alive to equip items.");
            return;
        }

        var playerTeam = (int)controller2!.Team;

        // Team restriction
        if (item.Team != 0 && item.Team != playerTeam)
        {
            client.Print(HudPrintChannel.Chat, "This item is restricted to the other team.");
            return;
        }

        // Permission check
        if (item.Permission is not null)
        {
            // Lazily resolve AdminManager if needed
            if (_adminManager?.Instance is null)
                TryResolveAdminManager();

            var steamId       = new SteamID(client.SteamId);
            var hasPermission = _adminManager?.Instance?.GetAdmin(steamId)?.HasPermission(item.Permission) ?? false;

            if (!hasPermission)
            {
                client.Print(HudPrintChannel.Chat, "You do not have permission to equip this item.");
                return;
            }
        }

        var success = await _equipmentManager.EquipAsync(client.SteamId, item.Unique, playerTeam);

        if (!success)
        {
            client.Print(HudPrintChannel.Chat, $"Failed to equip {item.Name}.");
            return;
        }

        client.Print(HudPrintChannel.Chat, $"{item.Name} equipped!");
        await RefreshCacheAsync(client);
        controller.Refresh();
    }

    // -------------------------
    // Unequip action
    // -------------------------

    private async Task DoUnequipAsync(IMenuController controller, Equipment equipment)
    {
        var client = controller.Client;

        var success = await _equipmentManager.UnequipAsync(client.SteamId, equipment.Slot, equipment.Team);

        if (!success)
        {
            client.Print(HudPrintChannel.Chat, "Failed to unequip item.");
            return;
        }

        var definition = _itemService.GetByUnique(equipment.Unique);
        client.Print(HudPrintChannel.Chat, $"{definition?.Name ?? equipment.Unique} unequipped.");

        await RefreshCacheAsync(client);

        // Rebuild the equipped menu so the list reflects the new state
        controller.Next(BuildEquippedMenu(client));
    }

    // -------------------------
    // Cache
    // -------------------------

    private PlayerMenuCache GetOrCreateCache(ulong steamId)
    {
        if (!_cache.TryGetValue(steamId, out var cache))
        {
            cache           = new PlayerMenuCache();
            _cache[steamId] = cache;
        }

        return cache;
    }

    private async Task RefreshCacheAsync(IGameClient client)
    {
        var steamId    = client.SteamId;
        var playerTeam = (int)(client.GetPlayerController()?.Team ?? CStrikeTeam.UnAssigned);

        var inventory = await _inventoryRepo.GetByPlayerAsync(steamId);

        var equipped = playerTeam > (int)CStrikeTeam.Spectator
            ? await _equipmentRepo.GetByPlayerAndTeamAsync(steamId, playerTeam)
            : [];

        var credits = _credits.GetCreditsBySlot(client.Slot);

        var cache = GetOrCreateCache(steamId);
        cache.OwnedUniques = [..inventory.Select(i => i.Unique)];
        cache.Equipped     = equipped;
        cache.Credits      = credits;
    }
}
