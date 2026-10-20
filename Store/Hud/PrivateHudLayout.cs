using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;

namespace InsanityGaming.Store.Hud;

/// <summary>
/// A store HUD entity transmitted to one player only. Because no other client receives the entity,
/// every write uses the layout's global state — never the per-player setters, which grow the
/// networked m_vecPlayerLayoutStates vector that every client receives.
/// </summary>
public readonly struct PrivateHudLayout
{
    public PrivateHudLayout(ICustomHudLayout entity)
    {
        Entity = entity;
    }

    public ICustomHudLayout Entity { get; }

    public void SetClass(string panelId, string className, bool enabled)
        => Entity.SetClassOverride(panelId, className,
            enabled ? HudPanelClassStatus.ForceEnable : HudPanelClassStatus.ForceDisable);

    public void SetText(string panelId, string value)
        => Entity.SetDialogVariableString(panelId, "value", value);
}
