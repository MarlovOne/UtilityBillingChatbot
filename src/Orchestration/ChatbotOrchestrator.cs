// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Agents.FAQ;
using UtilityBillingChatbot.Agents.Summarization;
using UtilityBillingChatbot.Agents.UtilityData;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Main orchestrator that routes user messages through classification
/// and dispatches to appropriate agents.
/// </summary>
public class ChatbotOrchestrator
{
    private readonly ClassifierAgent _classifierAgent;
    private readonly FAQAgent _faqAgent;
    private readonly AuthAgent _authAgent;
    private readonly UtilityDataAgent _utilityDataAgent;
    private readonly SummarizationAgent _summarizationAgent;
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<ChatbotOrchestrator> _logger;

    // Cache for active sessions to avoid constant store lookups
    private readonly ConcurrentDictionary<string, ChatSession> _activeSessions = new();

    // Session expiry duration (30 minutes of inactivity)
    private static readonly TimeSpan SessionExpiryDuration = TimeSpan.FromMinutes(30);

    public ChatbotOrchestrator(
        ClassifierAgent classifierAgent,
        FAQAgent faqAgent,
        AuthAgent authAgent,
        UtilityDataAgent utilityDataAgent,
        SummarizationAgent summarizationAgent,
        ISessionStore sessionStore,
        ILogger<ChatbotOrchestrator> logger)
    {
        _classifierAgent = classifierAgent;
        _faqAgent = faqAgent;
        _authAgent = authAgent;
        _utilityDataAgent = utilityDataAgent;
        _summarizationAgent = summarizationAgent;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    /// <summary>
    /// Processes a user message and returns the appropriate response.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response with message and any required actions.</returns>
    public async Task<ChatResponse> ProcessMessageAsync(
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing message for session {SessionId}", sessionId);

        // Get or create session
        var session = await GetOrCreateSessionAsync(sessionId, cancellationToken);

        // Update last interaction time
        session.UserContext.LastInteraction = DateTimeOffset.UtcNow;

        // Add user message to history
        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = userMessage
        });

        ChatResponse response;

        try
        {
            // Check if we're in an active auth flow
            if (session.AuthSession is not null &&
                session.UserContext.AuthState is not AuthenticationState.Authenticated)
            {
                response = await ContinueAuthenticationFlowAsync(session, userMessage, cancellationToken);
            }
            else
            {
                response = await RouteMessageAsync(session, userMessage, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for session {SessionId}", sessionId);
            response = new ChatResponse
            {
                Message = "I apologize, but I encountered an error processing your request. Please try again.",
                Category = QuestionCategory.OutOfScope,
                RequiredAction = RequiredAction.None
            };
        }

        // Add response to history
        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "assistant",
            Content = response.Message
        });

        // Save session
        await SaveSessionAsync(session, cancellationToken);

