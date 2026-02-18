// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Infrastructure;

/// <summary>
/// Background service that runs the interactive chatbot console.
/// </summary>
public class ChatbotService : BackgroundService
{
    private readonly ChatbotOrchestrator _orchestrator;
    private readonly LlmOptions _llmOptions;
    private readonly ILogger<ChatbotService> _logger;

    // Session ID for this console session
    private readonly string _sessionId = Guid.NewGuid().ToString();

    public ChatbotService(
        ChatbotOrchestrator orchestrator,
        IOptions<LlmOptions> llmOptions,
        ILogger<ChatbotService> logger)
    {
        _orchestrator = orchestrator;
        _llmOptions = llmOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PrintStartupInfo();

        Console.WriteLine("=== Utility Billing Customer Support ===");
        Console.WriteLine("Ask me about your bill, payments, or account.");
        Console.WriteLine("Type 'quit' to exit.");
        Console.WriteLine();

        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Write("> ");

            // Use a task to allow cancellation during ReadLine
            var inputTask = Task.Run(() => Console.ReadLine()?.Trim(), stoppingToken);

            try
            {
                var input = await inputTask;

                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Goodbye!");
                    break;
                }

                await HandleUserInputAsync(input, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
        }

        _logger.LogInformation("Chatbot session ended");
    }

    private void PrintStartupInfo()
    {
        Console.WriteLine($"Using {_llmOptions.Provider}: {GetModelName()}");
        Console.WriteLine();
    }

    private string GetModelName() => _llmOptions.Provider switch
    {
        "AzureOpenAI" => _llmOptions.AzureOpenAI?.DeploymentName ?? "unknown",
        "OpenAI" => _llmOptions.OpenAI?.Model ?? "unknown",
        "HuggingFace" => _llmOptions.HuggingFace?.Model ?? "unknown",
        _ => "unknown"
    };

    private async Task HandleUserInputAsync(string input, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("User input received: {Length} chars", input.Length);

            var response = await _orchestrator.ProcessMessageAsync(_sessionId, input, cancellationToken);

            Console.WriteLine();
            Console.WriteLine(response.Message);

            // Display next best action suggestions if present
            if (response.SuggestedActions is { Count: > 0 })
            {
                Console.WriteLine();
                Console.WriteLine("You might also want to ask:");
                foreach (var suggestion in response.SuggestedActions)
                {
                    Console.WriteLine($"  - \"{suggestion.SuggestedQuestion}\"");
                }
            }

            // Show status info for debugging
            Console.WriteLine();
            Console.WriteLine($"  [Category: {response.Category}, Action: {response.RequiredAction}]");
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user input: {Error}", ex.Message);
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine();
        }
    }
}
