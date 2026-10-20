using Microsoft.Extensions.Logging;
using Sharp.Shared.GameEntities;
using Sharp.Shared.Objects;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Hud;

/// <summary>
/// Owns one private store-HUD entity per human player. Every entity defaults to transmitting to
/// nobody and is explicitly enabled for only its matching controller, so one player's store state
/// is never sent to anyone else.
/// </summary>
public sealed class PlayerHudLayoutHost
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger _logger;
    private readonly string _layoutResource;
    private readonly string _targetNamePrefix;
    private readonly ICustomHudLayout?[] _layouts = new ICustomHudLayout?[PlayerSlot.MaxPlayerCount.AsPrimitive()];
    private bool _platformUnsupported;

    public PlayerHudLayoutHost(InterfaceBridge bridge, ILogger logger, string layoutResource, string targetNamePrefix)
    {
        _bridge           = bridge;
        _logger           = logger;
        _layoutResource   = layoutResource;
        _targetNamePrefix = targetNamePrefix;
    }

    public PrivateHudLayout? TryGet(PlayerSlot slot)
    {
        var entity = _layouts[slot];
        return entity is not null && entity.IsValid() ? new PrivateHudLayout(entity) : null;
    }

    public PrivateHudLayout? GetOrCreate(IGameClient client)
    {
        if (client.IsFakeClient)
            return null;

        var slot = client.Slot;
        if (TryGet(slot) is { } existing)
            return existing;

        var controller = client.GetPlayerController();
        var manager    = _bridge.PanoramaManager;
        if (controller is null || manager is null || _platformUnsupported)
            return null;

        ICustomHudLayout? entity;
        try
        {
            entity = manager.CreateLayout(_layoutResource, $"{_targetNamePrefix}_{slot.AsPrimitive()}");
        }
        catch (PlatformNotSupportedException e)
        {
            _platformUnsupported = true;
            _logger.LogWarning(e, "Private custom HUD layouts are not supported by this server build.");
            return null;
        }

        if (entity is null)
        {
            _logger.LogWarning("Failed to create store HUD layout for slot {Slot}.", slot);
            return null;
        }

        // Transmit to nobody by default, then to the owner only. Either failing = kill, never leak.
        if (!_bridge.TransmitManager.AddEntityHooks(entity, false))
        {
            _logger.LogError("Failed to install store HUD transmit hooks for slot {Slot}.", slot);
            entity.AcceptInput("Kill");
            return null;
        }

        if (!_bridge.TransmitManager.SetEntityState(entity.Index, controller.Index, true, -1))
        {
            _logger.LogError("Failed to enable store HUD transmission for slot {Slot}.", slot);
            entity.AcceptInput("Kill");
            return null;
        }

        _layouts[slot] = entity;
        return new PrivateHudLayout(entity);
    }

    public void Kill(PlayerSlot slot)
    {
        var entity = _layouts[slot];
        _layouts[slot] = null;

        if (entity is not null && entity.IsValid())
            entity.AcceptInput("Kill");
    }

    public void KillAll()
    {
        foreach (var entity in _layouts)
        {
            if (entity is not null && entity.IsValid())
                entity.AcceptInput("Kill");
        }

        Array.Clear(_layouts);
    }
}