        return response;
    }

    private async Task<ChatResponse> RouteMessageAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        // Classify the question
        var classificationResult = await _classifierAgent.ClassifyAsync(userMessage, cancellationToken);

        if (!classificationResult.IsSuccess)
        {
            _logger.LogWarning("Classification failed: {Error}", classificationResult.Error);
            return new ChatResponse
            {
                Message = "I'm having trouble understanding your question. Could you please rephrase it?",
                Category = QuestionCategory.OutOfScope,
                RequiredAction = RequiredAction.ClarificationNeeded
            };
        }

        var classification = classificationResult.Classification!;
        _logger.LogInformation("Classified as {Category} with confidence {Confidence}",
            classification.Category, classification.Confidence);

        return classification.Category switch
        {
            QuestionCategory.BillingFAQ => await HandleBillingFAQAsync(session, userMessage, cancellationToken),
            QuestionCategory.AccountData => await HandleAccountDataAsync(session, userMessage, cancellationToken),
            QuestionCategory.ServiceRequest => await HandleServiceRequestAsync(session, userMessage, cancellationToken),
            QuestionCategory.HumanRequested => await HandleHumanRequestedAsync(session, userMessage, cancellationToken),
            QuestionCategory.OutOfScope => HandleOutOfScope(),
            _ => HandleOutOfScope()
        };
    }

    private async Task<ChatResponse> HandleBillingFAQAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Routing to FAQ agent");

        var faqResponse = await _faqAgent.AnswerAsync(userMessage, cancellationToken: cancellationToken);

        // If the FAQ agent can't answer, escalate to human
        if (!faqResponse.FoundAnswer)
        {
            _logger.LogInformation("FAQ agent cannot answer (FoundAnswer=false), escalating to human");
            return await InitiateHumanHandoffAsync(
                session,
                userMessage,
                "Question outside FAQ knowledge base",
                cancellationToken);
        }

        return new ChatResponse
        {
            Message = faqResponse.Text,
            Category = QuestionCategory.BillingFAQ,
            RequiredAction = RequiredAction.None
        };
    }

    private async Task<ChatResponse> HandleAccountDataAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        // Check if already authenticated
        if (session.UserContext.IsAuthenticated && session.AuthSession is not null)
        {
            _logger.LogDebug("User authenticated, routing to UtilityData agent");
            return await GetAccountDataAsync(session, userMessage, cancellationToken);
        }

        // Need to authenticate first
        _logger.LogDebug("Authentication required, initiating auth flow");
        return await InitiateAuthenticationFlowAsync(session, userMessage, cancellationToken);
    }

    private async Task<ChatResponse> GetAccountDataAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var dataResponse = await _utilityDataAgent.RunAsync(
                userMessage,
                authSession: session.AuthSession,
                cancellationToken: cancellationToken);

            // If the UtilityData agent can't answer, escalate to human
            if (!dataResponse.FoundAnswer)
            {
                _logger.LogInformation("UtilityData agent cannot answer (FoundAnswer=false), escalating to human");
                return await InitiateHumanHandoffAsync(
                    session,
                    userMessage,
                    "Account question outside agent capabilities",
                    cancellationToken);
            }

            return new ChatResponse
            {
                Message = dataResponse.Text,
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.None
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "UtilityData agent error, may need re-authentication");
            // Session may have expired, need to re-auth
            session.UserContext.AuthState = AuthenticationState.Expired;
            session.AuthSession = null;
            return await InitiateAuthenticationFlowAsync(session, userMessage, cancellationToken);
        }
    }

    private async Task<ChatResponse> InitiateAuthenticationFlowAsync(
        ChatSession session,
        string pendingQuery,
        CancellationToken cancellationToken)
    {
        // Store the pending query to answer after authentication
        session.PendingQuery = pendingQuery;
        session.UserContext.AuthState = AuthenticationState.InProgress;

        // Start authentication flow
        var authResponse = await _authAgent.RunAsync(
            "I need to access my account.",
            session: null,
            cancellationToken);

        session.AuthSession = authResponse.Session;

        return new ChatResponse
        {
            Message = $"To access your account information, I'll need to verify your identity first.\n\n{authResponse.Text}",
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.AuthenticationInProgress
        };
    }

    private async Task<ChatResponse> ContinueAuthenticationFlowAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Continuing authentication flow");

        var authResponse = await _authAgent.RunAsync(
            userMessage,
            session.AuthSession,
            cancellationToken);

        // Update session with new auth state
        session.AuthSession = authResponse.Session;
        session.UserContext.AuthState = authResponse.AuthState;

        if (authResponse.IsAuthenticated)
        {
            // Authentication successful
            session.UserContext.CustomerId = authResponse.CustomerId;
            session.UserContext.CustomerName = authResponse.CustomerName;
            session.UserContext.SessionExpiry = DateTimeOffset.UtcNow.Add(SessionExpiryDuration);

            _logger.LogInformation("Authentication successful for {Customer}", authResponse.CustomerName);

            // If there's a pending query, answer it now
            if (!string.IsNullOrEmpty(session.PendingQuery))
            {
                var pendingQuery = session.PendingQuery;
                session.PendingQuery = null;

                var dataResponse = await GetAccountDataAsync(session, pendingQuery, cancellationToken);

                return new ChatResponse
                {
                    Message = $"Thank you, {authResponse.CustomerName}! You're now verified.\n\n{dataResponse.Message}",
                    Category = QuestionCategory.AccountData,
                    RequiredAction = RequiredAction.None
                };
            }

            return new ChatResponse
            {
                Message = $"Thank you, {authResponse.CustomerName}! You're now verified. How can I help you with your account?",
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.None
            };
        }

        if (authResponse.AuthState == AuthenticationState.LockedOut)
        {
            _logger.LogWarning("Authentication locked out for session {SessionId}", session.SessionId);
            session.PendingQuery = null;

            return new ChatResponse
            {
                Message = authResponse.Text,
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.AuthenticationFailed
            };
        }

        // Still in auth flow
        return new ChatResponse
        {
            Message = authResponse.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.AuthenticationInProgress
        };
    }

    private async Task<ChatResponse> HandleServiceRequestAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Initiating handoff for service request");
        return await InitiateHumanHandoffAsync(
            session,
            userMessage,
            "Service request requiring human assistance",
            cancellationToken);
    }

    private async Task<ChatResponse> HandleHumanRequestedAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Customer requested human agent");
        return await InitiateHumanHandoffAsync(
            session,
            userMessage,
            "Customer explicitly requested human agent",
            cancellationToken);
    }

    private async Task<ChatResponse> InitiateHumanHandoffAsync(
        ChatSession session,
        string userMessage,
        string escalationReason,
        CancellationToken cancellationToken)
    {
        // Build conversation text for summarization
        var conversationText = string.Join("\n",
            session.ConversationHistory.Select(m => $"{m.Role}: {m.Content}"));

        // Generate summary
        var summary = await _summarizationAgent.SummarizeAsync(
            conversationText,
            escalationReason,
            userMessage,
            cancellationToken);

        // Calculate conversation duration
        var conversationDuration = session.ConversationHistory.Count > 0
            ? DateTimeOffset.UtcNow - session.ConversationHistory[0].Timestamp
            : TimeSpan.Zero;

        // Build the handoff package
        var package = new HandoffPackage(
            SessionId: session.SessionId,
            CustomerName: session.UserContext.CustomerName,
            AccountNumber: session.UserContext.CustomerId,
            Intent: summary.OriginalQuestion,
            ConversationSummary: summary.Summary,
            ConversationDuration: conversationDuration,
            RecommendedOpening: BuildRecommendedOpening(session, summary),
            ConversationHistory: session.ConversationHistory.ToList()
        );

        // Log the handoff package (mock "send to Salesforce")
        LogHandoffPackage(package);

        return new ChatResponse
        {
            Message = "I've forwarded your request to a customer service representative. " +
                      "They'll reach out to you shortly to assist with your inquiry. " +
                      "Is there anything else I can help you with in the meantime?",
            Category = QuestionCategory.HumanRequested,
            RequiredAction = RequiredAction.None
        };
    }

    private static string BuildRecommendedOpening(ChatSession session, SummaryResponse summary)
    {
        var greeting = session.UserContext.CustomerName is not null
            ? $"Hello {session.UserContext.CustomerName},"
            : "Hello,";

        return $"{greeting} I'm following up on your recent chat with our virtual assistant. " +
               $"I understand you need help with: {summary.OriginalQuestion}";
    }

    private void LogHandoffPackage(HandoffPackage package)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("                    CSR HANDOFF PACKAGE");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine($"Session:           {package.SessionId}");
        sb.AppendLine($"Customer:          {package.CustomerName ?? "Not authenticated"}");
        sb.AppendLine($"Account:           {package.AccountNumber ?? "N/A"}");
        sb.AppendLine($"Duration:          {package.ConversationDuration:hh\\:mm\\:ss}");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine($"INTENT:            {package.Intent}");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("SUMMARY:");
        sb.AppendLine(package.ConversationSummary);
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("RECOMMENDED OPENING:");
        sb.AppendLine($"\"{package.RecommendedOpening}\"");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");

        _logger.LogInformation("{HandoffPackage}", sb.ToString());
    }

    private static ChatResponse HandleOutOfScope()
    {
        return new ChatResponse
        {
            Message = "I'm a utility billing assistant and can help you with billing questions, " +
                      "account information, and payment options. Could you please ask something " +
                      "related to your utility bill or account?",
            Category = QuestionCategory.OutOfScope,
            RequiredAction = RequiredAction.ClarificationNeeded
        };
    }

    private async Task<ChatSession> GetOrCreateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Check cache first
        if (_activeSessions.TryGetValue(sessionId, out var cachedSession))
        {
            return cachedSession;
        }

        // Try to load from store
        var session = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);

        if (session is null)
        {
            // Create new session
            session = new ChatSession
            {
                SessionId = sessionId,
                UserContext = new UserSessionContext { SessionId = sessionId }
            };
            _logger.LogInformation("Created new session {SessionId}", sessionId);
        }

        // Cache and return
        _activeSessions.TryAdd(sessionId, session);
        return session;
    }

    private async Task SaveSessionAsync(ChatSession session, CancellationToken cancellationToken)
    {
        _activeSessions.AddOrUpdate(session.SessionId, session, (_, _) => session);
        await _sessionStore.SaveSessionAsync(session, cancellationToken);
    }
}
