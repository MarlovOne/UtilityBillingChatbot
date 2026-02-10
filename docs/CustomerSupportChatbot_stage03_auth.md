## Stage 3: In-Band Authentication Agent

### Objective
Build a conversational authentication agent that verifies user identity through security questions. This approach works for both chat and phone conversations (no OAuth redirects needed).

### Authentication Flow

```
User requests account data
         │
         ▼
┌─────────────────────────────────┐
│ "To access your account, I'll  │
│  need to verify your identity." │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ "What's the phone number or    │
│  email on your account?"        │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ Lookup user in mock database   │
│ Get verification questions      │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ "For security, what are the    │
│  last 4 digits of your SSN?"    │──► Wrong answer (max 3 tries)
│  OR "What's your date of birth?"│         │
└─────────────────────────────────┘         ▼
         │                          ┌───────────────────┐
         │ Correct                  │ Lock out / Handoff│
         ▼                          └───────────────────┘
┌─────────────────────────────────┐
│ AuthState = Authenticated       │
│ Resume original query           │
└─────────────────────────────────┘
```

### Implementation

```csharp
/// <summary>
/// Mock CIS (Customer Information System) database for utility billing prototyping
/// </summary>
public class MockCISDatabase
{
    private readonly Dictionary<string, UtilityCustomer> _customersByPhone = new()
    {
        ["555-1234"] = new UtilityCustomer
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
            RateCode = "R1",  // Residential Standard
            MeterNumber = "MTR-00123456",
            BillingHistory = [
                new BillRecord("2024-01", 892, 142.50m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-48))),
                new BillRecord("2024-02", 1247, 187.43m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-18)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 892, 28.8m),  // ~29 kWh/day
                new UsageRecord("2024-02", 1247, 44.5m)  // ~45 kWh/day (winter spike)
            ],
            DelinquencyStatus = "Current",
            EligibleForExtension = true
        },
        ["555-5678"] = new UtilityCustomer
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
            BillingHistory = [
                new BillRecord("2024-01", 654, 98.50m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-33))),
                new BillRecord("2024-02", 687, 102.30m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-3)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 654, 23.4m),
                new UsageRecord("2024-02", 687, 24.5m)
            ],
            DelinquencyStatus = "Current",
            EligibleForExtension = false  // Already current
        },
        ["555-9999"] = new UtilityCustomer
        {
            AccountNumber = "5555555555",
            Name = "Robert Johnson",
            Phone = "555-9999",
            Email = "rjohnson@example.com",
            ServiceAddress = "789 Elm St, Anytown, ST 12345",
            LastFourSSN = "9999",
            DateOfBirth = new DateOnly(1972, 11, 8),
            AccountBalance = 423.67m,
            DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),  // Past due!
            LastPaymentAmount = 150.00m,
            LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-45)),
            IsOnAutoPay = false,
            RateCode = "R1",
            MeterNumber = "MTR-00345678",
            BillingHistory = [
                new BillRecord("2024-01", 1456, 218.40m, "E", DateOnly.FromDateTime(DateTime.Now.AddDays(-60))),
                new BillRecord("2024-02", 1523, 228.45m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-30)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 1456, 52.0m),  // High usage
                new UsageRecord("2024-02", 1523, 54.4m)
            ],
            DelinquencyStatus = "PastDue",
            EligibleForExtension = true
        }
    };

    private readonly Dictionary<string, UtilityCustomer> _customersByEmail;
    private readonly Dictionary<string, UtilityCustomer> _customersByAccount;

    public MockCISDatabase()
    {
        _customersByEmail = _customersByPhone.Values.ToDictionary(u => u.Email.ToLower(), u => u);
        _customersByAccount = _customersByPhone.Values.ToDictionary(u => u.AccountNumber, u => u);
    }

    public UtilityCustomer? FindByIdentifier(string identifier)
    {
        identifier = identifier.Trim();

        // Try phone first
        if (_customersByPhone.TryGetValue(identifier, out var byPhone))
            return byPhone;

        // Try email
        if (_customersByEmail.TryGetValue(identifier.ToLower(), out var byEmail))
            return byEmail;

        // Try account number
        if (_customersByAccount.TryGetValue(identifier, out var byAccount))
            return byAccount;

        return null;
    }
}

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
    public required string DelinquencyStatus { get; init; }  // Current, PastDue, Collections
    public required bool EligibleForExtension { get; init; }
}

/// <summary>Bill record from CIS</summary>
public record BillRecord(
    string BillingPeriod,
    int KwhUsage,
    decimal AmountDue,
    string ReadType,  // A=Actual, E=Estimated
    DateOnly BillDate);

/// <summary>Usage record from MDM</summary>
public record UsageRecord(
    string Period,
    int TotalKwh,
    decimal AvgDailyKwh);
```

