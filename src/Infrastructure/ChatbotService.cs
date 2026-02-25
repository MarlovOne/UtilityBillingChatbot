// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Infrastructure;

/// <summary>
/// Background service that runs the interactive chatbot console.
/// </summary>
public class ChatbotService : BackgroundService
{
    private readonly IChatbotOrchestrator _orchestrator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatbotService> _logger;

    private readonly string _sessionId = Guid.NewGuid().ToString();

    public ChatbotService(
        IChatbotOrchestrator orchestrator,
        IConfiguration configuration,
        ILogger<ChatbotService> logger)
    {
        _orchestrator = orchestrator;
        _configuration = configuration;
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
                break;
            }
        }

        _logger.LogInformation("Chatbot session ended");
    }

    private void PrintStartupInfo()
    {
        var provider = _configuration[$"{ILlmProvider.ConfigSection}:DefaultProvider"] ?? "OpenAI";
        var model = _configuration[$"{ILlmProvider.ConfigSection}:DefaultModel"] ?? "gpt-4o-mini";
        Console.WriteLine($"Using {provider}: {model}");
        Console.WriteLine();
    }

    private async Task HandleUserInputAsync(string input, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("User input received: {Length} chars", input.Length);

            Console.WriteLine();
            await foreach (var evt in _orchestrator.ProcessMessageStreamingAsync(
                _sessionId, input, cancellationToken))
            {
                switch (evt)
                {
                    case TextChunk t:
                        Console.Write(t.Text);
                        break;
                    case SuggestionsEvent s:
                        Console.WriteLine();
                        Console.WriteLine();
                        Console.WriteLine("You might also want to ask:");
                        foreach (var suggestion in s.Suggestions)
                            Console.WriteLine($"  - \"{suggestion.SuggestedQuestion}\"");
                        break;
                    case DebugEvent d:
                        Console.WriteLine($"\n\n  [Category: {d.Category}, Action: {d.Action}]");
                        break;
                }
            }
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user input: {Error}", ex.Message);
            Console.WriteLine($"\nError: {ex.Message}");
            Console.WriteLine();
        }
    }
}
