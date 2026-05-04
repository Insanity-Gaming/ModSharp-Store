using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;
using Sharp.Modules.TargetingManager.Shared;
using Sharp.Shared;

namespace InsanityGaming.Store.Services;

public sealed class TargetingManagerHolder : IModule
{
    private const string TargetingManagerAssemblyName = "Sharp.Modules.TargetingManager";

    private readonly ISharedSystem _shared;
    private readonly ILogger<TargetingManagerHolder> _logger;

    private IModSharpModuleInterface<ITargetingManager>? _targetingManager;

    public ITargetingManager? Manager => _targetingManager?.Instance;

    public TargetingManagerHolder(
        ISharedSystem shared,
        ILogger<TargetingManagerHolder> logger)
    {
        _shared = shared;
        _logger = logger;
    }

    public void TryResolve(bool logFailure = false)
    {
        if (_targetingManager?.Instance is not null)
            return;

        _targetingManager = _shared.GetSharpModuleManager()
            .GetOptionalSharpModuleInterface<ITargetingManager>(ITargetingManager.Identity);

        if (_targetingManager?.Instance is null && logFailure)
            _logger.LogWarning("TargetingManager is not installed. Target selectors will be limited.");
    }

    public bool IsConnected() => _targetingManager is not null;

    private static bool IsTargetingManager(string name) => name.Equals(TargetingManagerAssemblyName, StringComparison.InvariantCultureIgnoreCase);

    public bool Init()
    {
        return true;
    }

    public void OnLibraryConnected(string name)
    {
        if (IsConnected() || !IsTargetingManager(name)) return;
        TryResolve();
    }

    public void OnLibraryDisconnect(string name)
    {
        if (IsConnected() && IsTargetingManager(name))
            _targetingManager = null;
    }

    public void OnAllModulesLoaded()
    {
        if (IsConnected()) return;
        TryResolve();
    }
}
