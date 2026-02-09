# Dependency Injection Refactoring Design

## Goals

1. Align with .NET Generic Host patterns (`IHost`, `IOptions<T>`)
2. Make Program.cs minimal and clean
3. Enable easier unit testing with mockable dependencies
4. Prepare for multi-agent scaling with clear registration patterns

## Key Decisions

- **Hosting model**: Convert to `BackgroundService` for proper lifecycle management
- **Configuration**: `IOptions<T>` for complex configs, direct injection for simple values
- **Agent registration**: Explicit extension methods (e.g., `AddClassifierAgent()`)
- **Simplicity priority**: Fewer abstractions, code you can read top to bottom

---

## New Structure

```
src/
├── Program.cs ..................... ~10 lines, just host setup
├── appsettings.json ............... Unchanged
│
├── Hosting/
│   ├── ServiceCollectionExtensions.cs   Main AddUtilityBillingChatbot() method
│   └── ChatClientFactory.cs ........... Creates IChatClient (unchanged)
│
├── Services/
│   └── ChatbotService.cs ........... BackgroundService with chat loop
│
├── Agents/
│   ├── ClassifierAgent.cs .......... Agent class + registration extension
│   └── ClassifierPrompts.cs ........ System prompt as static string
│
├── Models/
│   ├── LlmOptions.cs ............... Unchanged
│   ├── QuestionClassification.cs ... Unchanged
│   └── VerifiedQuestion.cs ......... Unchanged
│
└── Telemetry/
    ├── TelemetryExtensions.cs ...... Simplified registration
    └── AgentMetrics.cs ............. Unchanged
```

## Files to Remove

- `Hosting/ChatbotHostBuilder.cs` - Replaced by extension methods
- `Hosting/ChatbotHost.cs` - Using standard IHost instead
- `MultiAgent/IAgentDefinition.cs` - Unnecessary abstraction
- `MultiAgent/Agents/ClassifierAgentDefinition.cs` - Merged into ClassifierAgent
- `MultiAgent/AgentRegistry.cs` - Agents injected directly
- `Extensions/AgentResponseExtensions.cs` - Logic moved into agent class

---

## Implementation Details

### Program.cs

```csharp
using UtilityBillingChatbot.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddUtilityBillingChatbot(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
```

### ServiceCollectionExtensions.cs

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUtilityBillingChatbot(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<LlmOptions>(configuration.GetSection("LLM"));
        services.Configure<TelemetryOptions>(configuration.GetSection("Telemetry"));
        services.AddVerifiedQuestions(configuration);

        // Core services
        services.AddSingleton<IChatClient>(sp =>
            ChatClientFactory.Create(sp.GetRequiredService<IOptions<LlmOptions>>().Value));

        // Agents
        services.AddClassifierAgent();

        // Infrastructure
        services.AddTelemetryServices();
        services.AddHostedService<ChatbotService>();

        return services;
    }
}
```

### ChatbotService.cs

```csharp
public class ChatbotService : BackgroundService
{
    private readonly ClassifierAgent _classifier;
    private readonly IReadOnlyList<VerifiedQuestion> _verifiedQuestions;
    private readonly ILogger<ChatbotService> _logger;

