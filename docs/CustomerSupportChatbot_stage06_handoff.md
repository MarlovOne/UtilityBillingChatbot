## Stage 6: Human Handoff with Summarization

### Objective
Build the summarization agent and human handoff flow. The summarization agent creates concise conversation summaries for human agents when customers need to be escalated.

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        HUMAN HANDOFF ARCHITECTURE                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  User Message                                                            │
│       │                                                                  │
│       ▼                                                                  │
│  ┌─────────────────┐                                                     │
│  │ ChatbotOrchestrator                                                   │
│  │ (Routes to handlers)                                                  │
│  └────────┬────────┘                                                     │
│           │                                                              │
│           │ HumanRequested / ServiceRequest                              │
│           ▼                                                              │
│  ┌─────────────────┐         ┌─────────────────┐                        │
│  │ SummarizationAgent        │ HandoffService  │                        │
│  │ (Creates summary)  ─────► │ (Queues ticket) │                        │
│  └─────────────────┘         └────────┬────────┘                        │
│                                       │                                  │
│                                       ▼                                  │
│                              ┌─────────────────┐                        │
│                              │ HandoffManager  │                        │
│                              │ - Ticket Queue  │                        │
│                              │ - Agent Pool    │                        │
│                              └─────────────────┘                        │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### File Structure

```
src/Agents/Summarization/
├── SummarizationAgent.cs           # Main agent class + DI extension
└── SummarizationModels.cs          # Response and summary types

src/Orchestration/Handoff/
├── HandoffService.cs               # IHandoffService implementation + DI extension
├── HandoffManager.cs               # In-memory ticket/agent management
├── HandoffModels.cs                # Request, response, ticket models
└── HandoffState.cs                 # Handoff-related enums
```

### Implementation - Summarization Agent

The summarization agent follows the same pattern as other agents in the codebase. It's stateless since it only needs to process a single conversation for summary.

```csharp
// src/Agents/Summarization/SummarizationAgent.cs

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Agents.Summarization;

/// <summary>
/// Agent that summarizes conversations for human agent handoff.
/// Creates structured summaries including issue, AI attempts, and user context.
/// </summary>
public class SummarizationAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<SummarizationAgent> _logger;

    private const string Instructions = """
        You are a conversation summarizer for customer support handoffs.
        Your job is to create a concise but complete summary for human agents.

        Include in your summary:
        1. User's main issue/question (1-2 sentences)
        2. What the AI tried (briefly)
        3. Why escalation is happening
        4. User's emotional state (if apparent)
        5. Any relevant account/order info mentioned

        Format your response as:

        ## Issue Summary
        [Main issue in 1-2 sentences]

        ## AI Attempts
        [What was tried, briefly]

        ## Escalation Reason
        [Why human needed]

        ## User Context
        - Name: [if known, otherwise "Unknown"]
        - Account: [if authenticated, otherwise "Not authenticated"]
        - Mood: [neutral/frustrated/urgent]

        ## Recommended Next Steps
        [1-2 suggestions for human agent]

        Keep the summary under 200 words. Be factual and neutral in tone.
        """;

    public SummarizationAgent(
        IChatClient chatClient,
        ILogger<SummarizationAgent> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// Summarizes a conversation for handoff to a human agent.
    /// </summary>
    /// <param name="conversation">The conversation history formatted as text.</param>
    /// <param name="escalationReason">Why the handoff is happening.</param>
    /// <param name="currentQuestion">The user's current/pending question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured summary for human agent.</returns>
    public async Task<SummaryResponse> SummarizeAsync(
        string conversation,
        string escalationReason,
        string currentQuestion,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Summarizing conversation for handoff. Reason: {Reason}", escalationReason);

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "SummarizationAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions
            }
        });

        var session = await agent.CreateSessionAsync(cancellationToken);

        var prompt = $"""
            Summarize this customer support conversation for handoff to a human agent.

            Escalation reason: {escalationReason}
            Current user question: {currentQuestion}

            Conversation:
            {conversation}
            """;

        var response = await agent.RunAsync(
            message: prompt,
            session: session,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Generated summary for handoff");

        return new SummaryResponse(
            Summary: response.Text ?? string.Empty,
            EscalationReason: escalationReason,
            OriginalQuestion: currentQuestion);
    }
}

/// <summary>
/// Extension methods for registering the SummarizationAgent.
/// </summary>
public static class SummarizationAgentExtensions
{
    public static IServiceCollection AddSummarizationAgent(this IServiceCollection services)
    {
        services.AddSingleton<SummarizationAgent>();
        return services;
    }
}
```

