using InsanityGaming.Store.Configs;
using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Models;
using InsanityGaming.Store.Services;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Modules;

public class CreditsModule : IModule, ICredits, IClientListener, IEventListener
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<CreditsModule> _logger;
    private readonly IUserRepository _repository;
    private readonly AdminManagerHolder _adminManager;
    private readonly Store _plugin;
    private StoreConfig _config;

    // All reads and writes to _credits MUST acquire _sync first.
    // _connectRetries is only ever touched on the game thread, so no lock needed.
    private readonly object _sync = new();
    private readonly Dictionary<PlayerSlot, uint> _credits = new();
    private readonly Dictionary<ulong, int> _connectRetries = new();

    private Guid _timer;

    public CreditsModule(
        InterfaceBridge bridge,
        Store plugin,
        StoreConfig config,
        ILogger<CreditsModule> logger,
        IUserRepository repository,
        AdminManagerHolder adminManager)
    {
        _plugin       = plugin;
        _config       = config;
        _bridge       = bridge;
        _logger       = logger;
        _repository   = repository;
        _adminManager = adminManager;
    }

    public bool Init()
    {
        _timer = _bridge.ModSharp.PushTimer(AwardCredits, _config.CreditFrequency, GameTimerFlags.Repeatable);
        _bridge.ClientManager.InstallClientListener(this);
        _bridge.EventManager.HookEvent("player_connect_full");
        _bridge.EventManager.InstallEventListener(this);
        return true;
    }

    private float GetFlagMultiplier(IGameClient client)
    {
        if (_config.FlagMultipliers.Count == 0) return 1.0f;

        var admin = _adminManager.Manager?.GetAdmin(new SteamID(client.SteamId));
        if (admin is null) return 1.0f;

        var best = 1.0f;
        foreach (var (flag, multiplier) in _config.FlagMultipliers)
        {
            if (admin.HasPermission(flag) && multiplier > best)
                best = multiplier;
        }
        return best;
    }

    public void Shutdown()
    {
        _bridge.ModSharp.StopTimer(_timer);

        // Snapshot under lock, save outside lock so no DB work holds _sync.
        var clients = _bridge.ClientManager.GetGameClients(true).ToList();

        foreach (var c in clients)
        {
            if (c.SteamId == 0) continue;

            uint amount;
            lock (_sync)
            {
                if (!_credits.TryGetValue(c.Slot, out amount)) continue;
            }

            try { _repository.UpdateCreditsAsync(c.SteamId, amount).Wait(); }
            catch (Exception ex) { _logger.LogError(ex, "[Credits] Shutdown persist failed for {SteamId}", c.SteamId); }
        }

        lock (_sync) { _credits.Clear(); }

        _bridge.EventManager.RemoveEventListener(this);
        _bridge.ClientManager.RemoveClientListener(this);
    }

    public void ConfigReload(IConfig cfg)
    {
        if (cfg is not StoreConfig config) return;
        if (_config.CreditFrequency != config.CreditFrequency)
        {
            _bridge.ModSharp.StopTimer(_timer);
            _timer = _bridge.ModSharp.PushTimer(AwardCredits, config.CreditFrequency, GameTimerFlags.Repeatable);
        }

        _config = config;
    }

    // -------------------------
    // IClientListener
    // -------------------------

    public void OnClientPostAdminCheck(IGameClient client) { }

    public void OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
    {
        var steamId = client.SteamId;
        var slot    = client.Slot;

        // _connectRetries is game-thread-only, no lock needed.
        _connectRetries.Remove(steamId);

        uint amount;
        lock (_sync)
        {
            // If the slot was never populated (churn connect that never completed its load),
            // there is nothing to save — and we must NOT save 0 for a real balance.
            if (!_credits.Remove(slot, out amount))
                return;
        }

        if (steamId == 0) return;

        // Fire-and-forget with a real async try/catch so exceptions are observable.
        _ = Task.Run(async () =>
        {
            try
            {
                await _repository.UpdateCreditsAsync(steamId, amount);
                _logger.LogDebug("[Credits] Persisted {Credits} for {SteamId}", amount, steamId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Credits] Failed to persist credits for {SteamId}", steamId);
            }
        });
    }

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    // -------------------------
    // IEventListener
    // -------------------------

    public void FireGameEvent(IGameEvent e)
    {
        if (e.Name != "player_connect_full") return;

        var controller = e.GetPlayerController("userid");
        if (controller is null) return;

        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.Slot == controller.PlayerSlot);

        if (client is null || client.SteamId == 0) return;

        if (controller.ConnectedState != PlayerConnectedState.PlayerConnected)
        {
            ScheduleConnectRetry(client.SteamId);
            return;
        }

        LoadCredits(client.SteamId, client.Slot, client.Name);
    }

    private void ScheduleConnectRetry(ulong steamId)
    {
        var attempt = _connectRetries.GetValueOrDefault(steamId, 0) + 1;

        if (attempt > 3)
        {
            _connectRetries.Remove(steamId);
            _logger.LogDebug("[Credits] Giving up waiting for PlayerConnected state for {SteamId} after 3 retries", steamId);
            return;
        }

        _connectRetries[steamId] = attempt;
        _logger.LogDebug("[Credits] ConnectedState not ready for {SteamId}, scheduling retry {Attempt}/3", steamId, attempt);
        _bridge.ModSharp.PushTimer(() => RetryLoadCredits(steamId), 5.0f, GameTimerFlags.None);
    }

    private void RetryLoadCredits(ulong steamId)
    {
        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.SteamId == steamId);

        if (client is null)
        {
            _connectRetries.Remove(steamId);
            return;
        }

        var controller = client.GetPlayerController();
        if (controller?.ConnectedState != PlayerConnectedState.PlayerConnected)
        {
            ScheduleConnectRetry(steamId);
            return;
        }

        _connectRetries.Remove(steamId);
        LoadCredits(client.SteamId, client.Slot, client.Name);
    }

    private void LoadCredits(ulong steamId, PlayerSlot slot, string? name)
    {
        Task.Run(async () =>
        {
            try
            {
                var user = await _repository.GetByIdAsync(steamId);
                if (user is null)
                {
                    user = new Users { Id = steamId, Name = name ?? string.Empty, Credits = 0 };
                    await _repository.UpsertAsync(user);
                }

                var value = user.Credits;

                // Marshal the commit back to the game thread so engine objects
                // (client list, slot identity) are touched only there.
                _bridge.ModSharp.PushTimer(() => CommitLoad(steamId, slot, value), 0f, GameTimerFlags.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Credits] Failed to load credits for {SteamId}", steamId);
            }
        });
    }

    /// <summary>
    /// Runs on the game thread. Commits a loaded balance only if the slot still belongs
    /// to the expected player AND credits have not already been populated this session
    /// (idempotency guard against duplicate player_connect_full events).
    /// </summary>
    private void CommitLoad(ulong steamId, PlayerSlot slot, uint value)
    {
        // Identity/liveness check: the slot must still be held by the same SteamID.
        // If the player disconnected and the slot was reused, the SteamID will differ
        // (or be 0 for a connecting player). Discard to prevent contamination.
        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.Slot == slot);

        if (client?.SteamId != steamId)
        {
            _logger.LogDebug("[Credits] CommitLoad discarded for {SteamId} slot {Slot}: slot reassigned or player gone", steamId, slot);
            return;
        }

        lock (_sync)
        {
            // If credits are already present for this slot the player's session is live
            // (purchases/awards have already touched the value). Overwriting with the stale
            // DB read would lose those changes — discard.
            if (_credits.ContainsKey(slot))
            {
                _logger.LogDebug("[Credits] CommitLoad discarded for {SteamId} slot {Slot}: already loaded (duplicate connect event)", steamId, slot);
                return;
            }

            _credits[slot] = value;
        }

        _logger.LogDebug("[Credits] Loaded {Credits} for {SteamId} (slot {Slot})", value, steamId, slot);
    }

    int IEventListener.ListenerVersion  => IEventListener.ApiVersion;
    int IEventListener.ListenerPriority => 0;

    // -------------------------
    // Credit distribution
    // -------------------------

    private void AwardCredits()
    {
        var maxPlayers = _bridge.GlobalVars.MaxClients;

        var players = _bridge.EntityManager
            .GetPlayerControllers()
            .Where(c => c is { Team: > CStrikeTeam.Spectator, IsFakeClient: false, ConnectedState: PlayerConnectedState.PlayerConnected })
            .ToList();

        var count = players.Count;
        if (count == 0 || maxPlayers <= 0)
            return;

        var percentFull = (float)count / maxPlayers;
        percentFull = Math.Clamp(percentFull, 0f, 1f);

        var dynamicMultiplier =
            1.0f + (_config.DynamicMultiplierMax - 1.0f) * (1.0f - percentFull);

        var basePoints = _config.CreditReward * _config.CreditMultiplier;

        var clientBySlot = _bridge.ClientManager.GetGameClients(true)
            .Where(c => c.SteamId != 0)
            .ToDictionary(c => c.Slot);

        foreach (var controller in players)
        {
            clientBySlot.TryGetValue(controller.PlayerSlot, out var client);
            var flagMultiplier = client is not null ? GetFlagMultiplier(client) : 1.0f;
            var reward         = (uint)(basePoints * dynamicMultiplier * flagMultiplier);

            if (!AddCreditsBySlot(controller.PlayerSlot, (int)reward))
                _logger.LogDebug("Failed to award credits to slot {Slot}", controller.PlayerSlot);
        }

        _logger.LogDebug(
            "Credits tick | Players: {Count}/{Max} ({Percent:P0}) | Base: {Base} | DynMult: {Mult:F2}",
            count,
            maxPlayers,
            percentFull,
            (uint)(basePoints * dynamicMultiplier),
            dynamicMultiplier
        );

        // Snapshot under lock, then persist on the threadpool.
        var clients = _bridge.ClientManager.GetGameClients(true)
            .Where(c => c.SteamId != 0)
            .ToList();

        List<(ulong SteamId, uint Credits)> snapshot;
        lock (_sync)
        {
            snapshot = clients
                .Where(c => _credits.ContainsKey(c.Slot))
                .Select(c => (SteamId: (ulong)c.SteamId, Credits: _credits[c.Slot]))
                .ToList();
        }

        _ = Task.Run((Func<Task>)(async () =>
        {
            try { await _repository.BulkUpdateCreditsAsync(snapshot); }
            catch (Exception ex) { _logger.LogError(ex, "[Credits] Safety save failed"); }
        }));
    }

    // -------------------------
    // Slot-based (online players)
    // -------------------------

    public uint GetCreditsBySlot(PlayerSlot slot)
    {
        lock (_sync)
            return _credits.GetValueOrDefault(slot);
    }

    public bool SetCreditsBySlot(PlayerSlot slot, uint amount)
    {
        lock (_sync)
        {
            _credits[slot] = amount;
            return true;
        }
    }

    public bool AddCreditsBySlot(PlayerSlot slot, int amount)
    {
        lock (_sync)
        {
            // Return false for unloaded slots so we don't ghost-create entries
            // with a stale 0 baseline (e.g., award tick firing before load completes).
            if (!_credits.TryGetValue(slot, out var current))
                return false;

            var result = (long)current + amount;

            // Prevent negative
            if (result < 0)
                return false;

            // Prevent overflow
            if (result > uint.MaxValue)
                result = uint.MaxValue;

            _credits[slot] = (uint)result;
            return true;
        }
    }

    public Task PersistAsync(ulong steamId, PlayerSlot slot)
    {
        uint value;
        lock (_sync)
        {
            if (!_credits.TryGetValue(slot, out value))
                return Task.CompletedTask;
        }
        return _repository.UpdateCreditsAsync(steamId, value);
    }

    // -------------------------
    // SteamID-based (API)
    // -------------------------

    public bool SetCredits(ulong steamId, uint amount)
    {
        var steam = new SteamID(steamId);
        if (!steam.IsValidUserId())
            return false;

        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.SteamId == steamId);

        if (client is not null)
        {
            return SetCreditsBySlot(client.Slot, amount);
        }

        try
        {
            _repository.UpdateCreditsAsync(steamId, amount).Wait();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set credits for {SteamId}", steamId);
            return false;
        }
    }

    public uint? GetCredits(ulong steamId)
    {
        var steam = new SteamID(steamId);
        if (!steam.IsValidUserId())
            return null;

        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.SteamId == steamId);

        if (client is not null)
        {
            return GetCreditsBySlot(client.Slot);
        }

        try
        {
            return _repository.GetByIdAsync(steamId).Result?.Credits;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get credits for {SteamId}", steamId);
            return null;
        }
    }

    public bool AddCredits(ulong steamId, int amount)
    {
        var steam = new SteamID(steamId);
        if (!steam.IsValidUserId())
            return false;

        var client = _bridge.ClientManager
            .GetGameClients(true)
            .FirstOrDefault(c => c.SteamId == steamId);

        if (client is not null)
        {
            return AddCreditsBySlot(client.Slot, amount);
        }

        try
        {
            var user = _repository.GetByIdAsync(steamId).Result;
            if (user is null)
                return false;

            var result = (long)user.Credits + amount;

            if (result < 0)
                return false;

            if (result > uint.MaxValue)
                result = uint.MaxValue;

            _repository.UpdateCreditsAsync(steamId, (uint)result).Wait();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add credits for {SteamId}", steamId);
            return false;
        }
    }
}
