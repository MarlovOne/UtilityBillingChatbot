## Stage 7: Session Persistence & Recovery

### Objective
Implement session serialization for persistence across service restarts and for horizontal scaling.

### Implementation

> **Note**: The `ISessionStore` interface is defined in the Overview document (Core Components - Service Abstractions).
> This section shows the serialization format and extended implementation details.

```csharp
/// <summary>
/// Serialization format for persisting ChatSession to storage.
/// Used internally by ISessionStore implementations.
/// </summary>
public class SerializedSession
{
    public string SessionId { get; set; } = string.Empty;
    public UserSessionContext UserContext { get; set; } = new();
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public string? PendingQuery { get; set; }
    public string? CurrentHandoffTicketId { get; set; }
    public HandoffState HandoffState { get; set; }
    public JsonElement? ClassifierAgentState { get; set; }
    public JsonElement? FAQAgentState { get; set; }
    public JsonElement? DataAgentState { get; set; }
    public DateTimeOffset LastInteraction { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Extended orchestrator with session persistence
/// </summary>
public partial class ChatbotOrchestrator
{
    private readonly ISessionStore _sessionStore;

    public async Task<ChatSession> GetOrCreateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Try to load from store
        var serialized = await _sessionStore.LoadSessionAsync(sessionId, cancellationToken);

        if (serialized != null)
        {
            return await DeserializeSessionAsync(serialized, cancellationToken);
        }

        // Create new session
        var session = new ChatSession
        {
            SessionId = sessionId,
            UserContext = new UserSessionContext { SessionId = sessionId },
            ConversationHistory = [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        await SaveSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task SaveSessionAsync(
        ChatSession session,
        CancellationToken cancellationToken)
    {
        session.UserContext.LastInteraction = DateTimeOffset.UtcNow;

        var serialized = new SerializedSession
        {
            SessionId = session.SessionId,
            UserContext = session.UserContext,
            ConversationHistory = session.ConversationHistory,
            PendingQuery = session.PendingQuery,
            CurrentHandoffTicketId = session.CurrentHandoffTicketId,
            HandoffState = session.HandoffState,
            LastInteraction = session.UserContext.LastInteraction,
            CreatedAt = session.CreatedAt
        };

        // Serialize agent sessions
        if (session.ClassifierSession != null)
        {
            serialized.ClassifierAgentState =
                _classifierFactory.CreateClassifierAgent().SerializeSession(session.ClassifierSession);
        }

        if (session.FAQSession != null)
        {
            serialized.FAQAgentState =
                _faqFactory.CreateFAQAgent().SerializeSession(session.FAQSession);
        }

        await _sessionStore.SaveSessionAsync(session.SessionId, serialized, cancellationToken);
        _sessionCache[session.SessionId] = session;
    }

    private async Task<ChatSession> DeserializeSessionAsync(
        SerializedSession serialized,
        CancellationToken cancellationToken)
    {
        var session = new ChatSession
        {
            SessionId = serialized.SessionId,
            UserContext = serialized.UserContext,
            ConversationHistory = serialized.ConversationHistory,
            PendingQuery = serialized.PendingQuery,
            CurrentHandoffTicketId = serialized.CurrentHandoffTicketId,
            HandoffState = serialized.HandoffState,
            CreatedAt = serialized.CreatedAt
        };

        // Restore agent sessions
        if (serialized.ClassifierAgentState.HasValue)
        {
            var classifier = _classifierFactory.CreateClassifierAgent();
            session.ClassifierSession = await classifier.DeserializeSessionAsync(
                serialized.ClassifierAgentState.Value);
        }

        if (serialized.FAQAgentState.HasValue)
        {
            var faq = _faqFactory.CreateFAQAgent();
            session.FAQSession = await faq.DeserializeSessionAsync(
                serialized.FAQAgentState.Value);
        }

        _sessionCache[session.SessionId] = session;
        return session;
    }
}

/// <summary>
/// Redis implementation for production use
/// </summary>
public class RedisSessionStore : ISessionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _sessionTtl = TimeSpan.FromHours(24);

    public async Task SaveSessionAsync(
        string sessionId,
        SerializedSession data,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(data);
        await db.StringSetAsync($"session:{sessionId}", json, _sessionTtl);
    }

    public async Task<SerializedSession?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync($"session:{sessionId}");

        if (json.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<SerializedSession>(json!);
    }

    public async Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"session:{sessionId}");
    }

    public async Task<IEnumerable<string>> GetActiveSessionIdsAsync(
        CancellationToken cancellationToken)
    {
        var server = _redis.GetServers().First();
        var keys = server.Keys(pattern: "session:*");
        return keys.Select(k => k.ToString().Replace("session:", ""));
    }
}
```

