using InsanityGaming.Store.Configs;
using InsanityGaming.Store.Interfaces;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Modules;

public class CreditsModule : IModule, ICredits
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<CreditsModule> _logger;
    private readonly IUserRepository _repository;
    private readonly Store _plugin;
    private StoreConfig _config;

    private readonly Dictionary<PlayerSlot, uint> _credits = new();

    private Guid _timer;

    public CreditsModule(
        InterfaceBridge bridge,
        Store plugin,
        StoreConfig config,
        ILogger<CreditsModule> logger,
        IUserRepository repository)
    {
        _plugin = plugin;
        _config = config;
        _bridge = bridge;
        _logger = logger;
        _repository = repository;
    }

    public bool Init()
    {
        _timer = _bridge.ModSharp.PushTimer(AwardCredits, _config.CreditFrequency, GameTimerFlags.Repeatable);

        return true;
    }

    public void Shutdown()
    {
        _bridge.ModSharp.StopTimer(_timer);
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
        var finalReward = (uint)(basePoints * dynamicMultiplier);

        foreach (var controller in players)
        {
            var success = AddCreditsBySlot(controller.PlayerSlot, (int)finalReward);

            if (!success)
            {
                _logger.LogWarning("Failed to award credits to slot {Slot}", controller.PlayerSlot);
            }
        }

        _logger.LogInformation(
            "Credits tick | Players: {Count}/{Max} ({Percent:P0}) | Reward: {Reward} | Mult: {Mult:F2}",
            count,
            maxPlayers,
            percentFull,
            finalReward,
            dynamicMultiplier
        );
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