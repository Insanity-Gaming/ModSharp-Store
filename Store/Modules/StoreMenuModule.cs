using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Items;
using InsanityGaming.Store.Models;
using InsanityGaming.Store.Services;
using Microsoft.Extensions.Logging;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Modules;

public class StoreMenuModule : IModule, IClientListener
{
    private const string MenuManagerAssemblyName = "Sharp.Modules.MenuManager";

    // Color palette
    private const string ColorAccent   = "#FFB400";
    private const string ColorPositive = "#7CFC8E";
    private const string ColorEquipped = "#7AB8FF";
    private const string ColorMuted    = "#9AA0A6";

    private readonly InterfaceBridge _bridge;
    private readonly IItemService _itemService;
    private readonly IItemType[] _itemTypes;
    private readonly IPurchaseService _purchaseService;
    private readonly IEquipmentManager _equipmentManager;
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly IInventoryRepository _inventoryRepo;
    private readonly ICredits _credits;
    private readonly AdminManagerHolder _adminManager;
    private readonly ILogger<StoreMenuModule> _logger;

    private IModSharpModuleInterface<IMenuManager>? _menuManager;

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
        ILogger<StoreMenuModule> logger,
        AdminManagerHolder adminManager)
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
        _adminManager = adminManager;
    }

    // -------------------------
    // IModule
    // -------------------------

    public bool Init()
    {
        _bridge.ClientManager.InstallCommandCallback("store",       OnStoreCommand);
        _bridge.ClientManager.InstallCommandCallback("shop",        OnStoreCommand);
        _bridge.ClientManager.InstallClientListener(this);

        TryResolveMenuManager();

        _logger.LogInformation("StoreMenuModule initialized");
        return true;
    }

    public void Shutdown()
    {
        _bridge.ClientManager.RemoveCommandCallback("store",       OnStoreCommand);
        _bridge.ClientManager.RemoveCommandCallback("shop",        OnStoreCommand);
        _bridge.ClientManager.RemoveClientListener(this);
    }

    public void ConfigReload(IConfig config) { }

    public void OnLibraryConnected(string name)
    {
        if (name.Equals(MenuManagerAssemblyName, StringComparison.OrdinalIgnoreCase))
            TryResolveMenuManager();
    }

    public void OnLibraryDisconnect(string name) { }

    public void OnAllModulesLoaded()
    {
        TryResolveMenuManager(logFailure: true);
    }

    // -------------------------
    // IClientListener
    // -------------------------

    public void OnClientConnected(IGameClient client) { }

    public void OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
    {
        _cache.Remove(client.SteamId);
        _pendingPurchases.Remove(client.SteamId);
    }

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
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
            _logger.LogWarning("MenuManager is not installed. The store menu will not be available.");
    }

    // -------------------------
    // Commands
    // -------------------------
    private ECommandAction OnStoreCommand(IGameClient client, StringCommand cmd)
    {
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
        menuManager.DisplayMenu(client, BuildMainMenu(client));
    }

    // -------------------------
    // Helpers
    // -------------------------

    private static string Chat(string message) =>
        $" {ChatColor.LightRed}[IG]{ChatColor.Grey} {message}";

    private static string FormatTypeName(string raw) =>
        string.Join(" ", raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
                            .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

    private static string Colored(string color, string text) =>
        $"<font color='{color}'>{text}</font>";

    private static string TeamLabel(int team) => team switch
    {
        2 => "T",
        3 => "CT",
        _ => "Any",
    };

    private bool HasPermission(IGameClient client, string permission)
    {
        if (_adminManager.Manager is null)
            return false;
        var steamId = new SteamID(client.SteamId);
        return _adminManager?.Manager?.GetAdmin(steamId)?.HasPermission(permission) ?? false;
    }

    // -------------------------
    // Main menu
    // -------------------------

    private Menu BuildMainMenu(IGameClient client)
    {
        var cache = GetOrCreateCache(client.SteamId);
        var menu  = new Menu();

        menu.SetTitle(
            $"{Colored(ColorAccent, "Store")} {Colored(ColorMuted, "—")} {Colored(ColorPositive, $"{cache.Credits}cr")}");

        menu.AddItem(Colored(ColorAccent, "Buy Items"),
            ctrl => _ = NavigateAsync(ctrl, async () =>
            {
                await RefreshCacheAsync(ctrl.Client);
                return BuildBuyCategoryMenu(ctrl.Client);
            }));

        menu.AddItem(Colored(ColorAccent, "Equip Items"),
            ctrl => _ = NavigateAsync(ctrl, async () =>
            {
                await RefreshCacheAsync(ctrl.Client);
                return BuildEquipCategoryMenu(ctrl.Client);
            }));

        menu.AddExitItem();

        return menu;
    }

    // -------------------------
    // Buy menus
    // -------------------------

    private Menu BuildBuyCategoryMenu(IGameClient client)
    {
        var menu = new Menu();
        menu.SetTitle(Colored(ColorAccent, "Buy Items"));

        foreach (var type in _itemTypes)
        {
            var capturedType = type;
            menu.AddItem(Colored(ColorAccent, FormatTypeName(type.TypeName)),
                ctrl => _ = NavigateAsync(ctrl, async () =>
                {
                    await RefreshCacheAsync(ctrl.Client);
                    return BuildBuyItemList(capturedType, ctrl.Client);
                }));
        }

        menu.AddItem(Colored(ColorMuted, "← Back"),
            ctrl => _ = NavigateAsync(ctrl, async () =>
            {
                await RefreshCacheAsync(ctrl.Client);
                return BuildMainMenu(ctrl.Client);
            }));

        menu.AddExitItem();
        return menu;
    }

    private Menu BuildBuyItemList(IItemType type, IGameClient client)
    {
        var cache = GetOrCreateCache(client.SteamId);
        var menu  = new Menu();

        menu.SetTitle(
            $"{Colored(ColorAccent, FormatTypeName(type.TypeName))} {Colored(ColorMuted, "—")} {Colored(ColorPositive, $"{cache.Credits}cr")}");

        var sortedBuyItems = _itemService.GetBySlot(type.Slot)
            .Where(i => !cache.OwnedUniques.Contains(i.Unique))
            .OrderBy(i => i.Permission is null || HasPermission(client, i.Permission) ? 1 : 0)
            .ThenByDescending(i => i.Price)
            .ToList();

        var hasItems = sortedBuyItems.Count > 0;
        foreach (var item in sortedBuyItems)
        {
            var capturedItem = item;

            var teamTag = Colored(ColorMuted, $"[{TeamLabel(item.Team)}]");

            if (item.Permission is not null && !HasPermission(client, item.Permission))
            {
                var errorMsg = item.PermissionError ?? "Permission required";
                menu.AddDisabledItem($"{capturedItem.Name} {teamTag} {Colored(ColorMuted, $"[{errorMsg}]")}");
                continue;
            }

            menu.AddItem($"{capturedItem.Name} {teamTag} {Colored(ColorPositive, $"[{capturedItem.Price}cr]")}",
                ctrl => ctrl.Next(BuildBuyConfirmMenu(capturedItem, type, ctrl.Client)));
        }

        if (!hasItems)
            menu.AddDisabledItem(Colored(ColorMuted, "All items owned"));

        menu.AddItem(Colored(ColorMuted, "← Back"),
            ctrl => _ = NavigateAsync(ctrl, async () =>
            {
                await RefreshCacheAsync(ctrl.Client);
                return BuildBuyCategoryMenu(ctrl.Client);
            }));

        menu.AddExitItem();
        return menu;
    }

    private Menu BuildBuyConfirmMenu(ItemDefinition item, IItemType type, IGameClient client)
    {
        var menu = new Menu();
        menu.SetTitle(
            $"Buy {item.Name} for {Colored(ColorPositive, $"{item.Price}cr")}?");

        menu.AddItem(Colored(ColorPositive, "Confirm purchase"),
            ctrl => _ = DoBuyAsync(ctrl, item, type));

        menu.AddItem(Colored(ColorMuted, "← Back"),
            ctrl => _ = NavigateAsync(ctrl, async () =>
            {
                await RefreshCacheAsync(ctrl.Client);
                return BuildBuyItemList(type, ctrl.Client);
            }));

        menu.AddExitItem();
        return menu;
    }

    // -------------------------
    // Equip menus
    // -------------------------

    private Menu BuildEquipCategoryMenu(IGameClient client)
    {
        var cache = GetOrCreateCache(client.SteamId);
        var menu  = new Menu();
        menu.SetTitle(Colored(ColorAccent, "Equip Items"));

        var anyOwned = false;
        foreach (var type in _itemTypes)
        {
            var capturedType = type;
            var ownsAny      = _itemService.GetBySlot(type.Slot)
                                           .Any(it => cache.OwnedUniques.Contains(it.Unique));

            if (!ownsAny)
            {
                menu.AddDisabledItem(
                    $"{Colored(ColorMuted, FormatTypeName(type.TypeName))} {Colored(ColorMuted, "— none owned")}");
                continue;
            }

            anyOwned = true;
            menu.AddItem(Colored(ColorAccent, FormatTypeName(type.TypeName)),
                ctrl => _ = NavigateAsync(ctrl, async () =>
                {
                    await RefreshCacheAsync(ctrl.Client);
                    return BuildEquipItemList(capturedType, ctrl.Client);
                }));
        }

        if (!anyOwned)
            _logger.LogDebug("BuildEquipCategoryMenu: player {SteamId} owns no items", client.SteamId);

        menu.AddItem(Colored(ColorMuted, "← Back"),
            ctrl => _ = NavigateAsync(ctrl, async () =>
            {
                await RefreshCacheAsync(ctrl.Client);
                return BuildMainMenu(ctrl.Client);
            }));

        menu.AddExitItem();
        return menu;
    }

    private Menu BuildEquipItemList(IItemType type, IGameClient client)
    {
        var cache = GetOrCreateCache(client.SteamId);
        var menu  = new Menu();
        menu.SetTitle(Colored(ColorAccent, FormatTypeName(type.TypeName)));

        var sortedEquipItems = _itemService.GetBySlot(type.Slot)
            .Where(i => cache.OwnedUniques.Contains(i.Unique))
            .OrderBy(i => i.Permission is null || HasPermission(client, i.Permission) ? 1 : 0)
            .ThenByDescending(i => i.Price)
            .ToList();

        foreach (var item in sortedEquipItems)
        {
            var teamTag = Colored(ColorMuted, $"[{TeamLabel(item.Team)}]");

            if (item.Permission is not null && !HasPermission(client, item.Permission))
            {
                var errorMsg = item.PermissionError ?? "Permission required";
                menu.AddDisabledItem($"{item.Name} {teamTag} {Colored(ColorMuted, $"[{errorMsg}]")}");
                continue;
            }

            var capturedItem    = item;
            var capturedType    = type;
            var equippedEntries = cache.Equipped.Where(e => e.Unique == item.Unique).ToList();
            var equippedTag     = equippedEntries.Count switch
            {
                0                                    => "",
                1 when equippedEntries[0].Team == 2  => $" {Colored(ColorEquipped, "[T]")}",
                1 when equippedEntries[0].Team == 3  => $" {Colored(ColorEquipped, "[CT]")}",
                _                                    => $" {Colored(ColorEquipped, "[T+CT]")}",
            };

            menu.AddItem($"{capturedItem.Name} {teamTag}{equippedTag}",
                ctrl => _ = NavigateAsync(ctrl, async () =>
                {
                    await RefreshCacheAsync(ctrl.Client);
                    return BuildEquipTeamMenu(capturedType, capturedItem, ctrl.Client);
                }));
        }

        menu.AddItem(Colored(ColorMuted, "← Back"),
            ctrl => _ = NavigateAsync(ctrl, async () =>
            {
                await RefreshCacheAsync(ctrl.Client);
                return BuildEquipCategoryMenu(ctrl.Client);
            }));

        menu.AddExitItem();
        return menu;
    }

    // -------------------------
    // Navigation helper
    // -------------------------

    private static async Task NavigateAsync(IMenuController ctrl, Func<Task<Menu>> buildMenu)
    {
        var menu = await buildMenu();
        ctrl.Next(menu);
    }

    // -------------------------
    // Buy action
    // -------------------------

    private async Task DoBuyAsync(IMenuController controller, ItemDefinition item, IItemType type)
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
                    PurchaseResult.InsufficientCredits => Chat($"You need {ChatColor.Green}{item.Price} credits{ChatColor.Grey} to buy {ChatColor.Green}{item.Name}{ChatColor.Grey}."),
                    PurchaseResult.AlreadyOwned        => Chat($"You already own {ChatColor.Green}{item.Name}{ChatColor.Grey}."),
                    PurchaseResult.NotPurchasable      => Chat($"{ChatColor.Green}{item.Name}{ChatColor.Grey} is not for sale."),
                    _                                  => Chat("Purchase failed. Please try again."),
                };

                client.Print(HudPrintChannel.Chat, message);
                return;
            }

            client.Print(HudPrintChannel.Chat, Chat($"You purchased {ChatColor.Green}{item.Name}{ChatColor.Grey} for {ChatColor.Green}{item.Price} credits{ChatColor.Grey}!"));
            await RefreshCacheAsync(client);
            controller.Next(BuildMainMenu(client));
        }
        finally
        {
            _pendingPurchases.Remove(client.SteamId);
        }
    }

    // -------------------------
    // Equip team submenu
    // -------------------------

    private Menu BuildEquipTeamMenu(IItemType type, ItemDefinition item, IGameClient client)
    {
        var cache = GetOrCreateCache(client.SteamId);
        var menu  = new Menu();
        menu.SetTitle($"{Colored(ColorAccent, "Equip")} {Colored(ColorMuted, "—")} {item.Name}");

        if (item.Team == 0 || item.Team == 2)
            AddTeamOption(menu, type, item, 2, "Terrorists", cache);

        if (item.Team == 0 || item.Team == 3)
            AddTeamOption(menu, type, item, 3, "Counter-Terrorists", cache);

        if (item.Team == 0)
        {
            var equippedT  = cache.Equipped.FirstOrDefault(e => e.Unique == item.Unique && e.Team == 2);
            var equippedCT = cache.Equipped.FirstOrDefault(e => e.Unique == item.Unique && e.Team == 3);

            if (equippedT is not null && equippedCT is not null)
            {
                var capturedT  = equippedT;
                var capturedCT = equippedCT;
                menu.AddItem(
                    $"{Colored(ColorEquipped, "Both Teams")} {Colored(ColorEquipped, "[EQUIPPED]")}",
                    ctrl => _ = DoUnequipBothAsync(ctrl, type, item, capturedT, capturedCT));
            }
            else
            {
                menu.AddItem("Both Teams",
                    ctrl => _ = DoEquipBothAsync(ctrl, type, item));
            }
        }

        menu.AddItem(Colored(ColorMuted, "← Back"),
            ctrl => _ = NavigateAsync(ctrl, async () =>
            {
                await RefreshCacheAsync(ctrl.Client);
                return BuildEquipItemList(type, ctrl.Client);
            }));

        menu.AddExitItem();
        return menu;
    }

    private void AddTeamOption(Menu menu, IItemType type, ItemDefinition item, int teamId, string label, PlayerMenuCache cache)
    {
        var equipped = cache.Equipped.FirstOrDefault(e => e.Unique == item.Unique && e.Team == teamId);

        if (equipped is not null)
        {
            var capturedEquipped = equipped;
            menu.AddItem(
                $"{Colored(ColorEquipped, label)} {Colored(ColorEquipped, "[EQUIPPED]")}",
                ctrl => _ = DoUnequipSingleAsync(ctrl, type, item, capturedEquipped));
        }
        else
        {
            var capturedTeamId = teamId;
            var capturedLabel  = label;
            menu.AddItem(label,
                ctrl => _ = DoEquipTeamAsync(ctrl, type, item, capturedTeamId, capturedLabel));
        }
    }

    // -------------------------
    // Equip actions
    // -------------------------

    private async Task DoEquipTeamAsync(IMenuController controller, IItemType type, ItemDefinition item, int team, string teamLabel)
    {
        var client = controller.Client;
        try
        {
            if (item.Permission is not null && !HasPermission(client, item.Permission))
            {
                client.Print(HudPrintChannel.Chat, Chat("You do not have permission to equip this item."));
                return;
            }

            var success = await _equipmentManager.EquipAsync(client.SteamId, item.Unique, team);

            if (!success)
            {
                client.Print(HudPrintChannel.Chat, Chat($"Failed to equip {ChatColor.Green}{item.Name}{ChatColor.Grey}."));
                return;
            }

            client.Print(HudPrintChannel.Chat, Chat($"{ChatColor.Green}{item.Name}{ChatColor.Grey} equipped for {ChatColor.Green}{teamLabel}{ChatColor.Grey}!"));
            await RefreshCacheAsync(client);
            controller.Next(BuildEquipItemList(type, client));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DoEquipTeamAsync failed for {SteamId} item '{Unique}' team {Team}", client.SteamId, item.Unique, team);
            client.Print(HudPrintChannel.Chat, Chat("An error occurred while equipping. Please try again."));
        }
    }

    private async Task DoEquipBothAsync(IMenuController controller, IItemType type, ItemDefinition item)
    {
        var client = controller.Client;
        try
        {
            if (item.Permission is not null && !HasPermission(client, item.Permission))
            {
                client.Print(HudPrintChannel.Chat, Chat("You do not have permission to equip this item."));
                return;
            }

            var tSuccess  = await _equipmentManager.EquipAsync(client.SteamId, item.Unique, 2);
            var ctSuccess = await _equipmentManager.EquipAsync(client.SteamId, item.Unique, 3);

            if (!tSuccess || !ctSuccess)
            {
                client.Print(HudPrintChannel.Chat, Chat($"Failed to equip {ChatColor.Green}{item.Name}{ChatColor.Grey} for both teams."));
                return;
            }

            client.Print(HudPrintChannel.Chat, Chat($"{ChatColor.Green}{item.Name}{ChatColor.Grey} equipped for {ChatColor.Green}both teams{ChatColor.Grey}!"));
            await RefreshCacheAsync(client);
            controller.Next(BuildEquipItemList(type, client));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DoEquipBothAsync failed for {SteamId} item '{Unique}'", client.SteamId, item.Unique);
            client.Print(HudPrintChannel.Chat, Chat("An error occurred while equipping. Please try again."));
        }
    }

    // -------------------------
    // Unequip actions
    // -------------------------

    private async Task DoUnequipSingleAsync(IMenuController controller, IItemType type, ItemDefinition item, Equipment equipment)
    {
        var client = controller.Client;
        try
        {
            var teamLabel = equipment.Team == 2 ? "Terrorists" : "Counter-Terrorists";
            var success   = await _equipmentManager.UnequipAsync(client.SteamId, equipment.Slot, equipment.Team);

            if (!success)
            {
                client.Print(HudPrintChannel.Chat, Chat("Failed to unequip item."));
                return;
            }

            client.Print(HudPrintChannel.Chat, Chat($"{ChatColor.Green}{item.Name}{ChatColor.Grey} unequipped from {ChatColor.Green}{teamLabel}{ChatColor.Grey}."));
            await RefreshCacheAsync(client);
            controller.Next(BuildEquipItemList(type, client));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DoUnequipSingleAsync failed for {SteamId} item '{Unique}'", client.SteamId, item.Unique);
            client.Print(HudPrintChannel.Chat, Chat("An error occurred while unequipping. Please try again."));
        }
    }

    private async Task DoUnequipBothAsync(IMenuController controller, IItemType type, ItemDefinition item, Equipment equippedT, Equipment equippedCT)
    {
        var client = controller.Client;
        try
        {
            var tSuccess  = await _equipmentManager.UnequipAsync(client.SteamId, equippedT.Slot, equippedT.Team);
            var ctSuccess = await _equipmentManager.UnequipAsync(client.SteamId, equippedCT.Slot, equippedCT.Team);

            if (!tSuccess || !ctSuccess)
            {
                client.Print(HudPrintChannel.Chat, Chat("Failed to unequip item."));
                return;
            }

            client.Print(HudPrintChannel.Chat, Chat($"{ChatColor.Green}{item.Name}{ChatColor.Grey} unequipped from {ChatColor.Green}both teams{ChatColor.Grey}."));
            await RefreshCacheAsync(client);
            controller.Next(BuildEquipItemList(type, client));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DoUnequipBothAsync failed for {SteamId} item '{Unique}'", client.SteamId, item.Unique);
            client.Print(HudPrintChannel.Chat, Chat("An error occurred while unequipping. Please try again."));
        }
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

        var equipped = await _equipmentRepo.GetByPlayerAsync(steamId);

        var credits = _credits.GetCreditsBySlot(client.Slot);

        var cache = GetOrCreateCache(steamId);
        cache.OwnedUniques = [..inventory.Select(i => i.Unique)];
        cache.Equipped     = equipped;
        cache.Credits      = credits;
    }
}
