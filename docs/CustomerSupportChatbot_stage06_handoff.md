## Stage 6: Human Handoff with Summarization (WebSocket)

### Objective
Build the summarization agent and human handoff flow using WebSocket for real-time communication between customer, AI, and human agents.

### Architecture: WebSocket + RequestPort Pattern

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        HUMAN HANDOFF ARCHITECTURE                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Customer                    Server                      Human Agent     │
│  ┌──────┐                ┌───────────┐                   ┌──────┐       │
│  │ Web  │◄──WebSocket──►│ SignalR   │◄────WebSocket────►│ Agent│       │
│  │Client│                │   Hub     │                   │  UI  │       │
│  └──────┘                └─────┬─────┘                   └──────┘       │
│                                │                                         │
│                                ▼                                         │
│                    ┌────────────────────┐                               │
│                    │ Handoff Manager    │                               │
│                    │ - Ticket Queue     │                               │
│                    │ - Session Mapping  │                               │
│                    │ - Event Routing    │                               │
│                    └─────────┬──────────┘                               │
│                              │                                           │
│                    ┌─────────▼──────────┐                               │
│                    │ Summarization      │                               │
│                    │ Agent              │                               │
│                    └────────────────────┘                               │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Implementation - Summarization Agent

```csharp
public class SummarizationAgentFactory : ISummarizationAgentFactory
{
    private readonly IChatClient _chatClient;

    public SummarizationAgentFactory(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public AIAgent CreateSummarizationAgent()
    {
        const string instructions = """
            You are a conversation summarizer for customer support handoffs.
            Your job is to create a concise but complete summary for human agents.

            Include in your summary:
            1. User's main issue/question (1-2 sentences)
            2. What the AI tried (briefly)
            3. Why escalation is happening
            4. User's emotional state (if apparent)
            5. Any relevant account/order info mentioned

            Format:
            ## Issue Summary
            [Main issue]

            ## AI Attempts
            [What was tried]

            ## Escalation Reason
            [Why human needed]

            ## User Context
            - Name: [if known]
            - Account: [if authenticated]
            - Mood: [neutral/frustrated/urgent]

            ## Recommended Next Steps
            [Suggestions for human agent]

            Keep the summary under 200 words. Be factual and neutral in tone.
            """;

        return _chatClient.AsAIAgent(instructions: instructions);
    }
}
```

### Handoff Service

> **Note**: The `IHandoffService` interface is defined in the Overview document (Core Components - Service Abstractions).

```csharp
public class HumanHandoffService : IHandoffService
{
    private readonly IHandoffQueue _queue;
    private readonly INotificationService _notifications;

    public async Task<string> CreateHandoffTicketAsync(
        HumanHandoffRequest request,
        CancellationToken cancellationToken)
    {
        // Add to human agent queue
        await _queue.EnqueueAsync(request, cancellationToken);

        // Notify available agents
        await _notifications.NotifyAgentsAsync(
            $"New handoff ticket: {request.TicketId}",
            cancellationToken);

        return request.TicketId;
    }

    public async Task<HumanResponse?> WaitForHumanResponseAsync(
        string ticketId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            return await _queue.WaitForResponseAsync(ticketId, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return null; // Timeout
        }
    }

    public async Task NotifyCustomerAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken)
    {
        await _notifications.SendToSessionAsync(sessionId, message, cancellationToken);
    }
}

public class HumanResponse
{
    public string TicketId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public HandoffResolution Resolution { get; set; }
    public DateTimeOffset RespondedAt { get; set; }
}

public enum HandoffResolution
{
    Resolved,
    ContinueConversation,
    TransferToSpecialist,
    ScheduleCallback
}
```

### Updated Orchestrator with Full Handoff Flow