```csharp
// src/Agents/Summarization/SummarizationModels.cs

namespace UtilityBillingChatbot.Agents.Summarization;

/// <summary>
/// Response from the summarization agent.
/// </summary>
public record SummaryResponse(
    string Summary,
    string EscalationReason,
    string OriginalQuestion);
```

### Handoff Models

```csharp
// src/Orchestration/Handoff/HandoffModels.cs

using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Orchestration.Handoff;

/// <summary>
/// Request to create a human handoff ticket.
/// </summary>
public class HumanHandoffRequest
{
    /// <summary>Unique ticket identifier.</summary>
    public string TicketId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>Session ID for the customer's conversation.</summary>
    public required string SessionId { get; set; }

    /// <summary>AI-generated conversation summary.</summary>
    public required string ConversationSummary { get; set; }

    /// <summary>The question that triggered the handoff.</summary>
    public required string OriginalQuestion { get; set; }

    /// <summary>Why the handoff is needed.</summary>
    public required string EscalationReason { get; set; }

    /// <summary>User context from the session.</summary>
    public required UserSessionContext UserContext { get; set; }

    /// <summary>Full conversation history.</summary>
    public List<ConversationMessage> ConversationHistory { get; set; } = [];

    /// <summary>Suggested department based on escalation reason.</summary>
    public string? SuggestedDepartment { get; set; }

    /// <summary>When the request was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Response from a human agent.
/// </summary>
public record HumanResponse(
    string TicketId,
    string AgentId,
    string AgentName,
    string Message,
    HandoffResolution Resolution,
    DateTimeOffset RespondedAt);

/// <summary>
/// Ticket tracking the handoff state.
/// </summary>
public class HandoffTicket
{
    public required string TicketId { get; set; }
    public required string SessionId { get; set; }
    public string? CustomerName { get; set; }
    public required string Summary { get; set; }
    public required string EscalationReason { get; set; }
    public string? SuggestedDepartment { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Pending;
    public string? AssignedAgentId { get; set; }
    public string? AssignedAgentName { get; set; }
    public string? Resolution { get; set; }
    public string? ResolutionNotes { get; set; }
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// Information about a human agent.
/// </summary>
public record AgentInfo(
    string AgentId,
    string Name,
    AgentStatus Status);
```

```csharp
// src/Orchestration/Handoff/HandoffState.cs

namespace UtilityBillingChatbot.Orchestration.Handoff;

/// <summary>
/// State of a handoff within a chat session.
/// </summary>
public enum HandoffState
{
    /// <summary>No handoff in progress.</summary>
    None,

    /// <summary>Waiting for a human agent to claim the ticket.</summary>
    WaitingForHuman,

    /// <summary>Human agent has responded.</summary>
    HumanResponded,

    /// <summary>Active conversation with human agent.</summary>
    HumanConversationActive,

    /// <summary>Handoff has been resolved.</summary>
    Resolved
}

/// <summary>
/// How the handoff was resolved.
/// </summary>
public enum HandoffResolution
{
    /// <summary>Issue was resolved by the human agent.</summary>
    Resolved,

    /// <summary>Human agent is continuing the conversation.</summary>
    ContinueConversation,

    /// <summary>Transferred to a specialist department.</summary>
    TransferToSpecialist,

    /// <summary>Callback has been scheduled.</summary>
    ScheduleCallback
}

/// <summary>
/// Status of a handoff ticket.
/// </summary>
public enum TicketStatus
{
    /// <summary>Waiting to be claimed by an agent.</summary>
    Pending,

    /// <summary>Being handled by an agent.</summary>
    Active,

    /// <summary>Ticket has been resolved.</summary>
    Resolved,

    /// <summary>Customer disconnected before resolution.</summary>
    Abandoned
}

/// <summary>
/// Availability status of a human agent.
/// </summary>
public enum AgentStatus
{
    Available,
    Busy,
    Away
}
```

