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
/// frozen first-person view and transmitted only to them. Previews are applied to this prop, never
/// to the pawn, and the rotate buttons turn it in place.
/// </summary>
public sealed class StorePreviewService
{
    private const string PropClassname = "prop_dynamic";

    private const float PreviewDistance  = 150f; // horizontal distance in front of the pawn
    private const float RotateStepDegrees = 45f; // per rotate click

    // TransmitManager channel argument meaning "all channels", as in ModSharp's transmit example.
    private const int AllTransmitChannels = -1;

    private sealed class PreviewState
    {
        public required IBaseModelEntity Model            { get; init; }
        public required IPlayerPawn      Pawn             { get; init; }
        public required MoveType         PreviousMoveType { get; init; }
        public required float            BaseYaw          { get; init; }
        public float                     RotateYaw        { get; set; }
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
    /// Spawns the preview prop in front of the pawn wearing the pawn's current model, levels the
    /// player's view onto it and freezes the player.
    /// </summary>
    public bool Start(PlayerSlot slot, IPlayerPawn pawn)
    {
        Stop(slot);

        if (!pawn.IsValid() || pawn.GetController() is not { } controller)
            return false;

        var model = pawn.GetBodyComponent()
                        .AsBodyComponentSkeletonInstance
                        .GetSkeletonInstance()?
                        .GetModelState()
                        .ModelName;

        if (string.IsNullOrEmpty(model))
        {
            _logger.LogWarning("Could not read the current model of slot {Slot} for the store preview.", slot);
            return false;
        }

        var yaw    = pawn.GetEyeAngles().Y;
        var rad    = yaw * MathF.PI / 180f;
        var center = pawn.GetAbsOrigin();

        // On the floor in front of the player, facing back at them.
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
        prop.RenderColor = pawn.RenderColor;
        PlayAnimation(prop);

        // Only the owner may see their preview.
        _bridge.TransmitManager.AddEntityHooks(prop, false);
        _bridge.TransmitManager.SetEntityState(prop.Index, controller.Index, true, AllTransmitChannels);

        var previousMoveType = pawn.MoveType;
        pawn.SetMoveType(MoveType.None);

        // Level the view so the prop sits in the middle of the screen.
        pawn.Teleport(angles: new Vector(0f, yaw, 0f));

        _states[slot] = new PreviewState
        {
            Model            = prop,
            Pawn             = pawn,
            PreviousMoveType = previousMoveType,
            BaseYaw          = baseYaw,
        };

        return true;
    }

    /// <summary>Shows <paramref name="item"/> on the preview prop. Returns false if there is no live preview.</summary>
    public bool Show(PlayerSlot slot, IItemType type, ItemDefinition item)
    {
        if (_states[slot] is not { } state || !state.Model.IsValid() || !state.Pawn.IsValid())
            return false;

        type.PreviewOnModel(state.Model, state.Pawn.Team, item);

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

    /// <summary>Restores movement and removes the preview prop. Idempotent.</summary>
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

        // A dead pawn already has the move type the game set on death; don't override it.
        if (state.Pawn.IsValid() && state.Pawn.IsAlive)
            state.Pawn.SetMoveType(state.PreviousMoveType);

        // Transmit hooks are destroyed with the entity.
        if (state.Model.IsValid())
            state.Model.Kill();
    }

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
