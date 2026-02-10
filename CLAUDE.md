# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Context

Multi-agent utility billing chatbot built on Microsoft Agent Framework. Learning/prototype project - prioritize clear, robust implementations over clever abstractions. If you're adding too much code or hacking around the framework, check samples or the `ms-agent-framework` plugin first.

**Reference materials:**
- Agent Framework samples: `/home/lmark/git/agent-framework/dotnet/samples`
- Use the `ms-agent-framework:agent-framework` skill for framework guidance
- Architecture docs: `docs/CustomerSupportChatbot_Architecture.md`
- Future plans: `docs/plans/`

## Build Commands

```bash
# Build
dotnet build

# Run (requires LLM config in appsettings.json)
dotnet run

# Run tests
dotnet test

# Run single test
dotnet test --filter "FullyQualifiedName~ClassifierAgentTests.Classifier_CategorizesBillingFAQ"
```

## Architecture

**Pattern**: Vertical (feature-based) architecture with .NET Generic Host and BackgroundService.

```
src/
├── Agents/
│   └── Classifier/           # Question classification feature
│       ├── ClassifierAgent.cs
│       ├── ClassifierPrompts.cs
│       ├── QuestionClassification.cs
│       ├── QuestionCategory.cs
│       └── VerifiedQuestion.cs
├── Infrastructure/           # Cross-cutting concerns
│   ├── ChatClientFactory.cs
│   ├── ChatbotService.cs
│   ├── LlmOptions.cs
│   └── ServiceCollectionExtensions.cs
├── Telemetry/               # Observability
│   ├── AgentMetrics.cs
│   ├── TelemetryOptions.cs
│   ├── TelemetryServiceCollectionExtensions.cs
│   └── Middleware/
├── Data/
│   └── verified-questions.json
└── Program.cs
```

**Key flow:**
1. User input → ClassifierAgent → QuestionClassification (structured JSON output)
2. Classification routes to downstream agents (FAQ, Auth, UtilityData - future stages)
3. All agents use `ChatResponseFormat.ForJsonSchema<T>()` for typed responses

**Agent pattern:**
```csharp
// Agents/{Feature}/{Feature}Agent.cs
public class FeatureAgent
{
    private readonly ChatClientAgent _agent;
    public FeatureAgent(IChatClient chatClient, ILogger<FeatureAgent> logger) { ... }
    public async Task<FeatureResult> DoAsync(string input) { ... }
}

public static class FeatureAgentExtensions
{
    public static IServiceCollection AddFeatureAgent(this IServiceCollection services)
        => services.AddSingleton<FeatureAgent>();
}
```

Register in `Infrastructure/ServiceCollectionExtensions.cs`: `services.AddFeatureAgent();`

## Key Files

| Path | Purpose |
|------|---------|
| `Infrastructure/ServiceCollectionExtensions.cs` | Main DI setup, `AddUtilityBillingChatbot()` |
| `Infrastructure/ChatClientFactory.cs` | Creates IChatClient (Azure/OpenAI/HuggingFace) |
| `Agents/Classifier/ClassifierAgent.cs` | Question categorization with structured output |
| `Infrastructure/ChatbotService.cs` | Console REPL loop (BackgroundService) |
| `Agents/Classifier/QuestionClassification.cs` | Classifier output schema |
| `Data/verified-questions.json` | Known question types with metadata |

## LLM Configuration

Three providers supported in `appsettings.json`:
- `AzureOpenAI`: Endpoint + ApiKey + DeploymentName
- `OpenAI`: ApiKey + Model (+ optional custom Endpoint for local LLMs)
- `HuggingFace`: ApiKey (or `HF_TOKEN` env var) + Model + Endpoint

## Adding New Features

1. Create `Agents/{Name}/` directory with agent class + models
2. Add `Add{Name}Agent()` extension in the agent file
3. Call `services.Add{Name}Agent()` in `Infrastructure/ServiceCollectionExtensions.cs`
4. Inject into `ChatbotService` and add routing logic

## Structured Output

All agents return typed results via JSON schema validation:
```csharp
var response = await _agent.RunAsync<QuestionClassification>(input);
// Handle with TryGetResult pattern for JSON parse errors
```

## Telemetry

OpenTelemetry enabled by default. Configure in `appsettings.json`:
- `Telemetry.Enabled`: Toggle all telemetry
- `Telemetry.EnableConsoleExporter`: Local dev visibility
- `Telemetry.OtlpEndpoint`: Production export (or `OTEL_EXPORTER_OTLP_ENDPOINT` env var)

## Staged Implementation

| Stage | Component | Status |
|-------|-----------|--------|
| 1 | Classifier Agent | Implemented |
| 2 | FAQ Agent | Planned |
| 3 | In-Band Auth Agent | Planned |
| 4 | Utility Data Agent | Planned |
| 5 | Orchestrator | Planned |
| 6 | Summarization/Handoff | Planned |
