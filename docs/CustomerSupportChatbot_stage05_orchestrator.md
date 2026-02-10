## Stage 5: Orchestrator

### Objective
Build the main orchestrator that routes user messages through the classifier and dispatches to appropriate agents.

### Architecture Pattern: Custom Orchestrator with Manual Routing

> **Note**: This implementation uses a custom orchestrator class with manual routing logic.
> For a declarative alternative using the framework's `WorkflowBuilder`, see **Appendix B**.
> The custom approach is suitable for prototyping; consider migrating for production.

```csharp
public class ChatbotOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly IClassifierAgentFactory _classifierFactory;
    private readonly IFAQAgentFactory _faqFactory;
    private readonly IUtilityDataAgentFactory _utilityDataFactory;
    private readonly IInBandAuthAgentFactory _authAgentFactory;
    private readonly ISessionStore _sessionStore;

    // In-memory cache for active sessions (backed by ISessionStore for persistence)
    private readonly ConcurrentDictionary<string, ChatSession> _sessionCache = new();

    public ChatbotOrchestrator(
        IChatClient chatClient,
        IClassifierAgentFactory classifierFactory,
        IFAQAgentFactory faqFactory,
        IUtilityDataAgentFactory utilityDataFactory,
        IInBandAuthAgentFactory authAgentFactory,
        ISessionStore sessionStore)
    {
        _chatClient = chatClient;
        _classifierFactory = classifierFactory;
        _faqFactory = faqFactory;
        _utilityDataFactory = utilityDataFactory;
        _authAgentFactory = authAgentFactory;
        _sessionStore = sessionStore;
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
            var classification = await ClassifyQuestionAsync(userMessage, chatSession, cancellationToken);

            // Step 2: Route based on classification
            var response = classification.Category switch
            {
                QuestionCategory.BillingFAQ =>
                    await HandleBillingFAQAsync(userMessage, chatSession, cancellationToken),

                QuestionCategory.AccountData =>
                    await HandleAccountDataAsync(userMessage, chatSession, classification, cancellationToken),

                QuestionCategory.ServiceRequest =>
                    await InitiateHumanHandoffAsync(userMessage, chatSession, "Service request requires CSR assistance", cancellationToken),

                QuestionCategory.HumanRequested =>
                    await InitiateHumanHandoffAsync(userMessage, chatSession, "User requested human assistance", cancellationToken),

                QuestionCategory.OutOfScope =>
                    await HandleOutOfScopeAsync(userMessage, chatSession, classification, cancellationToken),

                _ => throw new InvalidOperationException($"Unknown category: {classification.Category}")
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
            // Log error and attempt graceful degradation
            // In production, use proper logging
            Console.WriteLine($"Error processing message: {ex.Message}");

            return await InitiateHumanHandoffAsync(
                userMessage,
                chatSession,
                $"System error: {ex.Message}",
                cancellationToken);
        }
    }

    private async Task<QuestionClassification> ClassifyQuestionAsync(
        string message,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        var classifier = _classifierFactory.CreateClassifierAgent();

        // Create session with conversation context for better classification
        var agentSession = await classifier.CreateSessionAsync(
            AIContextProviderFactory: (ctx, ct) => new ValueTask<AIContextProvider>(
                new ConversationContextProvider(session.ConversationHistory)));

        var response = await classifier.RunAsync<QuestionClassification>(
            message,
            agentSession,
            cancellationToken: cancellationToken);

        return response.Result;
    }

    private async Task<ChatResponse> HandleBillingFAQAsync(
        string message,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        var faqAgent = _faqFactory.CreateFAQAgent();
        var agentSession = await faqAgent.CreateSessionAsync();

        // Inject conversation history for context
        foreach (var historyMessage in session.ConversationHistory.TakeLast(10))
        {
            // Add relevant history to session
        }

        var response = await faqAgent.RunAsync(message, agentSession, cancellationToken: cancellationToken);

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
        var dataAgent = _utilityDataFactory.CreateUtilityDataAgent(session.UserContext);
        var agentSession = await dataAgent.CreateSessionAsync(
            AIContextProviderFactory: (ctx, ct) => new ValueTask<AIContextProvider>(
                new AuthContextProvider(session.UserContext)));

        var response = await dataAgent.RunAsync(message, agentSession, cancellationToken: cancellationToken);

        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.None
        };
    }

    /// <summary>
    /// Initiates the in-band authentication flow using the InBandAuthAgent.
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

        // Create auth agent and session (pass context so tools can modify auth state)
        var authAgent = _authAgentFactory.CreateInBandAuthAgent(session.UserContext);
        session.AuthAgentSession = await authAgent.CreateSessionAsync();

        // Initial prompt from auth agent
        var response = await authAgent.RunAsync(
            "Start identity verification for account access",
            session.AuthAgentSession,
            cancellationToken: cancellationToken);

        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.AuthenticationInProgress
        };
    }

    /// <summary>
    /// Continues an in-progress authentication flow by routing user input to the InBandAuthAgent.
    /// Automatically resumes the pending query upon successful authentication.
    /// </summary>
    private async Task<ChatResponse> ContinueAuthenticationFlowAsync(
        string userMessage,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        if (session.AuthAgentSession == null)
        {
            // Shouldn't happen, but handle gracefully
            session.UserContext.AuthState = AuthenticationState.Anonymous;
            return new ChatResponse
            {
                Message = "I apologize, there was an issue with the verification process. Let's start over. " +
                         "Can you please provide your account number or phone number?",
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.AuthenticationRequired
            };
        }

        // Continue the auth conversation
        var authAgent = _authAgentFactory.CreateInBandAuthAgent(session.UserContext);
        var response = await authAgent.RunAsync(
            userMessage,
            session.AuthAgentSession,
            cancellationToken: cancellationToken);

        // Check if authentication completed (agent sets context)
        if (session.UserContext.AuthState == AuthenticationState.Authenticated)
        {
            // Clear auth session
            session.AuthAgentSession = null;

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
        if (session.UserContext.AuthState == AuthenticationState.LockedOut)
        {
            session.AuthAgentSession = null;
            session.PendingQuery = null;

            // Escalate to human
            return await InitiateHumanHandoffAsync(
                session.PendingQuery ?? "Authentication assistance needed",
                session,
                "User locked out after failed verification attempts",
                cancellationToken);
        }

        // Still in progress - return agent's response (asking for next verification item)
        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.AuthenticationInProgress
        };
    }

    private async Task<ChatResponse> HandleOutOfScopeAsync(
        string message,
        ChatSession session,
        QuestionClassification classification,
        CancellationToken cancellationToken)
    {
        // Low confidence - try to help but offer human handoff
        if (classification.Confidence < 0.3)
        {
            return await InitiateHumanHandoffAsync(
                message,
                session,
                $"Low confidence classification: {classification.Reasoning}",
                cancellationToken);
        }

        // Medium confidence - try to help
        return new ChatResponse
        {
            Message = "I'm not entirely sure I understand your question. Could you rephrase it, " +
                     "or would you like me to connect you with a customer service representative?",
            Category = QuestionCategory.OutOfScope,
            RequiredAction = RequiredAction.ClarificationNeeded
        };
    }

    private async Task<ChatResponse> InitiateHumanHandoffAsync(
        string message,
        ChatSession session,
        string reason,
        CancellationToken cancellationToken)
    {
        // This will be fully implemented in Stage 6
        // For now, return a placeholder response
        return new ChatResponse
        {
            Message = "I'm connecting you with a customer service representative. " +
                     "Please hold while I prepare a summary of our conversation.",
            Category = QuestionCategory.HumanRequested,
            RequiredAction = RequiredAction.HumanHandoffInitiated
        };
    }

    // ========== Session Management Helpers ==========

    /// <summary>
    /// Gets session from cache or persistent store, creating new if not found.
    /// </summary>
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

    /// <summary>
    /// Persists session to store (call periodically or on important state changes).
    /// </summary>
    public async Task SaveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessionCache.TryGetValue(sessionId, out var session))
        {
            await _sessionStore.SaveSessionAsync(session, cancellationToken);
        }
    }

    /// <summary>
    /// Gets session from cache (for read-only access).
    /// </summary>
    public ChatSession? GetSession(string sessionId)
    {
        _sessionCache.TryGetValue(sessionId, out var session);
        return session;
    }
}

// Response models
public class ChatResponse
{
    public string Message { get; set; } = string.Empty;
    public QuestionCategory Category { get; set; }
    public RequiredAction RequiredAction { get; set; }
    public string? PendingQuery { get; set; }
}

public enum RequiredAction
{
    None,
    AuthenticationRequired,
    AuthenticationInProgress,  // Multi-turn auth flow active - route next message to auth agent
    HumanHandoffInitiated,
    ClarificationNeeded,
    HumanConversationActive,   // Human agent has joined and is actively chatting
    TransferInProgress,        // Being transferred to specialist
    CallbackScheduled          // Callback has been scheduled
}

// Session storage
public class ChatSession
{
    public string SessionId { get; set; } = string.Empty;
    public UserSessionContext UserContext { get; set; } = new();
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public AgentSession? ClassifierSession { get; set; }
    public AgentSession? FAQSession { get; set; }
    public AgentSession? AuthAgentSession { get; set; } // For multi-turn in-band auth flow
    public AgentSession? DataSession { get; set; }
    public string? PendingQuery { get; set; }
}
```

