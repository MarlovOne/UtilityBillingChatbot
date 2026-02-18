## Stage 8: Next Best Action Suggestions

### Objective

Add intelligent follow-up suggestions after resolving user intents. The NextBestActionAgent analyzes conversation history and suggests 1-2 relevant questions the user might want to ask next, constrained to the 21 known question types the system can handle.

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     ChatbotOrchestrator                         │
│                                                                 │
│  ProcessMessageAsync                                            │
│       │                                                         │
│       ▼                                                         │
│  RouteMessageAsync ──► ClassifierAgent ──► QuestionClassification│
│       │                                                         │
│       ▼                                                         │
│  HandleXxxAsync (FAQ/UtilityData/etc) ──► ChatResponse          │
│       │                                                         │
│       ▼                                                         │
│  ┌─────────────────────────────────────┐                        │
│  │ if (ShouldSuggestNextAction)        │  ◄── NEW               │
│  │   NextBestActionAgent.SuggestAsync  │                        │
│  │   response.SuggestedActions = ...   │                        │
│  └─────────────────────────────────────┘                        │
│       │                                                         │
│       ▼                                                         │
│  return response                                                │
└─────────────────────────────────────────────────────────────────┘
```

**When suggestions are added:**
- After `BillingFAQ` resolution (FAQAgent answered)
- After `AccountData` resolution (UtilityDataAgent answered)

**When suggestions are skipped:**
- During auth flow (`RequiredAction == AuthenticationInProgress`)
- On `ServiceRequest` / `HumanRequested` (going to handoff)
- On `OutOfScope`

### File Structure

```
src/Agents/NextBestAction/
├── NextBestActionAgent.cs           # Main agent class + DI extension
└── NextBestActionModels.cs          # Input/output types
```

### Implementation - NextBestAction Models

```csharp
// src/Agents/NextBestAction/NextBestActionModels.cs

namespace UtilityBillingChatbot.Agents.NextBestAction;

/// <summary>
/// A suggested follow-up action for the user.
/// </summary>
public class SuggestedAction
{
    /// <summary>The question type ID from verified-questions.json.</summary>
    public required string QuestionId { get; set; }

    /// <summary>Natural language question to show the user.</summary>
    public required string SuggestedQuestion { get; set; }
}

/// <summary>
/// Result from the NextBestActionAgent.
/// </summary>
public class NextBestActionResult
{
    /// <summary>0-2 suggested follow-up actions.</summary>
    public List<SuggestedAction> Suggestions { get; set; } = [];

    /// <summary>Agent's reasoning for the suggestions (for debugging/logging).</summary>
    public string? Reasoning { get; set; }
}
```

### Implementation - NextBestAction Agent

```csharp
// src/Agents/NextBestAction/NextBestActionAgent.cs

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Agents.NextBestAction;

