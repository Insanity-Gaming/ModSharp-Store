using InsanityGaming.Store.Hud;
using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Items;
using InsanityGaming.Store.Models;
using InsanityGaming.Store.Services;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.HookParams;
using Sharp.Shared.Listeners;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Modules;

/// <summary>
/// Full-screen Panorama store (replaces the MenuManager chat menu). One private custom_hud_layout
/// per player, created on open and destroyed on close; behaviour mirrors the old Buy/Equip menus.
/// Every exit path goes through <see cref="CloseSession"/>.
/// </summary>
public class StoreHudModule : IModule, IClientListener, IGameListener, IEventListener
{
    private const string LayoutResource      = "panorama/layout/custom_game/store/store_hud.vxml_c";
    private const string LayoutSourcePath    = "panorama/layout/custom_game/store/store_hud.vxml";
    private const string StyleSourcePath     = "panorama/styles/custom_game/store/store_hud.vcss";
    private const string LayoutTargetPrefix  = "store_hud";

    private const int    MaxRows             = 48;
    private const int    MaxSections         = 8;
    private const int    MaxSettings         = 8;
    private const double InputCaptureDelay   = 0.1;
    private const double ToastSeconds        = 2.8;

    private const int TeamT  = 2;
    private const int TeamCT = 3;

    // Panel ids — must match content/panorama/layout/custom_game/store/store_hud.xml
    private const string RootPanel        = "store_root";
    private const string CreditsLabel     = "credits_lbl";
    private const string ShopView         = "shop_view";
    private const string SettingsView     = "settings_view";
    private const string ListScroll       = "list_scroll";
    private const string ListEmptyLabel   = "list_empty";
    private const string DetailNone       = "detail_none";
    private const string DetailBuy        = "detail_buy";
    private const string BuyNameLabel     = "buy_name";
    private const string BuyMetaLabel     = "buy_meta";
    private const string BuyDescLabel     = "buy_desc";
    private const string BuyBlockedLabel  = "buy_blocked";
    private const string BuyQuestionLabel = "buy_question";
    private const string BuyConfirm       = "buy_confirm";
    private const string BuyConfirmLabel  = "buy_confirm_lbl";
    private const string DetailEquip      = "detail_equip";
    private const string EquipNameLabel   = "equip_name";
    private const string EquipDescLabel   = "equip_desc";
    private const string EquipBlockedLabel = "equip_blocked";
    private const string PreviewBar       = "preview_bar";
    private const string PreviewLabel     = "preview_lbl";
    private const string ToastPanel       = "toast";
    private const string ToastLabel       = "toast_lbl";
    private const string SettingsEmpty    = "settings_empty";
    private const string CamControls      = "cam_controls";

    // Section header slots: sec_N sit above the item list, secb_N below it (see store_hud.xml).
    private const string SectionAbovePrefix = "sec_";
    private const string SectionBelowPrefix = "secb_";
    private const string ConfirmPurchaseText = "CONFIRM PURCHASE";

    // Delay between releasing input capture and killing the entity on a normal close, so the client
    // has processed the capture release before the layout that owns it disappears.
    private const double LayoutKillDelay  = 0.2;

    private static readonly string[] TabIds  = ["tab_buy", "tab_equip", "tab_settings"];
    private static readonly string[] TeamIds = ["team_t", "team_ct", "team_both"];
    private static readonly string[] ToastClasses = ["toast-ok", "toast-err", "toast-info"];
    private static readonly string[] ChipClasses  = ["chip-price", "chip-locked", "chip-short"];

    private enum Tab { Buy, Equip, Settings }
    private enum ToastKind { Ok, Error, Info }

    public enum CloseReason { Closed, Toggle, Death, TeamChange, Respawn, RoundStart, Disconnect, MapChange, Shutdown, ConfigReload, Invalid }

    private sealed class StoreSession
    {
        public required ulong      SteamId    { get; init; }
        public required PlayerSlot Slot       { get; init; }
        public required int        Generation { get; init; }

        /// <summary>False while the cache loads; true once the HUD is up.</summary>
        public bool Open { get; set; }

        public Tab     Tab       { get; set; } = Tab.Buy;
        /// <summary>Index of the open category section; -1 when every section is collapsed.</summary>
        public int     TypeIndex { get; set; }
        public string? Selected  { get; set; }
        public bool    Pending   { get; set; }

        /// <summary>
        /// True when the preview model is running: the viewer (living pawn, or the observer of a
        /// dead or spectating player) is held in place in front of it. False is browse mode, the
        /// fallback when no preview could be started for a dead or spectating player.
        /// </summary>
        public bool    LivePreview { get; set; }

        public HashSet<string>  OwnedUniques { get; set; } = [];
        public List<Equipment>  Equipped     { get; set; } = [];

        /// <summary>Items currently shown in row_0..row_n, in row order.</summary>
        public List<ItemDefinition> Rows { get; } = [];

        public ICustomHudLayout? ClickLayout     { get; set; }
        public int               ToastGeneration { get; set; }

        // Dirty-diff mirror of what's already on the entity (the server can't read panel state).
        public Dictionary<string, string> TextCache  { get; } = [];
        public Dictionary<string, bool>   ClassCache { get; } = [];
    }

    private readonly InterfaceBridge _bridge;
    private readonly IItemService _itemService;
    private readonly IItemType[] _itemTypes;
    private readonly IPurchaseService _purchaseService;
    private readonly IEquipmentManager _equipmentManager;
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly IInventoryRepository _inventoryRepo;
    private readonly ICredits _credits;
    private readonly AdminManagerHolder _adminManager;
    private readonly StorePreviewService _preview;
    private readonly VisibilityPreferences _visibility;
    private readonly ILogger<StoreHudModule> _logger;

    private readonly PlayerHudLayoutHost _layoutHost;
    private readonly IPanoramaManager.CustomHudClickedHandler _clickHandler;

    private readonly Dictionary<PlayerSlot, StoreSession> _sessions = new();
    private readonly int[] _generations = new int[PlayerSlot.MaxPlayerCount.AsPrimitive()];

