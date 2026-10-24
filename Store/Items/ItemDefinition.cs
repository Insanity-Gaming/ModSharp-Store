namespace InsanityGaming.Store.Items;

public class ItemDefinition
{
    public string Unique { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Model { get; set; }
    public uint Price { get; set; }
    public int Team { get; set; }
    public string? Permission { get; set; }
    public string? PermissionError { get; set; }

    // Right-panel copy (Panorama HTML allowed). PermissionDescription explains a locked item;
    // PermissionError stays the short row chip text.
    public string? Description { get; set; }
    public string? PermissionDescription { get; set; }
    public bool Purchasable { get; set; } = true;
    public string? Skin { get; set; }
    public string? Color { get; set; }
}

public class ItemTypeConfig
{
    public int Slot { get; set; }
    public List<ItemDefinition> Items { get; set; } = [];
}