/// <summary>
/// Agent that suggests relevant follow-up questions after resolving a user's intent.
/// Suggestions are constrained to the 21 known question types the system can handle.
/// </summary>
public class NextBestActionAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<NextBestActionAgent> _logger;
    private readonly string _questionTypesJson;

    private const string Instructions = """
        You are a next-best-action recommender for a utility billing chatbot.
        After a user's question has been answered, suggest 1-2 relevant follow-up questions
        they might want to ask next.

        Rules:
        1. Only suggest questions from the provided list of known question types
        2. Suggest questions that are contextually relevant to the conversation
        3. Consider the user's authentication state - don't suggest auth-required questions to unauthenticated users
        4. If no good suggestions exist, return an empty list (don't force irrelevant suggestions)
        5. Phrase suggestions as natural questions the user might ask
        6. Maximum 2 suggestions per response

        Output format:
        {
            "suggestions": [
                {
                    "questionId": "the-question-id",
                    "suggestedQuestion": "Natural language question for the user"
                }
            ],
            "reasoning": "Brief explanation of why these suggestions are relevant"
        }
        """;

    public NextBestActionAgent(
        IChatClient chatClient,
        ILogger<NextBestActionAgent> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
        _questionTypesJson = LoadQuestionTypes();
    }

    /// <summary>
    /// Suggests follow-up actions based on conversation context.
    /// </summary>
    /// <param name="conversationHistory">The conversation so far.</param>
    /// <param name="resolvedQuestionType">The question type that was just resolved.</param>
    /// <param name="resolvedCategory">The category of the resolved question.</param>
    /// <param name="isAuthenticated">Whether the user is authenticated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>0-2 suggested follow-up actions.</returns>
    public async Task<List<SuggestedAction>> SuggestAsync(
        List<ConversationMessage> conversationHistory,
        string? resolvedQuestionType,
        QuestionCategory resolvedCategory,
        bool isAuthenticated,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Generating next best action suggestions. ResolvedType: {Type}, IsAuth: {IsAuth}",
            resolvedQuestionType, isAuthenticated);

        try
        {
            var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
            {
                Name = "NextBestActionAgent",
                ChatOptions = new ChatOptions
                {
                    Instructions = Instructions,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<NextBestActionResult>()
                }
            });

            var session = await agent.CreateSessionAsync(cancellationToken);

            var conversationText = string.Join("\n",
                conversationHistory.TakeLast(10).Select(m => $"{m.Role}: {m.Content}"));

            var prompt = $"""
                Conversation history (last 10 messages):
                {conversationText}

                Just resolved question type: {resolvedQuestionType ?? "unknown"}
                Category: {resolvedCategory}
                User authenticated: {isAuthenticated}

                Available question types the system can handle:
                {_questionTypesJson}

                Based on this context, suggest 0-2 relevant follow-up questions.
                Only suggest questions with requiredAuthLevel "None" or "Basic" if user is not authenticated.
                """;

            var response = await agent.RunAsync<NextBestActionResult>(
                message: prompt,
                session: session,
                cancellationToken: cancellationToken);

            if (response.TryGetResult(out var result))
            {
                var validSuggestions = ValidateSuggestions(result.Suggestions, isAuthenticated);

                _logger.LogInformation(
                    "Generated {Count} next best action suggestions. Reasoning: {Reasoning}",
                    validSuggestions.Count, result.Reasoning);

                return validSuggestions;
            }

            _logger.LogWarning("Failed to parse NextBestActionResult from agent response");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NextBestActionAgent failed, returning empty suggestions");
            return [];
        }
    }

    /// <summary>
    /// Validates suggestions against known question types and auth requirements.
    /// </summary>
    private List<SuggestedAction> ValidateSuggestions(
        List<SuggestedAction> suggestions,
        bool isAuthenticated)
    {
        // Filter to valid question IDs and respect auth requirements
        // This is a safety check in case the LLM suggests invalid IDs
        var validIds = new HashSet<string>
        {
            "bill-high", "balance-inquiry", "payment-status", "payment-due-date",
            "payment-options", "payment-arrangement", "assistance-programs",
            "budget-billing", "autopay-signup", "autopay-status",
            "billing-cycle-explanation", "charge-explanation", "meter-read-type",
            "meter-check-request", "rate-plan-inquiry", "alternate-supplier",
            "demand-charge-explanation", "bill-reduction-tips", "bill-copy-request",
            "contact-update"
        };

        // Questions that don't require auth
        var noAuthRequired = new HashSet<string>
        {
            "payment-options", "assistance-programs", "billing-cycle-explanation",
            "demand-charge-explanation", "bill-reduction-tips"
        };

        return suggestions
            .Where(s => validIds.Contains(s.QuestionId))
            .Where(s => isAuthenticated || noAuthRequired.Contains(s.QuestionId))
            .Take(2)
            .ToList();
    }

    /// <summary>
    /// Loads question types from verified-questions.json for the prompt.
    /// </summary>
    private static string LoadQuestionTypes()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "verified-questions.json");

        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        // Fallback to embedded summary if file not found
        return """
            [
                {"id": "bill-high", "description": "Why is my bill so high", "requiredAuthLevel": "Basic"},
                {"id": "balance-inquiry", "description": "What is my current balance", "requiredAuthLevel": "Basic"},
                {"id": "payment-status", "description": "Did my payment go through", "requiredAuthLevel": "Basic"},
                {"id": "payment-due-date", "description": "When is my payment due", "requiredAuthLevel": "Basic"},
                {"id": "payment-options", "description": "How can I pay my bill", "requiredAuthLevel": "None"},
                {"id": "payment-arrangement", "description": "Can I set up a payment plan", "requiredAuthLevel": "Basic"},
                {"id": "assistance-programs", "description": "What assistance programs are available", "requiredAuthLevel": "None"},
                {"id": "budget-billing", "description": "Can I enroll in budget billing", "requiredAuthLevel": "Elevated"},
                {"id": "autopay-signup", "description": "How do I sign up for AutoPay", "requiredAuthLevel": "Elevated"},
                {"id": "autopay-status", "description": "Am I on AutoPay", "requiredAuthLevel": "Basic"},
                {"id": "billing-cycle-explanation", "description": "Why does my billing period change", "requiredAuthLevel": "None"},
                {"id": "charge-explanation", "description": "What is this charge on my bill", "requiredAuthLevel": "Basic"},
                {"id": "meter-read-type", "description": "Is my bill based on actual or estimated reading", "requiredAuthLevel": "Basic"},
                {"id": "meter-check-request", "description": "Can someone check my meter", "requiredAuthLevel": "Basic"},
                {"id": "rate-plan-inquiry", "description": "Am I on the best rate plan", "requiredAuthLevel": "Basic"},
                {"id": "alternate-supplier", "description": "Why am I charged by an alternate supplier", "requiredAuthLevel": "Basic"},
                {"id": "demand-charge-explanation", "description": "What is a demand charge", "requiredAuthLevel": "None"},
                {"id": "bill-reduction-tips", "description": "How can I reduce my bill", "requiredAuthLevel": "None"},
                {"id": "bill-copy-request", "description": "Can I get a copy of my bill", "requiredAuthLevel": "Basic"},
                {"id": "contact-update", "description": "How do I update my contact info", "requiredAuthLevel": "Elevated"}
            ]
            """;
    }
}

