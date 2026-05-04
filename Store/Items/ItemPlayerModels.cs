using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;

namespace InsanityGaming.Store.Items;

public class ItemPlayerModels : IItemType
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<ItemPlayerModels> _logger;

    public static int EquipmentSlot { get; private set; }

    public string TypeName => "player_models";
    public int Slot => EquipmentSlot;

    public ItemPlayerModels(InterfaceBridge bridge, ILogger<ItemPlayerModels> logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    public void Initialize(int slot) => EquipmentSlot = slot;

    public void Equip(IPlayerPawn pawn, ItemDefinition item)
    {
        if (string.IsNullOrEmpty(item.Model)) return;

        _bridge.ModSharp.PushTimer(() =>
        {
            var controller = pawn.GetController();
            if (controller is null) return;

            var playerName = controller.PlayerName;
            _logger.LogInformation("[{Type}] Equipping {Player} → model: {Model}", TypeName, playerName, item.Model);
            pawn.SetModel(item.Model);
        }, 0.5f, GameTimerFlags.None);
    }

    public void Unequip(IPlayerPawn pawn)
    {
    }
}
