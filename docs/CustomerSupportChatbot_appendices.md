# Utility Billing Chatbot - Appendices

## Complete Flow Diagram

```
┌───────────────────────────────────────────────────────────────────────────────────────┐
│                                COMPLETE CHATBOT FLOW                                   │
└───────────────────────────────────────────────────────────────────────────────────────┘

User Message
      │
      ▼
┌─────────────────┐
│ Load/Create     │──────────────────────────────────────────────────────┐
│ Session         │                                                       │
└────────┬────────┘                                                       │
         │                                                                │
         ▼                                                                │
┌─────────────────┐                                                       │
│ Classifier      │                                                       │
│ Agent           │                                                       │
└────────┬────────┘                                                       │
         │                                                                │
         ├──────────────┬──────────────┬──────────────┬──────────────┐   │
         ▼              ▼              ▼              ▼              ▼   │
    ┌─────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌─────────┤
    │   FAQ   │   │ General  │   │ Account  │   │ Unknown  │   │ Human   │
    │  Agent  │   │Knowledge │   │   Data   │   │   Low    │   │Requested│
    └────┬────┘   └────┬─────┘   └────┬─────┘   │Confidence│   └────┬────┘
         │             │              │         └────┬─────┘        │
         │             │              │              │              │
         │             │              ▼              │              │
         │             │        ┌──────────┐         │              │
         │             │        │  Auth    │         │              │
         │             │        │  Check   │         │              │
         │             │        └────┬─────┘         │              │
         │             │              │              │              │
         │             │         ┌────┴────┐         │              │
         │             │         ▼         ▼         │              │
         │             │    ┌────────┐ ┌────────┐    │              │
         │             │    │ Verify │ │ Data   │    │              │
         │             │    │Identity│ │ Agent  │    │              │
         │             │    └───┬────┘ └───┬────┘    │              │
         │             │        │          │         │              │
         │             │        │    ┌─────┴─────┐   │              │
         │             │        │    │ API / DB  │   │              │
         │             │        │    │   Tools   │   │              │
         │             │        │    └─────┬─────┘   │              │
         │             │        │          │         │              │
         │             │        │          │         │              │
         │             │        ▼          │         ▼              │
         │             │   ┌─────────┐     │    ┌─────────┐         │
         │             │   │  Auth   │     │    │Clarify/ │         │
         │             │   │Callback │     │    │ Retry   │         │
         │             │   └────┬────┘     │    └────┬────┘         │
         │             │        │          │         │              │
         │             │        ▼          │         │              │
         │             │   Resume Query    │         │              │
         │             │        │          │         │              │
         │             │        │          │         │              │
         └─────┬───────┴────────┴──────────┴─────────┤              │
               │                                      │              │
               ▼                                      ▼              ▼
         ┌──────────┐                          ┌───────────────────────┐
         │ Response │                          │   Human Handoff Flow  │
         │  Direct  │                          │                       │
         └────┬─────┘                          │  1. Summarize Conv    │
              │                                │  2. Create Ticket     │
              │                                │  3. Queue for Human   │
              │                                │  4. Wait/Timeout      │
              │                                │  5. Deliver Response  │
              │                                └───────────┬───────────┘
              │                                            │
              └─────────────────┬───────────────────────────┘
                                │
                                ▼
                         ┌──────────┐
                         │  Update  │
                         │ History  │
                         └────┬─────┘
                              │
                              ▼
                         ┌──────────┐
                         │  Save    │
                         │ Session  │
                         └────┬─────┘
                              │
                              ▼
                         ┌──────────┐
                         │ Return   │
                         │ Response │
                         └──────────┘
```

---

## Testing Summary By Stage

| Stage | Focus | Key Tests |
|-------|-------|-----------|
| 1 | Classifier Agent | Category accuracy, confidence scoring, auth detection |
| 2 | FAQ Agent | Knowledge base answers, unknown handling, context |
| 3 | In-Band Auth Agent | Identity lookup, SSN/DOB verification, lockout |
| 4 | Data Agent | Balance/usage fetch, auth guard, MockCIS data access |
| 5 | Orchestrator | Routing logic, auth flow, session management |
| 6 | Human Handoff | WebSocket, summarization, ticket creation, agent routing |
| 7 | Persistence | Serialization, restoration, multi-instance |

---

## Appendix A: Suggested Project Structure