### Handoff Service

```csharp
// src/Orchestration/Handoff/HandoffService.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Orchestration.Handoff;

/// <summary>
/// Interface for managing human handoff operations.
/// </summary>
public interface IHandoffService
{
    /// <summary>Creates a handoff ticket and queues it for human agents.</summary>
    Task<string> CreateHandoffTicketAsync(
        HumanHandoffRequest request,
        CancellationToken cancellationToken);

    /// <summary>Waits for a human agent to respond to a ticket.</summary>
    Task<HumanResponse?> WaitForHumanResponseAsync(
        string ticketId,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>Gets the current status of a ticket.</summary>
    HandoffTicket? GetTicket(string ticketId);
}

/// <summary>
/// Service for managing human handoff operations.
/// </summary>
public class HandoffService : IHandoffService
{
    private readonly HandoffManager _handoffManager;
    private readonly ILogger<HandoffService> _logger;

    public HandoffService(
        HandoffManager handoffManager,
        ILogger<HandoffService> logger)
    {
        _handoffManager = handoffManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CreateHandoffTicketAsync(
        HumanHandoffRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating handoff ticket for session {SessionId}. Reason: {Reason}",
            request.SessionId, request.EscalationReason);

        var ticketId = await _handoffManager.CreateTicketAsync(request, cancellationToken);

        _logger.LogInformation(
            "Created handoff ticket {TicketId} for session {SessionId}",
            ticketId, request.SessionId);

        return ticketId;
    }

    /// <inheritdoc />
    public async Task<HumanResponse?> WaitForHumanResponseAsync(
        string ticketId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Waiting for human response on ticket {TicketId}", ticketId);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            return await _handoffManager.WaitForResponseAsync(ticketId, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogDebug("Timeout waiting for human response on ticket {TicketId}", ticketId);
            return null;
        }
    }

    /// <inheritdoc />
    public HandoffTicket? GetTicket(string ticketId)
    {
        return _handoffManager.GetTicket(ticketId);
    }
}

/// <summary>
/// Extension methods for registering handoff services.
/// </summary>
public static class HandoffServiceExtensions
{
    public static IServiceCollection AddHandoffServices(this IServiceCollection services)
    {
        services.AddSingleton<HandoffManager>();
        services.AddSingleton<IHandoffService, HandoffService>();
        return services;
    }
}
```

### Handoff Manager (In-Memory)

