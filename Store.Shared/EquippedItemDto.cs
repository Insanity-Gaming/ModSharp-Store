namespace InsanityGaming.Store.Shared;

public sealed class EquippedItemDto
{
    public string Unique { get; init; } = string.Empty;
    public int Slot { get; init; }
    public int Team { get; init; }
}
