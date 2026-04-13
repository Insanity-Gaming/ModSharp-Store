using InsanityGaming.Store.Items;
using Sharp.Shared.GameEntities;

namespace InsanityGaming.Store.Interfaces;

public interface IItemType
{
    /// <summary>File name without extension — must match the JSON file name under items/.</summary>
    string TypeName { get; }

    /// <summary>Equipment slot for this item type. Set via Initialize() when the JSON is loaded.</summary>
    int Slot { get; }

    /// <summary>Called at startup with the slot value read from the type's JSON file.</summary>
    void Initialize(int slot);

    /// <summary>Apply this item's effect to the player pawn on spawn.</summary>
    void Equip(IPlayerPawn pawn, ItemDefinition item);

    /// <summary>Remove this item's effect from the player pawn.</summary>
    void Unequip(IPlayerPawn pawn);
}
