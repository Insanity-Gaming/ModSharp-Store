using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Shared;

namespace InsanityGaming.Store.Services;

public sealed class AdminManagerHolder : IModule
{
    private const string AdminManagerAssemblyName = "Sharp.Modules.AdminManager";

    private readonly ISharedSystem _shared;
    private readonly ILogger<AdminManagerHolder> _logger;

    private IModSharpModuleInterface<IAdminManager>? _adminManager;

    public IAdminManager? Manager => _adminManager?.Instance;

    public AdminManagerHolder(ISharedSystem shared, ILogger<AdminManagerHolder> logger)
    {
        _shared = shared;
        _logger = logger;
    }

    public void TryResolve(bool logFailure = false)
    {
        if (_adminManager?.Instance is not null)
            return;

        _adminManager = _shared.GetSharpModuleManager()
            .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity);

        if (_adminManager?.Instance is null && logFailure)
            _logger.LogDebug("AdminManager is not installed. Flag multipliers and permission checks will be skipped.");
    }

    public bool IsConnected() => _adminManager?.Instance is not null;

    private static bool IsAdminManager(string name) =>
        name.Equals(AdminManagerAssemblyName, StringComparison.InvariantCultureIgnoreCase);

    public bool Init() => true;

    public void OnLibraryConnected(string name)
    {
        if (IsConnected() || !IsAdminManager(name)) return;
        TryResolve();
    }

    public void OnLibraryDisconnect(string name)
    {
        if (IsConnected() && IsAdminManager(name))
            _adminManager = null;
    }

    public void OnAllModulesLoaded()
    {
        if (IsConnected()) return;
        TryResolve(logFailure: true);
    }
}
