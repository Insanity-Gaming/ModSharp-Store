using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.Types;

namespace InsanityGaming.Store.Items;

public class ItemPlayerModels : IItemType
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<ItemPlayerModels> _logger;

    public static int EquipmentSlot { get; private set; }

    public string TypeName => "player_models";
    public int Slot => EquipmentSlot;

    // A model is one model for everyone, so it cannot be hidden per viewer.
    public StoreVisibilityToggle? VisibilityToggle => null;

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
            _logger.LogDebug("[{Type}] Equipping {Player} → model: {Model} skin: {Skin} color: {Color}",
                TypeName, playerName, item.Model, item.Skin ?? "default", item.Color ?? "default");

            ApplyNow(pawn, pawn.Team, item);

        }, 0.5f, GameTimerFlags.None);
    }

    public void Preview(IPlayerPawn pawn, ItemDefinition item)
    {
        if (string.IsNullOrEmpty(item.Model) || !pawn.IsValid()) return;

        ApplyNow(pawn, pawn.Team, item);
    }

    public void PreviewOnModel(IBaseModelEntity model, CStrikeTeam team, ItemDefinition item)
    {
        if (string.IsNullOrEmpty(item.Model) || !model.IsValid()) return;

        // The previous preview's skin and colour would otherwise carry over to an item without them.
        model.AcceptInput("Skin", model, model, "0");
        model.RenderColor = new Color32(255, 255, 255, 255);
        ApplyNow(model, team, item);
    }

    private void ApplyNow(IBaseModelEntity target, CStrikeTeam team, ItemDefinition item)
    {
        target.SetModel(item.Model!);

        if (item.Skin is not null)
            target.AcceptInput("Skin", target, target, item.Skin);

        if (item.Color is null) return;

        if (TryParseColor(item.Color, out var color))
            target.RenderColor = color;
        else if(item.Color.Equals("random",  StringComparison.InvariantCultureIgnoreCase))
        {
            var random = new Random();
            target.RenderColor = new Color32((byte) random.Next(0, 255), (byte) random.Next(0, 255), (byte) random.Next(0, 255), 254);
        }else if (item.Color.Equals("team", StringComparison.InvariantCultureIgnoreCase))
        {
            switch (team)
            {
                case CStrikeTeam.TE:
                    target.RenderColor = new Color32(255, 0, 0, 255);
                    break;
                case CStrikeTeam.CT:
                    target.RenderColor = new Color32(0, 0, 255, 255);
                    break;
            }
        }
    }

    private static bool TryParseColor(string value, out Color32 color)
    {
        color = default;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3
            || !byte.TryParse(parts[0], out var r)
            || !byte.TryParse(parts[1], out var g)
            || !byte.TryParse(parts[2], out var b))
            return false;

        var a = parts.Length >= 4 && byte.TryParse(parts[3], out var parsed) ? parsed : (byte)255;
        color = new Color32(r, g, b, 254);
        return true;
    }

    public void Unequip(IPlayerPawn pawn)
    {
    }
}
