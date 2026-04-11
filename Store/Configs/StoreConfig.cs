using InsanityGaming.Store.Interfaces;

namespace InsanityGaming.Store.Configs;

public class StoreConfig : IConfig
{
    public bool Enabled { get; set; } = true;
    public int CreditFrequency { get; set; } = 5;
    public int CreditReward { get; set; } = 5;
    public float CreditMultiplier { get; set; } = 1.0f;
    public float DynamicMultiplierMax { get; set; } = 5.0f;

    public string DbHost { get; set; } = "localhost";
    public int DbPort { get; set; } = 3306;
    public string DbName { get; set; } = "store";
    public string DbUser { get; set; } = "root";
    public string DbPassword { get; set; } = "";
}
