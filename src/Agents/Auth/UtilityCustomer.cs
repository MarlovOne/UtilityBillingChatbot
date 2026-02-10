// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Represents a utility customer in the CIS (Customer Information System).
/// </summary>
public record UtilityCustomer
{
    public required string AccountNumber { get; init; }
    public required string Name { get; init; }
    public required string Phone { get; init; }
    public required string Email { get; init; }
    public required string ServiceAddress { get; init; }
    public required string LastFourSSN { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public required decimal AccountBalance { get; init; }
    public required DateOnly DueDate { get; init; }
    public required decimal LastPaymentAmount { get; init; }
    public required DateOnly LastPaymentDate { get; init; }
    public required bool IsOnAutoPay { get; init; }
    public required string RateCode { get; init; }
    public required string MeterNumber { get; init; }
    public required List<BillRecord> BillingHistory { get; init; }
    public required List<UsageRecord> UsageHistory { get; init; }
    /// <summary>Current, PastDue, Collections</summary>
    public required string DelinquencyStatus { get; init; }
    public required bool EligibleForExtension { get; init; }
}

/// <summary>
/// Bill record from CIS.
/// </summary>
/// <param name="BillingPeriod">The billing period (e.g., "2024-01").</param>
/// <param name="KwhUsage">Total kWh used in this period.</param>
/// <param name="AmountDue">Amount due for this bill.</param>
/// <param name="ReadType">A=Actual, E=Estimated.</param>
/// <param name="BillDate">Date the bill was generated.</param>
public record BillRecord(
    string BillingPeriod,
    int KwhUsage,
    decimal AmountDue,
    string ReadType,
    DateOnly BillDate);

/// <summary>
/// Usage record from MDM (Meter Data Management).
/// </summary>
/// <param name="Period">The period (e.g., "2024-01").</param>
/// <param name="TotalKwh">Total kWh used.</param>
/// <param name="AvgDailyKwh">Average daily kWh usage.</param>
public record UsageRecord(
    string Period,
    int TotalKwh,
    decimal AvgDailyKwh);