```csharp
public partial class ChatbotOrchestrator
{
    private readonly SummarizationAgentFactory _summarizationFactory;
    private readonly IHandoffService _handoffService;

    public async Task<ChatResponse> InitiateHumanHandoffAsync(
        string message,
        ChatSession session,
        string reason,
        CancellationToken cancellationToken)
    {
        // Step 1: Generate conversation summary
        var summarizer = _summarizationFactory.CreateSummarizationAgent();
        var summarySession = await summarizer.CreateSessionAsync();

        var conversationText = string.Join("\n",
            session.ConversationHistory.Select(m => $"{m.Role}: {m.Content}"));

        var summaryPrompt = $"""
            Summarize this customer support conversation for handoff to a human agent.

            Escalation reason: {reason}
            Current user question: {message}

            Conversation:
            {conversationText}
            """;

        var summaryResponse = await summarizer.RunAsync(
            summaryPrompt,
            summarySession,
            cancellationToken: cancellationToken);

        // Step 2: Create handoff request
        var handoffRequest = new HumanHandoffRequest
        {
            ConversationSummary = summaryResponse.Text,
            OriginalQuestion = message,
            EscalationReason = reason,
            UserContext = session.UserContext,
            ConversationHistory = session.ConversationHistory,
            SuggestedDepartment = DetermineDepartment(session, reason)
        };

        // Step 3: Create ticket and queue for human
        var ticketId = await _handoffService.CreateHandoffTicketAsync(
            handoffRequest,
            cancellationToken);

        // Store ticket ID in session for tracking
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
    /// Called when human agent responds to handoff
    /// </summary>
    public async Task<ChatResponse> HandleHumanResponseAsync(
        string sessionId,
        HumanResponse humanResponse,
        CancellationToken cancellationToken)
    {
        if (!_sessionCache.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException("Session not found");
        }

        // Update session state
        session.HandoffState = HandoffState.HumanResponded;

        // Add human response to history
        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "agent", // Distinguish from AI assistant
            Content = $"[Support Agent {humanResponse.AgentName}]: {humanResponse.Message}",
            Timestamp = humanResponse.RespondedAt
        });

        return humanResponse.Resolution switch
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
    }

    /// <summary>
    /// Polls for human response (for non-realtime integrations)
    /// </summary>
    public async Task<ChatResponse?> PollHandoffStatusAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!_sessionCache.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        if (session.HandoffState != HandoffState.WaitingForHuman ||
            string.IsNullOrEmpty(session.CurrentHandoffTicketId))
        {
            return null;
        }

        var response = await _handoffService.WaitForHumanResponseAsync(
            session.CurrentHandoffTicketId,
            TimeSpan.FromSeconds(5), // Short poll interval
            cancellationToken);

        if (response != null)
        {
            return await HandleHumanResponseAsync(sessionId, response, cancellationToken);
        }

        return null; // No response yet
    }

    private string DetermineDepartment(ChatSession session, string reason)
    {
        // Simple routing logic - could be enhanced with ML
        if (reason.Contains("payment", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("balance", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("bill", StringComparison.OrdinalIgnoreCase))
            return "Billing";
        if (reason.Contains("outage", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("meter", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("service", StringComparison.OrdinalIgnoreCase))
            return "Field Services";
        if (reason.Contains("start", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("stop", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("transfer", StringComparison.OrdinalIgnoreCase))
            return "New Service";
        if (reason.Contains("disconnect", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("shutoff", StringComparison.OrdinalIgnoreCase))
            return "Collections";

        return "General Support";
    }
}

// Extended session model
public partial class ChatSession
{
    public string? CurrentHandoffTicketId { get; set; }
    public HandoffState HandoffState { get; set; } = HandoffState.None;
}

public enum HandoffState
{
    None,
    WaitingForHuman,
    HumanResponded,
    HumanConversationActive,
    Resolved
}

// Note: RequiredAction enum is defined in Stage 5 (Orchestrator section)
// with all values including: HumanConversationActive, TransferInProgress, CallbackScheduled
```

### WebSocket Hub (SignalR)