    public ChatbotService(
        ClassifierAgent classifier,
        IReadOnlyList<VerifiedQuestion> verifiedQuestions,
        ILogger<ChatbotService> logger)
    {
        _classifier = classifier;
        _verifiedQuestions = verifiedQuestions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"Loaded {_verifiedQuestions.Count} verified question types.");
        Console.WriteLine("=== Utility Billing Customer Support Classifier ===");
        Console.WriteLine("Enter your question (or 'quit' to exit):");
        Console.WriteLine();

        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input)) continue;

            if (input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            await HandleUserInputAsync(input);
        }

        Console.WriteLine("Goodbye!");
    }

    private async Task HandleUserInputAsync(string input)
    {
        _logger.LogInformation("User input received: {Length} chars", input.Length);

        var classification = await _classifier.ClassifyAsync(input);

        if (classification == null)
        {
            Console.WriteLine("\nUnable to classify the question.\n");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Classification Result:");
        Console.WriteLine($"  Category:     {classification.Category}");
        Console.WriteLine($"  QuestionType: {classification.QuestionType ?? "(none)"}");
        Console.WriteLine($"  Confidence:   {classification.Confidence:F2}");
        Console.WriteLine($"  RequiresAuth: {classification.RequiresAuth}");
        Console.WriteLine($"  Reasoning:    {classification.Reasoning}");

        // Show matching verified question if found
        if (!string.IsNullOrEmpty(classification.QuestionType))
        {
            var matched = _verifiedQuestions.FirstOrDefault(q =>
                q.Id.Equals(classification.QuestionType, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                Console.WriteLine();
                Console.WriteLine("Matched Verified Question:");
                Console.WriteLine($"  ID:          {matched.Id}");
                Console.WriteLine($"  Description: {matched.Description}");
                Console.WriteLine($"  AuthLevel:   {matched.RequiredAuthLevel}");
                Console.WriteLine($"  Plugins:     {string.Join(", ", matched.RequiredPlugins)}");
            }
        }

        Console.WriteLine();
    }
}
```

### ClassifierAgent.cs

```csharp
public class ClassifierAgent
{
    private readonly ChatClientAgent _agent;
    private readonly ILogger<ClassifierAgent> _logger;

    public ClassifierAgent(IChatClient chatClient, ILogger<ClassifierAgent> logger)
    {
        _logger = logger;
        _agent = new ChatClientAgentBuilder()
            .UseChatClient(chatClient)
            .SetName("classifier")
            .SetInstructions(ClassifierPrompts.SystemPrompt)
            .Build();
    }

    public async Task<QuestionClassification?> ClassifyAsync(string userInput)
    {
        _logger.LogInformation("Classifying input: {Length} chars", userInput.Length);

        var response = await _agent.RunAsync<QuestionClassification>(userInput);

        if (!response.TryGetResult(out var result, out var error))
        {
            _logger.LogWarning("Classification failed: {Error}", error);
            return null;
        }

        _logger.LogInformation("Classified as {Category}, Confidence: {Confidence:F2}",
            result.Category, result.Confidence);
        return result;
    }
}

public static class ClassifierAgentExtensions
{
    public static IServiceCollection AddClassifierAgent(this IServiceCollection services)
    {
        services.AddSingleton<ClassifierAgent>();
        return services;
    }
}
```

### ClassifierPrompts.cs

```csharp
public static class ClassifierPrompts
{
    public const string SystemPrompt = """
        You are a customer service classifier for a utility billing company.
        ... (existing prompt content)
        """;
}
```

### VerifiedQuestionsExtensions.cs

```csharp
public static class VerifiedQuestionsExtensions
{
    public static IServiceCollection AddVerifiedQuestions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var path = configuration["VerifiedQuestionsPath"] ?? "verified-questions.json";

        var json = File.ReadAllText(path);
        var questions = JsonSerializer.Deserialize<List<VerifiedQuestion>>(json)
            ?? new List<VerifiedQuestion>();

        services.AddSingleton<IReadOnlyList<VerifiedQuestion>>(questions);

        return services;
    }
}
```

---

## Migration Steps

1. Create new folder structure (`Services/`, `Agents/`)
2. Create `ClassifierAgent.cs` with prompts extracted to `ClassifierPrompts.cs`
3. Create `ChatbotService.cs` (BackgroundService)
4. Create `ServiceCollectionExtensions.cs` with main registration method
5. Simplify `TelemetryExtensions.cs`
6. Update `Program.cs` to use new host pattern
7. Delete removed files
8. Run and test

---

## Adding New Agents (Future)

To add a new agent:

1. Create `Agents/NewAgent.cs` with the agent class
2. Add `AddNewAgent()` extension method in the same file
3. Call `services.AddNewAgent()` in `ServiceCollectionExtensions.cs`

Example:
```csharp
// Agents/BillingAgent.cs
public class BillingAgent
{
    public BillingAgent(IChatClient chatClient, ILogger<BillingAgent> logger) { ... }
    public async Task<BillingResponse?> HandleAsync(string input) { ... }
}

public static class BillingAgentExtensions
{
    public static IServiceCollection AddBillingAgent(this IServiceCollection services)
    {
        services.AddSingleton<BillingAgent>();
        return services;
    }
}
```
