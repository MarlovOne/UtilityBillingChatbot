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

### Data Models

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

public enum AuthenticationState
{
    Anonymous,
    Verifying,
    Authenticated,
    LockedOut
}
```

### Structured Response Models

Use typed response models for clear agent decision-making:

```csharp
public sealed class AuthVerificationResult
{
    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("remaining_attempts")]
    public int RemainingAttempts { get; set; }

    [JsonPropertyName("next_action")]
    public string NextAction { get; set; } = "";  // "ask_ssn", "ask_dob", "complete", "locked_out"

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class LookupResult
{
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("next_action")]
    public string NextAction { get; set; } = "";  // "verify", "not_found"

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
```

### Implementation with AIContextProvider

Following the `Agent_Step20_AdditionalAIContext` pattern, authentication state and tools are managed by an `AIContextProvider`. Tools are instance methods on the provider, giving them direct access to mutable state.

```csharp
/// <summary>
/// Manages authentication state and provides verification tools.
/// State persists across agent runs within a session and can be serialized for persistence.
/// </summary>
internal sealed class AuthenticationContextProvider : AIContextProvider
{
    private readonly MockCISDatabase _cisDatabase;
    private const int MaxAttempts = 3;

    // Mutable state
    private AuthenticationState _authState = AuthenticationState.Anonymous;
    private int _failedAttempts = 0;
    private readonly List<string> _verifiedFactors = new();
    private string? _identifyingInfo;
    private string? _customerId;
    private string? _customerName;
    private DateTimeOffset? _authenticatedAt;

    /// <summary>
    /// Constructor for new sessions.
    /// </summary>
    public AuthenticationContextProvider(MockCISDatabase cisDatabase)
    {
        _cisDatabase = cisDatabase;
    }

    /// <summary>
    /// Constructor for session restore from serialized state.
    /// </summary>
    public AuthenticationContextProvider(
        MockCISDatabase cisDatabase,
        JsonElement jsonElement,
        JsonSerializerOptions? options = null) : this(cisDatabase)
    {
        if (jsonElement.ValueKind == JsonValueKind.Object)
        {
            if (jsonElement.TryGetProperty("authState", out var state))
                _authState = Enum.Parse<AuthenticationState>(state.GetString() ?? "Anonymous");

            if (jsonElement.TryGetProperty("failedAttempts", out var attempts))
                _failedAttempts = attempts.GetInt32();

            if (jsonElement.TryGetProperty("verifiedFactors", out var factors))
            {
                foreach (var factor in factors.EnumerateArray())
                    _verifiedFactors.Add(factor.GetString() ?? "");
            }

            if (jsonElement.TryGetProperty("identifyingInfo", out var info))
                _identifyingInfo = info.GetString();

            if (jsonElement.TryGetProperty("customerId", out var id))
                _customerId = id.GetString();

            if (jsonElement.TryGetProperty("customerName", out var name))
                _customerName = name.GetString();

            if (jsonElement.TryGetProperty("authenticatedAt", out var authAt) &&
                DateTimeOffset.TryParse(authAt.GetString(), out var parsed))
                _authenticatedAt = parsed;
        }
    }

    // Public accessors for external code (e.g., routing decisions)
    public AuthenticationState AuthState => _authState;
    public string? CustomerId => _customerId;
    public string? CustomerName => _customerName;
    public bool IsAuthenticated => _authState == AuthenticationState.Authenticated;