```csharp
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// SignalR hub for real-time communication between customers and human agents.
/// Handles message routing, typing indicators, and connection management.
/// </summary>
public class ChatHub : Hub
{
    private readonly HandoffManager _handoffManager;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(HandoffManager handoffManager, ILogger<ChatHub> logger)
    {
        _handoffManager = handoffManager;
        _logger = logger;
    }

    // ========== Customer Methods ==========

    /// <summary>
    /// Customer joins their chat session
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _handoffManager.RegisterCustomerConnection(sessionId, Context.ConnectionId);
        _logger.LogInformation("Customer joined session {SessionId}", sessionId);
    }

    /// <summary>
    /// Customer sends a message (during handoff, goes to human agent)
    /// </summary>
    public async Task SendMessage(string sessionId, string message)
    {
        var ticket = _handoffManager.GetActiveTicket(sessionId);

        if (ticket != null && ticket.AssignedAgentId != null)
        {
            // Route to assigned human agent
            await Clients.Group($"agent:{ticket.AssignedAgentId}")
                .SendAsync("CustomerMessage", new
                {
                    TicketId = ticket.TicketId,
                    SessionId = sessionId,
                    Message = message,
                    Timestamp = DateTimeOffset.UtcNow
                });
        }
    }

    // ========== Human Agent Methods ==========

    /// <summary>
    /// Human agent joins the agent pool
    /// </summary>
    public async Task AgentJoin(string agentId, string agentName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "agents");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{agentId}");
        _handoffManager.RegisterAgent(agentId, agentName, Context.ConnectionId);

        // Send pending tickets to new agent
        var pendingTickets = _handoffManager.GetPendingTickets();
        await Clients.Caller.SendAsync("PendingTickets", pendingTickets);

        _logger.LogInformation("Agent {AgentName} ({AgentId}) connected", agentName, agentId);
    }

    /// <summary>
    /// Human agent claims a ticket
    /// </summary>
    public async Task ClaimTicket(string ticketId, string agentId)
    {
        var ticket = _handoffManager.ClaimTicket(ticketId, agentId);

        if (ticket != null)
        {
            // Notify customer that agent has joined
            await Clients.Group($"session:{ticket.SessionId}")
                .SendAsync("AgentJoined", new
                {
                    AgentName = ticket.AssignedAgentName,
                    Message = $"Hi! I'm {ticket.AssignedAgentName} from customer support. I've reviewed your conversation and I'm here to help."
                });

            // Notify all agents that ticket is claimed
            await Clients.Group("agents")
                .SendAsync("TicketClaimed", new { TicketId = ticketId, AgentId = agentId });

            // Send full context to claiming agent
            await Clients.Caller.SendAsync("TicketDetails", ticket);
        }
    }

    /// <summary>
    /// Human agent sends message to customer
    /// </summary>
    public async Task AgentMessage(string ticketId, string message)
    {
        var ticket = _handoffManager.GetTicket(ticketId);

        if (ticket != null)
        {
            await Clients.Group($"session:{ticket.SessionId}")
                .SendAsync("AgentMessage", new
                {
                    AgentName = ticket.AssignedAgentName,
                    Message = message,
                    Timestamp = DateTimeOffset.UtcNow
                });

            // Record in history
            _handoffManager.AddMessage(ticketId, "agent", message);
        }
    }

    /// <summary>
    /// Human agent resolves the ticket
    /// </summary>
    public async Task ResolveTicket(string ticketId, string resolution, string? notes)
    {
        var ticket = _handoffManager.ResolveTicket(ticketId, resolution, notes);

        if (ticket != null)
        {
            // Notify customer
            await Clients.Group($"session:{ticket.SessionId}")
                .SendAsync("ConversationResolved", new
                {
                    Resolution = resolution,
                    Message = "Is there anything else I can help you with today?"
                });

            // Notify agents
            await Clients.Group("agents")
                .SendAsync("TicketResolved", new { TicketId = ticketId });
        }
    }

    /// <summary>
    /// Handle disconnection
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _handoffManager.HandleDisconnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
```

### Handoff Manager (In-Memory for Prototyping)

