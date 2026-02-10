## Stage 4: Utility Data Agent

### Objective
Build an agent with tool access to the mock CIS database that answers account-specific utility billing questions for authenticated customers.

### Implementation

```csharp
/// <summary>
/// Data agent that fetches utility account data from the mock CIS database.
/// Requires customer to be authenticated first (via Stage 3).
/// Answers questions like: balance, payments, bill details, usage, AutoPay status
/// </summary>
public class UtilityDataAgentFactory : IUtilityDataAgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;

    public UtilityDataAgentFactory(IChatClient chatClient, MockCISDatabase cisDatabase)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
    }

    public AIAgent CreateUtilityDataAgent(UserSessionContext userContext)
    {
        // Validate auth before creating agent
        if (userContext.AuthState != AuthenticationState.Authenticated)
            throw new InvalidOperationException("Customer must be authenticated to create data agent");

        // Get customer data from mock CIS
        var customer = _cisDatabase.FindByIdentifier(userContext.IdentifyingInfo!);
        if (customer == null)
            throw new InvalidOperationException("Authenticated customer not found in CIS");

        // Define tools that access the mock CIS/MDM data
        var tools = new AIFunction[]
        {
            // Q2: "What is my current account balance?"
            AIFunctionFactory.Create(
                () => GetAccountBalance(customer),
                name: "GetAccountBalance",
                description: "Gets current balance, due date, and last payment info"),

            // Q3: "Did you receive my payment?"
            AIFunctionFactory.Create(
                () => GetPaymentStatus(customer),
                name: "GetPaymentStatus",
                description: "Gets last payment date, amount, and current account status"),

            // Q4: "When is my payment due?"
            AIFunctionFactory.Create(
                () => GetDueDate(customer),
                name: "GetDueDate",
                description: "Gets payment due date and billing cycle info"),

            // Q1: "Why is my bill so high?"
            AIFunctionFactory.Create(
                () => GetUsageAnalysis(customer),
                name: "GetUsageAnalysis",
                description: "Compares current vs previous usage to explain bill changes"),

            // Q10: "Am I on AutoPay?"
            AIFunctionFactory.Create(
                () => GetAutoPayStatus(customer),
                name: "GetAutoPayStatus",
                description: "Gets AutoPay enrollment status"),

            // Q12: "What is this charge on my bill?"
            AIFunctionFactory.Create(
                () => GetBillDetails(customer),
                name: "GetBillDetails",
                description: "Gets detailed breakdown of current bill charges"),

            // Q13: "Is my bill based on actual or estimated read?"
            AIFunctionFactory.Create(
                () => GetMeterReadType(customer),
                name: "GetMeterReadType",
                description: "Gets whether last bill was actual (A) or estimated (E) read"),

            // Q19: "Can I get a copy of my bill?"
            AIFunctionFactory.Create(
                () => GetBillingHistory(customer),
                name: "GetBillingHistory",
                description: "Gets recent billing history with amounts and dates")
        };

        string instructions = $"""
            You are a utility billing account assistant. You help customers access their
            account information using the available tools.

            Customer: {userContext.UserName}
            Account: {customer.AccountNumber}
            Service Address: {customer.ServiceAddress}
            Authenticated at: {userContext.AuthenticatedAt:g}

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

        return _chatClient.AsAIAgent(instructions: instructions, tools: tools);
    }

    private BalanceResult GetAccountBalance(UtilityCustomer customer)
    {
        return new BalanceResult(
            CurrentBalance: customer.AccountBalance,
            DueDate: customer.DueDate,
            LastPaymentAmount: customer.LastPaymentAmount,
            LastPaymentDate: customer.LastPaymentDate,
            DelinquencyStatus: customer.DelinquencyStatus);
    }

    private PaymentStatusResult GetPaymentStatus(UtilityCustomer customer)
    {
        bool paymentReceived = customer.LastPaymentDate > DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
        return new PaymentStatusResult(
            LastPaymentReceived: paymentReceived,
            PaymentAmount: customer.LastPaymentAmount,
            PaymentDate: customer.LastPaymentDate,
            CurrentBalance: customer.AccountBalance,
            AccountStatus: customer.DelinquencyStatus);
    }

    private DueDateResult GetDueDate(UtilityCustomer customer)
    {
        var daysUntilDue = (customer.DueDate.ToDateTime(TimeOnly.MinValue) - DateTime.Now).Days;
        return new DueDateResult(
            DueDate: customer.DueDate,
            AmountDue: customer.AccountBalance,
            DaysUntilDue: daysUntilDue,
            IsPastDue: daysUntilDue < 0);
    }

    private UsageAnalysisResult GetUsageAnalysis(UtilityCustomer customer)
    {
        var currentBill = customer.BillingHistory.LastOrDefault();
        var previousBill = customer.BillingHistory.Count > 1
            ? customer.BillingHistory[^2]
            : null;

        var usageChange = previousBill != null
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

    private AutoPayResult GetAutoPayStatus(UtilityCustomer customer)
    {
        return new AutoPayResult(
            IsEnrolled: customer.IsOnAutoPay,
            Message: customer.IsOnAutoPay
                ? "You are enrolled in AutoPay. Payments are automatically deducted on your due date."
                : "You are not currently enrolled in AutoPay.");
    }

    private BillDetailsResult GetBillDetails(UtilityCustomer customer)
    {
        var latestBill = customer.BillingHistory.LastOrDefault();
        return new BillDetailsResult(
            BillingPeriod: latestBill?.BillingPeriod ?? "N/A",
            TotalKwh: latestBill?.KwhUsage ?? 0,
            TotalAmount: latestBill?.AmountDue ?? 0,
            RateCode: customer.RateCode,
            ReadType: latestBill?.ReadType ?? "A",
            ServiceAddress: customer.ServiceAddress);
    }

    private MeterReadResult GetMeterReadType(UtilityCustomer customer)
    {
        var latestBill = customer.BillingHistory.LastOrDefault();
        var readType = latestBill?.ReadType ?? "A";
        return new MeterReadResult(
            ReadType: readType,
            ReadTypeDescription: readType == "A" ? "Actual meter reading" : "Estimated reading",
            MeterNumber: customer.MeterNumber,
            Note: readType == "E"
                ? "Your meter could not be read this month. The next bill will include a correction based on actual reading."
                : null);
    }

    private BillingHistoryResult GetBillingHistory(UtilityCustomer customer)
    {
        return new BillingHistoryResult(
            Bills: customer.BillingHistory.Select(b => new BillSummary(
                Period: b.BillingPeriod,
                Amount: b.AmountDue,
                Kwh: b.KwhUsage,
                BillDate: b.BillDate)).ToList());
    }
}

// Result records for utility billing data
public record BalanceResult(decimal CurrentBalance, DateOnly DueDate, decimal LastPaymentAmount, DateOnly LastPaymentDate, string DelinquencyStatus);
public record PaymentStatusResult(bool LastPaymentReceived, decimal PaymentAmount, DateOnly PaymentDate, decimal CurrentBalance, string AccountStatus);
public record DueDateResult(DateOnly DueDate, decimal AmountDue, int DaysUntilDue, bool IsPastDue);
public record UsageAnalysisResult(int CurrentPeriodKwh, decimal CurrentPeriodAmount, int PreviousPeriodKwh, decimal PreviousPeriodAmount, decimal UsageChangePercent, string Explanation);
public record AutoPayResult(bool IsEnrolled, string Message);
public record BillDetailsResult(string BillingPeriod, int TotalKwh, decimal TotalAmount, string RateCode, string ReadType, string ServiceAddress);
public record MeterReadResult(string ReadType, string ReadTypeDescription, string MeterNumber, string? Note);
public record BillingHistoryResult(List<BillSummary> Bills);
public record BillSummary(string Period, decimal Amount, int Kwh, DateOnly BillDate);
```

