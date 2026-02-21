// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Agents.UtilityData;

/// <summary>
/// Result from GetAccountBalance tool.
/// </summary>
public record BalanceResult(
    decimal Balance,
    string FormattedBalance,
    DateOnly DueDate,
    int DaysUntilDue,
    decimal LastPaymentAmount,
    DateOnly LastPaymentDate,
    string Status);

/// <summary>
/// Result from GetPaymentStatus tool.
/// </summary>
public record PaymentStatusResult(
    bool PaymentReceived,
    decimal LastPaymentAmount,
    DateOnly LastPaymentDate,
    int DaysSincePayment,
    string Message);

/// <summary>
/// Result from GetDueDate tool.
/// </summary>
public record DueDateResult(
    DateOnly DueDate,
    int DaysUntilDue,
    bool IsPastDue,
    string Message);

/// <summary>
/// Result from GetUsageAnalysis tool.
/// </summary>
public record UsageAnalysisResult(
    int CurrentKwh,
    int PreviousKwh,
    int DifferenceKwh,
    decimal PercentChange,
    string Trend,
    string Analysis);

/// <summary>
/// Result from GetAutoPayStatus tool.
/// </summary>
public record AutoPayResult(
    bool IsEnrolled,
    string Message);

/// <summary>
/// Result from GetBillDetails tool.
/// </summary>
public record BillDetailsResult(
    string BillingPeriod,
    int KwhUsage,
    decimal AmountDue,
    string FormattedAmount,
    DateOnly BillDate,
    string ReadType,
    string ReadTypeDescription);

/// <summary>
/// Result from GetMeterReadType tool.
/// </summary>
public record MeterReadResult(
    string ReadType,
    string Description,
    string BillingPeriod);

/// <summary>
/// Result from GetBillingHistory tool.
/// </summary>
public record BillingHistoryResult(
    List<BillSummary> Bills,
    int TotalBills);

/// <summary>
/// Summary of a single bill for history display.
/// </summary>
public record BillSummary(
    string BillingPeriod,
    decimal AmountDue,
    string FormattedAmount,
    int KwhUsage,
    DateOnly BillDate);

/// <summary>
/// Result from MakePayment tool.
/// </summary>
public record PaymentResult(
    bool Success,
    decimal Amount,
    string BillingPeriod,
    string ConfirmationNumber,
    string Message);