### Conversation Context Provider

```csharp
/// <summary>
/// Provides conversation history context to agents
/// </summary>
public class ConversationContextProvider : AIContextProvider
{
    private readonly List<ConversationMessage> _history;

    public ConversationContextProvider(List<ConversationMessage> history)
    {
        _history = history;
    }

    public override ValueTask<AIContext> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // Provide conversation summary to agent
        var recentHistory = _history.TakeLast(5);
        var summary = string.Join("\n", recentHistory.Select(m =>
            $"{m.Role}: {m.Content}"));

        return new ValueTask<AIContext>(new AIContext
        {
            Messages = [new ChatMessage(ChatRole.System,
                $"[Recent conversation history for context:\n{summary}]")]
        });
    }
}
```

### Testing Stage 5

```csharp
public class OrchestratorTests
{
    [Fact]
    public async Task Orchestrator_RoutesBillingFAQ_ToFAQAgent()
    {
        // Arrange - Q6: What are my payment options?
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "What are my payment options?");

        // Assert
        Assert.Equal(QuestionCategory.BillingFAQ, response.Category);
        Assert.Contains("online", response.Message.ToLower());
    }

    [Fact]
    public async Task Orchestrator_RequiresAuth_ForAccountData()
    {
        // Arrange - Q2: What is my balance?
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "What is my current balance?");

        // Assert
        Assert.Equal(RequiredAction.AuthenticationRequired, response.RequiredAction);
        Assert.Contains("verify", response.Message.ToLower());
    }

    [Fact]
    public async Task Orchestrator_ProcessesAccountData_WhenAuthenticated()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // First, authenticate via in-band flow
        await orchestrator.CompleteAuthenticationAsync(
            sessionId,
            accountNumber: "ACC-2024-0042",
            customerName: "Maria Garcia",
            tokenExpiry: DateTimeOffset.UtcNow.AddMinutes(30));

        // Act - Q1: Why is my bill so high?
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "Why is my bill so high this month?");

        // Assert
        Assert.Equal(QuestionCategory.AccountData, response.Category);
        Assert.Equal(RequiredAction.None, response.RequiredAction);
    }

    [Fact]
    public async Task Orchestrator_MaintainsSession_AcrossMessages()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act - Multi-turn conversation about assistance programs
        await orchestrator.ProcessMessageAsync(sessionId, "Hi there");
        await orchestrator.ProcessMessageAsync(sessionId, "Do you have any assistance programs?");
        var response = await orchestrator.ProcessMessageAsync(sessionId, "How do I apply?");

        // Assert - should understand context from previous message (Q7: LIHEAP)
        Assert.Contains("LIHEAP", response.Message);
    }

    [Fact]
    public async Task Orchestrator_InitiatesHandoff_ForHumanRequest()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "I need to speak with a customer service representative");

        // Assert
        Assert.Equal(RequiredAction.HumanHandoffInitiated, response.RequiredAction);
    }

    [Fact]
    public async Task Orchestrator_ResumesQuery_AfterAuthentication()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Try to access account data (triggers auth required)
        var authRequired = await orchestrator.ProcessMessageAsync(
            sessionId,
            "What is my current balance?");
        Assert.Equal(RequiredAction.AuthenticationRequired, authRequired.RequiredAction);

        // Complete authentication via in-band flow
        var afterAuth = await orchestrator.CompleteAuthenticationAsync(
            sessionId,
            accountNumber: "ACC-2024-0042",
            customerName: "Maria Garcia",
            tokenExpiry: DateTimeOffset.UtcNow.AddMinutes(30));

        // Assert - should have automatically processed pending query
        Assert.Contains("balance", afterAuth.Message.ToLower());
    }

    [Fact]
    public async Task Orchestrator_RoutesServiceRequest_ToHandoff()
    {
        // Arrange - Q17: Can I set up a payment arrangement?
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "I need to set up a payment arrangement");

        // Assert
        Assert.Equal(QuestionCategory.ServiceRequest, response.Category);
        Assert.Equal(RequiredAction.HumanHandoffInitiated, response.RequiredAction);
    }
}
```

### Validation Checklist - Stage 5
- [ ] Orchestrator correctly routes BillingFAQ questions to FAQ agent
- [ ] Orchestrator correctly routes AccountData questions to auth flow
- [ ] Orchestrator correctly routes ServiceRequest questions to handoff
- [ ] Orchestrator prompts for authentication when needed
- [ ] Orchestrator resumes pending query after successful authentication
- [ ] Orchestrator handles authentication expiry gracefully
- [ ] Orchestrator maintains conversation context across messages
- [ ] Orchestrator initiates handoff for human requests
- [ ] Orchestrator handles errors gracefully with fallback to human handoff
- [ ] Session data persists correctly across the conversation

---