### Auth Guard Pattern

```csharp
/// <summary>
/// Guards data agent creation - ensures auth is completed first.
/// Use this in the orchestrator to check before routing to data agent.
/// </summary>
public static class AuthGuard
{
    public static bool IsAuthenticated(UserSessionContext context)
    {
        if (context.AuthState != AuthenticationState.Authenticated)
            return false;

        // Check session expiry
        if (context.SessionExpiry.HasValue && context.SessionExpiry.Value < DateTimeOffset.UtcNow)
        {
            context.AuthState = AuthenticationState.Expired;
            return false;
        }

        return true;
    }

    public static string GetAuthRequiredMessage(UserSessionContext context)
    {
        return context.AuthState switch
        {
            AuthenticationState.Expired =>
                "Your session has expired. Let me verify your identity again.",
            AuthenticationState.LockedOut =>
                "Your account is temporarily locked due to too many failed attempts. " +
                "Please contact a representative for assistance.",
            _ =>
                "To access your account information, I'll need to verify your identity first."
        };
    }
```

### Testing Stage 4

```csharp
public class UtilityDataAgentTests
{
    private readonly MockCISDatabase _db = new();
    private readonly UtilityDataAgentFactory _factory;

    public UtilityDataAgentTests()
    {
        var chatClient = CreateMockChatClient();
        _factory = new UtilityDataAgentFactory(chatClient, _db);
    }

    [Fact]
    public async Task DataAgent_FetchesBalance_WhenAuthenticated()
    {
        // Arrange - Q2: What is my balance / how much do I owe?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("What is my current balance?", session);

        // Assert
        Assert.Contains("247.83", response.Text); // Maria Garcia's balance
        Assert.Contains("2026-02-15", response.Text); // Due date
    }

    [Fact]
    public async Task DataAgent_ThrowsOnUnauthenticated()
    {
        // Arrange
        var context = new UserSessionContext { AuthState = AuthenticationState.Anonymous };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _factory.CreateUtilityDataAgent(context));
    }

    [Fact]
    public async Task DataAgent_AnalyzesHighBill()
    {
        // Arrange - Q1: Why is my bill so high?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Why is my bill so high this month?", session);

        // Assert - Should show usage comparison
        Assert.Contains("usage", response.Text.ToLower());
        Assert.Contains("kWh", response.Text);
    }

    [Fact]
    public async Task DataAgent_ConfirmsPaymentReceived()
    {
        // Arrange - Q3: Did you receive my payment?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Did you receive my payment?", session);

        // Assert
        Assert.Contains("185.42", response.Text); // Last payment amount
        Assert.Contains("2026-01-28", response.Text); // Last payment date
    }

    [Fact]
    public async Task DataAgent_ChecksAutoPayStatus()
    {
        // Arrange - Q10: Am I signed up for autopay?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Am I enrolled in AutoPay?", session);

        // Assert
        Assert.Contains("not currently enrolled", response.Text.ToLower());
    }

    [Fact]
    public async Task DataAgent_ExplainsMeterReadType()
    {
        // Arrange - Q14: Was my meter actually read?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Was my meter actually read this month?", session);

        // Assert
        Assert.Contains("AMI", response.Text); // Smart meter type
    }

    private UserSessionContext CreateAuthenticatedContext() => new()
    {
        AccountNumber = "ACC-2024-0042",
        CustomerName = "Maria Garcia",
        AuthState = AuthenticationState.Authenticated,
        IdentifyingInfo = "555-9876",
        AuthenticatedAt = DateTimeOffset.UtcNow,
        SessionExpiry = DateTimeOffset.UtcNow.AddMinutes(30)
    };
}
```

### Validation Checklist - Stage 4
- [ ] Data agent fetches account balance for authenticated users (Q2)
- [ ] Data agent throws exception for unauthenticated users
- [ ] Data agent analyzes high bill with usage comparison (Q1)
- [ ] Data agent confirms payment received with amount and date (Q3)
- [ ] Data agent checks AutoPay enrollment status (Q10)
- [ ] Data agent explains meter read type (Q14)
- [ ] AuthGuard correctly checks authentication state
- [ ] AuthGuard detects expired sessions

---
