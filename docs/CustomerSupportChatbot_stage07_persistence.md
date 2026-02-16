## Stage 7: Session Persistence & Recovery

### Objective
Implement session serialization for persistence across service restarts and for horizontal scaling.

### Current State
The codebase already has:
- `ISessionStore` interface in `Orchestration/ISessionStore.cs`
- `InMemorySessionStore` implementation (single-instance only)
- `ChatSession` model with `SessionId`, `UserContext`, `ConversationHistory`, `AuthSession`, `PendingQuery`
- `AuthenticationContextProvider.Serialize()` method for auth state persistence
- Orchestrator's `GetOrCreateSessionAsync` and `SaveSessionAsync` (in-memory only)

### Implementation

#### 1. Serialization Format

The challenge: `AuthSession` contains non-serializable objects (`AIAgent`, `AgentSession`).
We serialize only the `AuthenticationContextProvider` state and recreate the agent on restore.

```csharp
// File: src/Orchestration/SerializedSession.cs

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// JSON-serializable format for persisting ChatSession to external storage.
/// </summary>
public class SerializedSession
{
    public string SessionId { get; set; } = string.Empty;
    public SerializedUserContext UserContext { get; set; } = new();
    public List<SerializedMessage> ConversationHistory { get; set; } = [];
    public string? PendingQuery { get; set; }

    /// <summary>
    /// Serialized AuthenticationContextProvider state (from Provider.Serialize()).
    /// Null if no auth session exists.
    /// </summary>
    public JsonElement? AuthProviderState { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Serializable subset of UserSessionContext.
/// </summary>
public class SerializedUserContext
{
    public string SessionId { get; set; } = string.Empty;
    public string AuthState { get; set; } = "Anonymous";
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTimeOffset? SessionExpiry { get; set; }
    public DateTimeOffset LastInteraction { get; set; }
}

/// <summary>
/// Serializable conversation message.
/// </summary>
public class SerializedMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
```

#### 2. Session Serializer

```csharp
// File: src/Orchestration/SessionSerializer.cs

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Converts between ChatSession (runtime) and SerializedSession (storage).
/// </summary>
public class SessionSerializer
{
    private readonly AuthAgent _authAgent;

    public SessionSerializer(AuthAgent authAgent)
    {
        _authAgent = authAgent;
    }

    public SerializedSession Serialize(ChatSession session)
    {
        var serialized = new SerializedSession
        {
            SessionId = session.SessionId,
            UserContext = new SerializedUserContext
            {
                SessionId = session.UserContext.SessionId,
                AuthState = session.UserContext.AuthState.ToString(),
                CustomerId = session.UserContext.CustomerId,
                CustomerName = session.UserContext.CustomerName,
                SessionExpiry = session.UserContext.SessionExpiry,
                LastInteraction = session.UserContext.LastInteraction
            },
            ConversationHistory = session.ConversationHistory
                .Select(m => new SerializedMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    Timestamp = m.Timestamp
                })
                .ToList(),
            PendingQuery = session.PendingQuery,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt
        };

        // Serialize auth provider state if auth session exists
        if (session.AuthSession?.Provider != null)
        {
            serialized.AuthProviderState = session.AuthSession.Provider.Serialize();
        }

        return serialized;
    }

    public async Task<ChatSession> DeserializeAsync(
        SerializedSession serialized,
        CancellationToken cancellationToken)
    {
        var session = new ChatSession
        {
            SessionId = serialized.SessionId,
            UserContext = new UserSessionContext
            {
                SessionId = serialized.UserContext.SessionId,
                AuthState = Enum.Parse<AuthenticationState>(serialized.UserContext.AuthState),
                CustomerId = serialized.UserContext.CustomerId,
                CustomerName = serialized.UserContext.CustomerName,
                SessionExpiry = serialized.UserContext.SessionExpiry,
                LastInteraction = serialized.UserContext.LastInteraction
            },
            ConversationHistory = serialized.ConversationHistory
                .Select(m => new ConversationMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    Timestamp = m.Timestamp
                })
                .ToList(),
            PendingQuery = serialized.PendingQuery,
            CreatedAt = serialized.CreatedAt,
            UpdatedAt = serialized.UpdatedAt
        };

        // Restore auth session if provider state exists
        if (serialized.AuthProviderState.HasValue)
        {
            session.AuthSession = await _authAgent.RestoreSessionAsync(
                serialized.AuthProviderState.Value,
                cancellationToken);
        }

        return session;
    }
}
```

#### 3. AuthAgent Extension for Session Restore

```csharp
// Add to: src/Agents/Auth/AuthAgent.cs

/// <summary>
/// Restores an authentication session from serialized provider state.
/// </summary>
public async Task<AuthSession> RestoreSessionAsync(
    JsonElement providerState,
    CancellationToken cancellationToken = default)
{
    // Create new agent with the serialized state injected via factory
    var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
    {
        Name = "AuthAgent",
        AIContextProviderFactory = (ctx, ct) =>
        {
            // Always restore from the provided state
            return new ValueTask<AIContextProvider>(
                new AuthenticationContextProvider(_cisDatabase, providerState, _providerLogger));
        }
    });

    // Create a new AgentSession - the provider state is restored via factory
    var agentSession = await agent.CreateSessionAsync(cancellationToken);

    // Get the restored provider
    var restoredProvider = new AuthenticationContextProvider(_cisDatabase, providerState, _providerLogger);

    return new AuthSession(agent, agentSession, restoredProvider);
}
```

