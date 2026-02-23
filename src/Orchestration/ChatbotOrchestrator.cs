// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Agents;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Agents.FAQ;
using UtilityBillingChatbot.Agents.NextBestAction;
using UtilityBillingChatbot.Agents.Summarization;
using UtilityBillingChatbot.Agents.UtilityData;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Main orchestrator that routes user messages through classification
/// and dispatches to appropriate streaming agents.
/// </summary>
public class ChatbotOrchestrator
{
    private readonly ClassifierAgent _classifierAgent;
    private readonly FAQAgent _faqAgent;
    private readonly AuthAgent _authAgent;
    private readonly UtilityDataAgent _utilityDataAgent;
    private readonly SummarizationAgent _summarizationAgent;
    private readonly NextBestActionAgent _nextBestActionAgent;
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<ChatbotOrchestrator> _logger;

    private readonly ConcurrentDictionary<string, ChatSession> _activeSessions = new();
    private static readonly TimeSpan SessionExpiryDuration = TimeSpan.FromMinutes(30);

    public ChatbotOrchestrator(
        ClassifierAgent classifierAgent,
        FAQAgent faqAgent,
        AuthAgent authAgent,
        UtilityDataAgent utilityDataAgent,
        SummarizationAgent summarizationAgent,
        NextBestActionAgent nextBestActionAgent,
        ISessionStore sessionStore,
        ILogger<ChatbotOrchestrator> logger)
    {
        _classifierAgent = classifierAgent;
        _faqAgent = faqAgent;
        _authAgent = authAgent;
        _utilityDataAgent = utilityDataAgent;
        _summarizationAgent = summarizationAgent;
        _nextBestActionAgent = nextBestActionAgent;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    /// <summary>
    /// Processes a user message, streaming the response token by token.
    /// </summary>
    public async IAsyncEnumerable<string> ProcessMessageStreamingAsync(
        string sessionId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogDebug("Processing message for session {SessionId}", sessionId);

        var session = await GetOrCreateSessionAsync(sessionId, ct);
        session.UserContext.LastInteraction = DateTimeOffset.UtcNow;
        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = userMessage
        });

        var responseText = new StringBuilder();

        var stream = session.AuthSession is not null &&
                     session.UserContext.AuthState is not AuthenticationState.Authenticated
            ? ContinueAuthFlowStreamingAsync(session, userMessage, ct)
            : RouteMessageStreamingAsync(session, userMessage, ct);

        await foreach (var chunk in stream.WithCancellation(ct))
        {
            responseText.Append(chunk);
            yield return chunk;
        }