    /// <summary>
    /// Called before each agent invocation. Injects current state and available tools.
    /// </summary>
    public override ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken ct)
    {
        var stateMessage = BuildStateMessage();
        var tools = GetToolsForCurrentState();
        var instructions = BuildInstructions();

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = instructions,
            Messages = [new ChatMessage(ChatRole.System, stateMessage)],
            Tools = tools
        });
    }

    private string BuildStateMessage()
    {
        return $"""
            Current authentication state:
            - Status: {_authState}
            - Failed attempts: {_failedAttempts}/{MaxAttempts}
            - Verified factors: {(_verifiedFactors.Count > 0 ? string.Join(", ", _verifiedFactors) : "none")}
            {(_customerId != null ? $"- Customer: {_customerName} ({_customerId})" : "")}
            """;
    }

    private string BuildInstructions()
    {
        return """
            You are a utility company security verification assistant. Your job is to verify
            the customer's identity before they can access their account information.

            VERIFICATION FLOW:
            1. If Anonymous: Ask for phone number, email, or account number on their utility account
            2. After lookup succeeds: Ask ONE security question (last 4 SSN or date of birth)
            3. After verification succeeds: Confirm they're verified and ready to help

            RULES:
            - Be polite and professional
            - Never reveal what the correct answer should be
            - If locked out, say you need to transfer them to a customer service representative
            - Keep responses concise
            - Only ask ONE question at a time
            """;
    }

    private List<AITool> GetToolsForCurrentState()
    {
        return _authState switch
        {
            AuthenticationState.Anonymous =>
            [
                AIFunctionFactory.Create(
                    this.LookupCustomerByIdentifier,
                    description: "Look up a customer by phone number, email, or account number")
            ],

            AuthenticationState.Verifying =>
            [
                AIFunctionFactory.Create(
                    this.VerifyLastFourSSN,
                    description: "Verify the last 4 digits of customer's SSN"),
                AIFunctionFactory.Create(
                    this.VerifyDateOfBirth,
                    description: "Verify customer's date of birth (format: MM/DD/YYYY)"),
                AIFunctionFactory.Create(
                    this.CompleteAuthentication,
                    description: "Mark customer as authenticated after successful verification")
            ],

            AuthenticationState.Authenticated => [],  // No auth tools needed
            AuthenticationState.LockedOut => [],      // No tools available
            _ => []
        };
    }

    #region Tool Methods (instance methods with direct state access)

    [Description("Look up a customer by phone number, email, or account number")]
    private LookupResult LookupCustomerByIdentifier(string identifier)
    {
        var customer = _cisDatabase.FindByIdentifier(identifier);

        if (customer == null)
        {
            return new LookupResult
            {
                Found = false,
                NextAction = "not_found",
                Message = "No account found with that phone, email, or account number."
            };
        }

        // Update state
        _identifyingInfo = identifier;
        _customerId = customer.AccountNumber;
        _customerName = customer.Name;
        _authState = AuthenticationState.Verifying;

        return new LookupResult
        {
            Found = true,
            CustomerName = customer.Name,
            NextAction = "verify",
            Message = $"Found account for {customer.Name} at {customer.ServiceAddress}. Proceed with verification."
        };
    }

    [Description("Verify the last 4 digits of customer's SSN")]
    private AuthVerificationResult VerifyLastFourSSN(string answer)
    {
        var customer = _cisDatabase.FindByIdentifier(_identifyingInfo!);
        if (customer == null)
        {
            return new AuthVerificationResult
            {
                Verified = false,
                RemainingAttempts = MaxAttempts - _failedAttempts,
                NextAction = "error",
                Message = "Customer not found"
            };
        }

        var cleaned = new string(answer.Where(char.IsDigit).ToArray());
        return VerifyAnswer(cleaned == customer.LastFourSSN, "SSN");
    }

    [Description("Verify customer's date of birth (format: MM/DD/YYYY)")]
    private AuthVerificationResult VerifyDateOfBirth(string answer)
    {
        var customer = _cisDatabase.FindByIdentifier(_identifyingInfo!);
        if (customer == null)
        {
            return new AuthVerificationResult
            {
                Verified = false,
                RemainingAttempts = MaxAttempts - _failedAttempts,
                NextAction = "error",
                Message = "Customer not found"
            };
        }

        var isCorrect = DateOnly.TryParse(answer, out var dob) && dob == customer.DateOfBirth;
        return VerifyAnswer(isCorrect, "DOB");
    }

    private AuthVerificationResult VerifyAnswer(bool isCorrect, string factorName)
    {
        // Check if already locked out
        if (_failedAttempts >= MaxAttempts)
        {
            _authState = AuthenticationState.LockedOut;
            return new AuthVerificationResult
            {
                Verified = false,
                RemainingAttempts = 0,
                NextAction = "locked_out",
                Message = "Account locked due to too many failed attempts. Please contact customer service."
            };
        }

        if (isCorrect)
        {
            _verifiedFactors.Add(factorName);
            return new AuthVerificationResult
            {
                Verified = true,
                RemainingAttempts = MaxAttempts - _failedAttempts,
                NextAction = "complete",
                Message = $"{factorName} verified successfully."
            };
        }

        // Failed attempt
        _failedAttempts++;
        var remaining = MaxAttempts - _failedAttempts;

        if (remaining <= 0)
        {
            _authState = AuthenticationState.LockedOut;
            return new AuthVerificationResult
            {
                Verified = false,
                RemainingAttempts = 0,
                NextAction = "locked_out",
                Message = "Account locked due to too many failed attempts. Please contact customer service."
            };
        }

        return new AuthVerificationResult
        {
            Verified = false,
            RemainingAttempts = remaining,
            NextAction = "retry",
            Message = $"Incorrect. {remaining} attempt{(remaining == 1 ? "" : "s")} remaining."
        };
    }

    [Description("Mark customer as authenticated after successful verification")]
    private AuthVerificationResult CompleteAuthentication()
    {
        if (_verifiedFactors.Count == 0)
        {
            return new AuthVerificationResult
            {
                Verified = false,
                RemainingAttempts = MaxAttempts - _failedAttempts,
                NextAction = "verify",
                Message = "No verification completed yet."
            };
        }

        _authState = AuthenticationState.Authenticated;
        _authenticatedAt = DateTimeOffset.UtcNow;

        return new AuthVerificationResult
        {
            Verified = true,
            RemainingAttempts = MaxAttempts - _failedAttempts,
            NextAction = "complete",
            Message = $"Authentication complete. Customer {_customerName} is now verified."
        };
    }

    #endregion

    /// <summary>
    /// Serialize state for session persistence.
    /// </summary>
    public override JsonElement Serialize(JsonSerializerOptions? options = null)
    {
        return JsonSerializer.SerializeToElement(new
        {
            authState = _authState.ToString(),
            failedAttempts = _failedAttempts,
            verifiedFactors = _verifiedFactors,
            identifyingInfo = _identifyingInfo,
            customerId = _customerId,
            customerName = _customerName,
            authenticatedAt = _authenticatedAt?.ToString("O")
        }, options);
    }
}
```

### Creating the Auth Agent

```csharp
public static class AuthAgentExtensions
{
    public static IServiceCollection AddAuthAgent(this IServiceCollection services)
    {
        services.AddSingleton<MockCISDatabase>();
        return services;
    }
}