```csharp
/// <summary>
/// Authentication agent that verifies utility customer identity through conversation.
/// Implements IInBandAuthAgentFactory for DI and testability.
/// </summary>
public class InBandAuthAgentFactory : IInBandAuthAgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;

    public InBandAuthAgentFactory(IChatClient chatClient, MockCISDatabase cisDatabase)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
    }

    /// <summary>
    /// Creates an auth agent with tools that can modify the provided UserSessionContext.
    /// </summary>
    public AIAgent CreateInBandAuthAgent(UserSessionContext context)
    {
        // Tools for the auth flow
        var tools = new AIFunction[]
        {
            AIFunctionFactory.Create(
                (string identifier) => LookupCustomer(identifier, context),
                name: "LookupCustomerByIdentifier",
                description: "Look up a customer by phone number, email, or account number"),

            AIFunctionFactory.Create(
                (string answer) => VerifyLastFourSSN(answer, context),
                name: "VerifyLastFourSSN",
                description: "Verify the last 4 digits of customer's SSN"),

            AIFunctionFactory.Create(
                (string answer) => VerifyDateOfBirth(answer, context),
                name: "VerifyDateOfBirth",
                description: "Verify customer's date of birth (format: MM/DD/YYYY)"),

            AIFunctionFactory.Create(
                () => CompleteAuthentication(context),
                name: "CompleteAuthentication",
                description: "Mark customer as authenticated after successful verification")
        };

        string instructions = $"""
            You are a utility company security verification assistant. Your job is to verify
            the customer's identity before they can access their account information.

            Current auth state: {context.AuthState}
            Failed attempts: {context.FailedAttempts}/3
            Verified factors: {string.Join(", ", context.VerifiedFactors)}

            VERIFICATION FLOW:
            1. If Anonymous: Ask for phone number, email, or account number on their utility account
            2. If IdentityProvided: Use LookupCustomerByIdentifier to find them in CIS
            3. If Verifying: Ask ONE security question and verify the answer
               - Ask for last 4 digits of SSN OR date of birth
               - Use the appropriate verify tool
            4. If verified (1 correct answer): Call CompleteAuthentication

            RULES:
            - Be polite and professional
            - Never reveal what the correct answer should be
            - If they fail 3 times, say you need to transfer them to a customer service representative
            - After successful auth, tell them they're verified and you'll help with their billing question
            - Keep responses concise

            IMPORTANT: Only ask ONE question at a time. Wait for their response.
            """;

        return _chatClient.AsAIAgent(instructions: instructions, tools: tools);
    }

    private LookupResult LookupCustomer(string identifier, UserSessionContext context)
    {
        var customer = _cisDatabase.FindByIdentifier(identifier);

        if (customer == null)
        {
            return new LookupResult(false, "No account found with that phone, email, or account number.");
        }

        context.IdentifyingInfo = identifier;
        context.UserId = customer.AccountNumber;
        context.UserName = customer.Name;
        context.AuthState = AuthenticationState.Verifying;

        return new LookupResult(true, $"Found account for {customer.Name} at {customer.ServiceAddress}. Proceed with verification.");
    }

    private VerificationResult VerifyLastFourSSN(string answer, UserSessionContext context)
    {
        if (context.UserId == null)
            return new VerificationResult(false, "No customer to verify");

        var customer = _cisDatabase.FindByIdentifier(context.IdentifyingInfo!);
        if (customer == null)
            return new VerificationResult(false, "Customer not found");

        var cleaned = new string(answer.Where(char.IsDigit).ToArray());

        if (cleaned == customer.LastFourSSN)
        {
            context.VerifiedFactors.Add("SSN");
            return new VerificationResult(true, "SSN verified successfully");
        }

        context.FailedAttempts++;
        if (context.FailedAttempts >= 3)
        {
            context.AuthState = AuthenticationState.LockedOut;
            return new VerificationResult(false, "Too many failed attempts. Account locked.");
        }

        return new VerificationResult(false,
            $"Incorrect. {3 - context.FailedAttempts} attempts remaining.");
    }

    private VerificationResult VerifyDateOfBirth(string answer, UserSessionContext context)
    {
        if (context.UserId == null)
            return new VerificationResult(false, "No customer to verify");

        var customer = _cisDatabase.FindByIdentifier(context.IdentifyingInfo!);
        if (customer == null)
            return new VerificationResult(false, "Customer not found");

        // Try to parse various date formats
        if (DateOnly.TryParse(answer, out var dob) && dob == customer.DateOfBirth)
        {
            context.VerifiedFactors.Add("DOB");
            return new VerificationResult(true, "Date of birth verified successfully");
        }

        context.FailedAttempts++;
        if (context.FailedAttempts >= 3)
        {
            context.AuthState = AuthenticationState.LockedOut;
            return new VerificationResult(false, "Too many failed attempts. Account locked.");
        }

        return new VerificationResult(false,
            $"Incorrect. {3 - context.FailedAttempts} attempts remaining.");
    }

    private AuthResult CompleteAuthentication(UserSessionContext context)
    {
        if (context.VerifiedFactors.Count == 0)
            return new AuthResult(false, "No verification completed");

        context.AuthState = AuthenticationState.Authenticated;
        context.AuthenticatedAt = DateTimeOffset.UtcNow;
        context.SessionExpiry = DateTimeOffset.UtcNow.AddMinutes(30);

        return new AuthResult(true,
            $"Authentication complete. Customer {context.UserName} is now verified.");
    }
}

public record LookupResult(bool Found, string Message);
public record VerificationResult(bool Success, string Message);
public record AuthResult(bool Success, string Message);
```

