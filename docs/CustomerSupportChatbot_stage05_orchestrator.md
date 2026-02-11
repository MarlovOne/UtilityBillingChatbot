## Stage 5: Orchestrator

### Objective
Build the main orchestrator that routes user messages through the classifier and dispatches to appropriate agents.

### Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Agent creation | Direct injection | Simpler, matches existing agent patterns |
| Session storage | `ISessionStore` interface with `InMemorySessionStore` | Easy to swap to Redis/database later |
| File location | `src/Orchestration/` folder | Coordinator is not an agent, deserves own namespace |
| Console UI | Refactor `ChatbotService` to thin wrapper | Separates I/O from routing logic |
| Conversation history | Full history with timestamps | Enables context-aware responses and future handoff summaries |
| Error handling | Simple error responses | Honest messaging; no fake handoff until Stage 6 |
| UtilityDataSession | Recreate each time (not stored) | Users don't check multiple accounts; keeps ChatSession simple |

### File Structure

```
src/Orchestration/
├── ChatbotOrchestrator.cs      # Main routing logic
├── OrchestratorModels.cs       # ChatResponse, RequiredAction, ConversationMessage
├── ChatSession.cs              # Session state with conversation history
├── UserSessionContext.cs       # Auth state, customer info, expiry
├── ISessionStore.cs            # Persistence interface
└── InMemorySessionStore.cs     # In-memory implementation

src/Infrastructure/
└── ChatbotService.cs           # Refactored to thin console wrapper (~50 lines)
```

### Dependencies

```
ChatbotOrchestrator
├── ClassifierAgent      (injected directly)
├── FAQAgent             (injected directly)
├── AuthAgent            (injected directly)
├── UtilityDataAgent     (injected directly)
├── ISessionStore        (injected)
└── ILogger              (injected)
```

### Architecture Pattern: Custom Orchestrator with Manual Routing

> **Note**: This implementation uses a custom orchestrator class with manual routing logic.
> For a declarative alternative using the framework's `WorkflowBuilder`, see **Appendix B**.
> The custom approach is suitable for prototyping; consider migrating for production.

