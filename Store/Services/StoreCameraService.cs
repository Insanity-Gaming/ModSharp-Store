using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Services;

/// <summary>
/// Third-person "try on" camera for the store HUD. One cs_player_camera per player, pointed at by
/// that player's CameraService.ViewEntity, orbiting their live pawn. The camera only drives the one
/// camera service it is assigned to, so it is not transmit-filtered.
/// </summary>
public sealed class StoreCameraService
{
    private const string CameraClassname = "cs_player_camera";

    private const float OrbitDistance    = 90f;  // horizontal distance from the pawn
    private const float FocusHeight      = 40f;  // aim point above the pawn origin (chest)
    private const float CameraHeight     = 54f;  // camera height above the pawn origin
    private const float OrbitStepDegrees = 45f;  // per rotate click

    private sealed class CameraState
    {
        public required ICustomPlayerCamera Camera { get; init; }
        public required IPlayerPawn         Pawn   { get; init; }
        public required MoveType            PreviousMoveType { get; init; }
        public required float               BaseYaw { get; init; }
        public float                        OrbitYaw { get; set; }
    }

    private readonly InterfaceBridge _bridge;
    private readonly ILogger<StoreCameraService> _logger;
    private readonly CameraState?[] _states = new CameraState?[PlayerSlot.MaxPlayerCount.AsPrimitive()];

    public StoreCameraService(InterfaceBridge bridge, ILogger<StoreCameraService> logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>Spawns the camera in front of the pawn, switches the view and freezes the player.</summary>
    public bool Start(PlayerSlot slot, IPlayerPawn pawn)
    {
        Stop(slot);

        if (!pawn.IsValid() || pawn.GetCameraService() is not { } cameraService)
            return false;

        // Start in front of the player, looking back at them.
        var baseYaw = pawn.GetEyeAngles().Y;
        var (origin, angles) = ComputePose(pawn, baseYaw);

        var keyValues = new Dictionary<string, KeyValuesVariantValueItem>
        {
            { "origin", origin.ToString() },
            { "angles", angles.ToString() },
        };

        if (_bridge.EntityManager.SpawnEntitySync<ICustomPlayerCamera>(CameraClassname, keyValues) is not { } camera)
        {
            _logger.LogWarning("Failed to spawn {Classname} for slot {Slot}.", CameraClassname, slot);
            return false;
        }

        camera.PawnHandle = pawn.Handle.As<IBasePlayerPawn>();
        camera.CameraMode = CustomCameraMode.Controlled;
        cameraService.ViewEntity = camera;

        var previousMoveType = pawn.MoveType;
        pawn.SetMoveType(MoveType.None);

        _states[slot] = new CameraState
        {
            Camera           = camera,
            Pawn             = pawn,
            PreviousMoveType = previousMoveType,
            BaseYaw          = baseYaw,
        };

        return true;
    }

    /// <summary>Steps the orbit left (-1) or right (+1). Returns false if the camera is gone.</summary>
    public bool Rotate(PlayerSlot slot, int direction)
    {
        if (_states[slot] is not { } state || !state.Camera.IsValid() || !state.Pawn.IsValid())
            return false;

        state.OrbitYaw = (state.OrbitYaw + direction * OrbitStepDegrees) % 360f;

        var (origin, angles) = ComputePose(state.Pawn, state.BaseYaw + state.OrbitYaw);
        state.Camera.Teleport(origin, angles);
        return true;
    }

    public bool IsActive(PlayerSlot slot)
        => _states[slot] is { } state && state.Camera.IsValid() && state.Pawn.IsValid();

    /// <summary>Restores the first-person view and movement and removes the camera. Idempotent.</summary>
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

        if (state.Pawn.IsValid())
        {
            if (state.Pawn.GetCameraService() is { } cameraService)
                cameraService.ViewEntity = null;

            // A dead pawn already has the move type the game set on death; don't override it.
            if (state.Pawn.IsAlive)
                state.Pawn.SetMoveType(state.PreviousMoveType);
        }

        if (state.Camera.IsValid())
        {
            state.Camera.CameraMode = CustomCameraMode.Disabled;
            state.Camera.Kill();
        }
    }

    /// <summary>Camera position on the orbit at <paramref name="yaw"/>, and angles aimed at the pawn's chest.</summary>
    private static (Vector Origin, Vector Angles) ComputePose(IPlayerPawn pawn, float yaw)
    {
        var center = pawn.GetAbsOrigin();
        var rad    = yaw * MathF.PI / 180f;

        var origin = new Vector(
            center.X + MathF.Cos(rad) * OrbitDistance,
            center.Y + MathF.Sin(rad) * OrbitDistance,
            center.Z + CameraHeight);

        var dx = center.X - origin.X;
        var dy = center.Y - origin.Y;
        var dz = center.Z + FocusHeight - origin.Z;

        var lookYaw   = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        var lookPitch = -MathF.Atan2(dz, MathF.Sqrt(dx * dx + dy * dy)) * 180f / MathF.PI; // Source: +pitch looks down

        return (origin, new Vector(lookPitch, lookYaw, 0f));
    }
}
