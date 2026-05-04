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
            _logger.LogInformation("[{Type}] Equipping {Player} → model: {Model} skin: {Skin} color: {Color}",
                TypeName, playerName, item.Model, item.Skin ?? "default", item.Color ?? "default");

            pawn.SetModel(item.Model);

            if (item.Skin is not null)
                pawn.AcceptInput("Skin", pawn, pawn, item.Skin);

            if (item.Color is not null && TryParseColor(item.Color, out var color))
                pawn.RenderColor = color;
            else if (item.Color is not null && item.Color.Equals("team", StringComparison.InvariantCultureIgnoreCase))
            {
                switch (pawn.Team)
                {
                    case CStrikeTeam.TE:
                        pawn.RenderColor = new Color32(255, 0, 0, 255);
                        break;
                    case CStrikeTeam.CT:
                        pawn.RenderColor = new Color32(0, 0, 255, 255);
                        break;
                }
            }
            
        }, 0.5f, GameTimerFlags.None);
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
        color = new Color32(r, g, b, a);
        return true;
    }

    public void Unequip(IPlayerPawn pawn)
    {
    }
}
