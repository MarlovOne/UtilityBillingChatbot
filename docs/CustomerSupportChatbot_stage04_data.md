## Stage 4: Utility Data Agent

### Objective
Build an agent with tool access to the mock CIS database that answers account-specific utility billing questions for authenticated customers.

### Architecture Notes

This agent follows the same patterns established in Stage 3 (AuthAgent):
- Uses `AIContextProvider` for state and tool injection
- Reuses existing `UtilityCustomer`, `BillRecord`, `UsageRecord` models from `Agents/Auth/`
- Receives authenticated customer info from completed auth session
- Instance methods with `[Description]` attributes for tools

**Directory structure:**
```
src/Agents/UtilityData/
├── UtilityDataAgent.cs           # Main agent class
├── UtilityDataContextProvider.cs # Context provider with tools
└── UtilityDataModels.cs          # Tool result records
```

### Implementation

#### UtilityDataAgent.cs

```csharp
// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Agents.Auth;

namespace UtilityBillingChatbot.Agents.UtilityData;

/// <summary>
/// Agent that fetches utility account data from the mock CIS database.
/// Requires customer to be authenticated first (via AuthAgent).
/// Answers questions like: balance, payments, bill details, usage, AutoPay status.
/// </summary>
public class UtilityDataAgent
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;
    private readonly ILogger<UtilityDataAgent> _logger;

    public UtilityDataAgent(
        IChatClient chatClient,
        MockCISDatabase cisDatabase,
        ILogger<UtilityDataAgent> logger)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
        _logger = logger;
    }

    /// <summary>
    /// Runs a query against the customer's account data.
    /// </summary>
    /// <param name="input">User's question about their account</param>
    /// <param name="session">Session from previous interaction, or null to create from auth session</param>
    /// <param name="authSession">Auth session from completed authentication (required if session is null)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response with account information</returns>
    public async Task<UtilityDataResponse> RunAsync(
        string input,
        UtilityDataSession? session = null,
        AuthSession? authSession = null,
        CancellationToken cancellationToken = default)
    {
        // Create session from auth if not provided
        if (session == null)
        {
            if (authSession == null || !authSession.Provider.IsAuthenticated)
                throw new InvalidOperationException("Customer must be authenticated to access account data");

            session = await CreateSessionAsync(authSession, cancellationToken);
        }

        _logger.LogDebug("UtilityData query: {Input}", input);

        var response = await session.Agent.RunAsync(
            message: input,
            session: session.AgentSession,
            cancellationToken: cancellationToken);

        _logger.LogInformation("UtilityData response for {Customer}", session.Provider.CustomerName);

        return new UtilityDataResponse(
            Text: response.Text ?? string.Empty,
            Session: session,
            CustomerName: session.Provider.CustomerName,
            AccountNumber: session.Provider.AccountNumber);
    }

    /// <summary>
    /// Creates a new utility data session from an authenticated auth session.
    /// </summary>
    public async Task<UtilityDataSession> CreateSessionAsync(
        AuthSession authSession,
        CancellationToken cancellationToken = default)
    {
        if (!authSession.Provider.IsAuthenticated)
            throw new InvalidOperationException("Customer must be authenticated to create data session");

        var customer = _cisDatabase.FindByIdentifier(authSession.Provider.CustomerId!);
        if (customer == null)
            throw new InvalidOperationException("Authenticated customer not found in CIS");

        var provider = new UtilityDataContextProvider(customer);

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "UtilityDataAgent",
            AIContextProviderFactory = (ctx, ct) => new ValueTask<AIContextProvider>(provider)
        });

        var agentSession = await agent.CreateSessionAsync(cancellationToken);

        return new UtilityDataSession(agent, agentSession, provider);
    }
}

/// <summary>
/// Holds the agent, session, and provider for a utility data query session.
/// </summary>
public record UtilityDataSession(
    AIAgent Agent,
    AgentSession AgentSession,
    UtilityDataContextProvider Provider);

/// <summary>
/// Response from the utility data agent.
/// </summary>
public record UtilityDataResponse(
    string Text,
    UtilityDataSession Session,
    string CustomerName,
    string AccountNumber);

/// <summary>
/// Extension methods for registering the UtilityDataAgent.
/// </summary>
public static class UtilityDataAgentExtensions
{
    public static IServiceCollection AddUtilityDataAgent(this IServiceCollection services)
    {
        // MockCISDatabase is already registered by AddAuthAgent
        services.AddSingleton<UtilityDataAgent>();
        return services;
    }
}
```