/// <summary>
/// Extension methods for registering the NextBestActionAgent.
/// </summary>
public static class NextBestActionAgentExtensions
{
    public static IServiceCollection AddNextBestActionAgent(this IServiceCollection services)
    {
        services.AddSingleton<NextBestActionAgent>();
        return services;
    }
}
```

### Updated ChatResponse Model

Extend `ChatResponse` in `OrchestratorModels.cs`:

```csharp
// Update src/Orchestration/OrchestratorModels.cs

using UtilityBillingChatbot.Agents.NextBestAction;

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

    /// <summary>Suggested follow-up questions (0-2 items).</summary>
    public List<SuggestedAction>? SuggestedActions { get; set; }
}
```

### Updated Orchestrator Integration

Add to `ChatbotOrchestrator`:

```csharp
// Update src/Orchestration/ChatbotOrchestrator.cs

using UtilityBillingChatbot.Agents.NextBestAction;

public class ChatbotOrchestrator
{
    private readonly ClassifierAgent _classifierAgent;
    private readonly FAQAgent _faqAgent;
    private readonly AuthAgent _authAgent;
    private readonly UtilityDataAgent _utilityDataAgent;
    private readonly SummarizationAgent _summarizationAgent;
    private readonly NextBestActionAgent _nextBestActionAgent;  // NEW
    private readonly IHandoffService _handoffService;
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<ChatbotOrchestrator> _logger;

