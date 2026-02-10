# AGENTS.md

Guidelines for AI coding agents working in this repository.

## Project Overview

Multi-agent utility billing chatbot built on Microsoft Agent Framework (.NET 10). Vertical (feature-based) architecture with agents in `src/Agents/{Feature}/` directories.

## Build Commands

```bash
# Build the solution
dotnet build

# Run the chatbot (requires LLM config in appsettings.json)
dotnet run --project src

# Run all tests
dotnet test

# Run a single test by method name
dotnet test --filter "FullyQualifiedName~ClassifierAgentTests.Classifier_CategorizesBillingFAQ"

# Run all tests in a class
dotnet test --filter "FullyQualifiedName~FAQAgentTests"

# Run tests matching a pattern
dotnet test --filter "DisplayName~Auth"
```

Note: Tests are integration tests requiring a configured LLM endpoint in `tests/appsettings.json`.

## Code Style Guidelines

### File Headers

Every `.cs` file must start with the copyright header:

```csharp
// Copyright (c) Microsoft. All rights reserved.
```

### Imports and Namespaces

- Use file-scoped namespaces (single line, no braces):
  ```csharp
  namespace UtilityBillingChatbot.Agents.Classifier;
  ```
- Order imports: System → Microsoft → Third-party → Project
- Implicit usings are enabled; avoid redundant `using System;` etc.
- Remove unused imports

### Formatting

- Indentation: 4 spaces (no tabs)
- Braces: Allman style (opening brace on new line for types/methods)
- Max line length: ~120 characters (soft limit)
- Single blank line between members
- No trailing whitespace

### Types and Nullability

- Nullable reference types enabled globally (`<Nullable>enable</Nullable>`)
- Use `?` suffix for nullable types: `string?`, `Customer?`
- Use `[NotNullWhen(true)]` for try-pattern methods
- Prefer records for immutable data transfer objects:
  ```csharp
  public record FAQResponse(string Text, AgentSession Session);
  ```
- Use `required` keyword for required properties when appropriate

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes, Records, Enums | PascalCase | `ClassifierAgent`, `QuestionCategory` |
| Interfaces | IPascalCase | `IChatClient` |
| Methods, Properties | PascalCase | `ClassifyAsync`, `IsSuccess` |
| Private fields | _camelCase | `_logger`, `_chatClient` |
| Parameters, locals | camelCase | `input`, `cancellationToken` |
| Constants | PascalCase | `MeterName`, `MaxAttempts` |
| Async methods | Suffix with Async | `ClassifyAsync`, `RunAsync` |

### JSON Serialization

- Use `[JsonPropertyName("camelCase")]` for explicit property names
- Use `[JsonConverter(typeof(JsonStringEnumConverter))]` for enums
- Use `[Description("...")]` on schema types for LLM structured output
- Deserialize with `JsonSerializerOptions.Web` for camelCase

```csharp
[JsonPropertyName("category")]
[JsonConverter(typeof(JsonStringEnumConverter))]
[Description("The category of the question")]
public QuestionCategory Category { get; set; }
```

### Error Handling

- Use the try-pattern for operations that can fail:
  ```csharp
  if (!TryGetResult(response, out var result, out var error))
  {
      _logger.LogWarning("Failed: {Error}", error);
      return new ClassificationResult(null, error);
  }
  ```
- Catch specific exceptions, not bare `Exception` unless re-throwing
- Log errors with structured logging: `_logger.LogError(ex, "Message: {Param}", value)`
- Use `InvalidOperationException` for configuration/setup errors
- Rethrow `OperationCanceledException` in async methods

### XML Documentation

All public types and members require XML documentation:

```csharp
/// <summary>
/// Classifies the user's input into a question category.
/// </summary>
/// <param name="input">The user's question</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>The classification result, or null if classification failed</returns>
public async Task<ClassificationResult> ClassifyAsync(string input, CancellationToken ct = default)
```

## Architecture Patterns

### Agent Structure

Each agent lives in `src/Agents/{Feature}/` with:
- `{Feature}Agent.cs` - Main agent class
- `{Feature}Prompts.cs` - Prompt building (if complex)
- Model classes for input/output

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
                Instructions = "...",
                ResponseFormat = ChatResponseFormat.ForJsonSchema<FeatureResult>()
            }
        });
    }

    public async Task<FeatureResult> RunAsync(string input, CancellationToken ct = default)
    {
        var response = await _agent.RunAsync<FeatureResult>(input);
        // Handle response...
    }
}
```

### Dependency Injection

Each agent provides an extension method in the same file:

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

Register in `Infrastructure/ServiceCollectionExtensions.cs`:
```csharp
services.AddFeatureAgent();
```

### Structured Output

Use `ChatResponseFormat.ForJsonSchema<T>()` for typed LLM responses. Handle JSON parse failures gracefully:

```csharp
var response = await _agent.RunAsync<QuestionClassification>(input);
if (!TryGetResult(response, out var result, out var error))
{
    return new ClassificationResult(null, error);
}
```

## Testing Conventions

- Test framework: xUnit
- Test files: `tests/{Feature}AgentTests.cs`
- Use `IAsyncLifetime` for setup/teardown with DI
- Test method naming: `{Method}_{Scenario}` or `{Agent}_{Behavior}`

```csharp
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
        Assert.True(result.IsSuccess);
    }
}
```

## Key Files Reference

| Path | Purpose |
|------|---------|
| `src/Program.cs` | Application entry point |
| `src/Infrastructure/ServiceCollectionExtensions.cs` | Main DI registration |
| `src/Infrastructure/ChatClientFactory.cs` | LLM client creation |
| `src/Infrastructure/ChatbotService.cs` | Console REPL (BackgroundService) |
| `src/Agents/Classifier/ClassifierAgent.cs` | Question classification |
| `src/Agents/FAQ/FAQAgent.cs` | FAQ knowledge base answers |
| `src/Agents/Auth/AuthAgent.cs` | Customer authentication flow |
| `Directory.Build.props` | Shared project settings |
| `Directory.Packages.props` | Centralized package versions |
