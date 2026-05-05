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

public class CreditsModule : IModule, ICredits, IClientListener
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<CreditsModule> _logger;
    private readonly IUserRepository _repository;
    private readonly AdminManagerHolder _adminManager;
    private readonly Store _plugin;
    private StoreConfig _config;

    private readonly Dictionary<PlayerSlot, uint> _credits = new();

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

        foreach (var c in _bridge.ClientManager.GetGameClients(true))
        {
            if (c.SteamId == 0) continue;
            if (!_credits.TryGetValue(c.Slot, out var amount)) continue;
            try { _repository.UpdateCreditsAsync(c.SteamId, amount).Wait(); }
            catch (Exception ex) { _logger.LogError(ex, "[Credits] Shutdown persist failed for {SteamId}", c.SteamId); }
        }

        _credits.Clear();
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

    public void OnClientConnected(IGameClient client)
    {
        if (client.SteamId == 0) return;

        var steamId = client.SteamId;
        var slot    = client.Slot;

        Task.Run(async () =>
        {
            try
            {
                var user = await _repository.GetByIdAsync(steamId);
                if (user is null)
                {
                    user = new Users { Id = steamId, Name = client.Name ?? string.Empty, Credits = 0 };
                    await _repository.UpsertAsync(user);
                }

                _credits[slot] = user.Credits;
                _logger.LogInformation("[Credits] Loaded {Credits} for {SteamId} (slot {Slot})",
                    user.Credits, steamId, slot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Credits] Failed to load credits for {SteamId}", steamId);
            }
        });
    }

    public void OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
    {
        var slot    = client.Slot;
        if (!_credits.Remove(slot, out var amount))
            return;

        if (client.SteamId == 0) return;

        
        var steamId = client.SteamId;

        try
        {
            _repository.UpdateCreditsAsync(steamId, amount).Wait();
            _logger.LogInformation("[Credits] Persisted {Credits} for {SteamId}", amount, steamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Credits] Failed to persist credits for {SteamId}", steamId);
        }
    }

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    // -------------------------
    // Credit distribution
    // -------------------------

    private void AwardCredits()
    {
        var maxPlayers = _bridge.GlobalVars.MaxClients;

        var players = _bridge.EntityManager
            .GetPlayerControllers()
            .Where(c => c is { Team: > CStrikeTeam.Spectator, IsFakeClient: false })
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
                _logger.LogWarning("Failed to award credits to slot {Slot}", controller.PlayerSlot);
        }

        _logger.LogInformation(
            "Credits tick | Players: {Count}/{Max} ({Percent:P0}) | Base: {Base} | DynMult: {Mult:F2}",
            count,
            maxPlayers,
            percentFull,
            (uint)(basePoints * dynamicMultiplier),
            dynamicMultiplier
        );

        var snapshot = _bridge.ClientManager.GetGameClients(true)
            .Where(c => c.SteamId != 0 && _credits.ContainsKey(c.Slot))
            .Select(c => (SteamId: (ulong)c.SteamId, Credits: _credits[c.Slot]))
            .ToList();

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
        return _credits.GetValueOrDefault(slot);
    }

    public bool SetCreditsBySlot(PlayerSlot slot, uint amount)
    {
        _credits[slot] = amount;
        return true;
    }

    public bool AddCreditsBySlot(PlayerSlot slot, int amount)
    {
        var current = _credits.GetValueOrDefault(slot);

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