### Testing Stage 7

```csharp
public class SessionPersistenceTests
{
    [Fact]
    public async Task Session_SerializesAndDeserializes()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Build session with history
        await orchestrator.ProcessMessageAsync(sessionId, "Hello");
        await orchestrator.ProcessMessageAsync(sessionId, "What are my payment options?");

        // Authenticate via in-band flow
        await orchestrator.CompleteAuthenticationAsync(
            sessionId,
            accountNumber: "ACC-2024-0042",
            customerName: "Maria Garcia",
            tokenExpiry: DateTimeOffset.UtcNow.AddMinutes(30));

        // Act - Save session
        var session = orchestrator.GetSession(sessionId);
        await orchestrator.SaveSessionAsync(session!, CancellationToken.None);

        // Clear in-memory cache
        orchestrator.ClearCache();

        // Reload session
        var restored = await orchestrator.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

        // Assert
        Assert.Equal(sessionId, restored.SessionId);
        Assert.Equal(2, restored.ConversationHistory.Count);
        Assert.Equal("Maria Garcia", restored.UserContext.CustomerName);
        Assert.Equal(AuthenticationState.Authenticated, restored.UserContext.AuthState);
    }

    [Fact]
    public async Task Session_PreservesAgentState()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Build multi-turn context about payment assistance
        await orchestrator.ProcessMessageAsync(sessionId, "Do you have any assistance programs?");
        await orchestrator.ProcessMessageAsync(sessionId, "What if I can't pay my bill?");

        // Save and restore
        var session = orchestrator.GetSession(sessionId);
        await orchestrator.SaveSessionAsync(session!, CancellationToken.None);
        orchestrator.ClearCache();
        await orchestrator.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

        // Act - Continue conversation (should have context)
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "How do I apply for help?");

        // Assert - Should understand context (Q7: LIHEAP)
        Assert.Contains("LIHEAP", response.Message);
    }

    [Fact]
    public async Task Session_SurvivesRestart()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();

        // First orchestrator instance
        using (var orchestrator1 = CreateOrchestrator())
        {
            await orchestrator1.ProcessMessageAsync(sessionId, "Hi, I need help with my bill");
            await orchestrator1.CompleteAuthenticationAsync(
                sessionId,
                accountNumber: "ACC-2024-0099",
                customerName: "Jane Wilson",
                tokenExpiry: DateTimeOffset.UtcNow.AddMinutes(30));

            var session = orchestrator1.GetSession(sessionId);
            await orchestrator1.SaveSessionAsync(session!, CancellationToken.None);
        }

        // Second orchestrator instance (simulating restart)
        using (var orchestrator2 = CreateOrchestrator())
        {
            // Act
            var session = await orchestrator2.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

            // Assert - Session restored
            Assert.Equal("Jane Wilson", session.UserContext.CustomerName);
            Assert.Equal(AuthenticationState.Authenticated, session.UserContext.AuthState);
        }
    }
}
```

### Validation Checklist - Stage 7
- [ ] Sessions serialize to persistent storage correctly
- [ ] Sessions deserialize and restore state correctly
- [ ] Conversation history is preserved across restarts
- [ ] Authentication state is preserved correctly
- [ ] Agent sessions maintain their context after restore
- [ ] Pending queries are preserved and resumable
- [ ] Handoff state survives restarts
- [ ] Session TTL/expiration works correctly
- [ ] Multiple orchestrator instances share session state

---
