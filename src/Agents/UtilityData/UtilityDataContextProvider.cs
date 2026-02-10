// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Agents.Auth;

namespace UtilityBillingChatbot.Agents.UtilityData;

/// <summary>
/// Context provider for utility data queries. Provides tools to query customer
/// account data such as balance, usage, payment status, and billing history.
/// </summary>
public sealed class UtilityDataContextProvider : AIContextProvider
{
    private readonly UtilityCustomer _customer;

    public UtilityDataContextProvider(UtilityCustomer customer)
    {
        _customer = customer;
    }

    /// <summary>
    /// Customer name for external reference.
    /// </summary>
    public string CustomerName => _customer.Name;

    /// <summary>
    /// Account number for external reference.
    /// </summary>
    public string AccountNumber => _customer.AccountNumber;

    /// <summary>
    /// Called before each agent invocation. Provides instructions and tools.
    /// </summary>
    public override ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken ct)
    {
        var instructions = BuildInstructions();
        var tools = BuildTools();

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = instructions,
            Tools = tools
        });
    }

    private string BuildInstructions()
    {
        return $"""
            You are a utility billing customer service assistant helping {_customer.Name}
            with their account ({_customer.AccountNumber}) at {_customer.ServiceAddress}.

            You have access to tools to look up account information. Use them to answer
            the customer's questions accurately.

            GUIDELINES:
            - Be helpful and professional
            - Use the tools to get accurate information before answering
            - Format currency amounts clearly (e.g., $187.43)
            - When discussing dates, be specific (e.g., "February 15, 2024")
            - If the customer asks about something you don't have tools for,
              let them know and offer to connect them with customer service
            - Keep responses concise but complete
            """;
    }

    private List<AITool> BuildTools()
    {
        return
        [
            AIFunctionFactory.Create(GetAccountBalance,
                description: "Get the current account balance, due date, and last payment info"),
            AIFunctionFactory.Create(GetPaymentStatus,
                description: "Check if a recent payment has been received"),
            AIFunctionFactory.Create(GetDueDate,
                description: "Get the bill due date and days until due"),
            AIFunctionFactory.Create(GetUsageAnalysis,
                description: "Compare current usage to previous period and analyze changes"),
            AIFunctionFactory.Create(GetAutoPayStatus,
                description: "Check if the customer is enrolled in AutoPay"),
            AIFunctionFactory.Create(GetBillDetails,
                description: "Get details of the most recent bill"),
            AIFunctionFactory.Create(GetMeterReadType,
                description: "Check if the last meter read was actual or estimated"),
            AIFunctionFactory.Create(GetBillingHistory,
                description: "Get a list of recent bills")
        ];
    }

    #region Tool Methods

    [Description("Get the current account balance, due date, and last payment info")]
    public BalanceResult GetAccountBalance()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var daysUntilDue = _customer.DueDate.DayNumber - today.DayNumber;
        var status = daysUntilDue < 0 ? "Past Due" :
                     daysUntilDue == 0 ? "Due Today" :
                     _customer.AccountBalance == 0 ? "Paid" : "Current";

        return new BalanceResult(
            Balance: _customer.AccountBalance,
            FormattedBalance: $"${_customer.AccountBalance:F2}",
            DueDate: _customer.DueDate,
            DaysUntilDue: daysUntilDue,
            LastPaymentAmount: _customer.LastPaymentAmount,
            LastPaymentDate: _customer.LastPaymentDate,
            Status: status);
    }

    [Description("Check if a recent payment has been received")]
    public PaymentStatusResult GetPaymentStatus()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var daysSincePayment = today.DayNumber - _customer.LastPaymentDate.DayNumber;
        var recentPayment = daysSincePayment <= 30;

        var message = recentPayment
            ? $"Payment of ${_customer.LastPaymentAmount:F2} was received on {_customer.LastPaymentDate:MMMM d, yyyy}."
            : $"Last payment of ${_customer.LastPaymentAmount:F2} was received {daysSincePayment} days ago on {_customer.LastPaymentDate:MMMM d, yyyy}.";

        return new PaymentStatusResult(
            PaymentReceived: recentPayment,
            LastPaymentAmount: _customer.LastPaymentAmount,
            LastPaymentDate: _customer.LastPaymentDate,
            DaysSincePayment: daysSincePayment,
            Message: message);
    }

    [Description("Get the bill due date and days until due")]
    public DueDateResult GetDueDate()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var daysUntilDue = _customer.DueDate.DayNumber - today.DayNumber;
        var isPastDue = daysUntilDue < 0;

        var message = isPastDue
            ? $"Your bill was due on {_customer.DueDate:MMMM d, yyyy} and is {Math.Abs(daysUntilDue)} days past due."
            : daysUntilDue == 0
                ? $"Your bill is due today, {_customer.DueDate:MMMM d, yyyy}."
                : $"Your bill is due on {_customer.DueDate:MMMM d, yyyy}, which is {daysUntilDue} days from now.";

        return new DueDateResult(
            DueDate: _customer.DueDate,
            DaysUntilDue: daysUntilDue,
            IsPastDue: isPastDue,
            Message: message);
    }

    [Description("Compare current usage to previous period and analyze changes")]
    public UsageAnalysisResult GetUsageAnalysis()
    {
        if (_customer.UsageHistory.Count < 2)
        {
            var current = _customer.UsageHistory.FirstOrDefault();
            return new UsageAnalysisResult(
                CurrentKwh: current?.TotalKwh ?? 0,
                PreviousKwh: 0,
                DifferenceKwh: 0,
                PercentChange: 0,
                Trend: "Unknown",
                Analysis: "Insufficient usage history for comparison.");
        }

        var currentUsage = _customer.UsageHistory[^1];
        var previousUsage = _customer.UsageHistory[^2];

        var difference = currentUsage.TotalKwh - previousUsage.TotalKwh;
        var percentChange = previousUsage.TotalKwh > 0
            ? (decimal)difference / previousUsage.TotalKwh * 100
            : 0;

        var trend = percentChange switch
        {
            > 20 => "Significantly Higher",
            > 5 => "Higher",
            < -20 => "Significantly Lower",
            < -5 => "Lower",
            _ => "Similar"
        };

        var analysis = trend switch
        {
            "Significantly Higher" => $"Your usage increased by {Math.Abs(percentChange):F0}% ({difference} kWh) compared to last month. This could be due to seasonal changes, new appliances, or increased occupancy.",
            "Higher" => $"Your usage is up {Math.Abs(percentChange):F0}% ({difference} kWh) from last month.",
            "Significantly Lower" => $"Your usage decreased by {Math.Abs(percentChange):F0}% ({Math.Abs(difference)} kWh) compared to last month.",
            "Lower" => $"Your usage is down {Math.Abs(percentChange):F0}% ({Math.Abs(difference)} kWh) from last month.",
            _ => "Your usage is similar to last month."
        };

        return new UsageAnalysisResult(
            CurrentKwh: currentUsage.TotalKwh,
            PreviousKwh: previousUsage.TotalKwh,
            DifferenceKwh: difference,
            PercentChange: Math.Round(percentChange, 1),
            Trend: trend,
            Analysis: analysis);
    }

    [Description("Check if the customer is enrolled in AutoPay")]
    public AutoPayResult GetAutoPayStatus()
    {
        var message = _customer.IsOnAutoPay
            ? "You are enrolled in AutoPay. Your bill will be automatically paid on the due date."
            : "You are not currently enrolled in AutoPay. Would you like information on how to enroll?";

        return new AutoPayResult(
            IsEnrolled: _customer.IsOnAutoPay,
            Message: message);
    }

    [Description("Get details of the most recent bill")]
    public BillDetailsResult GetBillDetails()
    {
        var latestBill = _customer.BillingHistory.LastOrDefault();
        if (latestBill is null)
        {
            return new BillDetailsResult(
                BillingPeriod: "N/A",
                KwhUsage: 0,
                AmountDue: 0,
                FormattedAmount: "$0.00",
                BillDate: DateOnly.FromDateTime(DateTime.Now),
                ReadType: "N/A",
                ReadTypeDescription: "No billing history available");
        }

        var readTypeDescription = latestBill.ReadType switch
        {
            "A" => "Actual meter read",
            "E" => "Estimated meter read",
            _ => "Unknown read type"
        };

        return new BillDetailsResult(
            BillingPeriod: latestBill.BillingPeriod,
            KwhUsage: latestBill.KwhUsage,
            AmountDue: latestBill.AmountDue,
            FormattedAmount: $"${latestBill.AmountDue:F2}",
            BillDate: latestBill.BillDate,
            ReadType: latestBill.ReadType,
            ReadTypeDescription: readTypeDescription);
    }

    [Description("Check if the last meter read was actual or estimated")]
    public MeterReadResult GetMeterReadType()
    {
        var latestBill = _customer.BillingHistory.LastOrDefault();
        if (latestBill is null)
        {
            return new MeterReadResult(
                ReadType: "N/A",
                Description: "No billing history available",
                BillingPeriod: "N/A");
        }

        var description = latestBill.ReadType switch
        {
            "A" => "Your last bill was based on an actual meter read.",
            "E" => "Your last bill was based on an estimated meter read. This can happen when the meter couldn't be accessed.",
            _ => "Unknown read type"
        };

        return new MeterReadResult(
            ReadType: latestBill.ReadType,
            Description: description,
            BillingPeriod: latestBill.BillingPeriod);
    }

    [Description("Get a list of recent bills")]
    public BillingHistoryResult GetBillingHistory()
    {
        var bills = _customer.BillingHistory
            .OrderByDescending(b => b.BillDate)
            .Select(b => new BillSummary(
                BillingPeriod: b.BillingPeriod,
                AmountDue: b.AmountDue,
                FormattedAmount: $"${b.AmountDue:F2}",
                KwhUsage: b.KwhUsage,
                BillDate: b.BillDate))
            .ToList();

        return new BillingHistoryResult(
            Bills: bills,
            TotalBills: bills.Count);
    }

    #endregion
}