```csharp
// src/Orchestration/Handoff/HandoffManager.cs

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Orchestration.Handoff;

/// <summary>
/// Manages handoff tickets and agent assignments.
/// In-memory implementation for prototyping - replace with persistent store for production.
/// </summary>
public class HandoffManager
{
    private readonly ConcurrentDictionary<string, HandoffTicket> _tickets = new();
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();
    private readonly ConcurrentDictionary<string, string> _sessionToTicket = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<HumanResponse>> _responseWaiters = new();
    private readonly ILogger<HandoffManager> _logger;

    public HandoffManager(ILogger<HandoffManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a new handoff ticket.
    /// </summary>
    public Task<string> CreateTicketAsync(
        HumanHandoffRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = new HandoffTicket
        {
            TicketId = request.TicketId,
            SessionId = request.SessionId,
            CustomerName = request.UserContext.CustomerName,
            Summary = request.ConversationSummary,
            EscalationReason = request.EscalationReason,
            ConversationHistory = request.ConversationHistory,
            SuggestedDepartment = request.SuggestedDepartment,
            Status = TicketStatus.Pending,
            CreatedAt = request.CreatedAt
        };

        _tickets[ticket.TicketId] = ticket;
        _sessionToTicket[request.SessionId] = ticket.TicketId;

        _logger.LogInformation(
            "Created ticket {TicketId} for session {SessionId}, department: {Department}",
            ticket.TicketId, ticket.SessionId, ticket.SuggestedDepartment ?? "General");

        // In a real implementation, this would notify available agents
        // via SignalR, webhook, or message queue

        return Task.FromResult(ticket.TicketId);
    }

    /// <summary>
    /// Claims a ticket for a human agent.
    /// </summary>
    public HandoffTicket? ClaimTicket(string ticketId, string agentId, string agentName)
    {
        if (!_tickets.TryGetValue(ticketId, out var ticket))
            return null;

        if (ticket.Status != TicketStatus.Pending)
            return null;

        ticket.AssignedAgentId = agentId;
        ticket.AssignedAgentName = agentName;
        ticket.Status = TicketStatus.Active;
        ticket.AssignedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Ticket {TicketId} claimed by agent {AgentName} ({AgentId})",
            ticketId, agentName, agentId);

        return ticket;
    }

    /// <summary>
    /// Submits a human agent's response to a ticket.
    /// </summary>
    public void SubmitResponse(HumanResponse response)
    {
        if (!_tickets.TryGetValue(response.TicketId, out var ticket))
        {
            _logger.LogWarning("Response submitted for unknown ticket {TicketId}", response.TicketId);
            return;
        }

        // Add to conversation history
        ticket.ConversationHistory.Add(new ConversationMessage
        {
            Role = "agent",
            Content = $"[{response.AgentName}]: {response.Message}"
        });

        // If there's a waiter for this ticket, complete it
        if (_responseWaiters.TryRemove(response.TicketId, out var tcs))
        {
            tcs.TrySetResult(response);
        }

        _logger.LogInformation(
            "Response submitted for ticket {TicketId} by agent {AgentName}",
            response.TicketId, response.AgentName);
    }

    /// <summary>
    /// Waits for a human response to a ticket.
    /// </summary>
    public async Task<HumanResponse?> WaitForResponseAsync(
        string ticketId,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<HumanResponse>();

        _responseWaiters[ticketId] = tcs;

        try
        {
            await using var registration = cancellationToken.Register(
                () => tcs.TrySetCanceled(cancellationToken));

            return await tcs.Task;
        }
        finally
        {
            _responseWaiters.TryRemove(ticketId, out _);
        }
    }

    /// <summary>
    /// Resolves a ticket.
    /// </summary>
    public HandoffTicket? ResolveTicket(string ticketId, string resolution, string? notes)
    {
        if (!_tickets.TryGetValue(ticketId, out var ticket))
            return null;

        ticket.Status = TicketStatus.Resolved;
        ticket.Resolution = resolution;
        ticket.ResolutionNotes = notes;
        ticket.ResolvedAt = DateTimeOffset.UtcNow;

        // Clean up mappings
        _sessionToTicket.TryRemove(ticket.SessionId, out _);

        _logger.LogInformation(
            "Ticket {TicketId} resolved. Resolution: {Resolution}",
            ticketId, resolution);

        return ticket;
    }

    /// <summary>Gets a ticket by ID.</summary>
    public HandoffTicket? GetTicket(string ticketId) =>
        _tickets.TryGetValue(ticketId, out var ticket) ? ticket : null;

    /// <summary>Gets the active ticket for a session.</summary>
    public HandoffTicket? GetActiveTicketForSession(string sessionId) =>
        _sessionToTicket.TryGetValue(sessionId, out var ticketId) &&
        _tickets.TryGetValue(ticketId, out var ticket) &&
        ticket.Status == TicketStatus.Active
            ? ticket : null;

    /// <summary>Gets all pending tickets.</summary>
    public IEnumerable<HandoffTicket> GetPendingTickets() =>
        _tickets.Values
            .Where(t => t.Status == TicketStatus.Pending)
            .OrderBy(t => t.CreatedAt);

    /// <summary>Registers a human agent.</summary>
    public void RegisterAgent(string agentId, string name)
    {
        _agents[agentId] = new AgentInfo(agentId, name, AgentStatus.Available);
        _logger.LogInformation("Agent registered: {AgentName} ({AgentId})", name, agentId);
    }
}
```

### Updated ChatSession Model

Extend the existing `ChatSession` to support handoff state:

```csharp
// Update src/Orchestration/ChatSession.cs

using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Orchestration.Handoff;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Represents a complete chat session including user context, conversation history,
/// and any in-progress authentication or handoff flow.
/// </summary>
public class ChatSession
{
    /// <summary>Unique identifier for this session.</summary>
    public required string SessionId { get; set; }

    /// <summary>User context and authentication state.</summary>
    public required UserSessionContext UserContext { get; set; }

    /// <summary>History of messages in this conversation.</summary>
    public List<ConversationMessage> ConversationHistory { get; set; } = [];

    /// <summary>Active authentication session, if auth flow is in progress.</summary>
    public AuthSession? AuthSession { get; set; }

    /// <summary>
    /// Query that was pending before authentication started.
    /// Will be answered after successful authentication.
    /// </summary>
    public string? PendingQuery { get; set; }

    /// <summary>Current handoff ticket ID, if a handoff is in progress.</summary>
    public string? CurrentHandoffTicketId { get; set; }

    /// <summary>Current handoff state.</summary>
    public HandoffState HandoffState { get; set; } = HandoffState.None;

    /// <summary>When this session was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this session was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

### Updated RequiredAction Enum

Extend the existing `RequiredAction` enum in `OrchestratorModels.cs`:

```csharp
/// <summary>
/// Actions that may be required from the user after a response.
/// </summary>
public enum RequiredAction
{
    /// <summary>No action required, response is complete.</summary>
    None,

    /// <summary>Authentication flow is in progress, awaiting user input.</summary>
    AuthenticationInProgress,

    /// <summary>Authentication failed (locked out or max retries).</summary>
    AuthenticationFailed,

    /// <summary>Request requires human agent assistance.</summary>
    HumanHandoffNeeded,

    /// <summary>Question was unclear, clarification needed.</summary>
    ClarificationNeeded,

    /// <summary>Human handoff has been initiated, waiting for agent.</summary>
    HumanHandoffInitiated,

    /// <summary>Active conversation with a human agent.</summary>
    HumanConversationActive,

    /// <summary>Transfer to specialist in progress.</summary>
    TransferInProgress,

    /// <summary>Callback has been scheduled.</summary>
    CallbackScheduled
}
```

Optionally, extend `ChatResponse` to include ticket info:

```csharp
public class ChatResponse
{
    /// <summary>The response message to show the user.</summary>
    public required string Message { get; set; }

    /// <summary>The category the question was classified as.</summary>
    public QuestionCategory Category { get; set; }

    /// <summary>Any required action the user must take.</summary>
    public RequiredAction RequiredAction { get; set; }

    /// <summary>Handoff ticket ID, if a handoff was initiated.</summary>
    public string? TicketId { get; set; }
}
```

### Updated Orchestrator with Handoff Flow

Add the handoff methods to `ChatbotOrchestrator`:

```csharp
// Add to ChatbotOrchestrator.cs

using UtilityBillingChatbot.Agents.Summarization;
using UtilityBillingChatbot.Orchestration.Handoff;

public class ChatbotOrchestrator
{
    private readonly ClassifierAgent _classifierAgent;
    private readonly FAQAgent _faqAgent;
    private readonly AuthAgent _authAgent;
    private readonly UtilityDataAgent _utilityDataAgent;
    private readonly SummarizationAgent _summarizationAgent;
    private readonly IHandoffService _handoffService;
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<ChatbotOrchestrator> _logger;

    // ... existing fields and constructor updated to include new dependencies

    public ChatbotOrchestrator(
        ClassifierAgent classifierAgent,
        FAQAgent faqAgent,
        AuthAgent authAgent,
        UtilityDataAgent utilityDataAgent,
        SummarizationAgent summarizationAgent,
        IHandoffService handoffService,
        ISessionStore sessionStore,
        ILogger<ChatbotOrchestrator> logger)
    {
        _classifierAgent = classifierAgent;
        _faqAgent = faqAgent;
        _authAgent = authAgent;
        _utilityDataAgent = utilityDataAgent;
        _summarizationAgent = summarizationAgent;
        _handoffService = handoffService;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    // ... existing ProcessMessageAsync updated to check handoff state

    public async Task<ChatResponse> ProcessMessageAsync(
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var session = await GetOrCreateSessionAsync(sessionId, cancellationToken);
        session.UserContext.LastInteraction = DateTimeOffset.UtcNow;

        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = userMessage
        });