#### UtilityDataContextProvider.cs

```csharp
// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UtilityBillingChatbot.Agents.Auth;

namespace UtilityBillingChatbot.Agents.UtilityData;

/// <summary>
/// Provides account data query tools for authenticated customers.
/// Tools are bound to a specific customer's data from the CIS.
/// </summary>
public sealed class UtilityDataContextProvider : AIContextProvider
{
    private readonly UtilityCustomer _customer;

    public UtilityDataContextProvider(UtilityCustomer customer)
    {
        _customer = customer;
    }

    // Public accessors
    public string CustomerName => _customer.Name;
    public string AccountNumber => _customer.AccountNumber;

    public override ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken ct)
    {
        var instructions = BuildInstructions();
        var tools = GetTools();

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = instructions,
            Tools = tools
        });
    }

    private string BuildInstructions()
    {
        return $"""
            You are a utility billing account assistant. You help customers access their
            account information using the available tools.

            Customer: {_customer.Name}
            Account: {_customer.AccountNumber}
            Service Address: {_customer.ServiceAddress}

            COMMON QUESTIONS YOU CAN ANSWER:
            - Q1: "Why is my bill so high?" → Use GetUsageAnalysis
            - Q2: "What's my balance?" → Use GetAccountBalance
            - Q3: "Did you receive my payment?" → Use GetPaymentStatus
            - Q4: "When is my payment due?" → Use GetDueDate
            - Q10: "Am I on AutoPay?" → Use GetAutoPayStatus
            - Q12: "What's this charge?" → Use GetBillDetails
            - Q13: "Is this an actual or estimated read?" → Use GetMeterReadType
            - Q19: "Can I get my bill history?" → Use GetBillingHistory

            RULES:
            1. Use the appropriate tool to fetch data the customer requests
            2. Present billing data clearly with dollar amounts and dates
            3. For high bill questions, compare usage periods and suggest reasons
            4. For actions requiring changes (payment arrangements, AutoPay enrollment),
               explain you can only view information and they need to speak to a representative
               or use self-service options
            5. Be empathetic about billing concerns
            """;
    }

    private List<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(GetAccountBalance,
                description: "Gets current balance, due date, and last payment info"),
            AIFunctionFactory.Create(GetPaymentStatus,
                description: "Gets last payment date, amount, and current account status"),
            AIFunctionFactory.Create(GetDueDate,
                description: "Gets payment due date and billing cycle info"),
            AIFunctionFactory.Create(GetUsageAnalysis,
                description: "Compares current vs previous usage to explain bill changes"),
            AIFunctionFactory.Create(GetAutoPayStatus,
                description: "Gets AutoPay enrollment status"),
            AIFunctionFactory.Create(GetBillDetails,
                description: "Gets detailed breakdown of current bill charges"),
            AIFunctionFactory.Create(GetMeterReadType,
                description: "Gets whether last bill was actual (A) or estimated (E) read"),
            AIFunctionFactory.Create(GetBillingHistory,
                description: "Gets recent billing history with amounts and dates")
        ];
    }

    #region Tool Methods

    [Description("Gets current balance, due date, and last payment info")]
    private BalanceResult GetAccountBalance()
    {
        return new BalanceResult(
            CurrentBalance: _customer.AccountBalance,
            DueDate: _customer.DueDate,
            LastPaymentAmount: _customer.LastPaymentAmount,
            LastPaymentDate: _customer.LastPaymentDate,
            DelinquencyStatus: _customer.DelinquencyStatus);
    }

    [Description("Gets last payment date, amount, and current account status")]
    private PaymentStatusResult GetPaymentStatus()
    {
        bool paymentReceived = _customer.LastPaymentDate > DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
        return new PaymentStatusResult(
            LastPaymentReceived: paymentReceived,
            PaymentAmount: _customer.LastPaymentAmount,
            PaymentDate: _customer.LastPaymentDate,
            CurrentBalance: _customer.AccountBalance,
            AccountStatus: _customer.DelinquencyStatus);
    }

    [Description("Gets payment due date and billing cycle info")]
    private DueDateResult GetDueDate()
    {
        var daysUntilDue = (_customer.DueDate.ToDateTime(TimeOnly.MinValue) - DateTime.Now).Days;
        return new DueDateResult(
            DueDate: _customer.DueDate,
            AmountDue: _customer.AccountBalance,
            DaysUntilDue: daysUntilDue,
            IsPastDue: daysUntilDue < 0);
    }

    [Description("Compares current vs previous usage to explain bill changes")]
    private UsageAnalysisResult GetUsageAnalysis()
    {
        var currentBill = _customer.BillingHistory.LastOrDefault();
        var previousBill = _customer.BillingHistory.Count > 1
            ? _customer.BillingHistory[^2]
            : null;

        var usageChange = previousBill != null && previousBill.KwhUsage > 0
            ? ((currentBill?.KwhUsage ?? 0) - previousBill.KwhUsage) / (decimal)previousBill.KwhUsage * 100
            : 0;

        string explanation = usageChange switch
        {
            > 20 => "Your usage increased significantly, possibly due to seasonal heating/cooling or new appliances.",
            > 10 => "Your usage increased moderately compared to last month.",
            < -10 => "Your usage actually decreased compared to last month.",
            _ => "Your usage is similar to last month."
        };

        return new UsageAnalysisResult(
            CurrentPeriodKwh: currentBill?.KwhUsage ?? 0,
            CurrentPeriodAmount: currentBill?.AmountDue ?? 0,
            PreviousPeriodKwh: previousBill?.KwhUsage ?? 0,
            PreviousPeriodAmount: previousBill?.AmountDue ?? 0,
            UsageChangePercent: Math.Round(usageChange, 1),
            Explanation: explanation);
    }

    [Description("Gets AutoPay enrollment status")]
    private AutoPayResult GetAutoPayStatus()
    {
        return new AutoPayResult(
            IsEnrolled: _customer.IsOnAutoPay,
            Message: _customer.IsOnAutoPay
                ? "You are enrolled in AutoPay. Payments are automatically deducted on your due date."
                : "You are not currently enrolled in AutoPay.");
    }

    [Description("Gets detailed breakdown of current bill charges")]
    private BillDetailsResult GetBillDetails()
    {
        var latestBill = _customer.BillingHistory.LastOrDefault();
        return new BillDetailsResult(
            BillingPeriod: latestBill?.BillingPeriod ?? "N/A",
            TotalKwh: latestBill?.KwhUsage ?? 0,
            TotalAmount: latestBill?.AmountDue ?? 0,
            RateCode: _customer.RateCode,
            ReadType: latestBill?.ReadType ?? "A",
            ServiceAddress: _customer.ServiceAddress);
    }

    [Description("Gets whether last bill was actual (A) or estimated (E) read")]
    private MeterReadResult GetMeterReadType()
    {
        var latestBill = _customer.BillingHistory.LastOrDefault();
        var readType = latestBill?.ReadType ?? "A";
        return new MeterReadResult(
            ReadType: readType,
            ReadTypeDescription: readType == "A" ? "Actual meter reading" : "Estimated reading",
            MeterNumber: _customer.MeterNumber,
            Note: readType == "E"
                ? "Your meter could not be read this month. The next bill will include a correction based on actual reading."
                : null);
    }

    [Description("Gets recent billing history with amounts and dates")]
    private BillingHistoryResult GetBillingHistory()
    {
        return new BillingHistoryResult(
            Bills: _customer.BillingHistory.Select(b => new BillSummary(
                Period: b.BillingPeriod,
                Amount: b.AmountDue,
                Kwh: b.KwhUsage,
                BillDate: b.BillDate)).ToList());
    }

    #endregion
}
```