        // Add response to history
        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "assistant",
            Content = responseText.ToString()
        });

        // Suggested actions (non-streamed, appended after main response)
        var category = session.LastCategory;
        var requiredAction = session.LastRequiredAction;

        if (ShouldSuggestNextAction(category, requiredAction))
        {
            var suggestions = await GetNextBestActionsAsync(session, category!.Value, ct);
            if (suggestions is { Count: > 0 })
            {
                yield return FormatSuggestions(suggestions);
            }
        }

        // Yield debug info
        yield return $"\n\n  [Category: {category}, Action: {requiredAction}]\n";

        await SaveSessionAsync(session, ct);
    }

    private async IAsyncEnumerable<string> RouteMessageStreamingAsync(
        ChatSession session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Stream the classifier
        var classifierResult = _classifierAgent.StreamAsync(userMessage, ct);

        await foreach (var chunk in classifierResult.TextStream.WithCancellation(ct))
        {
            yield return chunk;
        }

        var classification = await classifierResult.Metadata;

        // Null category = greeting/chitchat — text already streamed, we're done
        if (classification.Category is null)
        {
            _logger.LogInformation("Greeting/chitchat detected, response already streamed");
            session.LastCategory = null;
            session.LastRequiredAction = RequiredAction.None;
            yield break;
        }

        var category = classification.Category.Value;
        _logger.LogInformation("Classified as {Category} with confidence {Confidence}",
            category, classification.Confidence);

        session.LastCategory = category;

        // Resolve the handler stream, then forward it
        var handlerStream = category switch
        {
            QuestionCategory.BillingFAQ => HandleBillingFAQAsync(session, userMessage, ct),
            QuestionCategory.AccountData => HandleAccountDataAsync(session, userMessage, ct),
            QuestionCategory.ServiceRequest => HandleHandoffAsync(session, userMessage,
                "Service request requiring human assistance", ct),
            QuestionCategory.HumanRequested => HandleHandoffAsync(session, userMessage,
                "Customer explicitly requested human agent", ct),
            _ => HandleOutOfScopeAsync(session)
        };

        await foreach (var chunk in handlerStream.WithCancellation(ct))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<string> HandleBillingFAQAsync(
        ChatSession session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogDebug("Routing to FAQ agent");

        var faqResult = _faqAgent.StreamAsync(userMessage, ct);

        await foreach (var chunk in faqResult.TextStream.WithCancellation(ct))
        {
            yield return chunk;
        }

        var metadata = await faqResult.Metadata;

        if (!metadata.FoundAnswer)
        {
            _logger.LogInformation("FAQ agent cannot answer, escalating to human");
            await InitiateHumanHandoffAsync(session, userMessage,
                "Question outside FAQ knowledge base", ct);
            session.LastRequiredAction = RequiredAction.HumanHandoffNeeded;
        }
        else
        {
            session.LastRequiredAction = RequiredAction.None;
        }
    }

    private async IAsyncEnumerable<string> HandleAccountDataAsync(
        ChatSession session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!session.UserContext.IsAuthenticated || session.AuthSession is null)
        {
            _logger.LogDebug("Authentication required, initiating auth flow");
            await foreach (var chunk in InitiateAuthFlowAsync(session, userMessage, ct))
                yield return chunk;
            yield break;
        }

        _logger.LogDebug("User authenticated, routing to UtilityData agent");

        // Resolve utility session (may trigger re-auth if expired)
        var utilitySession = await ResolveUtilitySessionAsync(session, ct);
        if (utilitySession is null)
        {
            await foreach (var chunk in InitiateAuthFlowAsync(session, userMessage, ct))
                yield return chunk;
            yield break;
        }

        var result = _utilityDataAgent.StreamAsync(
            userMessage, session: utilitySession, ct: ct);

        await foreach (var chunk in result.TextStream.WithCancellation(ct))
        {
            yield return chunk;
        }

        var metadata = await result.Metadata;

        if (!metadata.FoundAnswer)
        {
            _logger.LogInformation("UtilityData agent cannot answer, escalating to human");
            await InitiateHumanHandoffAsync(session, userMessage,
                "Account question outside agent capabilities", ct);
            session.LastRequiredAction = RequiredAction.HumanHandoffNeeded;
        }
        else
        {
            session.LastRequiredAction = RequiredAction.None;
        }
    }

    private async IAsyncEnumerable<string> InitiateAuthFlowAsync(
        ChatSession session,
        string pendingQuery,
        [EnumeratorCancellation] CancellationToken ct)
    {
        session.PendingQuery = pendingQuery;
        session.UserContext.AuthState = AuthenticationState.InProgress;

        var authSession = await _authAgent.CreateSessionAsync(ct);
        session.AuthSession = authSession;

        var authResult = _authAgent.StreamAsync(
            "I need to access my account.", authSession, ct);

        yield return "To access your account information, I'll need to verify your identity first.\n\n";

        await foreach (var chunk in authResult.TextStream.WithCancellation(ct))
        {
            yield return chunk;
        }

        await authResult.Metadata;
        session.LastRequiredAction = RequiredAction.AuthenticationInProgress;
    }

    private async IAsyncEnumerable<string> ContinueAuthFlowStreamingAsync(
        ChatSession session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogDebug("Continuing authentication flow");

        var authResult = _authAgent.StreamAsync(userMessage, session.AuthSession!, ct);

        await foreach (var chunk in authResult.TextStream.WithCancellation(ct))
        {
            yield return chunk;
        }

        var metadata = await authResult.Metadata;
        session.UserContext.AuthState = metadata.State;

        if (metadata.State == AuthenticationState.Authenticated)
        {
            session.UserContext.CustomerId = metadata.CustomerId;
            session.UserContext.CustomerName = metadata.CustomerName;
            session.UserContext.SessionExpiry = DateTimeOffset.UtcNow.Add(SessionExpiryDuration);

            _logger.LogInformation("Authentication successful for {Customer}", metadata.CustomerName);

            if (!string.IsNullOrEmpty(session.PendingQuery))
            {
                var pendingQuery = session.PendingQuery;
                session.PendingQuery = null;

                yield return $"\n\nThank you, {metadata.CustomerName}! You're now verified.\n\n";

                // Replay the pending query through account data handler
                await foreach (var chunk in HandleAccountDataAsync(session, pendingQuery, ct))
                    yield return chunk;
            }
            else
            {
                yield return $"\n\nThank you, {metadata.CustomerName}! You're now verified. How can I help you with your account?";
            }

            session.LastRequiredAction = RequiredAction.None;
        }
        else if (metadata.State == AuthenticationState.LockedOut)
        {
            _logger.LogWarning("Authentication locked out for session {SessionId}", session.SessionId);
            session.PendingQuery = null;
            session.LastRequiredAction = RequiredAction.AuthenticationFailed;
        }
        else
        {
            session.LastRequiredAction = RequiredAction.AuthenticationInProgress;
        }
    }

    private async IAsyncEnumerable<string> HandleHandoffAsync(
        ChatSession session,
        string userMessage,
        string reason,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogDebug("Initiating handoff: {Reason}", reason);
        await InitiateHumanHandoffAsync(session, userMessage, reason, ct);
        session.LastRequiredAction = RequiredAction.None;

        yield return "I've forwarded your request to a customer service representative. " +
                     "They'll reach out to you shortly to assist with your inquiry. " +
                     "Is there anything else I can help you with in the meantime?";
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators
    private static async IAsyncEnumerable<string> HandleOutOfScopeAsync(ChatSession session)
    {
        session.LastRequiredAction = RequiredAction.ClarificationNeeded;
        yield return "I'm a utility billing assistant and can help you with billing questions, " +
                     "account information, and payment options. Could you please ask something " +
                     "related to your utility bill or account?";
    }
#pragma warning restore CS1998

    /// <summary>
    /// Resolves the UtilityDataSession, creating one if needed. Returns null if re-auth is needed.
    /// </summary>
    private async Task<UtilityDataSession?> ResolveUtilitySessionAsync(
        ChatSession session, CancellationToken ct)
    {
        try
        {
            var utilitySession = session.UtilityDataSession;
            if (utilitySession is null)
            {
                utilitySession = await _utilityDataAgent.CreateSessionAsync(
                    session.AuthSession!, ct);
                session.UtilityDataSession = utilitySession;
            }
            return utilitySession;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "UtilityData agent error, may need re-authentication");
            session.UserContext.AuthState = AuthenticationState.Expired;
            session.AuthSession = null;
            session.UtilityDataSession = null;
            return null;
        }
    }

    private async Task InitiateHumanHandoffAsync(
        ChatSession session,
        string userMessage,
        string escalationReason,
        CancellationToken ct)
    {
        var conversationText = string.Join("\n",
            session.ConversationHistory.Select(m => $"{m.Role}: {m.Content}"));

        var summary = await _summarizationAgent.SummarizeAsync(
            conversationText, escalationReason, userMessage, ct);

        var conversationDuration = session.ConversationHistory.Count > 0
            ? DateTimeOffset.UtcNow - session.ConversationHistory[0].Timestamp
            : TimeSpan.Zero;

        var package = new HandoffPackage(
            SessionId: session.SessionId,
            CustomerName: session.UserContext.CustomerName,
            AccountNumber: session.UserContext.CustomerId,
            Intent: summary.OriginalQuestion,
            ConversationSummary: summary.Summary,
            ConversationDuration: conversationDuration,
            RecommendedOpening: BuildRecommendedOpening(session, summary),
            ConversationHistory: session.ConversationHistory.ToList());

        LogHandoffPackage(package);
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

    private static bool ShouldSuggestNextAction(QuestionCategory? category, RequiredAction action)
    {
        if (category is not (QuestionCategory.BillingFAQ or QuestionCategory.AccountData))
            return false;

        if (action is RequiredAction.AuthenticationInProgress or RequiredAction.AuthenticationFailed)
            return false;

        return true;
    }

    private static string FormatSuggestions(List<SuggestedAction> suggestions)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("You might also want to ask:");
        foreach (var suggestion in suggestions)
        {
            sb.AppendLine($"  - \"{suggestion.SuggestedQuestion}\"");
        }
        return sb.ToString();
    }

    private async Task<List<SuggestedAction>?> GetNextBestActionsAsync(
        ChatSession session,
        QuestionCategory category,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var suggestions = await _nextBestActionAgent.SuggestAsync(
                session.ConversationHistory,
                category,
                session.UserContext.IsAuthenticated,
                linkedCts.Token);

            return suggestions.Count > 0 ? suggestions : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NextBestActionAgent failed, continuing without suggestions");
            return null;
        }
    }

    private async Task<ChatSession> GetOrCreateSessionAsync(
        string sessionId, CancellationToken ct)
    {
        if (_activeSessions.TryGetValue(sessionId, out var cachedSession))
            return cachedSession;

        var session = await _sessionStore.GetSessionAsync(sessionId, ct);

        if (session is null)
        {
            session = new ChatSession
            {
                SessionId = sessionId,
                UserContext = new UserSessionContext { SessionId = sessionId }
            };
            _logger.LogInformation("Created new session {SessionId}", sessionId);
        }

        _activeSessions.TryAdd(sessionId, session);
        return session;
    }

    private async Task SaveSessionAsync(ChatSession session, CancellationToken ct)
    {
        _activeSessions.AddOrUpdate(session.SessionId, session, (_, _) => session);
        await _sessionStore.SaveSessionAsync(session, ct);
    }
}