#### 4. Update ISessionStore Interface

Keep the existing interface but ensure implementations work with serialized JSON:

```csharp
// File: src/Orchestration/ISessionStore.cs (no changes needed)
// The interface already works with ChatSession directly.
// Serialization happens in a layer above (PersistentSessionStore wrapper).
```

#### 5. File-Based Session Store (Development)

For local development without external infrastructure:

```csharp
// File: src/Orchestration/FileSessionStore.cs

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// File-based session persistence for development and single-instance deployments.
/// Sessions are stored as JSON files in a configured directory.
/// </summary>
public class FileSessionStore : ISessionStore
{
    private readonly string _sessionDirectory;
    private readonly SessionSerializer _serializer;
    private readonly ILogger<FileSessionStore> _logger;
    private readonly TimeSpan _sessionTtl;

    public FileSessionStore(
        SessionSerializer serializer,
        ILogger<FileSessionStore> logger,
        IOptions<SessionStoreOptions>? options = null)
    {
        _serializer = serializer;
        _logger = logger;
        _sessionTtl = options?.Value.SessionTtl ?? TimeSpan.FromHours(24);
        _sessionDirectory = options?.Value.FileStorePath
            ?? Path.Combine(Path.GetTempPath(), "utility-chatbot-sessions");

        Directory.CreateDirectory(_sessionDirectory);
    }

    public async Task<ChatSession?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(sessionId);

        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var serialized = JsonSerializer.Deserialize<SerializedSession>(json);

            if (serialized == null)
                return null;

            // Check TTL
            if (DateTimeOffset.UtcNow - serialized.UpdatedAt > _sessionTtl)
            {
                _logger.LogInformation("Session {SessionId} expired, deleting", sessionId);
                await DeleteSessionAsync(sessionId, cancellationToken);
                return null;
            }

            return await _serializer.DeserializeAsync(serialized, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task SaveSessionAsync(
        ChatSession session,
        CancellationToken cancellationToken = default)
    {
        var serialized = _serializer.Serialize(session);
        var json = JsonSerializer.Serialize(serialized, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var filePath = GetFilePath(session.SessionId);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        _logger.LogDebug("Saved session {SessionId} to {Path}", session.SessionId, filePath);
    }

    public Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(sessionId);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogDebug("Deleted session {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    private string GetFilePath(string sessionId)
    {
        // Sanitize session ID for filesystem
        var safeId = string.Concat(sessionId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_sessionDirectory, $"{safeId}.json");
    }
}

/// <summary>
/// Configuration options for session storage.
/// </summary>
public class SessionStoreOptions
{
    public const string SectionName = "SessionStore";

    /// <summary>
    /// Storage provider: "InMemory", "File", or "SqlServer" (future).
    /// </summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Path for file-based storage. Defaults to temp directory.
    /// </summary>
    public string? FileStorePath { get; set; }

    /// <summary>
    /// Session time-to-live before expiration.
    /// </summary>
    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Connection string for SQL Server storage (future).
    /// </summary>
    public string? SqlConnectionString { get; set; }
}
```

#### 6. Update DI Registration

```csharp
// File: src/Orchestration/OrchestrationExtensions.cs

public static IServiceCollection AddOrchestration(this IServiceCollection services)
{
    services.AddSingleton<SessionSerializer>();

    // Register session store based on configuration
    services.AddSingleton<ISessionStore>(sp =>
    {
        var options = sp.GetService<IOptions<SessionStoreOptions>>();
        var provider = options?.Value.Provider ?? "InMemory";

        return provider switch
        {
            "File" => new FileSessionStore(
                sp.GetRequiredService<SessionSerializer>(),
                sp.GetRequiredService<ILogger<FileSessionStore>>(),
                options),

            // "SqlServer" => future implementation

            _ => new InMemorySessionStore()
        };
    });

    services.AddSingleton<ChatbotOrchestrator>();
    return services;
}
```

#### 7. Configuration

```json
// appsettings.json
{
  "SessionStore": {
    "Provider": "File",
    "FileStorePath": "./sessions",
    "SessionTtl": "24:00:00"
  }
}

// appsettings.Development.json - use in-memory for fast iteration
{
  "SessionStore": {
    "Provider": "InMemory"
  }
}
```

### Future: SQL Server Implementation

When infrastructure is available, add SQL Server support:

