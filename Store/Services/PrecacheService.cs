using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Listeners;

namespace InsanityGaming.Store.Services;

public class PrecacheService : IModule, IGameListener
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<PrecacheService> _logger;
    int IGameListener.ListenerVersion => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;

    public PrecacheService(InterfaceBridge bridge, ILogger<PrecacheService> logger)
    {
        _bridge = bridge;
        _logger = logger;
    }
    public bool Init()
    {
        _bridge.ModSharp.InstallGameListener(this);
        return true;
    }

    public void Shutdown()
    {
        _bridge.ModSharp.RemoveGameListener(this);
    }
    
    public void OnResourcePrecache()
    {
        var assetPath = Path.Combine(_bridge.SharpPath, "assets");

        if (!Directory.Exists(assetPath))
        {
            return;
        }

        var files = Directory.EnumerateFiles(assetPath, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var relative = file[(assetPath.Length + 1)..].Replace("\\", "/");

            var asset = relative.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
                ? relative[..^2]
                : relative;

            _logger.LogDebug("Preaching asset \"{0}\"", asset);
            _bridge.ModSharp.PrecacheResource(asset);

        }
    }
}