#### UtilityDataModels.cs

```csharp
// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Agents.UtilityData;

/// <summary>Tool result records for utility billing data queries.</summary>

public record BalanceResult(
    decimal CurrentBalance,
    DateOnly DueDate,
    decimal LastPaymentAmount,
    DateOnly LastPaymentDate,
    string DelinquencyStatus);

public record PaymentStatusResult(
    bool LastPaymentReceived,
    decimal PaymentAmount,
    DateOnly PaymentDate,
    decimal CurrentBalance,
    string AccountStatus);

public record DueDateResult(
    DateOnly DueDate,
    decimal AmountDue,
    int DaysUntilDue,
    bool IsPastDue);

public record UsageAnalysisResult(
    int CurrentPeriodKwh,
    decimal CurrentPeriodAmount,
    int PreviousPeriodKwh,
    decimal PreviousPeriodAmount,
    decimal UsageChangePercent,
    string Explanation);

public record AutoPayResult(
    bool IsEnrolled,
    string Message);

public record BillDetailsResult(
    string BillingPeriod,
    int TotalKwh,
    decimal TotalAmount,
    string RateCode,
    string ReadType,
    string ServiceAddress);

public record MeterReadResult(
    string ReadType,
    string ReadTypeDescription,
    string MeterNumber,
    string? Note);

public record BillingHistoryResult(
    List<BillSummary> Bills);

public record BillSummary(
    string Period,
    decimal Amount,
    int Kwh,
    DateOnly BillDate);
```