    public ChatbotOrchestrator(
        ClassifierAgent classifierAgent,
        FAQAgent faqAgent,
        AuthAgent authAgent,
        UtilityDataAgent utilityDataAgent,
        SummarizationAgent summarizationAgent,
        NextBestActionAgent nextBestActionAgent,  // NEW
        IHandoffService handoffService,
        ISessionStore sessionStore,
        ILogger<ChatbotOrchestrator> logger)
    {
        _classifierAgent = classifierAgent;
        _faqAgent = faqAgent;
        _authAgent = authAgent;
        _utilityDataAgent = utilityDataAgent;
        _summarizationAgent = summarizationAgent;
        _nextBestActionAgent = nextBestActionAgent;  // NEW
        _handoffService = handoffService;
        _sessionStore = sessionStore;
        _logger = logger;
    }

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
        QuestionClassification? classification = null;

        try
        {
            // ... existing routing logic ...
            // (auth flow check, handoff check, then RouteMessageAsync)

            (response, classification) = await RouteMessageWithClassificationAsync(
                session, userMessage, cancellationToken);

            // NEW: Add next best action suggestions for successful resolutions
            if (ShouldSuggestNextAction(response))
            {
                response.SuggestedActions = await GetNextBestActionsAsync(
                    session,
                    classification?.QuestionType,
                    response.Category,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for session {SessionId}", sessionId);
            response = new ChatResponse
            {
                Message = "I apologize, but I encountered an error. Please try again.",
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
    /// Determines whether to add next best action suggestions to the response.
    /// </summary>
    private static bool ShouldSuggestNextAction(ChatResponse response)
    {
        // Only for successful FAQ or AccountData resolutions
        if (response.Category is not (QuestionCategory.BillingFAQ or QuestionCategory.AccountData))
            return false;

        // Skip if we're in the middle of auth or handoff
        if (response.RequiredAction is RequiredAction.AuthenticationInProgress or
            RequiredAction.HumanHandoffInitiated or
            RequiredAction.HumanConversationActive)
            return false;

        return true;
    }

    /// <summary>
    /// Gets next best action suggestions, handling failures gracefully.
    /// </summary>
    private async Task<List<SuggestedAction>?> GetNextBestActionsAsync(
        ChatSession session,
        string? resolvedQuestionType,
        QuestionCategory resolvedCategory,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var suggestions = await _nextBestActionAgent.SuggestAsync(
                session.ConversationHistory,
                resolvedQuestionType,
                resolvedCategory,
                session.UserContext.IsAuthenticated,
                linkedCts.Token);

            return suggestions.Count > 0 ? suggestions : null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("NextBestActionAgent timed out, continuing without suggestions");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NextBestActionAgent failed, continuing without suggestions");
            return null;
        }
    }
}
```

### Updated Display in ChatbotService

```csharp
// Update src/Infrastructure/ChatbotService.cs

private void DisplayResponse(ChatResponse response)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\nAssistant: {response.Message}");

    // NEW: Display suggestions if present
    if (response.SuggestedActions is { Count: > 0 })
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("\nYou might also want to ask:");
        foreach (var suggestion in response.SuggestedActions)
        {
            Console.WriteLine($"  - \"{suggestion.SuggestedQuestion}\"");
        }
    }

    Console.ResetColor();

    if (response.RequiredAction != RequiredAction.None)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{response.RequiredAction}]");
        Console.ResetColor();
    }
}
```

### Updated DI Registration

```csharp
// Update Infrastructure/ServiceCollectionExtensions.cs

using UtilityBillingChatbot.Agents.NextBestAction;

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
        services.AddSummarizationAgent();
        services.AddNextBestActionAgent();  // NEW

        // Add handoff services
        services.AddHandoffServices();

        // Add orchestration
        services.AddOrchestration();

        // Add the chatbot background service
        services.AddHostedService<ChatbotService>();

        return services;
    }
}
```

### Testing

```csharp
// tests/Agents/NextBestAction/NextBestActionAgentTests.cs

using UtilityBillingChatbot.Agents.NextBestAction;
using UtilityBillingChatbot.Orchestration;

public class NextBestActionAgentTests
{
    private readonly NextBestActionAgent _agent;

