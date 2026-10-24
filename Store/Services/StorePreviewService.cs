using InsanityGaming.Store.Configs;
using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Items;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Services;

/// <summary>
/// "Try on" preview for the store HUD. One prop_dynamic per player, standing in front of their
/// held first-person view and transmitted only to them. The viewer is the living pawn, or the
/// observer pawn of a dead or spectating player (parked in place while the store is open).
/// Previews are applied to this prop, never to the pawn, and the rotate buttons turn it in place.
/// </summary>
public sealed class StorePreviewService
{
    private const string PropClassname = "prop_dynamic";

    private const float PreviewDistance   = 150f; // horizontal distance in front of the viewer
    private const float RotateStepDegrees = 45f;  // per rotate click
    private const float ObserverEyeHeight = 64f;  // observer eye to the floor the prop stands on

    // TransmitManager channel argument meaning "all channels", as in ModSharp's transmit example.
    private const int AllTransmitChannels = -1;

    private sealed class PreviewState
    {
        public required IBaseModelEntity Model            { get; init; }
        public required IBasePlayerPawn  Viewer           { get; init; }
        public required MoveType         PreviousMoveType { get; init; }
        public required float            BaseYaw          { get; init; }
        public float                     RotateYaw        { get; set; }

        // Observer viewers only: the spectating state restored on stop.
        public ObserverMode?               PreviousObserverMode   { get; init; }
        public CEntityHandle<IBaseEntity>? PreviousObserverTarget { get; init; }
    }

    private readonly InterfaceBridge _bridge;
    private readonly StoreConfig _config;
    private readonly ILogger<StorePreviewService> _logger;
    private readonly PreviewState?[] _states = new PreviewState?[PlayerSlot.MaxPlayerCount.AsPrimitive()];

