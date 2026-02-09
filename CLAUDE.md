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

**Pattern**: .NET Generic Host with BackgroundService. Agents registered via DI extension methods.

```
Program.cs → Host.CreateApplicationBuilder()
           → AddUtilityBillingChatbot(config)
           → ChatbotService (BackgroundService, REPL loop)
```

**Key flow:**
1. User input → ClassifierAgent → QuestionClassification (structured JSON output)
2. Classification routes to downstream agents (FAQ, Auth, UtilityData - future stages)
3. All agents use `ChatResponseFormat.ForJsonSchema<T>()` for typed responses

**Agent pattern:**
```csharp
// Agents/{Feature}Agent.cs
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

Register in `Hosting/ServiceCollectionExtensions.cs`: `services.AddFeatureAgent();`

## Key Files

| Path | Purpose |
|------|---------|
| `Hosting/ServiceCollectionExtensions.cs` | Main DI setup, `AddUtilityBillingChatbot()` |
| `Hosting/ChatClientFactory.cs` | Creates IChatClient (Azure/OpenAI/HuggingFace) |
| `Agents/ClassifierAgent.cs` | Question categorization with structured output |
| `Services/ChatbotService.cs` | Console REPL loop (BackgroundService) |
| `Models/QuestionClassification.cs` | Classifier output schema |
| `docs/verified-questions.json` | Known question types with metadata |

## LLM Configuration

Three providers supported in `appsettings.json`:
- `AzureOpenAI`: Endpoint + ApiKey + DeploymentName
- `OpenAI`: ApiKey + Model (+ optional custom Endpoint for local LLMs)
- `HuggingFace`: ApiKey (or `HF_TOKEN` env var) + Model + Endpoint

## Adding New Agents

1. Create `Agents/{Name}Agent.cs` with agent class + `Add{Name}Agent()` extension
2. Create `Models/{Name}Output.cs` if new output type needed (with `[JsonPropertyName]` attributes)
3. Call `services.Add{Name}Agent()` in `ServiceCollectionExtensions.cs`
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