    // One buy/equip in flight per player, keyed by SteamId so closing and reopening the store can't
    // start a second purchase while the first is still awaiting the DB (PurchaseService has no lock).
    // Touched from both the frame thread and await continuations, hence concurrent.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, byte> _pendingActions = new();

    // Types whose item count exceeded MaxRows, so the truncation warning is logged once, not per render.
    private readonly HashSet<string> _truncationWarned = [];

    public StoreHudModule(
        InterfaceBridge bridge,
        IItemService itemService,
        IEnumerable<IItemType> itemTypes,
        IPurchaseService purchaseService,
        IEquipmentManager equipmentManager,
        IEquipmentRepository equipmentRepo,
        IInventoryRepository inventoryRepo,
        ICredits credits,
        AdminManagerHolder adminManager,
        StorePreviewService preview,
        VisibilityPreferences visibility,
        ILogger<StoreHudModule> logger)
    {
        _bridge           = bridge;
        _itemService      = itemService;
        var allTypes      = itemTypes.ToArray();
        _itemTypes        = allTypes.Take(MaxSections).ToArray();
        _purchaseService  = purchaseService;
        _equipmentManager = equipmentManager;
        _equipmentRepo    = equipmentRepo;
        _inventoryRepo    = inventoryRepo;
        _credits          = credits;
        _adminManager     = adminManager;
        _preview          = preview;
        _visibility       = visibility;
        _logger           = logger;

        _layoutHost   = new PlayerHudLayoutHost(bridge, logger, LayoutResource, LayoutTargetPrefix);
        _clickHandler = OnClick;

        if (allTypes.Length > MaxSections)
            _logger.LogWarning("Store HUD shows {Max} item types; {Hidden} type(s) are not shown: {Types}",
                MaxSections, allTypes.Length - MaxSections,
                string.Join(", ", allTypes.Skip(MaxSections).Select(t => t.TypeName)));
    }

    // -------------------------
    // IModule
    // -------------------------

    public bool Init()
    {
        _bridge.ClientManager.InstallCommandCallback("store", OnStoreCommand);
        _bridge.ClientManager.InstallCommandCallback("shop",  OnStoreCommand);
        _bridge.ClientManager.InstallClientListener(this);
        _bridge.ModSharp.InstallGameListener(this);
        _bridge.EventManager.HookEvent("player_death");
        _bridge.EventManager.HookEvent("player_team");
        _bridge.EventManager.HookEvent("round_start");
        _bridge.EventManager.InstallEventListener(this);
        _bridge.HookManager.PlayerSpawnPost.InstallForward(OnPlayerSpawnPost);

        _logger.LogDebug("StoreHudModule initialized");
        return true;
    }

    public void Shutdown()
    {
        CloseAll(CloseReason.Shutdown);
        _layoutHost.KillAll();
        _preview.StopAll();

        _bridge.ClientManager.RemoveCommandCallback("store", OnStoreCommand);
        _bridge.ClientManager.RemoveCommandCallback("shop",  OnStoreCommand);
        _bridge.ClientManager.RemoveClientListener(this);
        _bridge.ModSharp.RemoveGameListener(this);
        _bridge.EventManager.RemoveEventListener(this);
        _bridge.HookManager.PlayerSpawnPost.RemoveForward(OnPlayerSpawnPost);
    }

    // Config reload fires on the file-watcher thread; the item list may have changed.
    public void ConfigReload(IConfig config)
        => _bridge.ModSharp.InvokeFrameAction(() => CloseAll(CloseReason.ConfigReload));

    // -------------------------
    // IGameListener
    // -------------------------

    public void OnResourcePrecache()
    {
        _bridge.ModSharp.PrecacheResource(LayoutSourcePath);
        _bridge.ModSharp.PrecacheResource(StyleSourcePath);
    }

    // Close while the old map's entities still exist, so teardown never touches destroyed ones.
    public void OnGameDeactivate()
    {
        CloseAll(CloseReason.MapChange);
        _layoutHost.KillAll();
        _preview.StopAll();
    }

    // Safety net for anything left over; every call here is validity-checked.
    public void OnGameActivate()
    {
        CloseAll(CloseReason.MapChange);
        _layoutHost.KillAll();
        _preview.StopAll();
    }

    int IGameListener.ListenerVersion  => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;

    // -------------------------
    // IClientListener
    // -------------------------

    public void OnClientPutInServer(IGameClient client)
    {
        CloseSession(client.Slot, CloseReason.Disconnect);
        _layoutHost.Kill(client.Slot);
        _preview.Stop(client.Slot);
    }

    public void OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
        => CloseSession(client.Slot, CloseReason.Disconnect);

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    // -------------------------
    // IEventListener / hooks — every one of these closes the store
    // -------------------------

    public void FireGameEvent(IGameEvent e)
    {
        switch (e.Name)
        {
            case "player_death":
                if (e.GetPlayerController("userid") is { } dead)
                    CloseLiveSession(dead.PlayerSlot, CloseReason.Death);
                break;

            case "player_team":
                if (e.GetPlayerController("userid") is { } switched)
                    CloseLiveSession(switched.PlayerSlot, CloseReason.TeamChange);
                break;

            case "round_start":
                foreach (var slot in _sessions.Keys.ToArray())
                    CloseLiveSession(slot, CloseReason.RoundStart);
                break;
        }
    }

    /// <summary>
    /// Pawn lifecycle events only end live-preview sessions (preview model and freeze are tied to
    /// that pawn). A browse-mode session never touched the pawn, so it stays open.
    /// </summary>
    private void CloseLiveSession(PlayerSlot slot, CloseReason reason)
    {
        if (_sessions.TryGetValue(slot, out var session) && session.LivePreview)
            CloseSession(slot, reason);
    }

    int IEventListener.ListenerVersion  => IEventListener.ApiVersion;
    int IEventListener.ListenerPriority => 0;

    private void OnPlayerSpawnPost(IPlayerSpawnForwardParams param)
    {
        if (param.Pawn.GetController() is { } controller)
            CloseLiveSession(controller.PlayerSlot, CloseReason.Respawn);
    }

