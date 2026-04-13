using InsanityGaming.Store.Interfaces;
using Sharp.Shared.GameEntities;

namespace InsanityGaming.Store.Items;

public class ItemPlayerModels : IItemType
{
    public static int EquipmentSlot { get; private set; }

    public string TypeName => "player_models";
    public int Slot => EquipmentSlot;

    public void Initialize(int slot) => EquipmentSlot = slot;

    public void Equip(IPlayerPawn pawn, ItemDefinition item)
    {
        // Apply the player model defined in item.Unique to the pawn.
        // Example: pawn.SetModel(item.Unique);
    }

    public void Unequip(IPlayerPawn pawn)
    {
        // Restore default player model.
        // Example: pawn.SetModel(string.Empty);
    }
}