### DI Registration

Update `Infrastructure/ServiceCollectionExtensions.cs`:

```csharp
using UtilityBillingChatbot.Agents.UtilityData;

// In AddUtilityBillingChatbot method:
services.AddAuthAgent();       // Already exists
services.AddUtilityDataAgent(); // Add this
```

### Testing Stage 4

Testing is split into two categories:
1. **Unit tests** - Test the context provider tools directly without LLM calls (fast, deterministic)
2. **Integration tests** - Test the full agent with LLM (requires configured endpoint)

#### Unit Tests - UtilityDataContextProviderTests.cs

These tests verify tool logic without LLM calls:

```csharp
// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.UtilityData;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Unit tests for UtilityDataContextProvider tool methods.
/// These tests do NOT require an LLM - they test the data access logic directly.
/// </summary>
public class UtilityDataContextProviderTests
{
    private readonly MockCISDatabase _db = new();

    private UtilityDataContextProvider CreateProvider(string phone)
    {
        var customer = _db.FindByIdentifier(phone)!;
        return new UtilityDataContextProvider(customer);
    }

    #region GetAccountBalance Tests

    [Fact]
    public void GetAccountBalance_ReturnsCorrectBalance_ForJohnSmith()
    {
        // Arrange
        var provider = CreateProvider("555-1234");

        // Act - Use reflection to call private method for unit testing
        var result = InvokeToolMethod<BalanceResult>(provider, "GetAccountBalance");

        // Assert
        Assert.Equal(187.43m, result.CurrentBalance);
        Assert.Equal("Current", result.DelinquencyStatus);
        Assert.Equal(142.50m, result.LastPaymentAmount);
    }

    [Fact]
    public void GetAccountBalance_ShowsZeroBalance_ForMariaGarcia()
    {
        // Arrange
        var provider = CreateProvider("555-5678");

        // Act
        var result = InvokeToolMethod<BalanceResult>(provider, "GetAccountBalance");

        // Assert
        Assert.Equal(0.00m, result.CurrentBalance);
    }

    [Fact]
    public void GetAccountBalance_ShowsPastDue_ForRobertJohnson()
    {
        // Arrange
        var provider = CreateProvider("555-9999");

        // Act
        var result = InvokeToolMethod<BalanceResult>(provider, "GetAccountBalance");

        // Assert
        Assert.Equal(423.67m, result.CurrentBalance);
        Assert.Equal("PastDue", result.DelinquencyStatus);
    }

    #endregion

    #region GetDueDate Tests

    [Fact]
    public void GetDueDate_CalculatesDaysCorrectly()
    {
        // Arrange
        var provider = CreateProvider("555-1234");

        // Act
        var result = InvokeToolMethod<DueDateResult>(provider, "GetDueDate");

        // Assert - John has ~12 days until due
        Assert.False(result.IsPastDue);
        Assert.True(result.DaysUntilDue > 0);
    }

    [Fact]
    public void GetDueDate_DetectsPastDue()
    {
        // Arrange - Robert is past due
        var provider = CreateProvider("555-9999");

        // Act
        var result = InvokeToolMethod<DueDateResult>(provider, "GetDueDate");

        // Assert
        Assert.True(result.IsPastDue);
        Assert.True(result.DaysUntilDue < 0);
    }

    #endregion

    #region GetUsageAnalysis Tests

    [Fact]
    public void GetUsageAnalysis_DetectsSignificantIncrease()
    {
        // Arrange - John has 40% usage increase (892 -> 1247 kWh)
        var provider = CreateProvider("555-1234");

        // Act
        var result = InvokeToolMethod<UsageAnalysisResult>(provider, "GetUsageAnalysis");

        // Assert
        Assert.Equal(1247, result.CurrentPeriodKwh);
        Assert.Equal(892, result.PreviousPeriodKwh);
        Assert.True(result.UsageChangePercent > 20); // ~40%
        Assert.Contains("significantly", result.Explanation.ToLower());
    }

    [Fact]
    public void GetUsageAnalysis_DetectsSimilarUsage()
    {
        // Arrange - Maria has ~5% increase (654 -> 687 kWh)
        var provider = CreateProvider("555-5678");

        // Act
        var result = InvokeToolMethod<UsageAnalysisResult>(provider, "GetUsageAnalysis");

        // Assert
        Assert.True(result.UsageChangePercent < 10);
        Assert.Contains("similar", result.Explanation.ToLower());
    }

    #endregion

    #region GetAutoPayStatus Tests

    [Fact]
    public void GetAutoPayStatus_ReturnsNotEnrolled_ForJohnSmith()
    {
        // Arrange
        var provider = CreateProvider("555-1234");

        // Act
        var result = InvokeToolMethod<AutoPayResult>(provider, "GetAutoPayStatus");

        // Assert
        Assert.False(result.IsEnrolled);
        Assert.Contains("not", result.Message.ToLower());
    }

    [Fact]
    public void GetAutoPayStatus_ReturnsEnrolled_ForMariaGarcia()
    {
        // Arrange
        var provider = CreateProvider("555-5678");

        // Act
        var result = InvokeToolMethod<AutoPayResult>(provider, "GetAutoPayStatus");

        // Assert
        Assert.True(result.IsEnrolled);
        Assert.Contains("enrolled", result.Message.ToLower());
    }

    #endregion

    #region GetMeterReadType Tests

    [Fact]
    public void GetMeterReadType_ReturnsActual_ForJohnSmith()
    {
        // Arrange
        var provider = CreateProvider("555-1234");

        // Act
        var result = InvokeToolMethod<MeterReadResult>(provider, "GetMeterReadType");

        // Assert
        Assert.Equal("A", result.ReadType);
        Assert.Contains("Actual", result.ReadTypeDescription);
        Assert.Null(result.Note); // No note for actual reads
    }

    [Fact]
    public void GetMeterReadType_ReturnsActual_ButPreviousWasEstimated_ForRobertJohnson()
    {
        // Arrange - Robert's previous bill was estimated
        var provider = CreateProvider("555-9999");

        // Act
        var result = InvokeToolMethod<MeterReadResult>(provider, "GetMeterReadType");

        // Assert - Current is actual (checking latest)
        Assert.Equal("A", result.ReadType);
    }

    #endregion

    #region GetBillingHistory Tests

    [Fact]
    public void GetBillingHistory_ReturnsAllBills()
    {
        // Arrange
        var provider = CreateProvider("555-1234");

        // Act
        var result = InvokeToolMethod<BillingHistoryResult>(provider, "GetBillingHistory");

        // Assert
        Assert.Equal(2, result.Bills.Count);
        Assert.Equal("2024-01", result.Bills[0].Period);
        Assert.Equal("2024-02", result.Bills[1].Period);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Invokes a private tool method on the context provider using reflection.
    /// This allows unit testing tool logic without going through the agent.
    /// </summary>
    private static T InvokeToolMethod<T>(UtilityDataContextProvider provider, string methodName)
    {
        var method = typeof(UtilityDataContextProvider)
            .GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Method {methodName} not found");

        var result = method.Invoke(provider, null)
            ?? throw new InvalidOperationException($"Method {methodName} returned null");

        return (T)result;
    }

    #endregion
}
```