/// <summary>
/// Factory for creating auth agents with session-scoped state.
/// </summary>
public class AuthAgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;

    public AuthAgentFactory(IChatClient chatClient, MockCISDatabase cisDatabase)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
    }

    /// <summary>
    /// Creates an auth agent for a new session.
    /// </summary>
    public (AIAgent Agent, AuthenticationContextProvider AuthProvider) CreateAuthAgent()
    {
        var authProvider = new AuthenticationContextProvider(_cisDatabase);

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            AIContextProviderFactory = (ctx, ct) =>
            {
                // If restoring session, deserialize provider state
                if (ctx.SerializedState.ValueKind == JsonValueKind.Object)
                {
                    return new ValueTask<AIContextProvider>(
                        new AuthenticationContextProvider(_cisDatabase, ctx.SerializedState, ctx.JsonSerializerOptions));
                }
                return new ValueTask<AIContextProvider>(authProvider);
            }
        });

        return (agent, authProvider);
    }
}
```

### Using the Auth Agent

```csharp
// Create agent and session
var factory = new AuthAgentFactory(chatClient, cisDatabase);
var (agent, authProvider) = factory.CreateAuthAgent();
var session = await agent.CreateSessionAsync();

// Run conversation
await agent.RunAsync("I want to check my bill", session);
await agent.RunAsync("555-1234", session);  // Phone lookup
await agent.RunAsync("1234", session);       // SSN verification

// Check auth state
if (authProvider.IsAuthenticated)
{
    Console.WriteLine($"Authenticated as {authProvider.CustomerName}");
}

// Serialize session for persistence
var serialized = session.Serialize();
await sessionStore.SaveAsync(sessionId, serialized);

// Later: restore session
var restored = await sessionStore.LoadAsync(sessionId);
var restoredSession = await agent.DeserializeSessionAsync(restored);
var restoredProvider = restoredSession.GetService<AuthenticationContextProvider>();
```

### Configuring Verification Questions

The provider can be extended to support different verification methods per customer:

```csharp
private List<AITool> GetToolsForCurrentState()
{
    if (_authState != AuthenticationState.Verifying)
        return GetBaseToolsForState();

    // Get available verification methods for this customer
    var customer = _cisDatabase.FindByIdentifier(_identifyingInfo!);
    var tools = new List<AITool>();

    if (!string.IsNullOrEmpty(customer?.LastFourSSN))
    {
        tools.Add(AIFunctionFactory.Create(
            this.VerifyLastFourSSN,
            description: "Verify the last 4 digits of customer's SSN"));
    }

    if (customer?.DateOfBirth != default)
    {
        tools.Add(AIFunctionFactory.Create(
            this.VerifyDateOfBirth,
            description: "Verify customer's date of birth"));
    }

    if (!string.IsNullOrEmpty(customer?.ServiceAddress))
    {
        tools.Add(AIFunctionFactory.Create(
            this.VerifyServiceAddress,
            description: "Verify customer's service address"));
    }

    // Always include complete if we have verified factors
    if (_verifiedFactors.Count > 0)
    {
        tools.Add(AIFunctionFactory.Create(
            this.CompleteAuthentication,
            description: "Complete authentication"));
    }

    return tools;
}
```

### Testing Stage 3

```csharp
public class AuthenticationContextProviderTests
{
    private readonly MockCISDatabase _db = new();