```csharp
// src/Orchestration/ChatbotOrchestrator.cs

public class ChatbotOrchestrator
{
    private readonly ClassifierAgent _classifierAgent;
    private readonly FAQAgent _faqAgent;
    private readonly AuthAgent _authAgent;
    private readonly UtilityDataAgent _utilityDataAgent;
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<ChatbotOrchestrator> _logger;

    // In-memory cache for active sessions (backed by ISessionStore for persistence)
    private readonly ConcurrentDictionary<string, ChatSession> _sessionCache = new();

    public ChatbotOrchestrator(
        ClassifierAgent classifierAgent,
        FAQAgent faqAgent,
        AuthAgent authAgent,
        UtilityDataAgent utilityDataAgent,
        ISessionStore sessionStore,
        ILogger<ChatbotOrchestrator> logger)
    {
        _classifierAgent = classifierAgent;
        _faqAgent = faqAgent;
        _authAgent = authAgent;
        _utilityDataAgent = utilityDataAgent;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public async Task<ChatResponse> ProcessMessageAsync(
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        // Get or create session (check cache first, then persistent store)
        var chatSession = await GetOrCreateSessionAsync(sessionId, cancellationToken);

        // Add user message to history
        chatSession.ConversationHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTimeOffset.UtcNow
        });

        try
        {
            // MULTI-TURN SUPPORT: Check if user is in active authentication flow
            if (chatSession.UserContext.AuthState == AuthenticationState.InProgress)
            {
                return await ContinueAuthenticationFlowAsync(userMessage, chatSession, cancellationToken);
            }

            // Step 1: Classify the question
            var classificationResult = await _classifierAgent.ClassifyAsync(userMessage, cancellationToken);

            if (!classificationResult.IsSuccess)
            {
                return CreateErrorResponse($"Classification failed: {classificationResult.Error}");
            }

            var classification = classificationResult.Classification!;

            // Step 2: Route based on classification
            var response = classification.Category switch
            {
                QuestionCategory.BillingFAQ =>
                    await HandleBillingFAQAsync(userMessage, chatSession, cancellationToken),

                QuestionCategory.AccountData =>
                    await HandleAccountDataAsync(userMessage, chatSession, classification, cancellationToken),

                QuestionCategory.ServiceRequest =>
                    CreateServiceRequestResponse(),

                QuestionCategory.HumanRequested =>
                    CreateHumanRequestedResponse(),

                QuestionCategory.OutOfScope =>
                    HandleOutOfScope(classification),

                _ => CreateErrorResponse($"Unknown category: {classification.Category}")
            };

            // Add response to history
            chatSession.ConversationHistory.Add(new ConversationMessage
            {
                Role = "assistant",
                Content = response.Message,
                Timestamp = DateTimeOffset.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: {Error}", ex.Message);
            return CreateErrorResponse($"An error occurred: {ex.Message}");
        }
    }

    private async Task<ChatResponse> HandleBillingFAQAsync(
        string message,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        var response = await _faqAgent.AnswerAsync(message, cancellationToken);

        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.BillingFAQ,
            RequiredAction = RequiredAction.None
        };
    }

    private async Task<ChatResponse> HandleAccountDataAsync(
        string message,
        ChatSession session,
        QuestionClassification classification,
        CancellationToken cancellationToken)
    {
        // Check if user is authenticated
        if (session.UserContext.AuthState != AuthenticationState.Authenticated)
        {
            // INITIATE IN-BAND AUTH FLOW: Route to authentication agent
            return await InitiateAuthenticationFlowAsync(message, session, cancellationToken);
        }

        // Check session expiry
        if (session.UserContext.SessionExpiry.HasValue &&
            session.UserContext.SessionExpiry.Value < DateTimeOffset.UtcNow)
        {
            session.UserContext.AuthState = AuthenticationState.Expired;
            // Re-initiate authentication for expired session
            return await InitiateAuthenticationFlowAsync(message, session, cancellationToken);
        }

        // User is authenticated - proceed with data agent
        var response = await _utilityDataAgent.RunAsync(
            message,
            authSession: session.AuthSession,
            cancellationToken: cancellationToken);

        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.None
        };
    }

    /// <summary>
    /// Initiates the in-band authentication flow.
    /// Stores the pending query to resume after successful authentication.
    /// </summary>
    private async Task<ChatResponse> InitiateAuthenticationFlowAsync(
        string pendingMessage,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        // Store the pending query to resume after authentication
        session.PendingQuery = pendingMessage;

        // Mark session as in authentication flow
        session.UserContext.AuthState = AuthenticationState.InProgress;

        // Start auth conversation
        var response = await _authAgent.RunAsync(
            pendingMessage,
            session: null,
            cancellationToken);

        // Store the auth session for multi-turn flow
        session.AuthSession = response.Session;

        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.AuthenticationInProgress
        };
    }

    /// <summary>
    /// Continues an in-progress authentication flow by routing user input to the AuthAgent.
    /// Automatically resumes the pending query upon successful authentication.
    /// </summary>
    private async Task<ChatResponse> ContinueAuthenticationFlowAsync(
        string userMessage,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        if (session.AuthSession == null)
        {
            // Shouldn't happen, but handle gracefully
            session.UserContext.AuthState = AuthenticationState.Anonymous;
            return new ChatResponse
            {
                Message = "I apologize, there was an issue with the verification process. " +
                         "Please try asking your question again.",
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.None
            };
        }

        // Continue the auth conversation
        var response = await _authAgent.RunAsync(
            userMessage,
            session.AuthSession,
            cancellationToken);

        // Update auth session
        session.AuthSession = response.Session;

        // Check if authentication completed
        if (response.IsAuthenticated)
        {
            session.UserContext.AuthState = AuthenticationState.Authenticated;
            session.UserContext.CustomerId = response.CustomerId;
            session.UserContext.CustomerName = response.CustomerName;
            session.UserContext.SessionExpiry = DateTimeOffset.UtcNow.AddMinutes(30);

            // Resume pending query if exists
            if (!string.IsNullOrEmpty(session.PendingQuery))
            {
                var pendingQuery = session.PendingQuery;
                session.PendingQuery = null;

                // Add confirmation message to history
                session.ConversationHistory.Add(new ConversationMessage
                {
                    Role = "assistant",
                    Content = response.Text,
                    Timestamp = DateTimeOffset.UtcNow
                });

                // Process the original query now that we're authenticated
                return await ProcessMessageAsync(session.SessionId, pendingQuery, cancellationToken);
            }

            return new ChatResponse
            {
                Message = response.Text,
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.None
            };
        }

        // Check if authentication failed (locked out)
        if (response.AuthState == AuthenticationState.LockedOut)
        {
            session.AuthSession = null;
            session.PendingQuery = null;
            session.UserContext.AuthState = AuthenticationState.LockedOut;

            return new ChatResponse
            {
                Message = response.Text,
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.AuthenticationFailed
            };
        }

        // Still in progress - return agent's response (asking for next verification item)
        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.AuthenticationInProgress
        };
    }

    private static ChatResponse HandleOutOfScope(QuestionClassification classification)
    {
        if (classification.Confidence < 0.3)
        {
            return new ChatResponse
            {
                Message = "I'm sorry, I couldn't understand your question. " +
                         "I can help with billing questions, account information, and payment options.",
                Category = QuestionCategory.OutOfScope,
                RequiredAction = RequiredAction.ClarificationNeeded
            };
        }

        return new ChatResponse
        {
            Message = "I'm not able to help with that question. " +
                     "I can assist with utility billing, account balances, payment options, and similar topics.",
            Category = QuestionCategory.OutOfScope,
            RequiredAction = RequiredAction.None
        };
    }

    private static ChatResponse CreateServiceRequestResponse()
    {
        // Stage 6 will implement actual handoff
        return new ChatResponse
        {
            Message = "Service requests like payment arrangements require speaking with a representative. " +
                     "Please call our customer service line at 1-800-XXX-XXXX.",
            Category = QuestionCategory.ServiceRequest,
            RequiredAction = RequiredAction.HumanHandoffNeeded
        };
    }

    private static ChatResponse CreateHumanRequestedResponse()
    {
        // Stage 6 will implement actual handoff
        return new ChatResponse
        {
            Message = "I understand you'd like to speak with a representative. " +
                     "Please call our customer service line at 1-800-XXX-XXXX.",
            Category = QuestionCategory.HumanRequested,
            RequiredAction = RequiredAction.HumanHandoffNeeded
        };
    }

    private static ChatResponse CreateErrorResponse(string message)
    {
        return new ChatResponse
        {
            Message = message,
            Category = QuestionCategory.OutOfScope,
            RequiredAction = RequiredAction.None
        };
    }

    // ========== Session Management ==========

    private async Task<ChatSession> GetOrCreateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Check in-memory cache first
        if (_sessionCache.TryGetValue(sessionId, out var cachedSession))
        {
            cachedSession.UserContext.LastInteraction = DateTimeOffset.UtcNow;
            return cachedSession;
        }

        // Check persistent store
        var persistedSession = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
        if (persistedSession != null)
        {
            persistedSession.UserContext.LastInteraction = DateTimeOffset.UtcNow;
            _sessionCache[sessionId] = persistedSession;
            return persistedSession;
        }

        // Create new session
        var newSession = new ChatSession
        {
            SessionId = sessionId,
            UserContext = new UserSessionContext { SessionId = sessionId },
            ConversationHistory = []
        };

        _sessionCache[sessionId] = newSession;
        return newSession;
    }

    public async Task SaveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessionCache.TryGetValue(sessionId, out var session))
        {
            await _sessionStore.SaveSessionAsync(session, cancellationToken);
        }
    }

    public ChatSession? GetSession(string sessionId)
    {
        _sessionCache.TryGetValue(sessionId, out var session);
        return session;
    }
}
```

