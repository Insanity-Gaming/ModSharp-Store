using InsanityGaming.Store.Items;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;

namespace InsanityGaming.Store.Interfaces;

public interface IItemType
{
    /// <summary>File name without extension — must match the JSON file name under items/.</summary>
    string TypeName { get; }

    /// <summary>Equipment slot for this item type. Set via Initialize() when the JSON is loaded.</summary>
    int Slot { get; }

    /// <summary>
    /// Per-viewer visibility setting shown in the store's Settings tab, or null when this type
    /// cannot be hidden per viewer (e.g. player models, which are one model for everyone).
    /// </summary>
    StoreVisibilityToggle? VisibilityToggle { get; }

    /// <summary>Called at startup with the slot value read from the type's JSON file.</summary>
    void Initialize(int slot);

    /// <summary>Apply this item's effect to the player pawn on spawn.</summary>
    void Equip(IPlayerPawn pawn, ItemDefinition item);

    /// <summary>Remove this item's effect from the player pawn.</summary>
    void Unequip(IPlayerPawn pawn);

    /// <summary>Apply this item's effect to the pawn immediately (instant equip from the store).</summary>
    void Preview(IPlayerPawn pawn, ItemDefinition item);

    /// <summary>
    /// Show this item on the store's preview model, as it would look on a player of <paramref name="team"/>.
    /// Must never touch the pawn.
    /// </summary>
    void PreviewOnModel(IBaseModelEntity model, CStrikeTeam team, ItemDefinition item);
}
