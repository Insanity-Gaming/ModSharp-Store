using System.Text.Json;
using InsanityGaming.Store.Configs;
using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Items;
using Microsoft.Extensions.Logging;

namespace InsanityGaming.Store.Services;

public class ItemService : IItemService
{
    private readonly InterfaceBridge _bridge;
    private readonly StoreConfig _config;
    private readonly Dictionary<string, IItemType> _typesByName;
    private readonly Dictionary<string, ItemDefinition> _definitions = new();
    private readonly Dictionary<string, IItemType> _typesByUnique = new();
    private readonly ILogger<ItemService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ItemService(
        InterfaceBridge bridge,
        StoreConfig config,
        IEnumerable<IItemType> itemTypes,
        ILogger<ItemService> logger)
    {
        _bridge = bridge;
        _config = config;
        _logger = logger;
        _typesByName = itemTypes.ToDictionary(t => t.TypeName);
    }

    public void LoadAll()
    {
        _definitions.Clear();
        _typesByUnique.Clear();

        var itemsDir = Path.Combine(_bridge.ConfigPath, "items");
        Directory.CreateDirectory(itemsDir);

        foreach (var typeName in _config.EnabledItems)
        {
            var filePath = Path.Combine(itemsDir, $"{typeName}.json");

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Item config not found: {Path}", filePath);
                continue;
            }

            ItemTypeConfig? typeConfig;
            try
            {
                var json = File.ReadAllText(filePath);
                typeConfig = JsonSerializer.Deserialize<ItemTypeConfig>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse item config: {Path}", filePath);
                continue;
            }

            if (typeConfig is null) continue;

            if (!_typesByName.TryGetValue(typeName, out var itemType))
            {
                _logger.LogWarning("No IItemType registered for type name '{TypeName}'", typeName);
                continue;
            }

            itemType.Initialize(typeConfig.Slot);

            foreach (var item in typeConfig.Items)
            {
                _definitions[item.Unique] = item;
                _typesByUnique[item.Unique] = itemType;
            }

            _logger.LogInformation("Loaded {Count} items from {TypeName} (slot {Slot})",
                typeConfig.Items.Count, typeName, typeConfig.Slot);
        }
    }

    public ItemDefinition? GetByUnique(string unique) =>
        _definitions.GetValueOrDefault(unique);

    public IReadOnlyList<ItemDefinition> GetPurchasable() =>
        _definitions.Values.Where(d => d.Purchasable).ToList();

    public IReadOnlyList<ItemDefinition> GetBySlot(int slot) =>
        _definitions.Values
            .Where(d => _typesByUnique.TryGetValue(d.Unique, out var t) && t.Slot == slot)
            .ToList();

    public IReadOnlyList<ItemDefinition> GetAccessible(string? permission) =>
        _definitions.Values
            .Where(d => d.Permission is null || d.Permission == permission)
            .ToList();

    public IItemType? GetItemType(string unique) =>
        _typesByUnique.GetValueOrDefault(unique);
}