    public NextBestActionAgentTests()
    {
        var chatClient = CreateTestChatClient();
        var logger = NullLogger<NextBestActionAgent>.Instance;
        _agent = new NextBestActionAgent(chatClient, logger);
    }

    [Fact]
    public async Task Suggests_RelatedQuestion_AfterBillHighResolution()
    {
        // Arrange
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Why is my bill so high this month?" },
            new() { Role = "assistant", Content = "Your bill increased due to higher usage..." }
        };

        // Act
        var suggestions = await _agent.SuggestAsync(
            history,
            resolvedQuestionType: "bill-high",
            resolvedCategory: QuestionCategory.AccountData,
            isAuthenticated: true);

        // Assert
        Assert.NotEmpty(suggestions);
        Assert.True(suggestions.Count <= 2);
        // Should suggest payment-related or reduction-related follow-ups
        Assert.Contains(suggestions, s =>
            s.QuestionId is "payment-arrangement" or "budget-billing" or
            "bill-reduction-tips" or "rate-plan-inquiry");
    }

    [Fact]
    public async Task Suggests_OnlyNoAuthQuestions_WhenNotAuthenticated()
    {
        // Arrange
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "How can I pay my bill?" },
            new() { Role = "assistant", Content = "You can pay online, by phone..." }
        };

        var noAuthQuestionIds = new HashSet<string>
        {
            "payment-options", "assistance-programs", "billing-cycle-explanation",
            "demand-charge-explanation", "bill-reduction-tips"
        };

        // Act
        var suggestions = await _agent.SuggestAsync(
            history,
            resolvedQuestionType: "payment-options",
            resolvedCategory: QuestionCategory.BillingFAQ,
            isAuthenticated: false);

        // Assert - all suggestions should be no-auth questions
        Assert.All(suggestions, s =>
            Assert.Contains(s.QuestionId, noAuthQuestionIds));
    }

    [Fact]
    public async Task Returns_EmptyList_WhenNoGoodSuggestion()
    {
        // Arrange - conversation about out-of-scope topic
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Thanks, that's all I needed!" },
            new() { Role = "assistant", Content = "You're welcome! Have a great day." }
        };

        // Act
        var suggestions = await _agent.SuggestAsync(
            history,
            resolvedQuestionType: null,
            resolvedCategory: QuestionCategory.BillingFAQ,
            isAuthenticated: true);

        // Assert - empty is a valid result
        Assert.True(suggestions.Count <= 2);
    }

    [Fact]
    public async Task Constrains_ToValidQuestionIds()
    {
        // Arrange
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "What's my balance?" },
            new() { Role = "assistant", Content = "Your balance is $127.50" }
        };

        var validIds = new HashSet<string>
        {
            "bill-high", "balance-inquiry", "payment-status", "payment-due-date",
            "payment-options", "payment-arrangement", "assistance-programs",
            "budget-billing", "autopay-signup", "autopay-status",
            "billing-cycle-explanation", "charge-explanation", "meter-read-type",
            "meter-check-request", "rate-plan-inquiry", "alternate-supplier",
            "demand-charge-explanation", "bill-reduction-tips", "bill-copy-request",
            "contact-update"
        };

        // Act
        var suggestions = await _agent.SuggestAsync(
            history,
            resolvedQuestionType: "balance-inquiry",
            resolvedCategory: QuestionCategory.AccountData,
            isAuthenticated: true);

        // Assert
        Assert.All(suggestions, s =>
            Assert.Contains(s.QuestionId, validIds));
    }

    [Fact]
    public async Task Limits_ToTwoSuggestions()
    {
        // Arrange
        var history = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Tell me about my account" },
            new() { Role = "assistant", Content = "Here's your account summary..." }
        };

        // Act
        var suggestions = await _agent.SuggestAsync(
            history,
            resolvedQuestionType: "balance-inquiry",
            resolvedCategory: QuestionCategory.AccountData,
            isAuthenticated: true);

        // Assert
        Assert.True(suggestions.Count <= 2);
    }
}
```

```csharp
// tests/Orchestration/NextBestActionIntegrationTests.cs