        ChatResponse response;

        try
        {
            // Check if in handoff state first
            if (session.HandoffState is HandoffState.WaitingForHuman or
                HandoffState.HumanConversationActive)
            {
                response = await HandleMessageDuringHandoffAsync(session, userMessage, cancellationToken);
            }
            // Check if in auth flow
            else if (session.AuthSession is not null &&
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

        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "assistant",
            Content = response.Message
        });

        await SaveSessionAsync(session, cancellationToken);
        return response;
    }

    /// <summary>
    /// Initiates a human handoff for the current session.
    /// </summary>
    public async Task<ChatResponse> InitiateHumanHandoffAsync(
        ChatSession session,
        string userMessage,
        string reason,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Initiating handoff for session {SessionId}. Reason: {Reason}",
            session.SessionId, reason);

        // Step 1: Generate conversation summary
        var conversationText = string.Join("\n",
            session.ConversationHistory.Select(m => $"{m.Role}: {m.Content}"));

        var summaryResponse = await _summarizationAgent.SummarizeAsync(
            conversation: conversationText,
            escalationReason: reason,
            currentQuestion: userMessage,
            cancellationToken: cancellationToken);

        // Step 2: Create handoff request
        var handoffRequest = new HumanHandoffRequest
        {
            SessionId = session.SessionId,
            ConversationSummary = summaryResponse.Summary,
            OriginalQuestion = userMessage,
            EscalationReason = reason,
            UserContext = session.UserContext,
            ConversationHistory = session.ConversationHistory,
            SuggestedDepartment = DetermineDepartment(reason)
        };

        // Step 3: Create ticket
        var ticketId = await _handoffService.CreateHandoffTicketAsync(
            handoffRequest, cancellationToken);

        // Step 4: Update session state
        session.CurrentHandoffTicketId = ticketId;
        session.HandoffState = HandoffState.WaitingForHuman;

        return new ChatResponse
        {
            Message = $"I've connected you to our support team. A representative will be with you shortly. " +
                     $"Your ticket number is {ticketId}. While you wait, is there anything else I can help clarify?",
            Category = QuestionCategory.HumanRequested,
            RequiredAction = RequiredAction.HumanHandoffInitiated,
            TicketId = ticketId
        };
    }

    /// <summary>
    /// Handles messages while a handoff is in progress.
    /// </summary>
    private async Task<ChatResponse> HandleMessageDuringHandoffAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        // If user wants to cancel waiting
        if (userMessage.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("nevermind", StringComparison.OrdinalIgnoreCase))
        {
            session.HandoffState = HandoffState.None;
            session.CurrentHandoffTicketId = null;

            return new ChatResponse
            {
                Message = "No problem! I've cancelled your request for a human agent. How else can I help you?",
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.None
            };
        }

        // Otherwise, acknowledge and continue waiting
        return new ChatResponse
        {
            Message = "I've noted your message and will pass it along to the support agent when they connect. " +
                     "Your ticket number is " + session.CurrentHandoffTicketId + ".",
            Category = QuestionCategory.HumanRequested,
            RequiredAction = RequiredAction.HumanHandoffInitiated,
            TicketId = session.CurrentHandoffTicketId
        };
    }

    /// <summary>
    /// Processes a response from a human agent.
    /// </summary>
    public async Task<ChatResponse> HandleHumanResponseAsync(
        string sessionId,
        HumanResponse humanResponse,
        CancellationToken cancellationToken)
    {
        var session = await GetOrCreateSessionAsync(sessionId, cancellationToken);

        session.HandoffState = HandoffState.HumanResponded;

        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "agent",
            Content = $"[Support Agent {humanResponse.AgentName}]: {humanResponse.Message}"
        });

        var response = humanResponse.Resolution switch
        {
            HandoffResolution.Resolved => new ChatResponse
            {
                Message = humanResponse.Message,
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.None
            },

            HandoffResolution.ContinueConversation => new ChatResponse
            {
                Message = humanResponse.Message +
                         "\n\n[You're now chatting with our support team. I'll be here if you need AI assistance again.]",
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.HumanConversationActive
            },

            HandoffResolution.TransferToSpecialist => new ChatResponse
            {
                Message = humanResponse.Message,
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.TransferInProgress
            },

            HandoffResolution.ScheduleCallback => new ChatResponse
            {
                Message = humanResponse.Message,
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.CallbackScheduled
            },

            _ => throw new InvalidOperationException($"Unknown resolution: {humanResponse.Resolution}")
        };

        if (humanResponse.Resolution == HandoffResolution.Resolved)
        {
            session.HandoffState = HandoffState.Resolved;
            session.CurrentHandoffTicketId = null;
        }
        else if (humanResponse.Resolution == HandoffResolution.ContinueConversation)
        {
            session.HandoffState = HandoffState.HumanConversationActive;
        }

        await SaveSessionAsync(session, cancellationToken);
        return response;
    }

    /// <summary>
    /// Determines which department should handle the request based on the reason.
    /// </summary>
    private static string DetermineDepartment(string reason)
    {
        var reasonLower = reason.ToLowerInvariant();

        if (reasonLower.Contains("payment") || reasonLower.Contains("balance") || reasonLower.Contains("bill"))
            return "Billing";

        if (reasonLower.Contains("outage") || reasonLower.Contains("meter") || reasonLower.Contains("service"))
            return "Field Services";

        if (reasonLower.Contains("start") || reasonLower.Contains("stop") || reasonLower.Contains("transfer"))
            return "New Service";

        if (reasonLower.Contains("disconnect") || reasonLower.Contains("shutoff"))
            return "Collections";

        return "General Support";
    }

    // Update existing handler methods to call InitiateHumanHandoffAsync

    private async Task<ChatResponse> HandleServiceRequestAsync(
        ChatSession session,
        string userMessage,
        CancellationToken cancellationToken)
    {
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
        return await InitiateHumanHandoffAsync(
            session,
            userMessage,
            "Customer requested human agent",
            cancellationToken);
    }
}
```

### Updated DI Registration

```csharp
// Update Infrastructure/ServiceCollectionExtensions.cs