    [Fact]
    public void LookupCustomer_FindsByPhone_TransitionsToVerifying()
    {
        // Arrange
        var provider = new AuthenticationContextProvider(_db);

        // Act - Use reflection or make method internal for testing
        var result = InvokeLookup(provider, "555-1234");

        // Assert
        Assert.True(result.Found);
        Assert.Equal("verify", result.NextAction);
        Assert.Equal(AuthenticationState.Verifying, provider.AuthState);
        Assert.Equal("1234567890", provider.CustomerId);
        Assert.Equal("John Smith", provider.CustomerName);
    }

    [Fact]
    public void VerifySSN_CorrectAnswer_AddsVerifiedFactor()
    {
        // Arrange
        var provider = new AuthenticationContextProvider(_db);
        InvokeLookup(provider, "555-1234");

        // Act
        var result = InvokeVerifySSN(provider, "1234");

        // Assert
        Assert.True(result.Verified);
        Assert.Equal("complete", result.NextAction);
    }

    [Fact]
    public void VerifySSN_ThreeWrongAnswers_LocksOut()
    {
        // Arrange
        var provider = new AuthenticationContextProvider(_db);
        InvokeLookup(provider, "555-1234");

        // Act
        InvokeVerifySSN(provider, "0000");
        InvokeVerifySSN(provider, "1111");
        var result = InvokeVerifySSN(provider, "2222");

        // Assert
        Assert.False(result.Verified);
        Assert.Equal(0, result.RemainingAttempts);
        Assert.Equal("locked_out", result.NextAction);
        Assert.Equal(AuthenticationState.LockedOut, provider.AuthState);
    }

    [Fact]
    public void Serialize_Deserialize_PreservesState()
    {
        // Arrange
        var provider = new AuthenticationContextProvider(_db);
        InvokeLookup(provider, "555-1234");
        InvokeVerifySSN(provider, "1234");

        // Act
        var serialized = provider.Serialize();
        var restored = new AuthenticationContextProvider(_db, serialized);

        // Assert
        Assert.Equal(AuthenticationState.Verifying, restored.AuthState);
        Assert.Equal("1234567890", restored.CustomerId);
        Assert.Equal("John Smith", restored.CustomerName);
    }

    [Fact]
    public async Task FullAuthFlow_WithAgent()
    {
        // Arrange
        var chatClient = CreateTestChatClient();
        var factory = new AuthAgentFactory(chatClient, _db);
        var (agent, authProvider) = factory.CreateAuthAgent();
        var session = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("I need to check my bill", session);
        await agent.RunAsync("555-1234", session);
        await agent.RunAsync("1234", session);

        // Assert
        Assert.Equal(AuthenticationState.Authenticated, authProvider.AuthState);
        Assert.Equal("John Smith", authProvider.CustomerName);
    }

    // Helper methods to invoke provider tools for testing
    private LookupResult InvokeLookup(AuthenticationContextProvider provider, string identifier)
    {
        // Use reflection or make internal for testing
        var method = typeof(AuthenticationContextProvider)
            .GetMethod("LookupCustomerByIdentifier", BindingFlags.NonPublic | BindingFlags.Instance);
        return (LookupResult)method!.Invoke(provider, [identifier])!;
    }

    private AuthVerificationResult InvokeVerifySSN(AuthenticationContextProvider provider, string answer)
    {
        var method = typeof(AuthenticationContextProvider)
            .GetMethod("VerifyLastFourSSN", BindingFlags.NonPublic | BindingFlags.Instance);
        return (AuthVerificationResult)method!.Invoke(provider, [answer])!;
    }
}
```

### Validation Checklist - Stage 3
- [ ] `AuthenticationContextProvider` manages all auth state
- [ ] Tools are instance methods on the provider
- [ ] State serializes/deserializes correctly for session persistence
- [ ] Provider injects different tools based on auth state
- [ ] Auth agent finds customers in mock CIS database
- [ ] Auth agent asks verification questions one at a time
- [ ] Auth agent accepts correct answers and updates verified factors
- [ ] Auth agent tracks failed attempts
- [ ] Auth agent locks out after 3 failed attempts
- [ ] Auth agent completes authentication with 1 verified factor
- [ ] Session can be restored with full state preserved

---
