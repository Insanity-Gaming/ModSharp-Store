namespace InsanityGaming.Store.Items;

/// <summary>A per-viewer visibility switch an item type registers for the store's Settings tab.</summary>
/// <param name="Label">Row title, e.g. "Other players' trails".</param>
/// <param name="Description">One-line explanation shown under the title.</param>
public sealed record StoreVisibilityToggle(string Label, string Description);
