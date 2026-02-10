// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.UtilityData;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Unit tests for UtilityDataContextProvider tool methods.
/// These tests do not require an LLM - they test the tool logic directly.
/// </summary>
public class UtilityDataContextProviderTests
{
    private static UtilityCustomer CreateJohnSmith() => new()
    {
        AccountNumber = "1234567890",
        Name = "John Smith",
        Phone = "555-1234",
        Email = "john.smith@example.com",
        ServiceAddress = "123 Main St, Anytown, ST 12345",
        LastFourSSN = "1234",
        DateOfBirth = new DateOnly(1985, 3, 15),
        AccountBalance = 187.43m,
        DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(12)),
        LastPaymentAmount = 142.50m,
        LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-18)),
        IsOnAutoPay = false,
        RateCode = "R1",
        MeterNumber = "MTR-00123456",
        BillingHistory =
        [
            new BillRecord("2024-01", 892, 142.50m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-48))),
            new BillRecord("2024-02", 1247, 187.43m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-18)))
        ],
        UsageHistory =
        [
            new UsageRecord("2024-01", 892, 28.8m),
            new UsageRecord("2024-02", 1247, 44.5m)
        ],
        DelinquencyStatus = "Current",
        EligibleForExtension = true
    };

    private static UtilityCustomer CreateMariaGarcia() => new()
    {
        AccountNumber = "9876543210",
        Name = "Maria Garcia",
        Phone = "555-5678",
        Email = "maria.garcia@example.com",
        ServiceAddress = "456 Oak Ave, Anytown, ST 12345",
        LastFourSSN = "5678",
        DateOfBirth = new DateOnly(1990, 7, 22),
        AccountBalance = 0.00m,
        DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(5)),
        LastPaymentAmount = 98.50m,
        LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-3)),
        IsOnAutoPay = true,
        RateCode = "R1",
        MeterNumber = "MTR-00789012",
        BillingHistory =
        [
            new BillRecord("2024-01", 654, 98.50m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-33))),
            new BillRecord("2024-02", 687, 102.30m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-3)))
        ],
        UsageHistory =
        [
            new UsageRecord("2024-01", 654, 23.4m),
            new UsageRecord("2024-02", 687, 24.5m)
        ],
        DelinquencyStatus = "Current",
        EligibleForExtension = false
    };

    private static UtilityCustomer CreateRobertJohnson() => new()
    {
        AccountNumber = "5555555555",
        Name = "Robert Johnson",
        Phone = "555-9999",
        Email = "rjohnson@example.com",
        ServiceAddress = "789 Elm St, Anytown, ST 12345",
        LastFourSSN = "9999",
        DateOfBirth = new DateOnly(1972, 11, 8),
        AccountBalance = 423.67m,
        DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
        LastPaymentAmount = 150.00m,
        LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-45)),
        IsOnAutoPay = false,
        RateCode = "R1",
        MeterNumber = "MTR-00345678",
        BillingHistory =
        [
            new BillRecord("2024-01", 1456, 218.40m, "E", DateOnly.FromDateTime(DateTime.Now.AddDays(-60))),
            new BillRecord("2024-02", 1523, 228.45m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-30)))
        ],
        UsageHistory =
        [
            new UsageRecord("2024-01", 1456, 52.0m),
            new UsageRecord("2024-02", 1523, 54.4m)
        ],
        DelinquencyStatus = "PastDue",
        EligibleForExtension = true
    };

    #region GetAccountBalance Tests

    [Fact]
    public void GetAccountBalance_JohnSmith_ReturnsCorrectBalance()
    {
        var provider = new UtilityDataContextProvider(CreateJohnSmith());

        var result = provider.GetAccountBalance();

        Assert.Equal(187.43m, result.Balance);
        Assert.Equal("$187.43", result.FormattedBalance);
        Assert.Equal(12, result.DaysUntilDue);
        Assert.Equal("Current", result.Status);
    }

    [Fact]
    public void GetAccountBalance_MariaGarcia_ReturnsZeroBalance()
    {
        var provider = new UtilityDataContextProvider(CreateMariaGarcia());

        var result = provider.GetAccountBalance();

        Assert.Equal(0.00m, result.Balance);
        Assert.Equal("$0.00", result.FormattedBalance);
        Assert.Equal("Paid", result.Status);
    }

    [Fact]
    public void GetAccountBalance_RobertJohnson_ReturnsPastDue()
    {
        var provider = new UtilityDataContextProvider(CreateRobertJohnson());

        var result = provider.GetAccountBalance();

        Assert.Equal(423.67m, result.Balance);
        Assert.Equal(-5, result.DaysUntilDue);
        Assert.Equal("Past Due", result.Status);
    }

    #endregion

    #region GetDueDate Tests

    [Fact]
    public void GetDueDate_FutureDate_ShowsDaysRemaining()
    {
        var provider = new UtilityDataContextProvider(CreateJohnSmith());

        var result = provider.GetDueDate();

        Assert.Equal(12, result.DaysUntilDue);
        Assert.False(result.IsPastDue);
        Assert.Contains("12 days from now", result.Message);
    }

    [Fact]
    public void GetDueDate_PastDate_ShowsPastDue()
    {
        var provider = new UtilityDataContextProvider(CreateRobertJohnson());

        var result = provider.GetDueDate();

        Assert.True(result.IsPastDue);
        Assert.Contains("5 days past due", result.Message);
    }

    #endregion

    #region GetUsageAnalysis Tests

    [Fact]
    public void GetUsageAnalysis_JohnSmith_ShowsSignificantIncrease()
    {
        var provider = new UtilityDataContextProvider(CreateJohnSmith());

        var result = provider.GetUsageAnalysis();

        Assert.Equal(1247, result.CurrentKwh);
        Assert.Equal(892, result.PreviousKwh);
        Assert.Equal(355, result.DifferenceKwh);
        Assert.Equal("Significantly Higher", result.Trend);
        Assert.True(result.PercentChange > 20);
    }

    [Fact]
    public void GetUsageAnalysis_MariaGarcia_ShowsSlightIncrease()
    {
        // Maria: 687 vs 654 kWh = 5.05% increase (just over 5% threshold)
        // Note: Trend uses unrounded value (5.05 > 5), but PercentChange returns rounded (5.0)
        var provider = new UtilityDataContextProvider(CreateMariaGarcia());

        var result = provider.GetUsageAnalysis();

        Assert.Equal(687, result.CurrentKwh);
        Assert.Equal(654, result.PreviousKwh);
        Assert.Equal("Higher", result.Trend);
        Assert.Equal(5.0m, result.PercentChange);
    }

    #endregion

    #region GetAutoPayStatus Tests

    [Fact]
    public void GetAutoPayStatus_NotEnrolled_ReturnsFalse()
    {
        var provider = new UtilityDataContextProvider(CreateJohnSmith());

        var result = provider.GetAutoPayStatus();

        Assert.False(result.IsEnrolled);
        Assert.Contains("not currently enrolled", result.Message);
    }

    [Fact]
    public void GetAutoPayStatus_Enrolled_ReturnsTrue()
    {
        var provider = new UtilityDataContextProvider(CreateMariaGarcia());

        var result = provider.GetAutoPayStatus();

        Assert.True(result.IsEnrolled);
        Assert.Contains("enrolled in AutoPay", result.Message);
    }

    #endregion

    #region GetMeterReadType Tests

    [Fact]
    public void GetMeterReadType_ActualRead_ReturnsA()
    {
        var provider = new UtilityDataContextProvider(CreateJohnSmith());

        var result = provider.GetMeterReadType();

        Assert.Equal("A", result.ReadType);
        Assert.Contains("actual meter read", result.Description);
    }

    [Fact]
    public void GetMeterReadType_EstimatedThenActual_ReturnsActual()
    {
        // Robert Johnson has an estimated read for 2024-01 but actual for 2024-02
        var provider = new UtilityDataContextProvider(CreateRobertJohnson());

        var result = provider.GetMeterReadType();

        Assert.Equal("A", result.ReadType);
        Assert.Equal("2024-02", result.BillingPeriod);
    }

    #endregion

    #region GetBillingHistory Tests

    [Fact]
    public void GetBillingHistory_ReturnsBillsInDescendingOrder()
    {
        var provider = new UtilityDataContextProvider(CreateJohnSmith());

        var result = provider.GetBillingHistory();

        Assert.Equal(2, result.TotalBills);
        Assert.Equal("2024-02", result.Bills[0].BillingPeriod);
        Assert.Equal("2024-01", result.Bills[1].BillingPeriod);
    }

    [Fact]
    public void GetBillingHistory_IncludesCorrectAmounts()
    {
        var provider = new UtilityDataContextProvider(CreateMariaGarcia());

        var result = provider.GetBillingHistory();

        var latestBill = result.Bills[0];
        Assert.Equal(102.30m, latestBill.AmountDue);
        Assert.Equal("$102.30", latestBill.FormattedAmount);
        Assert.Equal(687, latestBill.KwhUsage);
    }

    #endregion

    #region GetPaymentStatus Tests

    [Fact]
    public void GetPaymentStatus_RecentPayment_ReturnsReceived()
    {
        var provider = new UtilityDataContextProvider(CreateMariaGarcia());

        var result = provider.GetPaymentStatus();

        Assert.True(result.PaymentReceived);
        Assert.Equal(98.50m, result.LastPaymentAmount);
        Assert.Equal(3, result.DaysSincePayment);
    }

    [Fact]
    public void GetPaymentStatus_OldPayment_ReturnsNotRecent()
    {
        var provider = new UtilityDataContextProvider(CreateRobertJohnson());

        var result = provider.GetPaymentStatus();

        Assert.False(result.PaymentReceived);
        Assert.Equal(45, result.DaysSincePayment);
    }

    #endregion

    #region GetBillDetails Tests

    [Fact]
    public void GetBillDetails_ReturnsLatestBill()
    {
        var provider = new UtilityDataContextProvider(CreateJohnSmith());

        var result = provider.GetBillDetails();

        Assert.Equal("2024-02", result.BillingPeriod);
        Assert.Equal(1247, result.KwhUsage);
        Assert.Equal(187.43m, result.AmountDue);
        Assert.Equal("$187.43", result.FormattedAmount);
        Assert.Equal("A", result.ReadType);
        Assert.Equal("Actual meter read", result.ReadTypeDescription);
    }

    #endregion

    #region Provider Properties Tests

    [Fact]
    public void Provider_ExposesCustomerInfo()
    {
        var provider = new UtilityDataContextProvider(CreateJohnSmith());

        Assert.Equal("John Smith", provider.CustomerName);
        Assert.Equal("1234567890", provider.AccountNumber);
    }

    #endregion
}