#### Integration Tests - UtilityDataAgentTests.cs

These tests verify end-to-end agent behavior with LLM:

```csharp
// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.UtilityData;
using UtilityBillingChatbot.Infrastructure;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the Utility Data Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
public class UtilityDataAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private AuthAgent _authAgent = null!;
    private UtilityDataAgent _dataAgent = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _authAgent = _host.Services.GetRequiredService<AuthAgent>();
        _dataAgent = _host.Services.GetRequiredService<UtilityDataAgent>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    #region Authentication Helpers

    private async Task<AuthSession> AuthenticateCustomer(string phone, string ssn)
    {
        var r1 = await _authAgent.RunAsync("I need help with my account");
        var r2 = await _authAgent.RunAsync(phone, r1.Session);
        var r3 = await _authAgent.RunAsync(ssn, r2.Session);
        Assert.True(r3.IsAuthenticated, $"Failed to authenticate with phone {phone}");
        return r3.Session;
    }

    private Task<AuthSession> AuthenticateJohnSmith() => AuthenticateCustomer("555-1234", "1234");
    private Task<AuthSession> AuthenticateMariaGarcia() => AuthenticateCustomer("555-5678", "5678");
    private Task<AuthSession> AuthenticateRobertJohnson() => AuthenticateCustomer("555-9999", "9999");

    #endregion

    #region Auth Guard Tests

    [Fact]
    public async Task DataAgent_ThrowsOnUnauthenticated()
    {
        // Arrange - create session without completing auth
        var r1 = await _authAgent.RunAsync("I need help");
        var r2 = await _authAgent.RunAsync("555-1234", r1.Session);
        // Not authenticated yet (no SSN verification)

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _dataAgent.RunAsync("What's my balance?", authSession: r2.Session));
    }

    [Fact]
    public async Task DataAgent_ThrowsOnNullAuthSession()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _dataAgent.RunAsync("What's my balance?", authSession: null));
    }

    #endregion

    #region Balance Inquiry Tests (Q2)

    [Fact]
    public async Task DataAgent_FetchesBalance_WhenAuthenticated()
    {
        // Arrange - Q2: What is my balance?
        var authSession = await AuthenticateJohnSmith();

        // Act
        var response = await _dataAgent.RunAsync(
            "What is my current balance?",
            authSession: authSession);

        // Assert - John Smith has $187.43 balance
        Assert.Contains("187", response.Text);
        Assert.Equal("John Smith", response.CustomerName);
    }

    [Fact]
    public async Task DataAgent_ShowsZeroBalance_ForPaidAccount()
    {
        // Arrange - Maria has $0 balance (AutoPay customer)
        var authSession = await AuthenticateMariaGarcia();

        // Act
        var response = await _dataAgent.RunAsync(
            "What's my balance?",
            authSession: authSession);

        // Assert
        Assert.True(
            response.Text.Contains("0") ||
            response.Text.Contains("zero", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("paid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DataAgent_ShowsPastDueStatus()
    {
        // Arrange - Robert is past due
        var authSession = await AuthenticateRobertJohnson();

        // Act
        var response = await _dataAgent.RunAsync(
            "What's my account status?",
            authSession: authSession);

        // Assert
        Assert.True(
            response.Text.Contains("past due", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("overdue", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("423", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region High Bill Analysis Tests (Q1)

    [Fact]
    public async Task DataAgent_AnalyzesHighBill()
    {
        // Arrange - Q1: Why is my bill so high?
        var authSession = await AuthenticateJohnSmith();

        // Act
        var response = await _dataAgent.RunAsync(
            "Why is my bill so high this month?",
            authSession: authSession);

        // Assert - Should show usage comparison (John has 40% usage increase)
        Assert.Contains("usage", response.Text.ToLower());
        Assert.True(
            response.Text.Contains("kWh", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("increas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DataAgent_ExplainsSimilarUsage()
    {
        // Arrange - Maria has stable usage (~5% change)
        var authSession = await AuthenticateMariaGarcia();

        // Act
        var response = await _dataAgent.RunAsync(
            "Why is my bill different from last month?",
            authSession: authSession);

        // Assert - Should indicate usage is similar
        Assert.True(
            response.Text.Contains("similar", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("5%", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("slight", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Payment Status Tests (Q3)

    [Fact]
    public async Task DataAgent_ConfirmsPaymentReceived()
    {
        // Arrange - Q3: Did you receive my payment?
        var authSession = await AuthenticateJohnSmith();

        // Act
        var response = await _dataAgent.RunAsync(
            "Did you receive my payment?",
            authSession: authSession);

        // Assert - John's last payment was $142.50
        Assert.Contains("142", response.Text);
    }

    #endregion

    #region Due Date Tests (Q4)

    [Fact]
    public async Task DataAgent_ShowsDueDate()
    {
        // Arrange - Q4: When is my payment due?
        var authSession = await AuthenticateJohnSmith();

        // Act
        var response = await _dataAgent.RunAsync(
            "When is my payment due?",
            authSession: authSession);

        // Assert - Should mention due date
        Assert.True(
            response.Text.Contains("due", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("day", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region AutoPay Tests (Q10)

    [Fact]
    public async Task DataAgent_ChecksAutoPayStatus_NotEnrolled()
    {
        // Arrange - Q10: Am I on AutoPay? (John is NOT enrolled)
        var authSession = await AuthenticateJohnSmith();

        // Act
        var response = await _dataAgent.RunAsync(
            "Am I enrolled in AutoPay?",
            authSession: authSession);

        // Assert
        Assert.Contains("not", response.Text.ToLower());
    }

    [Fact]
    public async Task DataAgent_ChecksAutoPayStatus_Enrolled()
    {
        // Arrange - Maria IS enrolled in AutoPay
        var authSession = await AuthenticateMariaGarcia();

        // Act
        var response = await _dataAgent.RunAsync(
            "Am I on AutoPay?",
            authSession: authSession);

        // Assert
        Assert.True(
            response.Text.Contains("enrolled", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("yes", StringComparison.OrdinalIgnoreCase) ||
            (response.Text.Contains("AutoPay", StringComparison.OrdinalIgnoreCase) &&
             !response.Text.Contains("not", StringComparison.OrdinalIgnoreCase)));
    }

    #endregion

    #region Meter Read Tests (Q13)

    [Fact]
    public async Task DataAgent_ExplainsMeterReadType()
    {
        // Arrange - Q13: Actual or estimated read?
        var authSession = await AuthenticateJohnSmith();

        // Act
        var response = await _dataAgent.RunAsync(
            "Was my meter actually read this month?",
            authSession: authSession);

        // Assert - John's last bill was "A" (actual)
        Assert.True(
            response.Text.Contains("actual", StringComparison.OrdinalIgnoreCase) ||
            response.Text.Contains("read", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Session Management Tests

    [Fact]
    public async Task DataAgent_MaintainsSession_AcrossQueries()
    {
        // Arrange
        var authSession = await AuthenticateJohnSmith();

        // Act - first query
        var r1 = await _dataAgent.RunAsync(
            "What's my balance?",
            authSession: authSession);

        // Act - second query reuses session
        var r2 = await _dataAgent.RunAsync(
            "Am I on AutoPay?",
            session: r1.Session);

        // Assert
        Assert.Equal(r1.CustomerName, r2.CustomerName);
        Assert.Contains("not", r2.Text.ToLower()); // Not on AutoPay
    }

    [Fact]
    public async Task DataAgent_ReturnsCorrectAccountNumber()
    {
        // Arrange
        var authSession = await AuthenticateJohnSmith();

        // Act
        var response = await _dataAgent.RunAsync(
            "What's my account number?",
            authSession: authSession);

        // Assert
        Assert.Equal("1234567890", response.AccountNumber);
    }

    #endregion

    #region Multi-Customer Scenario Tests

    [Theory]
    [InlineData("555-1234", "1234", "John Smith", "187")]      // Current, balance
    [InlineData("555-5678", "5678", "Maria Garcia", "0")]       // AutoPay, zero balance
    [InlineData("555-9999", "9999", "Robert Johnson", "423")]   // Past due, high balance
    public async Task DataAgent_ReturnsCorrectBalance_ForEachCustomer(
        string phone, string ssn, string expectedName, string expectedBalanceContains)
    {
        // Arrange
        var authSession = await AuthenticateCustomer(phone, ssn);

        // Act
        var response = await _dataAgent.RunAsync(
            "What is my balance?",
            authSession: authSession);

        // Assert
        Assert.Equal(expectedName, response.CustomerName);
        Assert.Contains(expectedBalanceContains, response.Text);
    }

    #endregion
}
```