```csharp
// File: src/Orchestration/SqlSessionStore.cs (future)

/// <summary>
/// SQL Server session persistence for production deployments.
/// Requires a Sessions table with SessionId, Data (nvarchar(max)), UpdatedAt columns.
/// </summary>
public class SqlSessionStore : ISessionStore
{
    // Implementation uses Dapper or EF Core
    // Table schema:
    // CREATE TABLE Sessions (
    //     SessionId NVARCHAR(100) PRIMARY KEY,
    //     Data NVARCHAR(MAX) NOT NULL,
    //     CreatedAt DATETIMEOFFSET NOT NULL,
    //     UpdatedAt DATETIMEOFFSET NOT NULL,
    //     INDEX IX_Sessions_UpdatedAt (UpdatedAt)
    // );
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
        var services = CreateServices(provider: "File");
        var orchestrator = services.GetRequiredService<ChatbotOrchestrator>();
        var sessionId = Guid.NewGuid().ToString();

        // Build session with history
        await orchestrator.ProcessMessageAsync(sessionId, "Hello", CancellationToken.None);
        await orchestrator.ProcessMessageAsync(sessionId, "What are my payment options?", CancellationToken.None);

        // Simulate authentication
        var session = await GetSessionAsync(orchestrator, sessionId);
        session.UserContext.AuthState = AuthenticationState.Authenticated;
        session.UserContext.CustomerName = "Maria Garcia";
        session.UserContext.CustomerId = "ACC-2024-0042";

        // Act - Clear in-memory cache (simulates restart)
        orchestrator.ClearCache();

        // Reload session from file store
        var restored = await orchestrator.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

        // Assert
        Assert.Equal(sessionId, restored.SessionId);
        Assert.Equal(2, restored.ConversationHistory.Count);
        Assert.Equal("Maria Garcia", restored.UserContext.CustomerName);
        Assert.Equal(AuthenticationState.Authenticated, restored.UserContext.AuthState);
    }

    [Fact]
    public async Task Session_PreservesAuthProviderState()
    {
        // Arrange
        var services = CreateServices(provider: "File");
        var orchestrator = services.GetRequiredService<ChatbotOrchestrator>();
        var sessionId = Guid.NewGuid().ToString();

        // Start auth flow
        await orchestrator.ProcessMessageAsync(sessionId, "What's my balance?", CancellationToken.None);
        // User provides phone number
        await orchestrator.ProcessMessageAsync(sessionId, "555-123-4567", CancellationToken.None);

        // Get session - should be in Verifying state
        var session = await GetSessionAsync(orchestrator, sessionId);
        Assert.Equal(AuthenticationState.Verifying, session.AuthSession?.Provider.AuthState);

        // Act - Clear cache (simulates restart)
        orchestrator.ClearCache();

        // Reload and continue auth
        var restored = await orchestrator.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

        // Assert - Auth state preserved
        Assert.NotNull(restored.AuthSession);
        Assert.Equal(AuthenticationState.Verifying, restored.AuthSession.Provider.AuthState);
        Assert.NotNull(restored.AuthSession.Provider.CustomerName); // Customer was looked up
    }

    [Fact]
    public async Task Session_ExpiresAfterTtl()
    {
        // Arrange - use short TTL for testing
        var options = Options.Create(new SessionStoreOptions
        {
            Provider = "File",
            SessionTtl = TimeSpan.FromMilliseconds(100)
        });
        var services = CreateServices(options);
        var orchestrator = services.GetRequiredService<ChatbotOrchestrator>();
        var sessionId = Guid.NewGuid().ToString();

        await orchestrator.ProcessMessageAsync(sessionId, "Hello", CancellationToken.None);
        orchestrator.ClearCache();

        // Wait for expiry
        await Task.Delay(150);

        // Act - Session should be expired
        var session = await orchestrator.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

        // Assert - New session created (empty history)
        Assert.Empty(session.ConversationHistory);
    }

    [Fact]
    public async Task FileStore_HandlesInvalidJson()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-sessions-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var options = Options.Create(new SessionStoreOptions
            {
                Provider = "File",
                FileStorePath = tempDir
            });
            var services = CreateServices(options);
            var store = services.GetRequiredService<ISessionStore>();

            // Write invalid JSON
            var sessionId = "test-invalid";
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, $"{sessionId}.json"),
                "not valid json {{{");

            // Act
            var session = await store.GetSessionAsync(sessionId, CancellationToken.None);

            // Assert - Returns null, doesn't throw
            Assert.Null(session);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
```

### Validation Checklist - Stage 7
- [ ] `SerializedSession` class captures all session state
- [ ] `SessionSerializer` correctly converts to/from `ChatSession`
- [ ] `AuthAgent.RestoreSessionAsync` recreates auth session from provider state
- [ ] `FileSessionStore` persists sessions to JSON files
- [ ] Sessions survive application restarts (file store)
- [ ] Conversation history is preserved correctly
- [ ] Authentication state (including partial auth flow) is preserved
- [ ] `AuthenticationContextProvider` state survives serialization round-trip
- [ ] Pending queries are preserved and resumable
- [ ] Session TTL/expiration works correctly
- [ ] Invalid/corrupted session files are handled gracefully
- [ ] Configuration allows switching between InMemory/File providers
- [ ] InMemory store still works for development

### Migration Notes

When adding SQL Server or Redis support later:
1. Add new `ISessionStore` implementation
2. Add provider option in `SessionStoreOptions`
3. Update the factory in `OrchestrationExtensions`
4. No changes needed to `SessionSerializer` or orchestrator

---