```csharp
/// <summary>
/// Manages handoff tickets and routing between customers and human agents.
/// In-memory implementation for prototyping - replace with Redis/database for production.
/// </summary>
public class HandoffManager
{
    private readonly ConcurrentDictionary<string, HandoffTicket> _tickets = new();
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();
    private readonly ConcurrentDictionary<string, string> _sessionToTicket = new();
    private readonly ConcurrentDictionary<string, string> _connectionToSession = new();
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<HandoffManager> _logger;

    public HandoffManager(IHubContext<ChatHub> hubContext, ILogger<HandoffManager> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Create a new handoff ticket from the orchestrator
    /// </summary>
    public async Task<string> CreateTicketAsync(HumanHandoffRequest request)
    {
        var ticket = new HandoffTicket
        {
            TicketId = request.TicketId,
            SessionId = request.UserContext.SessionId,
            CustomerName = request.UserContext.UserName,
            Summary = request.ConversationSummary,
            EscalationReason = request.EscalationReason,
            ConversationHistory = request.ConversationHistory,
            SuggestedDepartment = request.SuggestedDepartment,
            Status = TicketStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _tickets[ticket.TicketId] = ticket;
        _sessionToTicket[request.UserContext.SessionId] = ticket.TicketId;

        // Notify all connected agents about new ticket
        await _hubContext.Clients.Group("agents")
            .SendAsync("NewTicket", new
            {
                ticket.TicketId,
                ticket.CustomerName,
                ticket.Summary,
                ticket.EscalationReason,
                ticket.SuggestedDepartment,
                ticket.CreatedAt
            });

        _logger.LogInformation("Created handoff ticket {TicketId} for session {SessionId}",
            ticket.TicketId, ticket.SessionId);

        return ticket.TicketId;
    }

    public HandoffTicket? ClaimTicket(string ticketId, string agentId)
    {
        if (!_tickets.TryGetValue(ticketId, out var ticket))
            return null;

        if (ticket.Status != TicketStatus.Pending)
            return null;

        if (!_agents.TryGetValue(agentId, out var agent))
            return null;

        ticket.AssignedAgentId = agentId;
        ticket.AssignedAgentName = agent.Name;
        ticket.Status = TicketStatus.Active;
        ticket.AssignedAt = DateTimeOffset.UtcNow;

        return ticket;
    }

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

        return ticket;
    }

    public void RegisterAgent(string agentId, string name, string connectionId)
    {
        _agents[agentId] = new AgentInfo
        {
            AgentId = agentId,
            Name = name,
            ConnectionId = connectionId,
            Status = AgentStatus.Available
        };
    }

    public void RegisterCustomerConnection(string sessionId, string connectionId)
    {
        _connectionToSession[connectionId] = sessionId;
    }

    public HandoffTicket? GetActiveTicket(string sessionId) =>
        _sessionToTicket.TryGetValue(sessionId, out var ticketId) &&
        _tickets.TryGetValue(ticketId, out var ticket) &&
        ticket.Status == TicketStatus.Active
            ? ticket : null;

    public HandoffTicket? GetTicket(string ticketId) =>
        _tickets.TryGetValue(ticketId, out var ticket) ? ticket : null;

    public IEnumerable<HandoffTicket> GetPendingTickets() =>
        _tickets.Values.Where(t => t.Status == TicketStatus.Pending)
            .OrderBy(t => t.CreatedAt);

    public void AddMessage(string ticketId, string role, string content)
    {
        if (_tickets.TryGetValue(ticketId, out var ticket))
        {
            ticket.ConversationHistory.Add(new ConversationMessage
            {
                Role = role,
                Content = content,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    public void HandleDisconnection(string connectionId)
    {
        // Handle customer disconnection
        if (_connectionToSession.TryRemove(connectionId, out var sessionId))
        {
            _logger.LogInformation("Customer disconnected from session {SessionId}", sessionId);
        }

        // Handle agent disconnection
        var agent = _agents.Values.FirstOrDefault(a => a.ConnectionId == connectionId);
        if (agent != null)
        {
            _agents.TryRemove(agent.AgentId, out _);
            _logger.LogInformation("Agent {AgentId} disconnected", agent.AgentId);
        }
    }
}

public class HandoffTicket
{
    public string TicketId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string EscalationReason { get; set; } = string.Empty;
    public string? SuggestedDepartment { get; set; }
    public TicketStatus Status { get; set; }
    public string? AssignedAgentId { get; set; }
    public string? AssignedAgentName { get; set; }
    public string? Resolution { get; set; }
    public string? ResolutionNotes { get; set; }
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public enum TicketStatus { Pending, Active, Resolved, Abandoned }

public class AgentInfo
{
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public AgentStatus Status { get; set; }
}

public enum AgentStatus { Available, Busy, Away }
```

### Program.cs Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add SignalR
builder.Services.AddSignalR();

