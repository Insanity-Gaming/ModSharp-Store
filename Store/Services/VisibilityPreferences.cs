using Microsoft.Extensions.Logging;
using Sharp.Modules.ClientPreferences.Shared;
using Sharp.Shared;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Services;

/// <summary>
/// Per-player "show other players' X" switches for the store's Settings tab, keyed by item type.
/// Persisted through the optional ClientPreferences module; every type defaults to visible, and
/// everything degrades to "visible, not saved" when ClientPreferences isn't installed.
/// Item types that support per-viewer hiding read <see cref="IsVisible"/> when deciding what each
/// viewer sees.
/// </summary>
public sealed class VisibilityPreferences
{
    private const string CookiePrefix = "store_vis_";

    private readonly InterfaceBridge _bridge;
    private readonly ILogger<VisibilityPreferences> _logger;
    private IModSharpModuleInterface<IClientPreference>? _clientPreferences;

    public VisibilityPreferences(InterfaceBridge bridge, ILogger<VisibilityPreferences> logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    public bool IsAvailable => Resolve() is not null;

    public bool IsVisible(ulong steamId, string typeName)
    {
        if (Resolve() is not { } prefs)
            return true;

        var identity = new SteamID(steamId);
        if (!prefs.IsLoaded(identity))
            return true;

        return prefs.GetCookie(identity, CookiePrefix + typeName) is not { } cookie || cookie.GetNumber() != 0;
    }

    /// <summary>Returns false when the preference couldn't be saved (module missing or not loaded yet).</summary>
    public bool SetVisible(ulong steamId, string typeName, bool visible)
    {
        if (Resolve() is not { } prefs)
            return false;

        var identity = new SteamID(steamId);
        if (!prefs.IsLoaded(identity))
            return false;

        prefs.SetCookie(identity, CookiePrefix + typeName, visible ? 1L : 0L);
        return true;
    }

    private IClientPreference? Resolve()
    {
        if (_clientPreferences?.Instance is { } instance)
            return instance;

        _clientPreferences = _bridge.ModuleManager
                                    .GetOptionalSharpModuleInterface<IClientPreference>(IClientPreference.Identity);

        if (_clientPreferences?.Instance is null)
            _logger.LogDebug("ClientPreferences is not installed; store visibility settings will not persist.");

        return _clientPreferences?.Instance;
    }
}
