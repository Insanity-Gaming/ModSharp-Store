using InsanityGaming.Store.Shared;
using Sharp.Shared.Units;

namespace InsanityGaming.Store.Interfaces;

public interface ICredits : ICreditsApi
{
    uint GetCreditsBySlot(PlayerSlot slot);
    bool SetCreditsBySlot(PlayerSlot slot, uint amount);
    bool AddCreditsBySlot(PlayerSlot slot, int amount);
}