// Add chat client (configure your provider)
builder.Services.AddSingleton<IChatClient>(sp =>
{
    // Configure OpenAI, Azure OpenAI, or other provider
    return new OpenAIChatClient("gpt-4o", Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
});

// Add mock data layer (replace with real CIS integration in production)
builder.Services.AddSingleton<MockCISDatabase>();

// Register agent factories via interfaces (enables testing and swapping implementations)
builder.Services.AddSingleton<IClassifierAgentFactory, ClassifierAgentFactory>();
builder.Services.AddSingleton<IFAQAgentFactory, FAQAgentFactory>();
builder.Services.AddSingleton<IInBandAuthAgentFactory, InBandAuthAgentFactory>();
builder.Services.AddSingleton<IUtilityDataAgentFactory, UtilityDataAgentFactory>();
builder.Services.AddSingleton<ISummarizationAgentFactory, SummarizationAgentFactory>();

// Register session store (use Redis/SQL implementation in production)
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();

// Register handoff services
builder.Services.AddSingleton<IHandoffService, HumanHandoffService>();
builder.Services.AddSingleton<HandoffManager>();

// Register orchestrator
builder.Services.AddSingleton<ChatbotOrchestrator>();

var app = builder.Build();

// Map SignalR hub
app.MapHub<ChatHub>("/chat");

app.Run();
```

### Testing Stage 6

```csharp
public class HandoffTests
{
    [Fact]
    public async Task Summarizer_CreatesCompleteSummary()
    {
        // Arrange
        var factory = new SummarizationAgentFactory(_chatClient);
        var agent = factory.CreateSummarizationAgent();
        var session = await agent.CreateSessionAsync();

        var conversation = """
            user: Hi, I need help with my bill
            assistant: I'd be happy to help! Can I get your account number?
            user: It's ACC-2024-0042. My bill is way too high this month!
            assistant: I can see your usage increased significantly. Would you like me to explain the charges?
            user: This is ridiculous! I want to talk to a supervisor about this!
            """;

        // Act
        var response = await agent.RunAsync(
            $"Summarize this conversation:\n{conversation}",
            session);

        // Assert
        Assert.Contains("high bill", response.Text.ToLower());
        Assert.Contains("ACC-2024-0042", response.Text);
        Assert.Contains("frustrated", response.Text.ToLower()); // Should detect mood
    }

    [Fact]
    public async Task Orchestrator_CreatesHandoffTicket()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Build some history - Q17: Payment arrangement request
        await orchestrator.ProcessMessageAsync(sessionId, "Hi, I have a problem");
        await orchestrator.ProcessMessageAsync(sessionId, "I can't pay my bill right now and I'm worried about disconnection");

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "I need to set up a payment arrangement with someone");

        // Assert
        Assert.Equal(RequiredAction.HumanHandoffInitiated, response.RequiredAction);
        Assert.NotNull(response.TicketId);
    }

    [Fact]
    public async Task Orchestrator_ProcessesHumanResponse()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Initiate handoff
        await orchestrator.ProcessMessageAsync(sessionId, "I need to speak to a representative");

        // Simulate human response
        var humanResponse = new HumanResponse
        {
            AgentName = "Maria",
            Message = "Hi, I'm Maria from Billing. I can help you set up a payment arrangement today.",
            Resolution = HandoffResolution.ContinueConversation,
            RespondedAt = DateTimeOffset.UtcNow
        };

        // Act
        var response = await orchestrator.HandleHumanResponseAsync(
            sessionId, humanResponse, CancellationToken.None);

        // Assert
        Assert.Contains("Maria", response.Message);
        Assert.Equal(RequiredAction.HumanConversationActive, response.RequiredAction);
    }

    [Fact]
    public async Task HandoffService_QueuesAndWaitsForResponse()
    {
        // Arrange
        var handoffService = new HumanHandoffService(_queue, _notifications);
        var request = new HumanHandoffRequest
        {
            ConversationSummary = "Test summary",
            OriginalQuestion = "Test question"
        };

        // Act
        var ticketId = await handoffService.CreateHandoffTicketAsync(request, CancellationToken.None);

        // Simulate human response in background
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            await _queue.RespondToTicketAsync(ticketId, new HumanResponse
            {
                Message = "I can help",
                Resolution = HandoffResolution.Resolved
            });
        });

        var response = await handoffService.WaitForHumanResponseAsync(
            ticketId,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("I can help", response.Message);
    }
}
```

### Validation Checklist - Stage 6
- [ ] Summarization agent creates complete, structured summaries
- [ ] Summary includes user issue, AI attempts, and escalation reason
- [ ] Summary correctly detects user emotional state
- [ ] Handoff tickets are created with all required information
- [ ] Human agents receive notifications for new tickets
- [ ] Customers receive ticket number and queue position
- [ ] Human responses are delivered to customers
- [ ] Timeout handling works when no human available
- [ ] Conversation history is preserved for human agent review
- [ ] Resolution types are handled correctly (Resolved, Continue, Transfer, Callback)

---
