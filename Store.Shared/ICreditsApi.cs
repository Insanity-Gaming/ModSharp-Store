namespace InsanityGaming.Store.Shared;

public interface ICreditsApi
{
    const string Identity = "InsanityGaming.Store.Shared.ICreditsApi";

    bool SetCredits(ulong steamId, uint amount);
    uint? GetCredits(ulong steamId);
    bool AddCredits(ulong steamId, int amount);
}