```
UtilityBillingChatbot/
├── src/
│   ├── UtilityBillingChatbot.Core/
│   │   ├── Agents/
│   │   │   ├── ClassifierAgentFactory.cs
│   │   │   ├── FAQAgentFactory.cs
│   │   │   ├── InBandAuthAgentFactory.cs
│   │   │   ├── UtilityDataAgentFactory.cs
│   │   │   └── SummarizationAgentFactory.cs
│   │   ├── Models/
│   │   │   ├── QuestionClassification.cs
│   │   │   ├── UserSessionContext.cs
│   │   │   ├── ChatSession.cs
│   │   │   ├── ChatResponse.cs
│   │   │   ├── HandoffTicket.cs
│   │   │   └── HumanHandoffRequest.cs
│   │   ├── Mock/
│   │   │   ├── MockCISDatabase.cs
│   │   │   ├── UtilityCustomer.cs
│   │   │   ├── BillRecord.cs
│   │   │   └── UsageRecord.cs
│   │   ├── Services/
│   │   │   ├── ChatbotOrchestrator.cs
│   │   │   ├── HandoffManager.cs
│   │   │   └── AuthGuard.cs
│   │   └── Storage/
│   │       ├── ISessionStore.cs
│   │       └── InMemorySessionStore.cs
│   │
│   └── UtilityBillingChatbot.Api/
│       ├── Hubs/
│       │   └── ChatHub.cs
│       └── Program.cs
│
├── tests/
│   ├── UtilityBillingChatbot.Core.Tests/
│   │   ├── Agents/
│   │   │   ├── ClassifierAgentTests.cs
│   │   │   ├── FAQAgentTests.cs
│   │   │   ├── InBandAuthAgentTests.cs
│   │   │   └── UtilityDataAgentTests.cs
│   │   ├── Services/
│   │   │   ├── OrchestratorTests.cs
│   │   │   └── HandoffManagerTests.cs
│   │   └── Storage/
│   │       └── SessionStoreTests.cs
│   │
│   └── UtilityBillingChatbot.Integration.Tests/
│       ├── EndToEndTests.cs
│       └── WebSocketHandoffTests.cs
│
├── data/
│   └── utility_billing_faq.md
│
└── UtilityBillingChatbot.sln
```

---

## Appendix B: Pattern Analysis & Future Considerations

### Current Pattern: Custom Orchestrator with Manual Routing

The current implementation uses a **custom orchestrator class** with manual routing logic:

```
┌─────────────────────────────────────────────────────────────────┐
│                  ChatbotOrchestrator                             │
├─────────────────────────────────────────────────────────────────┤
│  ProcessMessageAsync(sessionId, message)                         │
│      │                                                           │
│      ├─► Check: AuthState == InProgress?                        │
│      │       YES → ContinueAuthenticationFlowAsync()            │
│      │                                                           │
│      ├─► ClassifyQuestionAsync() → QuestionClassification       │
│      │                                                           │
│      └─► switch (classification.Category)                        │
│              BillingFAQ    → HandleBillingFAQAsync()            │
│              AccountData   → HandleAccountDataAsync()            │
│              ServiceRequest→ InitiateHumanHandoffAsync()        │
│              HumanRequested→ InitiateHumanHandoffAsync()        │
│              OutOfScope    → HandleOutOfScopeAsync()            │
└─────────────────────────────────────────────────────────────────┘
```

### Comparison with Framework Patterns