### Session Models

```csharp
// src/Orchestration/ChatSession.cs

public class ChatSession
{
    public string SessionId { get; set; } = string.Empty;
    public UserSessionContext UserContext { get; set; } = new();
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public AuthSession? AuthSession { get; set; }  // For multi-turn auth flow
    public string? PendingQuery { get; set; }
    // Note: UtilityDataSession is NOT stored - recreated each query from AuthSession
}

// src/Orchestration/UserSessionContext.cs

public class UserSessionContext
{
    public string SessionId { get; set; } = string.Empty;
    public AuthenticationState AuthState { get; set; } = AuthenticationState.Anonymous;
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTimeOffset? SessionExpiry { get; set; }
    public DateTimeOffset LastInteraction { get; set; } = DateTimeOffset.UtcNow;
}

// src/Orchestration/OrchestratorModels.cs

public class ChatResponse
{
    public string Message { get; set; } = string.Empty;
    public QuestionCategory Category { get; set; }
    public RequiredAction RequiredAction { get; set; }
}

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty;  // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

public enum RequiredAction
{
    None,
    AuthenticationInProgress,
    AuthenticationFailed,
    HumanHandoffNeeded,
    ClarificationNeeded
}
```

### Session Store Interface

```csharp
// src/Orchestration/ISessionStore.cs

public interface ISessionStore
{
    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task SaveSessionAsync(ChatSession session, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

// src/Orchestration/InMemorySessionStore.cs

public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();

    public Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task SaveSessionAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        _sessions[session.SessionId] = session;
        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
```

### DI Registration

```csharp
// src/Orchestration/OrchestrationExtensions.cs

public static class OrchestrationExtensions
{
    public static IServiceCollection AddOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<ChatbotOrchestrator>();
        return services;
    }
}

// Update src/Infrastructure/ServiceCollectionExtensions.cs:
// Add: services.AddOrchestration();
```

### Refactored ChatbotService

