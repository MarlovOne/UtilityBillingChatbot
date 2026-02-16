# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Context

Multi-agent utility billing chatbot built on Microsoft Agent Framework. Learning/prototype project - prioritize clear, robust implementations over clever abstractions. If you're adding too much code or hacking around the framework, check samples or the `ms-agent-framework` plugin first.

**Reference materials:**
- Agent Framework samples: `/home/lmark/git/agent-framework/dotnet/samples`
- Use the `ms-agent-framework:agent-framework` skill for framework guidance
- Architecture docs: `docs/CustomerSupportChatbot_Architecture.md`
- Stage implementation specs: `docs/CustomerSupportChatbot_stage*.md`

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
│   ├── Classifier/           # Question classification
│   ├── FAQ/                  # FAQ knowledge base
│   ├── Auth/                 # In-band authentication flow
│   ├── UtilityData/          # Account data queries (requires auth)
│   └── Summarization/        # Conversation summarization for handoff
├── Orchestration/            # Session management and routing
│   ├── ChatbotOrchestrator.cs
│   ├── ChatSession.cs
│   ├── ISessionStore.cs
│   ├── InMemorySessionStore.cs
│   └── Handoff/              # Human agent handoff
├── Infrastructure/           # Cross-cutting concerns
│   ├── ChatClientFactory.cs
│   ├── ChatbotService.cs
│   └── ServiceCollectionExtensions.cs
├── Telemetry/               # Observability (OpenTelemetry)
├── Data/
│   ├── verified-questions.json
│   └── faq-knowledge-base.md
└── Program.cs
```

**Key flow:**
1. User input → `ChatbotOrchestrator` → `ClassifierAgent` → `QuestionClassification`
2. Classification routes to appropriate agent:
   - `BillingFAQ` → `FAQAgent` (knowledge base lookup)
   - `AccountData` → `AuthAgent` (if needed) → `UtilityDataAgent` (mock CIS data)
   - `ServiceRequest` / `HumanRequested` → `SummarizationAgent` → Handoff ticket
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
| `Orchestration/ChatbotOrchestrator.cs` | Main routing logic, session management |
| `Orchestration/ChatSession.cs` | Session state (auth, history, handoff) |
| `Agents/Classifier/ClassifierAgent.cs` | Question categorization |
| `Agents/FAQ/FAQAgent.cs` | FAQ answers from knowledge base |
| `Agents/Auth/AuthAgent.cs` | In-band authentication flow |
| `Agents/Auth/AuthenticationContextProvider.cs` | Auth state + verification tools |
| `Agents/UtilityData/UtilityDataAgent.cs` | Account data queries |
| `Agents/Summarization/SummarizationAgent.cs` | Conversation summaries for handoff |
| `Orchestration/Handoff/HandoffService.cs` | Human agent ticket management |
| `Infrastructure/ServiceCollectionExtensions.cs` | Main DI setup |
| `Infrastructure/ChatbotService.cs` | Console REPL loop |
| `Data/faq-knowledge-base.md` | FAQ knowledge base content |

## LLM Configuration

Three providers supported in `appsettings.json`:
- `AzureOpenAI`: Endpoint + ApiKey + DeploymentName
- `OpenAI`: ApiKey + Model (+ optional custom Endpoint for local LLMs)
- `HuggingFace`: ApiKey (or `HF_TOKEN` env var) + Model + Endpoint

## Adding New Features

1. Create `Agents/{Name}/` directory with agent class + models
2. Add `Add{Name}Agent()` extension in the agent file
3. Call `services.Add{Name}Agent()` in `Infrastructure/ServiceCollectionExtensions.cs`
4. Inject into `ChatbotOrchestrator` and add routing logic in `RouteMessageAsync()`

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
| 1 | Classifier Agent | ✅ Implemented |
| 2 | FAQ Agent | ✅ Implemented |
| 3 | In-Band Auth Agent | ✅ Implemented |
| 4 | Utility Data Agent | ✅ Implemented |
| 5 | Orchestrator | ✅ Implemented |
| 6 | Summarization/Handoff | ✅ Implemented |
| 7 | Session Persistence | Planned |