| Aspect | Our Implementation | Framework WorkflowBuilder |
|--------|-------------------|---------------------------|
| **Definition** | Imperative (C# switch statements) | Declarative (graph-based) |
| **Routing** | Manual `switch` in `ProcessMessageAsync` | `AddSwitch()` with predicates |
| **State Management** | `ConcurrentDictionary<string, ChatSession>` | `IWorkflowContext.StateAsync()` |
| **Human-in-Loop** | Custom `AuthAgentSession` + polling | `RequestPort` with `StreamingRun` |
| **Session Persistence** | Manual serialization | Built-in with Durable Tasks |
| **Multi-turn** | Manual state tracking | Automatic with AgentSession |
| **Testability** | Mock each factory | Mock executors |

### Drawbacks of Current Approach

#### 1. ~~**No Built-in Persistence/Durability**~~ ✅ ADDRESSED
- ~~Sessions stored in `ConcurrentDictionary` (memory-only)~~
- **Solution**: Added `ISessionStore` interface with `GetSessionAsync`, `SaveSessionAsync`, `DeleteSessionAsync`
- Orchestrator now uses `_sessionCache` backed by `ISessionStore` for persistence
- **Remaining**: Need to implement concrete `ISessionStore` (Redis, SQL, etc.)

#### 2. ~~**Tight Coupling**~~ ✅ ADDRESSED
- ~~Orchestrator directly instantiates agents via factories~~
- **Solution**: All dependencies now use interfaces (`IClassifierAgentFactory`, `IFAQAgentFactory`, etc.)
- Constructor injection enables easy mocking and testing
- **See**: Overview document, Section 4 "Service Abstractions (Loose Coupling)"

#### 3. **Manual State Machine**
- Authentication flow managed via `AuthenticationState` enum + `AuthAgentSession`
- Easy to introduce bugs in state transitions
- No visualization of conversation flow
- **Impact**: Complex debugging, state inconsistencies possible

#### 4. **No Streaming Support**
- Responses are returned as complete messages
- No support for token-by-token streaming to UI
- **Impact**: Poor UX for long responses

#### 5. **Limited Observability**
- No built-in tracing/metrics
- No workflow visualization
- Conversation history stored but not indexed
- **Impact**: Difficult to debug production issues

#### 6. **Single-threaded Processing**
- One message processed at a time per session
- No concurrent agent execution
- **Impact**: Slower for complex queries that could parallelize

#### 7. **No Compensation/Rollback**
- If handoff fails mid-conversation, state is inconsistent
- No saga pattern for multi-step operations
- **Impact**: Potential for orphaned sessions

### Nice-to-Have Additions

#### Immediate Priorities (P0)

1. **Streaming Response Support**
```csharp
public async IAsyncEnumerable<string> ProcessMessageStreamingAsync(
    string sessionId,
    string userMessage,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    // ... classification ...
    await foreach (var token in agent.RunStreamingAsync(message, session, ct))
    {
        yield return token;
    }
}
```

2. **Session Timeout & Cleanup**
```csharp
public class SessionCleanupService : BackgroundService
{
    private readonly ISessionStore _sessionStore;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sessionIds = await _sessionStore.GetActiveSessionIdsAsync(ct);

            foreach (var sessionId in sessionIds)
            {
                var session = await _sessionStore.GetSessionAsync(sessionId, ct);
                if (session?.UserContext.LastInteraction < DateTimeOffset.UtcNow - _sessionTimeout)
                {
                    await _sessionStore.DeleteSessionAsync(sessionId, ct);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
```

3. **Retry with Exponential Backoff**
```csharp
private async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try { return await operation(); }
        catch (Exception ex) when (i < maxRetries - 1 && IsTransient(ex))
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
        }
    }
    throw new InvalidOperationException("Max retries exceeded");
}
```

#### Medium Priority (P1)

4. **Conversation Analytics**
```csharp
public class ConversationMetrics
{
    public int TotalMessages { get; set; }
    public Dictionary<QuestionCategory, int> CategoryBreakdown { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    public int HandoffRate { get; set; }
    public int AuthSuccessRate { get; set; }
    public List<string> TopQuestions { get; set; }
}
```

5. **Proactive Notifications**
```csharp
// Notify customer of outages, payment due dates, etc.
public async Task SendProactiveMessageAsync(
    string accountNumber,
    string message,
    NotificationType type)
{
    var session = await FindSessionByAccountAsync(accountNumber);
    if (session != null)
    {
        await _signalR.Clients.Group($"session:{session.SessionId}")
            .SendAsync("ProactiveMessage", new { message, type });
    }
}
```

6. **Sentiment Analysis Integration**
```csharp
public class SentimentAwareOrchestrator : ChatbotOrchestrator
{
    public override async Task<ChatResponse> ProcessMessageAsync(...)
    {
        var sentiment = await _sentimentAnalyzer.AnalyzeAsync(userMessage);

        // Escalate frustrated customers faster
        if (sentiment.Score < -0.5 && sentiment.Confidence > 0.8)
        {
            return await InitiateHumanHandoffAsync(
                userMessage, session,
                $"Customer appears frustrated (sentiment: {sentiment.Score})",
                cancellationToken);
        }

        return await base.ProcessMessageAsync(...);
    }
}
```

7. **Caching for FAQ Responses**
```csharp
public class CachedFAQAgent
{
    private readonly IDistributedCache _cache;

    public async Task<string> GetAnswerAsync(string question)
    {
        var cacheKey = $"faq:{ComputeHash(question)}";
        var cached = await _cache.GetStringAsync(cacheKey);

        if (cached != null) return cached;

        var answer = await _faqAgent.RunAsync(question, session);
        await _cache.SetStringAsync(cacheKey, answer.Text,
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(24) });

        return answer.Text;
    }
}
```

#### Future Enhancements (P2)

8. **Multi-Language Support**
```csharp
public class MultilingualOrchestrator : ChatbotOrchestrator
{
    public override async Task<ChatResponse> ProcessMessageAsync(...)
    {
        var detectedLanguage = await _languageDetector.DetectAsync(userMessage);

        if (detectedLanguage != "en")
        {
            userMessage = await _translator.TranslateToEnglishAsync(userMessage);
        }

        var response = await base.ProcessMessageAsync(sessionId, userMessage, ct);

        if (detectedLanguage != "en")
        {
            response.Message = await _translator.TranslateFromEnglishAsync(
                response.Message, detectedLanguage);
        }

        return response;
    }
}
```

9. **Voice Channel Support (IVR Integration)**
```csharp
public interface IChannelAdapter
{
    Task<string> TranscribeAsync(Stream audioStream);
    Task<Stream> SynthesizeAsync(string text);
}

public class VoiceOrchestrator : ChatbotOrchestrator
{
    public async Task<Stream> ProcessVoiceAsync(Stream audioInput, string sessionId)
    {
        var text = await _channelAdapter.TranscribeAsync(audioInput);
        var response = await ProcessMessageAsync(sessionId, text);
        return await _channelAdapter.SynthesizeAsync(response.Message);
    }
}
```

10. **A/B Testing Framework**
```csharp
public class ABTestingOrchestrator : ChatbotOrchestrator
{
    public override async Task<ChatResponse> ProcessMessageAsync(...)
    {
        var variant = _abTestService.GetVariant(sessionId, "faq-prompt-v2");

        var factory = variant == "control"
            ? _faqFactoryV1
            : _faqFactoryV2;

        var response = await ProcessWithFactory(factory, ...);

        await _abTestService.RecordOutcome(sessionId, response.RequiredAction);

        return response;
    }
}
```

11. **Workflow Visualization Dashboard**
```
┌─────────────────────────────────────────────────────────────┐
│  Session: abc-123  |  Status: Auth In Progress  |  2:34    │
├─────────────────────────────────────────────────────────────┤
│  [Classifier] → AccountData                                 │
│       ↓                                                     │
│  [AuthAgent] → Awaiting DOB verification                   │
│       ↓                                                     │
│  [Pending] "What is my balance?"                           │
└─────────────────────────────────────────────────────────────┘
```

### Migration Path to WorkflowBuilder

If you decide to migrate to the framework's `WorkflowBuilder` pattern:

1. **Phase 1**: Wrap existing agents as `Executor<TIn, TOut>` classes
2. **Phase 2**: Replace `switch` routing with `AddSwitch()`
3. **Phase 3**: Replace manual auth flow with `RequestPort`
4. **Phase 4**: Add `StreamingRun` for real-time updates
5. **Phase 5**: Integrate Durable Tasks for persistence

### Recommendation

For a **prototype/MVP**, the current custom orchestrator approach is acceptable:
- Simpler to understand and debug
- Faster to implement changes
- No additional framework dependencies

For **production deployment**, consider migrating to `WorkflowBuilder`:
- Built-in durability and persistence
- Better observability and tracing
- Declarative workflow definition
- Easier to visualize and test

---

## Appendix C: Dependencies

```xml
<ItemGroup>
  <!-- Core Agent Framework -->
  <PackageReference Include="Microsoft.Extensions.AI" Version="9.0.0" />
  <PackageReference Include="Microsoft.Agents.AI" Version="0.1.0" />

  <!-- AI Provider (choose one) -->
  <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.0.0" />
  <!-- OR -->
  <PackageReference Include="Microsoft.Extensions.AI.AzureAIInference" Version="9.0.0" />

  <!-- SignalR for real-time WebSocket communication -->
  <PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.1.0" />

  <!-- Optional: Session persistence (for production) -->
  <PackageReference Include="StackExchange.Redis" Version="2.7.0" />
</ItemGroup>
```

---

## Next Steps

1. **Start with Stage 1** - Build and thoroughly test the Classifier Agent
2. **Proceed sequentially** - Each stage builds on the previous
3. **Validate each stage** - Use the validation checklists before moving on
4. **Create integration tests** - As you complete stages, add integration tests that span multiple components
5. **Consider observability** - Add logging and metrics at each stage
6. **Plan for scale** - Stage 7 enables horizontal scaling

**Recommended Testing Order:**
- Stages 1-4: Unit tests with mock chat client
- Stage 5: Integration tests with in-memory session
- Stage 6: Manual testing with two browser windows (customer + agent)
- Stage 7: Test session persistence across app restarts

This architecture provides a solid foundation for a prototype multi-agent customer support chatbot that can evolve into production.
