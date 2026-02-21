# AGENTS.md

Guidelines for AI coding agents working in this repository.

## Project Overview

Multi-agent utility billing chatbot built on Microsoft Agent Framework (.NET 10). Vertical (feature-based) architecture with agents in `src/Agents/{Feature}/` directories. This is a learning/prototype project — prioritize clear, robust implementations over clever abstractions. If you're adding too much code or hacking around the framework, check the Agent Framework samples at `/home/lmark/git/agent-framework/dotnet/samples` first.

## Build Commands

```bash
# Build the solution (TreatWarningsAsErrors=true — all warnings are build errors)
dotnet build

# Run the chatbot (requires LLM config in appsettings.json)
dotnet run --project src

# Run all tests
dotnet test

# Run a single test by fully-qualified method name
dotnet test --filter "FullyQualifiedName~ClassifierAgentTests.Classifier_CategorizesBillingFAQ"

# Run all tests in a class
dotnet test --filter "FullyQualifiedName~FAQAgentTests"

# Run tests matching a pattern
dotnet test --filter "DisplayName~Auth"
```

**Important:** Tests are integration tests requiring a configured LLM endpoint in `tests/appsettings.json`. All test classes use `[Collection("Sequential")]` to prevent parallel LLM calls. `UtilityDataContextProviderTests` is the only class with pure unit tests (no LLM needed).

## Code Style Guidelines

### File Headers

Every `.cs` file must start with:

```csharp
// Copyright (c) Microsoft. All rights reserved.
```

### Imports and Namespaces

- File-scoped namespaces (single line, no braces):
  ```csharp
  namespace UtilityBillingChatbot.Agents.Classifier;
  ```
- Import order: System → Microsoft → Third-party → Project namespaces
- Implicit usings are enabled — avoid redundant `using System;` etc.
- Remove all unused imports

### Formatting

- Indentation: 4 spaces (no tabs)
- Braces: Allman style (opening brace on its own line for types and methods)
- Max line length: ~120 characters (soft limit)
- Single blank line between members
- No trailing whitespace

### Types and Nullability

- Nullable reference types are enabled globally (`<Nullable>enable</Nullable>`)
- Use `?` suffix for nullable types: `string?`, `UtilityCustomer?`
- Use `[NotNullWhen(true)]` on output parameters of try-pattern methods
- Prefer positional records for immutable DTOs:
  ```csharp
  public record FAQResponse(string Text, AgentSession Session);
  ```
- Use `required` for required init-only properties when appropriate

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes, Records, Enums | PascalCase | `ClassifierAgent`, `QuestionCategory` |
| Interfaces | IPascalCase | `ISessionStore`, `IApprovalHandler` |
| Methods, Properties | PascalCase | `ClassifyAsync`, `IsAuthenticated` |
| Private fields | _camelCase | `_logger`, `_chatClient`, `_orchestrator` |
| Parameters, locals | camelCase | `input`, `cancellationToken`, `session` |
| Constants | PascalCase | `MeterName`, `MaxAttempts` |
| Async methods | Suffix `Async` | `RunAsync`, `RouteMessageAsync` |

### JSON Serialization

- Use `[JsonPropertyName("camelCase")]` for explicit property name control
- Use `[JsonConverter(typeof(JsonStringEnumConverter))]` on enum properties in schemas
- Use `[Description("...")]` on LLM structured output schema types/properties
- Deserialize with `JsonSerializerOptions.Web` for camelCase support

```csharp
[JsonPropertyName("category")]
[JsonConverter(typeof(JsonStringEnumConverter))]
[Description("The category of the question")]
public QuestionCategory Category { get; set; }
```

### Experimental APIs

When using `ApprovalRequiredAIFunction` (or other `MEAI001`-flagged APIs), suppress the warning locally:

```csharp
#pragma warning disable MEAI001
var tool = AIFunctionFactory.Create(..., new ApprovalRequiredAIFunctionFactoryOptions { ... });
#pragma warning restore MEAI001
```

### Error Handling

- Use the try-pattern for operations that may fail. `AgentResponseParser.TryGetResult` is the shared helper:
  ```csharp
  if (!TryGetResult(response, out var result, out var error))
  {
      _logger.LogWarning("Failed to parse response: {Error}", error);
      return new ClassificationResult(null, error);
  }
  ```
- Catch specific exception types — never a bare `catch (Exception)` unless re-throwing
- Structured logging: `_logger.LogError(ex, "Message: {Param}", value)`
- Use `InvalidOperationException` for configuration/setup errors
- Always rethrow `OperationCanceledException` in async methods

### XML Documentation

All public types and members require XML doc comments:

```csharp
/// <summary>
/// Classifies the user's input into a question category.
/// </summary>
/// <param name="input">The user's question text.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>The classification result, or a failed result if parsing failed.</returns>
public async Task<ClassificationResult> ClassifyAsync(string input, CancellationToken ct = default)
```

## Architecture Patterns

### Agent Structure

Each agent lives in `src/Agents/{Feature}/`:

- `{Feature}Agent.cs` — main agent class + DI extension method
- `{Feature}Prompts.cs` — prompt builder (only when prompt construction is non-trivial)
- Model files — input/output records and enums

