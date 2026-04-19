using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Abstractions;
using Sharp.Shared.Enums;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Modules;

public class AdminCommandModule : IModule
{
    private static readonly string ModuleIdentity =
        typeof(AdminCommandModule).Assembly.GetName().Name ?? "InsanityGaming.Store";

    private const string AdminManagerAssemblyName = "Sharp.Modules.AdminManager";
    private const string CreditsPermission        = "store:credits";
    private const string ItemsPermission          = "store:items";

    private readonly InterfaceBridge _bridge;
    private readonly ICreditsApi _credits;
    private readonly IItemsApi _items;
    private readonly ILogger<AdminCommandModule> _logger;

    private IModSharpModuleInterface<IAdminManager>? _adminManager;

    public AdminCommandModule(
        InterfaceBridge bridge,
        ICreditsApi credits,
        IItemsApi items,
        ILogger<AdminCommandModule> logger)
    {
        _bridge  = bridge;
        _credits = credits;
        _items   = items;
        _logger  = logger;
    }

    public bool Init() => true;

    public void Shutdown() { }

    public void ConfigReload(IConfig cfg) { }

    public void OnLibraryConnected(string name)
    {
        if (name == AdminManagerAssemblyName)
            TryResolveAdminManager();
    }

    public void OnLibraryDisconnect(string name) { }

    public void OnAllModulesLoaded()
    {
        TryResolveAdminManager();

        if (_adminManager?.Instance is not { } adminManager)
        {
            _logger.LogWarning("AdminManager not available — store commands and permissions not registered.");
            return;
        }

        var registry = adminManager.GetCommandRegistry(ModuleIdentity);

        registry.RegisterPermissions([CreditsPermission, ItemsPermission]);

        registry.RegisterAdminCommand("addcredits",  OnAddCreditsCommand,  [CreditsPermission]);
        registry.RegisterAdminCommand("setcredits",  OnSetCreditsCommand,  [CreditsPermission]);
        registry.RegisterAdminCommand("getcredits",  OnGetCreditsCommand,  [CreditsPermission]);
        registry.RegisterAdminCommand("giveitem",    OnGiveItemCommand,    [ItemsPermission]);
        registry.RegisterAdminCommand("removeitem",  OnRemoveItemCommand,  [ItemsPermission]);
    }

    private void TryResolveAdminManager()
    {
        if (_adminManager?.Instance is not null) return;
        _adminManager = _bridge.ModuleManager
            .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity);
    }

    // -------------------------
    // Command handlers
    // -------------------------

    private void OnAddCreditsCommand(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 3)
        {
            Reply(caller, "Usage: !addcredits <name|steamid> <amount>");
            return;
        }

        if (!int.TryParse(cmd.GetArg(2), out var amount))
        {
            Reply(caller, "Invalid amount — must be a whole number.");
            return;
        }

        if (!TryResolveTarget(caller, cmd.GetArg(1), out var steamId, out var targetName))
            return;

        var success = _credits.AddCredits(steamId, amount);
        Reply(caller, success
            ? $"Added {amount} credits to {targetName}."
            : $"Failed to add credits to {targetName}.");
    }

    private void OnSetCreditsCommand(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 3)
        {
            Reply(caller, "Usage: !setcredits <name|steamid> <amount>");
            return;
        }

        if (!uint.TryParse(cmd.GetArg(2), out var amount))
        {
            Reply(caller, "Invalid amount — must be a non-negative whole number.");
            return;
        }

        if (!TryResolveTarget(caller, cmd.GetArg(1), out var steamId, out var targetName))
            return;

        var success = _credits.SetCredits(steamId, amount);
        Reply(caller, success
            ? $"Set {targetName}'s credits to {amount}."
            : $"Failed to set credits for {targetName}.");
    }

    private void OnGetCreditsCommand(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 2)
        {
            Reply(caller, "Usage: !getcredits <name|steamid>");
            return;
        }

        if (!TryResolveTarget(caller, cmd.GetArg(1), out var steamId, out var targetName))
            return;

        var credits = _credits.GetCredits(steamId);
        Reply(caller, credits is not null
            ? $"{targetName} has {credits} credits."
            : $"No record found for {targetName}.");
    }

    private void OnGiveItemCommand(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 3)
        {
            Reply(caller, "Usage: !giveitem <name|steamid> <item>");
            return;
        }

        if (!TryResolveTarget(caller, cmd.GetArg(1), out var steamId, out var targetName))
            return;

        var unique = cmd.GetArg(2);
        _ = _items.GiveItemAsync(steamId, unique).ContinueWith(t =>
            Reply(caller, t.Result
                ? $"Gave {unique} to {targetName}."
                : $"Failed to give {unique} to {targetName} — they may already own it."));
    }

    private void OnRemoveItemCommand(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 3)
        {
            Reply(caller, "Usage: !removeitem <name|steamid> <item>");
            return;
        }

        if (!TryResolveTarget(caller, cmd.GetArg(1), out var steamId, out var targetName))
            return;

        var unique = cmd.GetArg(2);
        _ = _items.RemoveItemAsync(steamId, unique).ContinueWith(t =>
            Reply(caller, t.Result
                ? $"Removed {unique} from {targetName}."
                : $"Failed to remove {unique} from {targetName} — they may not own it."));
    }

    // -------------------------
    // Helpers
    // -------------------------

    private void Reply(IGameClient? caller, string message)
    {
        if (caller is not null)
            caller.Print(HudPrintChannel.Chat, message);
        else
            _logger.LogInformation("{Message}", message);
    }

    private bool TryResolveTarget(
        IGameClient? caller,
        string arg,
        out ulong steamId,
        out string targetName)
    {
        steamId    = 0;
        targetName = string.Empty;

        if (arg.Length == 17 && arg.All(char.IsDigit) && ulong.TryParse(arg, out var sid))
        {
            steamId    = sid;
            targetName = arg;
            return true;
        }

        var matches = _bridge.ClientManager
            .GetGameClients(true)
            .Where(c => c.Name.Contains(arg, StringComparison.OrdinalIgnoreCase))
            .ToList();

        switch (matches.Count)
        {
            case 0:
                Reply(caller, $"No player found matching \"{arg}\".");
                return false;

            case 1:
                steamId    = matches[0].SteamId;
                targetName = matches[0].Name;
                return true;

            default:
                var names = string.Join(", ", matches.Select(c => c.Name));
                Reply(caller, $"Ambiguous match — found: {names}");
                return false;
        }
    }
}
