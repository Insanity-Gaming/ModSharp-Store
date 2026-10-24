using InsanityGaming.HudArbiter.Shared;
using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;
using Sharp.Shared;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Services;

/// <summary>
/// Optional link to HudArbiter. Registers the store menu as a full-screen, input-capturing Modal element.
/// Without HudArbiter every activation is granted (pre-arbiter behaviour).
/// </summary>
public sealed class HudArbiterHolder : IModule
{
    private const string ArbiterAssemblyName = "HudArbiter";
    public const string MenuElementId        = "store.menu";

    private static readonly string Owner = typeof(HudArbiterHolder).Assembly.GetName().Name ?? "Store";

    private readonly ISharedSystem _shared;
    private readonly ILogger<HudArbiterHolder> _logger;

    private IModSharpModuleInterface<IHudArbiter>? _arbiter;
    private IHudElement? _menu;

    /// <summary>Raised on the game thread when the store menu's state changes for a player.</summary>
    public event HudStateChanged? MenuStateChanged;

    public HudArbiterHolder(ISharedSystem shared, ILogger<HudArbiterHolder> logger)
    {
        _shared = shared;
        _logger = logger;
    }

    public HudActivationResult ActivateMenu(PlayerSlot slot)
        => _menu?.Activate(slot) ?? HudActivationResult.Allowed;

    public void DeactivateMenu(PlayerSlot slot)
        => _menu?.Deactivate(slot);

    private void TryResolve(bool logFailure)
    {
        if (_menu is not null)
            return;

        _arbiter = _shared.GetSharpModuleManager()
            .GetOptionalSharpModuleInterface<IHudArbiter>(IHudArbiter.Identity);

        if (_arbiter?.Instance is not { } arbiter)
        {
            if (logFailure)
                _logger.LogDebug("HudArbiter is not installed. The store HUD will not coordinate with other HUD plugins.");
            return;
        }

        _menu = arbiter.Register(
            new HudElementDescriptor(
                Owner,
                MenuElementId,
                "Store",
                HudTier.Modal,
                Priority: 0,
                HudZone.All,
                HudTierMask.Misc | HudTierMask.Core,
                CapturesInput: true),
            (slot, state, blockedBy) => MenuStateChanged?.Invoke(slot, state, blockedBy));
    }

    private static bool IsArbiter(string name) =>
        name.Equals(ArbiterAssemblyName, StringComparison.InvariantCultureIgnoreCase);

    public bool Init() => true;

    public void OnLibraryConnected(string name)
    {
        if (IsArbiter(name))
            TryResolve(logFailure: false);
    }

    public void OnLibraryDisconnect(string name)
    {
        if (!IsArbiter(name))
            return;

        // The arbiter drops our registration itself; just forget the handle.
        _menu    = null;
        _arbiter = null;
    }

    public void OnAllModulesLoaded() => TryResolve(logFailure: true);

    public void Shutdown()
    {
        _menu?.Dispose();
        _menu    = null;
        _arbiter = null;
    }
}