using UtilityBillingChatbot.Agents.Summarization;
using UtilityBillingChatbot.Orchestration.Handoff;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUtilityBillingChatbot(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ... existing registrations ...

        // Add agents
        services.AddClassifierAgent();
        services.AddFAQAgent();
        services.AddAuthAgent();
        services.AddUtilityDataAgent();
        services.AddSummarizationAgent();  // NEW

        // Add handoff services
        services.AddHandoffServices();      // NEW

        // Add orchestration
        services.AddOrchestration();

        // Add the chatbot background service
        services.AddHostedService<ChatbotService>();

        return services;
    }
}
```

### Testing Stage 6

```csharp
// tests/SummarizationAgentTests.cs

public class SummarizationAgentTests
{
    private readonly SummarizationAgent _agent;

    public SummarizationAgentTests()
    {
        var chatClient = CreateTestChatClient();
        var logger = NullLogger<SummarizationAgent>.Instance;
        _agent = new SummarizationAgent(chatClient, logger);
    }

    [Fact]
    public async Task Summarize_CreatesCompleteSummary()
    {
        // Arrange
        var conversation = """
            user: Hi, I need help with my bill
            assistant: I'd be happy to help! Can I get your account number?
            user: It's ACC-2024-0042. My bill is way too high this month!
            assistant: I can see your usage increased significantly. Would you like me to explain the charges?
            user: This is ridiculous! I want to talk to a supervisor about this!
            """;

        // Act
        var response = await _agent.SummarizeAsync(
            conversation: conversation,
            escalationReason: "Customer requested supervisor",
            currentQuestion: "I want to talk to a supervisor");

        // Assert
        Assert.Contains("bill", response.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ACC-2024-0042", response.Summary);
        // Summary should detect frustrated mood
        Assert.True(
            response.Summary.Contains("frustrated", StringComparison.OrdinalIgnoreCase) ||
            response.Summary.Contains("upset", StringComparison.OrdinalIgnoreCase));
    }
}
```

```csharp
// tests/HandoffTests.cs

public class HandoffTests
{
    [Fact]
    public async Task Orchestrator_InitiatesHandoff_WhenHumanRequested()
    {
        // Arrange
        var orchestrator = CreateTestOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Build some conversation history
        await orchestrator.ProcessMessageAsync(sessionId, "Hi, I have a problem");

        // Act - request human agent
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "I need to speak to a real person");