```csharp
public class FeatureAgent
{
    private readonly ChatClientAgent _agent;
    private readonly ILogger<FeatureAgent> _logger;

    public FeatureAgent(IChatClient chatClient, ILogger<FeatureAgent> logger)
    {
        _logger = logger;
        _agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "FeatureAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = "System prompt here.",
                ResponseFormat = ChatResponseFormat.ForJsonSchema<FeatureResult>()
            }
        });
    }

    public async Task<FeatureResult?> RunAsync(string input, CancellationToken ct = default)
    {
        var response = await _agent.RunAsync<FeatureResult>(input, cancellationToken: ct);
        if (!TryGetResult(response, out var result, out var error))
        {
            _logger.LogWarning("FeatureAgent failed: {Error}", error);
            return null;
        }
        return result;
    }
}
```

### Dependency Injection

Each agent file includes its own DI extension at the bottom:

```csharp
public static class FeatureAgentExtensions
{
    public static IServiceCollection AddFeatureAgent(this IServiceCollection services)
    {
        services.AddSingleton<FeatureAgent>();
        return services;
    }
}
```

Register by calling `services.AddFeatureAgent()` in `src/Infrastructure/ServiceCollectionExtensions.cs`.

### AIContextProvider Pattern

Stateful, multi-turn agents (Auth, UtilityData) use an `AIContextProvider` that carries session state and exposes tools to the LLM. The context provider is instantiated per-session (not singleton) and passed into `RunAsync`:

```csharp
var context = new FeatureContextProvider(customer, _logger);
var response = await _agent.RunAsync<FeatureResult>(input, contextProvider: context, cancellationToken: ct);
```

### Structured Output

Always use `ChatResponseFormat.ForJsonSchema<T>()` for typed LLM responses. Handle JSON parse failures via `AgentResponseParser.TryGetResult` (see Error Handling above).

### NextBestActionAgent — Best-Effort Only

`NextBestActionAgent` runs opportunistically after every response. Its failures must be swallowed (never surfaced to the user), and the orchestrator enforces a 5-second hard timeout on it.

### Orchestration Flow

```
User input → ChatbotOrchestrator → ClassifierAgent → QuestionClassification
    BillingFAQ     → FAQAgent
    AccountData    → (AuthAgent if unauthenticated) → UtilityDataAgent
    ServiceRequest / HumanRequested → SummarizationAgent → Handoff ticket
```

## Adding a New Agent

1. Create `src/Agents/{Name}/` with `{Name}Agent.cs` and model files.
2. Add `Add{Name}Agent()` extension in the agent file.
3. Call `services.Add{Name}Agent()` in `src/Infrastructure/ServiceCollectionExtensions.cs`.
4. Inject into `ChatbotOrchestrator` and add routing logic in `RouteMessageAsync()`.

## Testing Conventions

- Framework: xUnit
- All integration test classes carry `[Collection("Sequential")]`
- Use `IAsyncLifetime` for host setup/teardown — never share state between tests
- Test method naming: `{Agent/Method}_{Scenario}`
- Mock CIS customers available in `MockCISDatabase`: John Smith (phone `555-1234`, SSN `1234`), Maria Garcia (`555-5678` / `5678`), Robert Johnson (`555-9999` / `9999`)

```csharp
[Collection("Sequential")]
public class FeatureAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private FeatureAgent _agent = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Services.AddUtilityBillingChatbot(builder.Configuration);
        _host = builder.Build();
        _agent = _host.Services.GetRequiredService<FeatureAgent>();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Feature_DoesExpectedBehavior()
    {
        var result = await _agent.RunAsync("test input");
        Assert.NotNull(result);
    }
}
```

## Key Files Reference

| Path | Purpose |
|------|---------|
| `src/Program.cs` | Minimal entry point |
| `src/Infrastructure/ServiceCollectionExtensions.cs` | Root DI registration |
| `src/Infrastructure/ChatClientFactory.cs` | LLM client creation (AzureOpenAI / OpenAI / HuggingFace) |
| `src/Infrastructure/ChatbotService.cs` | Console REPL (BackgroundService) |
| `src/Infrastructure/AgentResponseParser.cs` | Shared `TryGetResult` + JSON markdown extraction |
| `src/Orchestration/ChatbotOrchestrator.cs` | Main routing, session management, handoff |
| `src/Orchestration/ChatSession.cs` | Per-session state (auth, history, handoff) |
| `src/Agents/Classifier/ClassifierAgent.cs` | Question classification |
| `src/Agents/FAQ/FAQAgent.cs` | FAQ knowledge-base answers |
| `src/Agents/Auth/AuthAgent.cs` | In-band customer authentication |
| `src/Agents/Auth/AuthenticationContextProvider.cs` | Auth state + verification tools |
| `src/Agents/UtilityData/UtilityDataAgent.cs` | Account data queries (requires auth) |
| `src/Agents/Summarization/SummarizationAgent.cs` | Conversation summary for handoff |
| `src/Agents/NextBestAction/NextBestActionAgent.cs` | Best-effort proactive suggestions |
| `src/Data/faq-knowledge-base.md` | FAQ content injected into FAQAgent |
| `src/Data/verified-questions.json` | Known question types with auth requirements |
| `Directory.Build.props` | Shared settings (net10.0, TreatWarningsAsErrors=true, Nullable=enable) |
| `Directory.Packages.props` | Centralized NuGet package versions |

## Staged Implementation Status

| Stage | Component | Status |
|-------|-----------|--------|
| 1 | Classifier Agent | Complete |
| 2 | FAQ Agent | Complete |
| 3 | In-Band Auth Agent | Complete |
| 4 | Utility Data Agent | Complete |
| 5 | Orchestrator | Complete |
| 6 | Summarization / Handoff | Complete |
| 7 | Session Persistence | Planned |
| 8 | Next Best Action | Complete |