#### Running Tests

```bash
# Run all tests
dotnet test

# Run only unit tests (fast, no LLM required)
dotnet test --filter "FullyQualifiedName~ContextProviderTests"

# Run only integration tests
dotnet test --filter "FullyQualifiedName~UtilityDataAgentTests"

# Run specific test
dotnet test --filter "FullyQualifiedName~DataAgent_FetchesBalance_WhenAuthenticated"

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"
```

### Auth Integration Notes

The UtilityDataAgent is designed to work with completed auth sessions:

```csharp
// In ChatbotService or Orchestrator:
if (_authSession?.Provider.IsAuthenticated == true)
{
    // Route account questions to UtilityDataAgent
    var response = await _dataAgent.RunAsync(input, authSession: _authSession);
    Console.WriteLine(response.Text);
}
else
{
    // Route to AuthAgent first
    var response = await _authAgent.RunAsync(input, _authSession);
    _authSession = response.Session;
    Console.WriteLine(response.Text);
}
```

No separate `AuthGuard` class is needed - the auth state is already tracked in `AuthenticationContextProvider.IsAuthenticated`.

### Validation Checklist - Stage 4

**Implementation:**
- [ ] `UtilityDataAgent.cs` created with `RunAsync` and `CreateSessionAsync` methods
- [ ] `UtilityDataContextProvider.cs` created with all 8 tool methods
- [ ] `UtilityDataModels.cs` created with result records
- [ ] DI registration added to `ServiceCollectionExtensions.cs`
- [ ] Project builds without errors

