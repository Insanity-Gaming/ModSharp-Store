using InsanityGaming.Store.Hud;
using InsanityGaming.Store.Items;
using Xunit;

namespace InsanityGaming.Store.Tests;

public class StoreItemRulesTests
{
    [Fact]
    public void Evaluate_LockedWhenPermissionMissing()
        => Assert.Equal(ItemAvailability.Locked,
            StoreItemRules.Evaluate(new ItemDefinition { Permission = "vip", Price = 10 }, false, 0, true));

    [Fact]
    public void Evaluate_LockedBeatsUnaffordable()
        => Assert.Equal(ItemAvailability.Locked,
            StoreItemRules.Evaluate(new ItemDefinition { Permission = "vip", Price = 999 }, false, 0, true));

    [Fact]
    public void Evaluate_PermissionMetIsNotLocked()
        => Assert.Equal(ItemAvailability.Available,
            StoreItemRules.Evaluate(new ItemDefinition { Permission = "vip", Price = 10 }, true, 10, true));

    [Fact]
    public void Evaluate_UnaffordableWhenCreditsBelowPrice()
        => Assert.Equal(ItemAvailability.Unaffordable,
            StoreItemRules.Evaluate(new ItemDefinition { Price = 750 }, true, 500, true));

    [Fact]
    public void Evaluate_EquipTabIgnoresCredits()
        => Assert.Equal(ItemAvailability.Available,
            StoreItemRules.Evaluate(new ItemDefinition { Price = 750 }, true, 0, false));

    [Fact]
    public void Evaluate_ExactCreditsIsAvailable()
        => Assert.Equal(ItemAvailability.Available,
            StoreItemRules.Evaluate(new ItemDefinition { Price = 500 }, true, 500, true));

    [Fact]
    public void BlockedActionText_UsesCurrentCredits()
        => Assert.Equal("Need 250 more credits", StoreItemRules.BlockedActionText(ItemAvailability.Unaffordable, 750, 500));

    [Fact]
    public void BlockedActionText_Locked()
        => Assert.Equal("Locked", StoreItemRules.BlockedActionText(ItemAvailability.Locked, 750, 9999));

    [Fact]
    public void BlockedActionText_AvailableIsNull()
        => Assert.Null(StoreItemRules.BlockedActionText(ItemAvailability.Available, 1, 1));

    [Fact]
    public void PermissionText_PrefersDescription()
        => Assert.Equal("long", StoreItemRules.PermissionText(new ItemDefinition { PermissionDescription = "long", PermissionError = "short" }));

    [Fact]
    public void PermissionText_FallsBackToError()
        => Assert.Equal("short", StoreItemRules.PermissionText(new ItemDefinition { PermissionError = "short" }));

    [Fact]
    public void PermissionText_DefaultCopy()
        => Assert.Equal("You don't have access to this item.", StoreItemRules.PermissionText(new ItemDefinition()));

    [Fact]
    public void Order_PriceDescThenName()
    {
        var b = new ItemDefinition { Name = "B", Price = 100 };
        var a = new ItemDefinition { Name = "a", Price = 100 };
        var c = new ItemDefinition { Name = "C", Price = 500 };

        Assert.Equal([c, a, b], StoreItemRules.Order([b, a, c]));
    }

    [Fact]
    public void SplitSections_SecondOpen()
    {
        var (above, below) = StoreItemRules.SplitSections(4, 1);
        Assert.Equal([0, 1], above);
        Assert.Equal([2, 3], below);
    }

    [Fact]
    public void SplitSections_NoneOpen_AllAbove()
    {
        var (above, below) = StoreItemRules.SplitSections(3, -1);
        Assert.Equal([0, 1, 2], above);
        Assert.Empty(below);
    }

    [Fact]
    public void SplitSections_OpenIndexOutOfRange_AllAbove()
    {
        var (above, below) = StoreItemRules.SplitSections(2, 5);
        Assert.Equal([0, 1], above);
        Assert.Empty(below);
    }

    [Theory]
    [InlineData(2, 3, 2)]
    [InlineData(3, 0, 3)]
    [InlineData(1, 3, 3)]
    [InlineData(1, 0, 2)]
    [InlineData(0, 2, 2)]
    public void PreviewTeam_Rule(int playerTeam, int itemTeam, int expected)
        => Assert.Equal(expected, StoreItemRules.PreviewTeam(playerTeam, itemTeam));
}