```csharp
// src/Infrastructure/ChatbotService.cs (refactored)

public class ChatbotService : BackgroundService
{
    private readonly ChatbotOrchestrator _orchestrator;
    private readonly ILogger<ChatbotService> _logger;
    private readonly string _sessionId = Guid.NewGuid().ToString();

    public ChatbotService(
        ChatbotOrchestrator orchestrator,
        ILogger<ChatbotService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("=== Utility Billing Customer Support ===");
        Console.WriteLine("Type 'quit' to exit.");
        Console.WriteLine();

        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;

            if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Goodbye!");
                break;
            }

            try
            {
                var response = await _orchestrator.ProcessMessageAsync(
                    _sessionId, input, stoppingToken);

                Console.WriteLine();
                Console.WriteLine($"Assistant: {response.Message}");
                Console.WriteLine($"  [Category: {response.Category}, Action: {response.RequiredAction}]");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
```

### Testing Stage 5

```csharp
[Collection("Sequential")]
public class OrchestratorTests : IAsyncLifetime
{
    private IHost _host = null!;
    private ChatbotOrchestrator _orchestrator = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _orchestrator = _host.Services.GetRequiredService<ChatbotOrchestrator>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Orchestrator_RoutesBillingFAQ_ToFAQAgent()
    {
        var sessionId = Guid.NewGuid().ToString();
        var response = await _orchestrator.ProcessMessageAsync(
            sessionId, "What are my payment options?");

        Assert.Equal(QuestionCategory.BillingFAQ, response.Category);
        Assert.Contains("online", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Orchestrator_InitiatesAuth_ForAccountData()
    {
        var sessionId = Guid.NewGuid().ToString();
        var response = await _orchestrator.ProcessMessageAsync(
            sessionId, "What is my current balance?");

        Assert.Equal(QuestionCategory.AccountData, response.Category);
        Assert.Equal(RequiredAction.AuthenticationInProgress, response.RequiredAction);
    }

    [Fact]
    public async Task Orchestrator_CompletesAuthFlow_AndAnswersQuery()
    {
        var sessionId = Guid.NewGuid().ToString();

        // Initial query triggers auth
        var r1 = await _orchestrator.ProcessMessageAsync(sessionId, "What is my balance?");
        Assert.Equal(RequiredAction.AuthenticationInProgress, r1.RequiredAction);

        // Provide phone number
        var r2 = await _orchestrator.ProcessMessageAsync(sessionId, "555-1234");
        Assert.Equal(RequiredAction.AuthenticationInProgress, r2.RequiredAction);

        // Provide SSN last 4
        var r3 = await _orchestrator.ProcessMessageAsync(sessionId, "1234");

        // Should be authenticated and have answered the balance query
        Assert.Equal(RequiredAction.None, r3.RequiredAction);
        Assert.Contains("187", r3.Message);  // John Smith's balance
    }

    [Fact]
    public async Task Orchestrator_HandlesOutOfScope()
    {
        var sessionId = Guid.NewGuid().ToString();
        var response = await _orchestrator.ProcessMessageAsync(
            sessionId, "What's the weather like today?");

        Assert.Equal(QuestionCategory.OutOfScope, response.Category);
    }
}
```

### Implementation Order

1. Create `src/Orchestration/` directory
2. Create `OrchestratorModels.cs` (ChatResponse, ConversationMessage, RequiredAction)
3. Create `UserSessionContext.cs`
4. Create `ChatSession.cs`
5. Create `ISessionStore.cs`
6. Create `InMemorySessionStore.cs`
7. Create `ChatbotOrchestrator.cs`
8. Create `OrchestrationExtensions.cs`
9. Update `ServiceCollectionExtensions.cs` to call `AddOrchestration()`
10. Refactor `ChatbotService.cs` to use orchestrator
11. Build and verify: `dotnet build`
12. Create `tests/OrchestratorTests.cs`
13. Run tests: `dotnet test --filter "FullyQualifiedName~OrchestratorTests"`

### Validation Checklist - Stage 5
- [ ] Orchestrator correctly routes BillingFAQ questions to FAQ agent
- [ ] Orchestrator correctly routes AccountData questions to auth flow
- [ ] Orchestrator correctly routes ServiceRequest questions (placeholder response)
- [ ] Orchestrator initiates authentication when needed
- [ ] Orchestrator resumes pending query after successful authentication
- [ ] Orchestrator handles authentication lockout
- [ ] Orchestrator maintains conversation history
- [ ] Orchestrator handles out-of-scope questions
- [ ] Orchestrator returns simple error messages on failure
- [ ] Session persists across messages within same session ID
- [ ] ChatbotService works as thin console wrapper

---