### Testing Stage 3

```csharp
public class InBandAuthAgentTests
{
    private readonly MockCISDatabase _db = new();
    private readonly InBandAuthAgentFactory _factory;

    public InBandAuthAgentTests()
    {
        var chatClient = CreateMockChatClient();
        _factory = new InBandAuthAgentFactory(chatClient, _db);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomerByPhone()
    {
        // Arrange
        var context = new UserSessionContext();
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act - Customer asks about their bill
        await agent.RunAsync("Why is my bill so high?", session);
        var response = await agent.RunAsync("555-1234", session);

        // Assert
        Assert.Equal(AuthenticationState.Verifying, context.AuthState);
        Assert.Equal("1234567890", context.UserId);  // Account number
        Assert.Equal("John Smith", context.UserName);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomerByAccountNumber()
    {
        // Arrange
        var context = new UserSessionContext();
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act - Customer provides account number
        await agent.RunAsync("What's my balance?", session);
        var response = await agent.RunAsync("9876543210", session);

        // Assert
        Assert.Equal("Maria Garcia", context.UserName);
    }

    [Fact]
    public async Task AuthAgent_VerifiesSSN()
    {
        // Arrange
        var context = new UserSessionContext
        {
            AuthState = AuthenticationState.Verifying,
            UserId = "1234567890",
            IdentifyingInfo = "555-1234"
        };
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("1234", session);

        // Assert
        Assert.Contains("SSN", context.VerifiedFactors);
    }

    [Fact]
    public async Task AuthAgent_LocksAfterThreeFailures()
    {
        // Arrange
        var context = new UserSessionContext
        {
            AuthState = AuthenticationState.Verifying,
            UserId = "1234567890",
            IdentifyingInfo = "555-1234"
        };
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act - Three wrong answers
        await agent.RunAsync("0000", session);
        await agent.RunAsync("1111", session);
        await agent.RunAsync("2222", session);

        // Assert
        Assert.Equal(AuthenticationState.LockedOut, context.AuthState);
    }

    [Fact]
    public async Task AuthAgent_CompletesFullAuthFlow()
    {
        // Arrange
        var context = new UserSessionContext();
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act - Full conversation for utility billing
        await agent.RunAsync("Did you receive my payment?", session);  // Q3
        await agent.RunAsync("555-1234", session);  // Phone
        await agent.RunAsync("1234", session);       // Last 4 SSN

        // Assert
        Assert.Equal(AuthenticationState.Authenticated, context.AuthState);
        Assert.NotNull(context.AuthenticatedAt);
    }
}
```

### Validation Checklist - Stage 3
- [ ] Auth agent asks for identifier (phone/email/account number) when anonymous
- [ ] Auth agent finds customers in mock CIS database
- [ ] Auth agent asks verification questions one at a time
- [ ] Auth agent accepts correct answers and updates verified factors
- [ ] Auth agent tracks failed attempts
- [ ] Auth agent locks out after 3 failed attempts
- [ ] Auth agent completes authentication with 1 verified factor
- [ ] Auth state persists correctly across conversation turns

---