    // -------------------------
    // Command
    // -------------------------

    private ECommandAction OnStoreCommand(IGameClient client, StringCommand cmd)
    {
        if (client.IsFakeClient)
            return ECommandAction.Handled;

        if (_sessions.ContainsKey(client.Slot))
        {
            CloseSession(client.Slot, CloseReason.Toggle);
            return ECommandAction.Handled;
        }

        if (_bridge.PanoramaManager is null)
        {
            client.Print(HudPrintChannel.Chat, Chat("The store is unavailable on this server right now."));
            return ECommandAction.Handled;
        }

        if (client.GetPlayerController() is null)
            return ECommandAction.Handled;

        // Dead players and spectators can shop too (as with the old menu); they get browse mode.
        BeginOpen(client);
        return ECommandAction.Handled;
    }

    // -------------------------
    // Open
    // -------------------------

    private void BeginOpen(IGameClient client)
    {
        var slot = client.Slot;
        var session = new StoreSession
        {
            SteamId    = client.SteamId,
            Slot       = slot,
            Generation = ++_generations[slot],
        };

        // Registered before the async load so a second !store, death or disconnect cancels it.
        _sessions[slot] = session;
        _ = LoadThenOpenAsync(session);
    }

    private async Task LoadThenOpenAsync(StoreSession session)
    {
        try
        {
            var (owned, equipped) = await LoadPlayerDataAsync(session.SteamId);

            _bridge.ModSharp.InvokeFrameAction(() =>
            {
                if (!IsCurrent(session))
                    return;

                session.OwnedUniques = owned;
                session.Equipped     = equipped;
                FinishOpen(session);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Store open failed for {SteamId}", session.SteamId);
            _bridge.ModSharp.InvokeFrameAction(() =>
            {
                if (!IsCurrent(session))
                    return;

                CloseSession(session.Slot, CloseReason.Invalid);
                PrintChat(session.Slot, Chat("The store failed to load. Please try again."));
            });
        }
    }

    private void FinishOpen(StoreSession session)
    {
        var slot = session.Slot;

        if (_bridge.ClientManager.GetGameClient(slot) is not { } client
            || client.SteamId != session.SteamId
            || _layoutHost.GetOrCreate(client) is not { } layout
            || _bridge.PanoramaManager is not { } panorama)
        {
            CloseSession(slot, CloseReason.Invalid);
            PrintChat(slot, Chat("The store could not be opened right now."));
            return;
        }

        panorama.InstallClickCallback(layout.Entity, _clickHandler);
        session.ClickLayout = layout.Entity;
        session.Open        = true;

        // Everyone previews: a living pawn on a team, or the observer of a dead / spectating player.
        var livePawn = GetLivePawn(slot);
        IBasePlayerPawn? viewer = livePawn is { Team: CStrikeTeam.TE or CStrikeTeam.CT } ? livePawn : GetObserverPawn(slot);
        var team     = StoreItemRules.PreviewTeam((int)(client.GetPlayerController()?.Team ?? CStrikeTeam.UnAssigned), 0);

        session.LivePreview = viewer is not null
                              && _preview.Start(slot, viewer, team, FallbackPreviewModel(session, team));

        // A living player who can't be frozen in front of a preview must not walk around in a live
        // store; a dead or spectating player falls back to browse mode instead.
        if (!session.LivePreview && viewer is not null && viewer == livePawn)
        {
            CloseSession(slot, CloseReason.Invalid);
            PrintChat(slot, Chat("The store could not be opened right now."));
            return;
        }

        if (session.LivePreview)
            ShowEquippedOnPreview(session, team);

        Render(session, layout);
        Class(session, layout, RootPanel, "show", true);

        // Deferred: capture can otherwise land in the same tick as CreateLayout, before the entity
        // is networked to the client. Capture also resets whenever the layout is rebuilt.
        var generation = session.Generation;
        _bridge.ModSharp.PushTimer(() =>
        {
            if (_sessions.TryGetValue(slot, out var current) && current.Generation == generation && current.Open
                && _layoutHost.TryGet(slot) is { } live)
            {
                live.Entity.SetInputCaptureEnabled(slot, true);
            }
        }, InputCaptureDelay);
    }

    // -------------------------
    // Close — the single teardown path
    // -------------------------

    /// <summary>
    /// Idempotent. Removes the preview model, restores movement and the mouse, removes the click
    /// callback and the HUD entity, and drops all per-slot state. Safe with a dead or missing pawn.
    /// </summary>
    public void CloseSession(PlayerSlot slot, CloseReason reason)
    {
        if (!_sessions.Remove(slot, out var session))
            return;

        // Bump the generation so any in-flight async continuation or toast timer becomes a no-op.
        _generations[slot]++;
        session.Open = false;

        _preview.Stop(slot);

        // Release the mouse first and hide the store; the entity itself is killed afterwards.
        var detached = _layoutHost.Detach(slot);
        if (detached is not null)
        {
            detached.SetInputCaptureEnabled(slot, false);
            detached.SetClassOverride(RootPanel, "show", HudPanelClassStatus.ForceDisable);
        }

        if (session.ClickLayout is { } clickLayout && _bridge.PanoramaManager is { } panorama)
        {
            // On map change the entity is already gone; removal must never abort the rest of teardown.
            try
            {
                panorama.RemoveClickCallback(clickLayout, _clickHandler);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "RemoveClickCallback failed for slot {Slot} ({Reason})", slot, reason);
            }
        }
        session.ClickLayout = null;

        if (detached is not null)
        {
            // Unload / map change / disconnect: kill now (timers die with the module, entities with
            // the map, and the client is gone). Otherwise give the capture release a moment to land.
            if (reason is CloseReason.Shutdown or CloseReason.MapChange or CloseReason.Disconnect)
                detached.AcceptInput("Kill");
            else
                _bridge.ModSharp.PushTimer(() =>
                {
                    if (detached.IsValid())
                        detached.AcceptInput("Kill");
                }, LayoutKillDelay);
        }

        session.Pending = false;
        session.TextCache.Clear();
        session.ClassCache.Clear();
        session.Rows.Clear();

        _logger.LogDebug("Store closed for slot {Slot} ({Reason})", slot, reason);
    }

    private void CloseAll(CloseReason reason)
    {
        foreach (var slot in _sessions.Keys.ToArray())
            CloseSession(slot, reason);
    }

    // -------------------------
    // Clicks
    // -------------------------

    private void OnClick(IPlayerController player, ICustomHudLayout layout, string buttonId)
    {
        var slot = player.PlayerSlot;
        if (!_sessions.TryGetValue(slot, out var session) || !session.Open)
            return;

        // Browse mode has no pawn requirement; live-preview mode needs its preview model and viewer.
        if (_layoutHost.TryGet(slot) is not { } live || (session.LivePreview && !_preview.IsActive(slot)))
        {
            CloseSession(slot, CloseReason.Invalid);
            return;
        }

        switch (buttonId)
        {
            case "close":
                CloseSession(slot, CloseReason.Closed);
                return;

            case "tab_buy":      SwitchTab(session, Tab.Buy);      break;
            case "tab_equip":    SwitchTab(session, Tab.Equip);    break;
            case "tab_settings": SwitchTab(session, Tab.Settings); break;

            case "cam_left":
                if (!_preview.Rotate(slot, -1)) _logger.LogDebug("Store preview rotate ignored for slot {Slot}", slot);
                return;
            case "cam_right":
                if (!_preview.Rotate(slot, 1)) _logger.LogDebug("Store preview rotate ignored for slot {Slot}", slot);
                return;

            case BuyConfirm:
                _ = DoBuyAsync(session);
                return;

            case "team_t":    _ = DoTeamOptionAsync(session, TeamT);  return;
            case "team_ct":   _ = DoTeamOptionAsync(session, TeamCT); return;
            case "team_both": _ = DoBothTeamsAsync(session);          return;

            default:
                if (TryParseIndex(buttonId, SectionBelowPrefix, out var below))
                    ToggleSection(session, below);
                else if (TryParseIndex(buttonId, SectionAbovePrefix, out var above))
                    ToggleSection(session, above);
                else if (TryParseIndex(buttonId, "row_", out var row))
                    SelectRow(session, row);
                else if (TryParseIndex(buttonId, "set_", out var setting))
                    ToggleSetting(session, setting);
                break;
        }

        Render(session, live);
    }

    private void SwitchTab(StoreSession session, Tab tab)
    {
        session.Tab      = tab;
        session.Selected = null;
    }

    /// <summary>Opens the section, or collapses it when it is already the open one.</summary>
    private void ToggleSection(StoreSession session, int index)
    {
        if (index < 0 || index >= _itemTypes.Length)
            return;

        session.TypeIndex = session.TypeIndex == index ? -1 : index;
        session.Selected  = null;
    }

    // Every row previews, including locked and unaffordable ones; the detail panel says why they
    // can't be bought or equipped.
    private void SelectRow(StoreSession session, int index)
    {
        if (index < 0 || index >= session.Rows.Count)
            return;

        var item = session.Rows[index];
        session.Selected = item.Unique;
        PreviewItem(session, item);
    }

    private void ToggleSetting(StoreSession session, int index)
    {
        var toggles = VisibilityTypes();
        if (index < 0 || index >= toggles.Count)
            return;

        var typeName = toggles[index].TypeName;
        var visible  = _visibility.IsVisible(session.SteamId, typeName);
        if (!_visibility.SetVisible(session.SteamId, typeName, !visible))
            ShowToast(session, "Settings could not be saved right now.", ToastKind.Error);
    }

    /// <summary>
    /// Dresses the freshly spawned preview model in the items equipped for <paramref name="team"/>.
    /// The store's equipment is the source of truth: a pawn's skin can't be read back from the game.
    /// </summary>
    private void ShowEquippedOnPreview(StoreSession session, int team)
    {
        foreach (var equipped in session.Equipped.Where(e => e.Team == team))
        {
            if (_itemService.GetByUnique(equipped.Unique) is not { } item
                || _itemService.GetItemType(item.Unique) is not { } type)
                continue;

            if (!_preview.Show(session.Slot, type, item))
                _logger.LogDebug("Equipped item {Unique} not shown on the store preview for slot {Slot}", item.Unique, session.Slot);
        }
    }

    private void PreviewItem(StoreSession session, ItemDefinition item)
    {
        if (!session.LivePreview || _itemService.GetItemType(item.Unique) is not { } type)
            return;

        // Previews only ever touch the preview model, never the pawn.
        if (!_preview.Show(session.Slot, type, item))
            _logger.LogDebug("Store preview ignored for slot {Slot}", session.Slot);
    }

    // -------------------------
    // Buy — mirrors the old DoBuyAsync
    // -------------------------

    private async Task DoBuyAsync(StoreSession session)
    {
        if (session.Tab != Tab.Buy || SelectedItem(session) is not { } item)
            return;

        // The confirm button is disabled for these, but never trust the client's view of it.
        switch (Availability(session, item, buying: true))
        {
            case ItemAvailability.Locked:
                ShowToast(session, "You do not have permission to buy this item.", ToastKind.Error);
                return;
            case ItemAvailability.Unaffordable:
                ShowToast(session, $"You need {item.Price} credits to buy {item.Name}.", ToastKind.Error);
                return;
        }

        if (!_pendingActions.TryAdd(session.SteamId, 0))
            return;

        var slot = session.Slot;
        session.Pending = true;
        RenderIfOpen(session);

        try
        {
            var result = await _purchaseService.BuyAsync(session.SteamId, slot, item.Unique);

            if (result != PurchaseResult.Success)
            {
                var message = result switch
                {
                    PurchaseResult.InsufficientCredits => $"You need {item.Price} credits to buy {item.Name}.",
                    PurchaseResult.AlreadyOwned        => $"You already own {item.Name}.",
                    PurchaseResult.NotPurchasable      => $"{item.Name} is not for sale.",
                    _                                  => "Purchase failed. Please try again.",
                };

                _bridge.ModSharp.InvokeFrameAction(() => FinishAction(session, message, ToastKind.Error, clearSelection: false));
                return;
            }

            var data = await LoadPlayerDataAsync(session.SteamId);
            _bridge.ModSharp.InvokeFrameAction(() =>
            {
                ApplyPlayerData(session, data);
                FinishAction(session, $"You purchased {item.Name} for {item.Price} credits!", ToastKind.Ok, clearSelection: true);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Store purchase failed for {SteamId} item '{Unique}'", session.SteamId, item.Unique);
            _bridge.ModSharp.InvokeFrameAction(() =>
                FinishAction(session, "Purchase failed. Please try again.", ToastKind.Error, clearSelection: false));
        }
        finally
        {
            _pendingActions.TryRemove(session.SteamId, out _);
        }
    }

    // -------------------------
    // Equip — mirrors the old BuildEquipTeamMenu / AddTeamOption actions
    // -------------------------

    private async Task DoTeamOptionAsync(StoreSession session, int team)
    {
        if (session.Tab != Tab.Equip || SelectedItem(session) is not { } item)
            return;

        if (item.Team != 0 && item.Team != team)
            return;

        var teamLabel = team == TeamT ? "Terrorists" : "Counter-Terrorists";
        var equipped  = session.Equipped.FirstOrDefault(e => e.Unique == item.Unique && e.Team == team);

        if (equipped is null && item.Permission is not null && !HasPermission(session.Slot, item.Permission))
        {
            ShowToast(session, "You do not have permission to equip this item.", ToastKind.Error);
            return;
        }

        if (!_pendingActions.TryAdd(session.SteamId, 0))
            return;

        session.Pending = true;
        RenderIfOpen(session);

        try
        {
            bool success;
            string message;

            if (equipped is not null)
            {
                success = await _equipmentManager.UnequipAsync(session.SteamId, equipped.Slot, equipped.Team);
                message = success ? $"{item.Name} unequipped from {teamLabel}." : "Failed to unequip item.";
            }
            else
            {
                success = await _equipmentManager.EquipAsync(session.SteamId, item.Unique, team);
                message = success ? $"{item.Name} equipped for {teamLabel}!" : $"Failed to equip {item.Name}.";
            }

            var data = await LoadPlayerDataAsync(session.SteamId);
            _bridge.ModSharp.InvokeFrameAction(() =>
            {
                ApplyPlayerData(session, data);
                if (success && equipped is null)
                    ApplyNowIfCurrentTeam(session.Slot, item, team, team);

                // Reads the live pawn, so it is built here on the frame thread, never after an await.
                var text = success && equipped is not null ? message + NextSpawnSuffix(session, team) : message;
                FinishAction(session, text, success ? ToastKind.Ok : ToastKind.Error, clearSelection: false);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Store equip failed for {SteamId} item '{Unique}' team {Team}", session.SteamId, item.Unique, team);
            _bridge.ModSharp.InvokeFrameAction(() =>
                FinishAction(session, "An error occurred while equipping. Please try again.", ToastKind.Error, clearSelection: false));
        }
        finally
        {
            _pendingActions.TryRemove(session.SteamId, out _);
        }
    }

    private async Task DoBothTeamsAsync(StoreSession session)
    {
        if (session.Tab != Tab.Equip || SelectedItem(session) is not { Team: 0 } item)
            return;

        var equippedT  = session.Equipped.FirstOrDefault(e => e.Unique == item.Unique && e.Team == TeamT);
        var equippedCT = session.Equipped.FirstOrDefault(e => e.Unique == item.Unique && e.Team == TeamCT);
        var unequip    = equippedT is not null && equippedCT is not null;

        if (!unequip && item.Permission is not null && !HasPermission(session.Slot, item.Permission))
        {
            ShowToast(session, "You do not have permission to equip this item.", ToastKind.Error);
            return;
        }

        if (!_pendingActions.TryAdd(session.SteamId, 0))
            return;

        session.Pending = true;
        RenderIfOpen(session);

        try
        {
            bool tSuccess, ctSuccess;
            if (unequip)
            {
                tSuccess  = await _equipmentManager.UnequipAsync(session.SteamId, equippedT!.Slot, equippedT.Team);
                ctSuccess = await _equipmentManager.UnequipAsync(session.SteamId, equippedCT!.Slot, equippedCT.Team);
            }
            else
            {
                tSuccess  = await _equipmentManager.EquipAsync(session.SteamId, item.Unique, TeamT);
                ctSuccess = await _equipmentManager.EquipAsync(session.SteamId, item.Unique, TeamCT);
            }

            var success = tSuccess && ctSuccess;
            var message = (unequip, success) switch
            {
                (true, true)   => $"{item.Name} unequipped from both teams.",
                (true, false)  => "Failed to unequip item.",
                (false, true)  => $"{item.Name} equipped for both teams!",
                (false, false) => $"Failed to equip {item.Name} for both teams.",
            };

            var data = await LoadPlayerDataAsync(session.SteamId);
            _bridge.ModSharp.InvokeFrameAction(() =>
            {
                ApplyPlayerData(session, data);
                if (!unequip && success)
                    ApplyNowIfCurrentTeam(session.Slot, item, TeamT, TeamCT);

                // Reads the live pawn, so it is built here on the frame thread, never after an await.
                var text = unequip && success ? message + NextSpawnSuffix(session, null) : message;
                FinishAction(session, text, success ? ToastKind.Ok : ToastKind.Error, clearSelection: false);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Store equip-both failed for {SteamId} item '{Unique}'", session.SteamId, item.Unique);
            _bridge.ModSharp.InvokeFrameAction(() =>
                FinishAction(session, "An error occurred while equipping. Please try again.", ToastKind.Error, clearSelection: false));
        }
        finally
        {
            _pendingActions.TryRemove(session.SteamId, out _);
        }
    }

    /// <summary>Equip applies immediately when it targets the team the pawn is on right now.</summary>
    private void ApplyNowIfCurrentTeam(PlayerSlot slot, ItemDefinition item, int teamA, int teamB)
    {
        if (GetLivePawn(slot) is not { } pawn || _itemService.GetItemType(item.Unique) is not { } type)
            return;

        var current = (int)pawn.Team;
        if (current == teamA || current == teamB)
            type.Preview(pawn, item);
    }

    /// <summary>
    /// Unequipping can't restore a default model (none is stored), so it lands on next spawn.
    /// Only mentioned when the unequip touched the team the player is on now.
    /// </summary>
    private string NextSpawnSuffix(StoreSession session, int? team)
    {
        if (GetLivePawn(session.Slot) is not { } pawn)
            return string.Empty;

        var current = (int)pawn.Team;
        return team is null || team == current ? " It changes on your next spawn." : string.Empty;
    }

    /// <summary>
    /// Runs on the frame thread after an action. If the store was closed or reopened meanwhile, the
    /// action still stands (it's in the database) and the result goes to chat instead.
    /// </summary>
    private void FinishAction(StoreSession session, string message, ToastKind kind, bool clearSelection)
    {
        if (!IsCurrent(session) || !session.Open)
        {
            PrintChat(session.Slot, Chat(message), session.SteamId);
            return;
        }

        session.Pending = false;
        if (clearSelection)
            session.Selected = null;

        ShowToast(session, message, kind);
        RenderIfOpen(session);
    }

    // -------------------------
    // Rendering (dirty-diffed, class-only styling)
    // -------------------------

    private void RenderIfOpen(StoreSession session)
    {
        if (IsCurrent(session) && session.Open && _layoutHost.TryGet(session.Slot) is { } layout)
            Render(session, layout);
    }

    private void Render(StoreSession session, PrivateHudLayout layout)
    {
        Text(session, layout, CreditsLabel, $"{_credits.GetCreditsBySlot(session.Slot)}cr");

        for (var i = 0; i < TabIds.Length; i++)
            Class(session, layout, TabIds[i], "on", (int)session.Tab == i);

        var settings = session.Tab == Tab.Settings;
        Class(session, layout, ShopView,     "hud-hidden", settings);
        Class(session, layout, SettingsView, "hud-hidden", !settings);

        if (settings)
        {
            RenderSettings(session, layout);
            Class(session, layout, PreviewBar, "hud-hidden", true);
            return;
        }

        RenderSections(session, layout);
        RenderRows(session, layout);
        RenderDetail(session, layout);

        // Browse mode (dead / spectating): no preview model to rotate.
        Class(session, layout, CamControls, "hud-hidden", !session.LivePreview);

        var selected = session.LivePreview ? SelectedItem(session) : null;
        Class(session, layout, PreviewBar, "hud-hidden", selected is null);
        if (selected is not null)
            Text(session, layout, PreviewLabel, $"Previewing {selected.Name}");
    }

    /// <summary>
    /// One header per item type. The open section's header and those before it use the sec_ slots
    /// above the item list; the rest use the secb_ slots below it, so the list reads as expanded
    /// under its own header.
    /// </summary>
    private void RenderSections(StoreSession session, PrivateHudLayout layout)
    {
        var buy = session.Tab == Tab.Buy;
        var (above, _) = StoreItemRules.SplitSections(_itemTypes.Length, session.TypeIndex);

        for (var i = 0; i < MaxSections; i++)
        {
            var exists  = i < _itemTypes.Length;
            var isAbove = exists && i < above.Length;

            RenderSectionHeader(session, layout, $"{SectionAbovePrefix}{i}", i, exists && isAbove, buy);
            RenderSectionHeader(session, layout, $"{SectionBelowPrefix}{i}", i, exists && !isAbove, buy);
        }

        Class(session, layout, ListScroll, "hud-hidden", CurrentType(session) is null);
    }

    private void RenderSectionHeader(StoreSession session, PrivateHudLayout layout, string id, int index, bool visible, bool buy)
    {
        Class(session, layout, id, "hud-hidden", !visible);
        if (!visible)
            return;

        var open  = index == session.TypeIndex;
        var count = ListedItems(session, _itemTypes[index], buy).Count();

        Class(session, layout, id, "open", open);
        Text(session, layout, $"{id}_arrow", open ? "▾" : "▸");
        Text(session, layout, $"{id}_lbl", FormatTypeName(_itemTypes[index].TypeName));
        Text(session, layout, $"{id}_count", count.ToString());
    }

    private void RenderRows(StoreSession session, PrivateHudLayout layout)
    {
        var buy   = session.Tab == Tab.Buy;
        var type  = CurrentType(session);
        var items = type is null ? [] : SortedItems(session, type, buy);

        if (items.Count > MaxRows && _truncationWarned.Add(type!.TypeName))
            _logger.LogWarning("Store type {Type} has {Count} items; only the first {Max} are shown.", type.TypeName, items.Count, MaxRows);

        var shown = items.Take(MaxRows).ToList();
        session.Rows.Clear();
        session.Rows.AddRange(shown.Select(r => r.Item));

        Class(session, layout, ListEmptyLabel, "hud-hidden", session.Rows.Count > 0);
        Text(session, layout, ListEmptyLabel, buy ? "All items owned" : "Nothing owned yet");

        for (var i = 0; i < MaxRows; i++)
        {
            var id = $"row_{i}";
            if (i >= session.Rows.Count)
            {
                Class(session, layout, id, "hud-hidden", true);
                continue;
            }

            var (item, availability) = shown[i];
            var locked = availability == ItemAvailability.Locked;
            var unaffordable = availability == ItemAvailability.Unaffordable;

            Class(session, layout, id, "hud-hidden", false);
            Class(session, layout, id, "sel", item.Unique == session.Selected);
            Class(session, layout, id, "disabled", locked);

            Text(session, layout, $"{id}_tile", Initials(item.Name));
            Text(session, layout, $"{id}_name", item.Name);
            Text(session, layout, $"{id}_team", $"[{TeamLabel(item.Team)}]");

            // Right-hand chip: price (buy; red when short), permission error (locked), equipped teams (equip).
            var equippedText = buy || locked ? string.Empty : EquippedTeamsText(session, item);
            Class(session, layout, $"{id}_eq", "hud-hidden", equippedText.Length == 0);
            Text(session, layout, $"{id}_eq", equippedText);

            var chipText = locked ? item.PermissionError ?? "Permission required" : buy ? $"{item.Price}cr" : string.Empty;
            var chipId   = $"{id}_chip";
            Class(session, layout, chipId, "hud-hidden", chipText.Length == 0);
            Class(session, layout, chipId, ChipClasses[0], !locked && !unaffordable);
            Class(session, layout, chipId, ChipClasses[1], locked);
            Class(session, layout, chipId, ChipClasses[2], unaffordable);
            Text(session, layout, chipId, chipText);
        }
    }

    private void RenderDetail(StoreSession session, PrivateHudLayout layout)
    {
        var item  = SelectedItem(session);
        var buy   = session.Tab == Tab.Buy && item is not null;
        var equip = session.Tab == Tab.Equip && item is not null;

        Class(session, layout, DetailNone,  "hud-hidden", item is not null);
        Class(session, layout, DetailBuy,   "hud-hidden", !buy);
        Class(session, layout, DetailEquip, "hud-hidden", !equip);

        if (buy)
        {
            var availability = Availability(session, item!, buying: true);
            var credits      = _credits.GetCreditsBySlot(session.Slot);
            var blocked      = StoreItemRules.BlockedActionText(availability, item!.Price, credits);

            Text(session, layout, BuyNameLabel, item.Name);
            Text(session, layout, BuyMetaLabel, $"[{TeamLabel(item.Team)}] · {item.Price}cr");
            RenderDescription(session, layout, BuyDescLabel, BuyBlockedLabel, item, availability);
            Class(session, layout, BuyQuestionLabel, "hud-hidden", blocked is not null);
            Text(session, layout, BuyQuestionLabel, $"Buy {item.Name} for {item.Price}cr?");
            Class(session, layout, BuyConfirm, "disabled", session.Pending || blocked is not null);
            Text(session, layout, BuyConfirmLabel, blocked ?? ConfirmPurchaseText);
        }

        if (equip)
        {
            var locked = Availability(session, item!, buying: false) == ItemAvailability.Locked;

            Text(session, layout, EquipNameLabel, item!.Name);
            RenderDescription(session, layout, EquipDescLabel, EquipBlockedLabel, item,
                locked ? ItemAvailability.Locked : ItemAvailability.Available);

            var eqT  = session.Equipped.Any(e => e.Unique == item.Unique && e.Team == TeamT);
            var eqCT = session.Equipped.Any(e => e.Unique == item.Unique && e.Team == TeamCT);

            RenderTeamOption(session, layout, TeamIds[0], item.Team is 0 or TeamT, eqT, locked);
            RenderTeamOption(session, layout, TeamIds[1], item.Team is 0 or TeamCT, eqCT, locked);
            RenderTeamOption(session, layout, TeamIds[2], item.Team == 0, eqT && eqCT, locked);
        }
    }

    /// <summary>Item description, plus the permission explanation when the item is locked.</summary>
    private static void RenderDescription(StoreSession session, PrivateHudLayout layout, string descId, string blockedId,
        ItemDefinition item, ItemAvailability availability)
    {
        var description = item.Description ?? string.Empty;
        Class(session, layout, descId, "hud-hidden", description.Length == 0);
        Text(session, layout, descId, description);

        var locked = availability == ItemAvailability.Locked;
        Class(session, layout, blockedId, "hud-hidden", !locked);
        Text(session, layout, blockedId, locked ? StoreItemRules.PermissionText(item) : string.Empty);
    }

    // A locked item can still be unequipped from a team it is on; only equipping it is blocked
    // (DoTeamOptionAsync / DoBothTeamsAsync enforce the same rule server-side).
    private void RenderTeamOption(StoreSession session, PrivateHudLayout layout, string id, bool visible, bool equipped, bool locked)
    {
        Class(session, layout, id, "hud-hidden", !visible);
        Class(session, layout, id, "on", equipped);
        Class(session, layout, id, "disabled", session.Pending || (locked && !equipped));
        Class(session, layout, $"{id}_eq", "hud-hidden", !equipped);
    }

    private void RenderSettings(StoreSession session, PrivateHudLayout layout)
    {
        var toggles = VisibilityTypes();
        Class(session, layout, SettingsEmpty, "hud-hidden", toggles.Count > 0);

        for (var i = 0; i < MaxSettings; i++)
        {
            var id = $"set_{i}";
            if (i >= toggles.Count)
            {
                Class(session, layout, id, "hud-hidden", true);
                continue;
            }

            var type   = toggles[i];
            var toggle = type.VisibilityToggle!;

            Class(session, layout, id, "hud-hidden", false);
            Class(session, layout, id, "on", _visibility.IsVisible(session.SteamId, type.TypeName));
            Text(session, layout, $"{id}_title", toggle.Label);
            Text(session, layout, $"{id}_desc", toggle.Description);
        }
    }

    private void ShowToast(StoreSession session, string message, ToastKind kind)
    {
        if (_layoutHost.TryGet(session.Slot) is not { } layout)
            return;

        Text(session, layout, ToastLabel, message);
        for (var i = 0; i < ToastClasses.Length; i++)
            Class(session, layout, ToastPanel, ToastClasses[i], (int)kind == i);
        Class(session, layout, ToastPanel, "show", true);

        var toastGeneration   = ++session.ToastGeneration;
        var sessionGeneration = session.Generation;
        var slot              = session.Slot;

        _bridge.ModSharp.PushTimer(() =>
        {
            if (_sessions.TryGetValue(slot, out var current)
                && current.Generation == sessionGeneration
                && current.ToastGeneration == toastGeneration
                && _layoutHost.TryGet(slot) is { } live)
            {
                Class(current, live, ToastPanel, "show", false);
            }
        }, ToastSeconds);
    }

    private static void Text(StoreSession session, PrivateHudLayout layout, string panelId, string value)
    {
        if (session.TextCache.TryGetValue(panelId, out var previous) && previous == value)
            return;

        layout.SetText(panelId, value);
        session.TextCache[panelId] = value;
    }

    private static void Class(StoreSession session, PrivateHudLayout layout, string panelId, string className, bool enabled)
    {
        var key = $"{panelId}|{className}";
        if (session.ClassCache.TryGetValue(key, out var previous) && previous == enabled)
            return;

        layout.SetClass(panelId, className, enabled);
        session.ClassCache[key] = enabled;
    }

    // -------------------------
    // Data
    // -------------------------

    private async Task<(HashSet<string> Owned, List<Equipment> Equipped)> LoadPlayerDataAsync(ulong steamId)
    {
        var inventory = await _inventoryRepo.GetByPlayerAsync(steamId);
        var equipped  = await _equipmentRepo.GetByPlayerAsync(steamId);
        return ([..inventory.Select(i => i.Unique)], equipped);
    }

    private void ApplyPlayerData(StoreSession session, (HashSet<string> Owned, List<Equipment> Equipped) data)
    {
        // Applied even to a closing session's object; harmless.
        session.OwnedUniques = data.Owned;
        session.Equipped     = data.Equipped;
    }

    private bool IsCurrent(StoreSession session)
        => _sessions.TryGetValue(session.Slot, out var current)
           && ReferenceEquals(current, session)
           && current.Generation == _generations[session.Slot];

    private IItemType? CurrentType(StoreSession session)
        => session.TypeIndex >= 0 && session.TypeIndex < _itemTypes.Length ? _itemTypes[session.TypeIndex] : null;

    private ItemDefinition? SelectedItem(StoreSession session)
        => session.Selected is { } unique && _itemService.GetByUnique(unique) is { } item ? item : null;

    /// <summary>Buy: unowned items. Equip: owned items.</summary>
    private IEnumerable<ItemDefinition> ListedItems(StoreSession session, IItemType type, bool buy)
        => _itemService.GetBySlot(type.Slot)
                       .Where(i => buy ? !session.OwnedUniques.Contains(i.Unique) : session.OwnedUniques.Contains(i.Unique));

    /// <summary>
    /// <see cref="ListedItems"/> in display order, each with its availability. Availability is
    /// computed per render from live credits and permissions, never cached on the session.
    /// </summary>
    private List<(ItemDefinition Item, ItemAvailability Availability)> SortedItems(StoreSession session, IItemType type, bool buy)
        => StoreItemRules.Order(ListedItems(session, type, buy))
                         .Select(i => (i, Availability(session, i, buy)))
                         .ToList();

    private ItemAvailability Availability(StoreSession session, ItemDefinition item, bool buying)
        => StoreItemRules.Evaluate(item, !IsLocked(session.Slot, item), _credits.GetCreditsBySlot(session.Slot), buying);

    private List<IItemType> VisibilityTypes()
        => _itemTypes.Where(t => t.VisibilityToggle is not null).Take(MaxSettings).ToList();

    private string EquippedTeamsText(StoreSession session, ItemDefinition item)
    {
        var entries = session.Equipped.Where(e => e.Unique == item.Unique).ToList();
        return entries.Count switch
        {
            0                                 => string.Empty,
            1 when entries[0].Team == TeamT   => "T",
            1 when entries[0].Team == TeamCT  => "CT",
            _                                 => "T+CT",
        };
    }

    private bool IsLocked(PlayerSlot slot, ItemDefinition item)
        => item.Permission is not null && !HasPermission(slot, item.Permission);

    private bool HasPermission(PlayerSlot slot, string permission)
    {
        if (_adminManager.Manager is null || _bridge.ClientManager.GetGameClient(slot) is not { } client)
            return false;

        return _adminManager.Manager.GetAdmin(new SteamID(client.SteamId))?.HasPermission(permission) ?? false;
    }

    private IPlayerPawn? GetLivePawn(PlayerSlot slot)
        => _bridge.ClientManager.GetGameClient(slot)?.GetPlayerController()?.GetPlayerPawn() is { IsAlive: true } pawn
           && pawn.IsValid()
            ? pawn
            : null;

    private IObserverPawn? GetObserverPawn(PlayerSlot slot)
        => _bridge.ClientManager.GetGameClient(slot)?.GetPlayerController()?.GetObserverPawn() is { } observer
           && observer.IsValid()
            ? observer
            : null;

    /// <summary>
    /// Model an observer's preview prop spawns with before it is dressed: the first equipped model
    /// for <paramref name="team"/>, else the first model in the catalog. A living pawn uses its own.
    /// </summary>
    private string? FallbackPreviewModel(StoreSession session, int team)
        => session.Equipped.Where(e => e.Team == team)
                  .Select(e => _itemService.GetByUnique(e.Unique)?.Model)
                  .Concat(_itemTypes.SelectMany(t => _itemService.GetBySlot(t.Slot)).Select(i => i.Model))
                  .FirstOrDefault(m => !string.IsNullOrEmpty(m));

    // -------------------------
    // Helpers
    // -------------------------

    private void PrintChat(PlayerSlot slot, string message, ulong? expectedSteamId = null)
    {
        if (_bridge.ClientManager.GetGameClient(slot) is not { } client)
            return;

        if (expectedSteamId is { } steamId && client.SteamId != steamId)
            return;

        client.Print(HudPrintChannel.Chat, message);
    }

    private static string Chat(string message) =>
        $" {ChatColor.LightRed}[IG]{ChatColor.Grey} {message}";

    private static string FormatTypeName(string raw) =>
        string.Join(" ", raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
                            .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

    private static string TeamLabel(int team) => team switch
    {
        TeamT  => "T",
        TeamCT => "CT",
        _      => "Any",
    };

    /// <summary>Tile text in place of item art (there's no clean per-item render): first letters of up to two words.</summary>
    private static string Initials(string name)
    {
        var words = name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
                        .Where(w => w.Length > 0)
                        .Take(2)
                        .ToList();

        return words.Count == 0 ? "?" : string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
    }

    private static bool TryParseIndex(string buttonId, string prefix, out int index)
    {
        index = -1;
        return buttonId.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(buttonId.AsSpan(prefix.Length), out index);
    }
}
