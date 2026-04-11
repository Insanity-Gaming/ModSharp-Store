using InsanityGaming.Store.Interfaces;
using InsanityGaming.Store.Shared;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Modules;

public class CreditsModule : IModule, ICreditsApi
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<CreditsModule> _logger;
    private readonly IUserRepository _repository;
    private readonly Store _plugin;

    private readonly Dictionary<PlayerSlot, uint> _credits = new();

    public CreditsModule(
        InterfaceBridge bridge,
        Store plugin,
        ILogger<CreditsModule> logger,
        IUserRepository repository)
    {
        _plugin = plugin;
        _bridge = bridge;
        _logger = logger;
        _repository = repository;
    }

    public bool Init()
    {
        _bridge.ModuleManager.RegisterSharpModuleInterface<ICreditsApi>(
            _plugin,
            ICreditsApi.Identity,
            this
        );

        return true;
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

    public bool AddCreditsBySlot(PlayerSlot slot, uint amount)
    {
        if (!_credits.ContainsKey(slot))
        {
            _credits[slot] = amount;
            return true;
        }

        _credits[slot] += amount;
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

        // Offline → persist directly
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

        // Offline → fetch from DB
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

    public bool AddCredits(ulong steamId, uint amount)
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

        // Offline → read + update
        try
        {
            var user = _repository.GetByIdAsync(steamId).Result;
            if (user is null)
                return false;

            var newAmount = user.Credits + amount;
            _repository.UpdateCreditsAsync(steamId, newAmount).Wait();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add credits for {SteamId}", steamId);
            return false;
        }
    }
}