**Unit Tests (no LLM required):**
- [ ] `GetAccountBalance` returns correct data for each customer
- [ ] `GetDueDate` calculates days correctly and detects past due
- [ ] `GetUsageAnalysis` detects significant vs similar usage changes
- [ ] `GetAutoPayStatus` returns correct enrollment status
- [ ] `GetMeterReadType` returns correct read type and note
- [ ] `GetBillingHistory` returns all bills

**Integration Tests (requires LLM):**
- [ ] Data agent throws exception for unauthenticated sessions
- [ ] Data agent throws exception for null auth session
- [ ] Data agent fetches account balance for authenticated users (Q2)
- [ ] Data agent shows zero balance for paid accounts
- [ ] Data agent shows past due status
- [ ] Data agent analyzes high bill with usage comparison (Q1)
- [ ] Data agent explains similar usage patterns
- [ ] Data agent confirms payment received with amount and date (Q3)
- [ ] Data agent shows due date information (Q4)
- [ ] Data agent checks AutoPay status - not enrolled (Q10)
- [ ] Data agent checks AutoPay status - enrolled (Q10)
- [ ] Data agent explains meter read type (Q13)
- [ ] Data agent maintains session across multiple queries
- [ ] Data agent returns correct account number
- [ ] Theory test passes for all 3 customers

**Test Commands:**
```bash
# All tests
dotnet test

# Unit tests only (fast)
dotnet test --filter "FullyQualifiedName~ContextProviderTests"

# Integration tests only
dotnet test --filter "FullyQualifiedName~UtilityDataAgentTests"
```

### Test Data Reference

The MockCISDatabase contains 3 customers for testing:

| Customer | Phone | Balance | AutoPay | Status | Last Bill Type |
|----------|-------|---------|---------|--------|----------------|
| John Smith | 555-1234 | $187.43 | No | Current | Actual |
| Maria Garcia | 555-5678 | $0.00 | Yes | Current | Actual |
| Robert Johnson | 555-9999 | $423.67 | No | PastDue | Actual (prev: Estimated) |

---
