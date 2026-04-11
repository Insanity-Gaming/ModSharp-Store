using InsanityGaming.Store.Shared;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Interfaces;

public interface ICredits : ICreditsApi
{
    int GetCreditsBySlot(PlayerSlot slot);
    bool SetCreditsBySlot(PlayerSlot slot, int amount);
    bool AddCreditsBySlot(PlayerSlot slot, int amount);
}