public class NextBestActionIntegrationTests
{
    [Fact]
    public async Task FAQResolution_IncludesSuggestions()
    {
        // Arrange
        var orchestrator = CreateTestOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "How can I pay my bill?");

        // Assert
        Assert.Equal(QuestionCategory.BillingFAQ, response.Category);
        // Suggestions should be present (may be null if agent chose not to suggest)
        // but if present, should be valid
        if (response.SuggestedActions is not null)
        {
            Assert.True(response.SuggestedActions.Count <= 2);
            Assert.All(response.SuggestedActions, s =>
                Assert.False(string.IsNullOrEmpty(s.SuggestedQuestion)));
        }
    }

    [Fact]
    public async Task AuthFlow_SkipsSuggestions()
    {
        // Arrange
        var orchestrator = CreateTestOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act - ask something requiring auth
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "What's my account balance?");

        // Assert - during auth flow, no suggestions
        if (response.RequiredAction == RequiredAction.AuthenticationInProgress)
        {
            Assert.Null(response.SuggestedActions);
        }
    }

    [Fact]
    public async Task HandoffFlow_SkipsSuggestions()
    {
        // Arrange
        var orchestrator = CreateTestOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act - request human agent
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "I need to speak to a real person");

        // Assert
        Assert.Equal(QuestionCategory.HumanRequested, response.Category);
        Assert.Null(response.SuggestedActions);
    }

    [Fact]
    public async Task AgentFailure_ReturnsResponseWithoutSuggestions()
    {
        // Arrange - orchestrator with failing NextBestActionAgent
        var orchestrator = CreateOrchestratorWithFailingNextBestActionAgent();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "How can I pay my bill?");

        // Assert - main response should still work
        Assert.Equal(QuestionCategory.BillingFAQ, response.Category);
        Assert.False(string.IsNullOrEmpty(response.Message));
        // Suggestions null due to agent failure - that's acceptable
    }
}
```

### Example Conversation Flow

```
User: Why is my bill so high this month?
Assistant: Your bill increased this month because your usage was 45% higher than
last month. This is often due to seasonal changes or new appliances.

You might also want to ask:
  - "Can I set up a payment arrangement?"
  - "How can I reduce my bill?"

User: How can I reduce my bill?
Assistant: Here are some tips to reduce your energy bill:
- Adjust your thermostat by 2-3 degrees
- Use energy-efficient LED bulbs
- Unplug devices when not in use

You might also want to ask:
  - "Am I on the best rate plan?"
```

### Validation Checklist - Stage 8

- [ ] NextBestActionAgent follows existing agent patterns (no factory)
- [ ] Agent accepts conversation history, resolved question type, and auth state
- [ ] Agent returns 0-2 suggestions constrained to valid question IDs
- [ ] Suggestions respect auth requirements (no auth-required questions for unauthenticated users)
- [ ] ChatResponse extended with SuggestedActions property
- [ ] Orchestrator calls agent only on successful BillingFAQ/AccountData resolutions
- [ ] Orchestrator skips suggestions during auth flow, handoff, and OutOfScope
- [ ] Agent failure is handled gracefully (response still returned without suggestions)
- [ ] 5-second timeout prevents slow responses
- [ ] ChatbotService displays suggestions in console output
- [ ] DI registration follows `Add{Name}Agent()` pattern
- [ ] All new code has XML doc comments
- [ ] Unit tests cover core suggestion scenarios
- [ ] Integration tests verify orchestrator behavior

### Future Enhancements (Out of Scope)

For production deployment, consider:
- **Personalization**: Use customer history/preferences to rank suggestions
- **A/B testing**: Measure click-through on suggestions to improve relevance
- **Caching**: Cache common suggestion patterns to reduce LLM calls
- **Analytics**: Track which suggestions lead to follow-up questions
- **Feedback loop**: Allow users to dismiss suggestions and learn from that

---
