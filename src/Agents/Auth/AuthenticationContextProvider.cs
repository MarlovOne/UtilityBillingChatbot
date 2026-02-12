// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Agents.Auth;

/// <summary>
/// Manages authentication state and provides verification tools.
/// State persists across agent runs within a session and can be serialized for persistence.
/// </summary>
public sealed class AuthenticationContextProvider : AIContextProvider
{
    private readonly MockCISDatabase _cisDatabase;

    private readonly ILogger<AuthenticationContextProvider> _logger;

    private const int MaxAttempts = 3;

    // Mutable state
    private AuthenticationState _authState = AuthenticationState.Anonymous;
    private int _failedAttempts;
    private readonly List<string> _verifiedFactors = [];
    private string? _identifyingInfo;
    private string? _customerId;
    private string? _customerName;
    private DateTimeOffset? _authenticatedAt;

    /// <summary>
    /// Constructor for new sessions.
    /// </summary>
    public AuthenticationContextProvider(
        MockCISDatabase cisDatabase, ILogger<AuthenticationContextProvider> logger)
    {
        _cisDatabase = cisDatabase;
        _logger = logger;
    }

    /// <summary>
    /// Constructor for session restore from serialized state.
    /// </summary>
    public AuthenticationContextProvider(
        MockCISDatabase cisDatabase,
        JsonElement jsonElement,
        ILogger<AuthenticationContextProvider> logger,
        JsonSerializerOptions? options = null) : this(cisDatabase, logger)
    {
        if (jsonElement.ValueKind != JsonValueKind.Object)
        {
            _logger.LogError("Invalid JSON for AuthenticationContextProvider state. Expected an object.");
            throw new ArgumentException("Invalid JSON for AuthenticationContextProvider state. Expected an object.");
        }

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

    private static string BuildInstructions()
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
                    LookupCustomerByIdentifier,
                    description: "Look up a customer by phone number, email, or account number")
            ],

            AuthenticationState.Verifying =>
            [
                AIFunctionFactory.Create(
                    VerifyLastFourSSN,
                    description: "Verify the last 4 digits of customer's SSN"),
                AIFunctionFactory.Create(
                    VerifyDateOfBirth,
                    description: "Verify customer's date of birth (format: MM/DD/YYYY)"),
                AIFunctionFactory.Create(
                    CompleteAuthentication,
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