    public StorePreviewService(InterfaceBridge bridge, StoreConfig config, ILogger<StorePreviewService> logger)
    {
        _bridge = bridge;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Spawns the preview prop in front of <paramref name="viewer"/> and holds the viewer in place
    /// with a level view. A living pawn's prop starts with the pawn's model; an observer's with
    /// <paramref name="fallbackModel"/>.
    /// </summary>
    public bool Start(PlayerSlot slot, IBasePlayerPawn viewer, string? fallbackModel)
    {
        Stop(slot);

        if (!viewer.IsValid() || viewer.GetController() is not { } controller)
            return false;

        var player = viewer.AsPlayer();
        var model  = player is not null ? CurrentModel(player) : fallbackModel;

        if (string.IsNullOrEmpty(model))
        {
            _logger.LogWarning("No model to show on the store preview of slot {Slot}.", slot);
            return false;
        }

        var yaw = viewer.GetEyeAngles().Y;
        var rad = yaw * MathF.PI / 180f;

        // The floor under a living pawn is its origin; an observer floats, so drop from its eyes.
        var center = player is not null
            ? viewer.GetAbsOrigin()
            : viewer.GetEyePosition() - new Vector(0f, 0f, ObserverEyeHeight);

        // On the floor in front of the viewer, facing back at them.
        var origin  = new Vector(center.X + MathF.Cos(rad) * PreviewDistance,
                                 center.Y + MathF.Sin(rad) * PreviewDistance,
                                 center.Z);
        var baseYaw = yaw + 180f;

        var keyValues = new Dictionary<string, KeyValuesVariantValueItem>
        {
            { "model", model },
            { "solid", "0" }, // not solid: the player and bullets pass through it
        };

        if (_bridge.EntityManager.SpawnEntitySync<IBaseModelEntity>(PropClassname, keyValues) is not { } prop)
        {
            _logger.LogWarning("Failed to spawn {Classname} for the store preview of slot {Slot}.", PropClassname, slot);
            return false;
        }

        prop.Teleport(origin, new Vector(0f, baseYaw, 0f));
        if (player is not null)
            prop.RenderColor = player.RenderColor;
        PlayAnimation(prop);

        // Only the owner may see their preview.
        _bridge.TransmitManager.AddEntityHooks(prop, false);
        _bridge.TransmitManager.SetEntityState(prop.Index, controller.Index, true, AllTransmitChannels);

        // Park an observer where it is instead of following its spectating target.
        var observerService = viewer.AsObserver()?.GetObserverService();
        var previousMode    = observerService?.ObserverMode;
        var previousTarget  = observerService?.ObserverTarget;
        if (observerService is not null)
            observerService.ObserverMode = ObserverMode.Roaming;

        var previousMoveType = viewer.MoveType;
        viewer.SetMoveType(MoveType.None);

        // Level the view so the prop sits in the middle of the screen.
        var level = new Vector(0f, yaw, 0f);
        if (player is not null)
            player.Teleport(angles: level);
        else
            viewer.SnapViewAngles(level);

        _states[slot] = new PreviewState
        {
            Model                  = prop,
            Viewer                 = viewer,
            PreviousMoveType       = previousMoveType,
            BaseYaw                = baseYaw,
            PreviousObserverMode   = previousMode,
            PreviousObserverTarget = previousTarget,
        };

        return true;
    }

    /// <summary>True while the preview prop and its viewer both still exist.</summary>
    public bool IsActive(PlayerSlot slot)
        => _states[slot] is { } state && state.Model.IsValid() && state.Viewer.IsValid();

    /// <summary>
    /// Shows <paramref name="item"/> on the preview prop as it looks on a player of <paramref name="team"/>.
    /// Returns false if there is no live preview.
    /// </summary>
    public bool Show(PlayerSlot slot, IItemType type, ItemDefinition item, int team)
    {
        if (!IsActive(slot))
            return false;

        var state = _states[slot]!;
        type.PreviewOnModel(state.Model, (CStrikeTeam)team, item);

        // A model change resets the sequence.
        PlayAnimation(state.Model);
        return true;
    }

    /// <summary>Turns the preview prop left (-1) or right (+1). Returns false if the prop is gone.</summary>
    public bool Rotate(PlayerSlot slot, int direction)
    {
        if (_states[slot] is not { } state || !state.Model.IsValid())
            return false;

        state.RotateYaw = (state.RotateYaw + direction * RotateStepDegrees) % 360f;
        state.Model.Teleport(angles: new Vector(0f, state.BaseYaw + state.RotateYaw, 0f));
        return true;
    }

    /// <summary>Releases the viewer, restores spectating and removes the preview prop. Idempotent.</summary>
    public void Stop(PlayerSlot slot) => StopAt(slot);

    public void StopAll()
    {
        for (var i = 0; i < _states.Length; i++)
            StopAt(i);
    }

    private void StopAt(int index)
    {
        if (_states[index] is not { } state)
            return;

        _states[index] = null;

        if (state.Viewer.IsValid())
        {
            if (state.Viewer.AsObserver() is { } observer)
            {
                // After a respawn or team change the game has moved the player off this observer;
                // writing the old spectating state onto it would fight the game.
                if (IsCurrentObserver(observer) && observer.GetObserverService() is { } observerService)
                {
                    state.Viewer.SetMoveType(state.PreviousMoveType);
                    if (state.PreviousObserverMode is { } mode)
                        observerService.ObserverMode = mode;
                    // The spectated player may have left or been removed meanwhile.
                    if (state.PreviousObserverTarget is { } target && _bridge.EntityManager.FindEntityByHandle(target) is not null)
                        observerService.ObserverTarget = target;
                }
            }
            // A dead pawn already has the move type the game set on death; don't override it.
            else if (state.Viewer.IsAlive)
            {
                state.Viewer.SetMoveType(state.PreviousMoveType);
            }
        }

        // Transmit hooks are destroyed with the entity.
        if (state.Model.IsValid())
            state.Model.Kill();
    }

    /// <summary>True when <paramref name="observer"/> is still the view of a player who is not alive.</summary>
    private static bool IsCurrentObserver(IObserverPawn observer)
        => observer.GetController() is { } controller
           && controller.GetObserverPawn() is { } current
           && current.Handle == observer.Handle
           && controller.GetPlayerPawn() is not { IsAlive: true };

    private static string? CurrentModel(IPlayerPawn pawn)
        => pawn.GetBodyComponent()
               .AsBodyComponentSkeletonInstance
               .GetSkeletonInstance()?
               .GetModelState()
               .ModelName;

    private void PlayAnimation(IBaseModelEntity prop)
    {
        var animation = _config.PreviewAnimation;
        if (string.IsNullOrWhiteSpace(animation))
            return;

        // Default animation is what the prop loops on once the current one ends.
        prop.AcceptInput("SetDefaultAnimation", value: animation);
        prop.AcceptInput("SetAnimation", value: animation);
    }
}