        // Assert
        Assert.Equal(RequiredAction.HumanHandoffInitiated, response.RequiredAction);
        Assert.NotNull(response.TicketId);
        Assert.Contains("ticket", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Orchestrator_HandlesHumanResponse()
    {
        // Arrange
        var orchestrator = CreateTestOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Initiate handoff
        await orchestrator.ProcessMessageAsync(sessionId, "I need to speak to a representative");

        // Simulate human response
        var humanResponse = new HumanResponse(
            TicketId: "TEST123",
            AgentId: "agent-1",
            AgentName: "Maria",
            Message: "Hi, I'm Maria from Billing. I can help you set up a payment arrangement today.",
            Resolution: HandoffResolution.ContinueConversation,
            RespondedAt: DateTimeOffset.UtcNow);

        // Act
        var response = await orchestrator.HandleHumanResponseAsync(
            sessionId, humanResponse, CancellationToken.None);

        // Assert
        Assert.Contains("Maria", response.Message);
        Assert.Equal(RequiredAction.HumanConversationActive, response.RequiredAction);
    }

    [Fact]
    public async Task HandoffManager_CreatesAndTracksTickets()
    {
        // Arrange
        var manager = new HandoffManager(NullLogger<HandoffManager>.Instance);
        var request = new HumanHandoffRequest
        {
            SessionId = "session-123",
            ConversationSummary = "Customer has billing dispute",
            OriginalQuestion = "Why is my bill so high?",
            EscalationReason = "Customer requested human",
            UserContext = new UserSessionContext { SessionId = "session-123" }
        };

        // Act
        var ticketId = await manager.CreateTicketAsync(request);
        var ticket = manager.GetTicket(ticketId);

        // Assert
        Assert.NotNull(ticket);
        Assert.Equal(TicketStatus.Pending, ticket.Status);
        Assert.Equal("session-123", ticket.SessionId);
    }

    [Fact]
    public async Task HandoffManager_AgentCanClaimTicket()
    {
        // Arrange
        var manager = new HandoffManager(NullLogger<HandoffManager>.Instance);
        var ticketId = await manager.CreateTicketAsync(new HumanHandoffRequest
        {
            SessionId = "session-123",
            ConversationSummary = "Test",
            OriginalQuestion = "Test",
            EscalationReason = "Test",
            UserContext = new UserSessionContext { SessionId = "session-123" }
        });

        // Act
        var claimed = manager.ClaimTicket(ticketId, "agent-1", "Maria");

        // Assert
        Assert.NotNull(claimed);
        Assert.Equal(TicketStatus.Active, claimed.Status);
        Assert.Equal("Maria", claimed.AssignedAgentName);
    }
}
```

### Validation Checklist - Stage 6

- [ ] SummarizationAgent follows existing agent patterns (no factory)
- [ ] Summarization produces structured markdown summaries
- [ ] Summary includes user issue, AI attempts, and escalation reason
- [ ] Summary detects user emotional state
- [ ] HandoffService creates tickets with all required information
- [ ] HandoffManager tracks tickets and supports agent claims
- [ ] ChatSession extended with handoff state fields
- [ ] Orchestrator initiates handoff on ServiceRequest/HumanRequested
- [ ] Orchestrator handles messages during active handoff
- [ ] Human responses are processed and update session state
- [ ] DI registration follows `Add{Name}Agent()` / `Add{Name}Services()` pattern
- [ ] All new code has XML doc comments
- [ ] Unit tests cover core handoff scenarios

### Future Enhancements (Out of Scope)

For production deployment, consider:
- **Real-time communication**: Replace in-memory with SignalR hub for WebSocket support
- **Persistent storage**: Replace `HandoffManager` with Redis/SQL-backed implementation
- **Agent UI**: Build Blazor/React dashboard for human agents
- **Queue management**: Add priority queuing, SLA tracking, agent workload balancing
- **Analytics**: Track handoff metrics (time to claim, resolution time, etc.)

---
